using HoloRepository.Core.Imaging;
using HoloRepository.Core.Markup;
using HoloRepository.Core.Notes;

namespace HoloRepository.Core.Tests;

public class TranscriptTests
{
    [Fact]
    public void Add_assigns_offsets_and_raises_events()
    {
        var t = new ConsultationTranscript();
        int added = 0, cleared = 0;
        t.LineAdded += (_, _) => added++;
        t.Cleared += (_, _) => cleared++;

        Assert.True(t.IsEmpty);
        t.Add("Radiologist", "First line.");
        var second = t.Add(" ", "Second line.");
        Assert.Equal("Unknown", second.Speaker);
        Assert.Equal(TimeSpan.FromSeconds(12), second.Offset);
        Assert.Contains("Radiologist: First line.", t.FullText);
        Assert.Equal(2, t.Speakers.Count);
        Assert.Throws<ArgumentException>(() => t.Add("x", " "));

        t.LoadSample();
        Assert.Equal(ConsultationTranscript.SampleDialogue.Count, t.Lines.Count);
        Assert.Equal(2 + ConsultationTranscript.SampleDialogue.Count, added);
        Assert.Equal(1, cleared);
    }
}

public class TerminologyTests
{
    [Fact]
    public void Grounds_concepts_once_with_word_boundaries()
    {
        var concepts = ClinicalTerminology.Ground("Spiculated nodule. Recommend PET-CT then biopsy. Nodule again. Hypertension.");
        var ids = concepts.Select(c => c.ConceptId).ToList();
        Assert.Contains("427359005", ids);
        Assert.Contains("82918005", ids);
        Assert.Contains("86273004", ids);
        Assert.Contains("38341003", ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.DoesNotContain("77477000", ids); // "CT" inside "PET-CT" must not match the CT concept
        Assert.StartsWith("https://termbrowser.nhs.uk/", concepts[0].BrowserUrl);
        Assert.Empty(ClinicalTerminology.Ground(""));
        Assert.True(ClinicalTerminology.ConceptCount >= 10);
    }

    [Fact]
    public void Links_distinct_nice_guidelines()
    {
        var concepts = ClinicalTerminology.Ground("lung nodule, lobectomy, biopsy, type 2 diabetes, pleural effusion");
        var guidelines = ClinicalTerminology.GuidelinesFor(concepts);
        Assert.Equal(["NG122", "NG28"], guidelines.Select(g => g.Code).OrderBy(c => c).ToList());
        Assert.Equal("https://www.nice.org.uk/guidance/ng122", guidelines.First(g => g.Code == "NG122").Url);
    }
}

public class ReadabilityTests
{
    [Theory]
    [InlineData("cat", 1)]
    [InlineData("table", 2)]
    [InlineData("pressure", 2)]
    [InlineData("hypertension", 4)]
    [InlineData("18", 1)]
    [InlineData("used", 1)]
    [InlineData("''", 0)]
    public void Counts_syllables(string word, int expected)
    {
        Assert.Equal(expected, ReadabilityScorer.CountSyllables(word));
    }

    [Fact]
    public void Simple_text_scores_lower_than_complex_text()
    {
        var simple = ReadabilityScorer.Score("The cat sat on the mat. It was a big cat.");
        var complex = ReadabilityScorer.Score(
            "Histopathological characterisation of the spiculated pulmonary abnormality necessitates percutaneous transthoracic investigation.");
        Assert.True(simple.ReadingAge < complex.ReadingAge);
        Assert.True(simple.MeetsTarget(ReadabilityScorer.PatientTargetReadingAge));
        Assert.False(complex.MeetsTarget(ReadabilityScorer.PatientTargetReadingAge));
        Assert.Equal(2, simple.Sentences);
        Assert.Equal(5, ReadabilityScorer.Score("  ").ReadingAge);
    }
}

public class NoteGeneratorTests
{
    private static NoteRequest SampleRequest(bool withCaliper = true)
    {
        var t = new ConsultationTranscript();
        t.LoadSample();
        var annotations = new List<Annotation>
        {
            new() { SeriesId = "s", Kind = AnnotationKind.Pin, Start = new Point3(1, 2, 3), Label = "RUL nodule", Author = "Radiologist" },
        };
        if (withCaliper)
            annotations.Add(new Annotation
            {
                SeriesId = "s", Kind = AnnotationKind.Caliper, Start = new Point3(0, 0, 0), End = new Point3(0, 21, 0), Label = "Nodule diameter",
            });
        return new NoteRequest(t, annotations, PhantomGenerator.CreateChestSeries(16, 16, 8).Metadata);
    }

    [Fact]
    public async Task Generates_structured_mdt_note()
    {
        var notes = await new TemplateNoteGenerator().GenerateAsync(SampleRequest());
        var mdt = notes.Mdt;

        Assert.Contains("SYNTH-0001", mdt.Header);
        Assert.Equal(4, mdt.Attendees.Count);
        Assert.Contains(mdt.Findings, f => f.Contains("spiculated nodule"));
        Assert.Contains(mdt.Findings, f => f.StartsWith("Marked on imaging: RUL nodule"));
        Assert.Contains(mdt.History, h => h.Contains("hypertension"));
        Assert.Contains(mdt.Plan, p => p.Contains("PET-CT"));
        Assert.Contains(mdt.Measurements, m => m.Contains("21.0 mm"));
        Assert.Contains(notes.Concepts, c => c.ConceptId == "363358000");
        Assert.Contains(notes.Guidelines, g => g.Code == "NG122");
        Assert.Contains("SNOMED CT", notes.MdtText);
        Assert.Contains("Draft for clinician review", notes.MdtText);
    }

    [Fact]
    public async Task Patient_summary_meets_reading_age_nine()
    {
        var notes = await new TemplateNoteGenerator().GenerateAsync(SampleRequest());
        Assert.True(notes.PatientReadability.ReadingAge <= ReadabilityScorer.PatientTargetReadingAge,
            $"Reading age was {notes.PatientReadability.ReadingAge}");
        Assert.Contains("right lung", notes.PatientSummary);
        Assert.Contains("21 mm", notes.PatientSummary);
        Assert.Contains("grape", notes.PatientSummary);
        Assert.Contains("PET scan", notes.PatientSummary);
    }

    [Fact]
    public async Task Falls_back_to_size_spoken_in_transcript()
    {
        var notes = await new TemplateNoteGenerator().GenerateAsync(SampleRequest(withCaliper: false));
        Assert.Contains("18 mm", notes.PatientSummary);
    }

    [Fact]
    public async Task Empty_consultation_still_produces_safe_notes()
    {
        var notes = await new TemplateNoteGenerator().GenerateAsync(
            new NoteRequest(new ConsultationTranscript(), Array.Empty<Annotation>(), null));
        Assert.Contains("not specified", notes.Mdt.Header);
        Assert.Contains("None recorded", notes.MdtText);
        Assert.Contains("not recorded", notes.MdtText);
        Assert.Empty(notes.Concepts);
        var md = NoteFormatter.ToMarkdown(notes);
        Assert.Contains("_None identified._", md);
        Assert.Contains("_None linked._", md);
    }

    [Fact]
    public async Task Markdown_export_contains_all_sections()
    {
        var notes = await new TemplateNoteGenerator().GenerateAsync(SampleRequest());
        var md = NoteFormatter.ToMarkdown(notes);
        Assert.Contains("# MDT clinical note", md);
        Assert.Contains("| 427359005 |", md);
        Assert.Contains("[NG122:", md);
        Assert.Contains("# Patient summary", md);
        Assert.Contains("Estimated reading age", md);
    }

    [Theory]
    [InlineData(5, "a pea")]
    [InlineData(30, "a plum")]
    [InlineData(60, "an egg")]
    public async Task Size_comparisons_scale_with_measurement(double mm, string expected)
    {
        var t = new ConsultationTranscript();
        t.Add("Radiologist", "There is a nodule in the left lower lobe.");
        var caliper = new Annotation { SeriesId = "s", Kind = AnnotationKind.Caliper, Start = default, End = new Point3(mm, 0, 0) };
        var notes = await new TemplateNoteGenerator().GenerateAsync(new NoteRequest(t, [caliper], null));
        Assert.Contains(expected, notes.PatientSummary);
        Assert.Contains("left lung", notes.PatientSummary);
    }

    [Fact]
    public async Task Rejects_null_and_honours_cancellation()
    {
        var gen = new TemplateNoteGenerator();
        await Assert.ThrowsAsync<ArgumentNullException>(() => gen.GenerateAsync(null!));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gen.GenerateAsync(SampleRequest(), cts.Token));
    }
}
