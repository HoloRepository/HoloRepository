using System.Text;
using HoloRepository.Core.Imaging;
using HoloRepository.Core.Markup;

namespace HoloRepository.Core.Notes;

public sealed record NoteRequest(
    ConsultationTranscript Transcript,
    IReadOnlyList<Annotation> Annotations,
    StudyMetadata? Study);

public sealed record MdtNote(
    string Header,
    IReadOnlyList<string> Attendees,
    IReadOnlyList<string> Discussion,
    IReadOnlyList<string> Findings,
    IReadOnlyList<string> History,
    IReadOnlyList<string> Measurements,
    IReadOnlyList<string> Plan);

public sealed record GeneratedNotes(
    MdtNote Mdt,
    string PatientSummary,
    ReadabilityResult PatientReadability,
    IReadOnlyList<SnomedConcept> Concepts,
    IReadOnlyList<GuidelineReference> Guidelines,
    string EngineName,
    DateTimeOffset GeneratedAt)
{
    public string MdtText => NoteFormatter.FormatMdt(this);
}

/// <summary>
/// Produces the dual-tier notes in FR-07. The production engine (Granite 4.1 + ClinicAVT running on the
/// local daemon) implements this interface; the prototype uses <see cref="TemplateNoteGenerator"/>.
/// </summary>
public interface INoteGenerator
{
    string Name { get; }
    Task<GeneratedNotes> GenerateAsync(NoteRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Deterministic, rule-based note generator. It extracts findings and actions from the transcript,
/// grounds them to SNOMED CT and writes a short plain-language patient summary.
/// </summary>
public sealed class TemplateNoteGenerator : INoteGenerator
{
    private static readonly string[] PlanCues = ["plan", "recommend", "we will", "i will", "arrange", "refer", "next step", "follow up", "follow-up"];

    public string Name => "Template engine (offline mock of Granite 4.1)";

    public Task<GeneratedNotes> GenerateAsync(NoteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var lines = request.Transcript.Lines;
        var concepts = ClinicalTerminology.Ground(request.Transcript.FullText + " " +
                                                 string.Join(" ", request.Annotations.Select(a => a.Label)));
        var guidelines = ClinicalTerminology.GuidelinesFor(concepts);

        var findings = new List<string>();
        var history = new List<string>();
        var plan = new List<string>();
        var discussion = new List<string>();

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string lower = line.Text.ToLowerInvariant();
            var lineConcepts = ClinicalTerminology.Ground(line.Text);

            if (PlanCues.Any(lower.Contains))
                plan.Add($"{line.Text} ({line.Speaker})");
            else if (lineConcepts.Any(c => c.Category == ConceptCategory.History))
                history.Add(line.Text);
            else if (lineConcepts.Any(c => c.Category is ConceptCategory.Finding or ConceptCategory.Disorder) ||
                     lower.Contains("no ") || lower.Contains("measur"))
                findings.Add(line.Text);
            else
                discussion.Add($"{line.Speaker}: {line.Text}");
        }

        foreach (var a in request.Annotations.Where(a => a.Kind != AnnotationKind.Caliper))
            findings.Add($"Marked on imaging: {a.DisplayLabel} at {a.Start} ({a.Author}).");

        var measurements = request.Annotations
            .Where(a => a.LengthMm is not null)
            .Select(a => $"{a.DisplayLabel}: {a.LengthMm:0.0} mm ({a.Author})")
            .ToList();

        var study = request.Study;
        string header = study is null
            ? "Study: not specified"
            : $"Patient: {study.PatientDisplayName} ({study.PatientId}) · {study.Modality} · {study.StudyDescription}" +
              (study.StudyDate is DateTime d ? $" · {d:dd MMM yyyy}" : string.Empty);

        var mdt = new MdtNote(header, request.Transcript.Speakers, discussion, findings, history, measurements, plan);

        string summary = WritePatientSummary(request, concepts);
        var readability = ReadabilityScorer.Score(summary);

        var notes = new GeneratedNotes(mdt, summary, readability, concepts, guidelines, Name, DateTimeOffset.Now);
        return Task.FromResult(notes);
    }

    private static string WritePatientSummary(NoteRequest request, IReadOnlyList<SnomedConcept> concepts)
    {
        bool Has(string id) => concepts.Any(c => c.ConceptId == id);
        string text = request.Transcript.FullText.ToLowerInvariant();
        string side = text.Contains("right") ? "right " : text.Contains("left") ? "left " : string.Empty;

        var sb = new StringBuilder();
        sb.Append("You had a scan of your chest. A team of doctors looked at it together. ");

        if (Has("427359005"))
        {
            sb.Append($"They saw a small round spot on your {side}lung. ");
            double? size = LargestMeasurement(request);
            if (size is double mm)
                sb.Append($"It is about {mm:0} mm wide. That is about the size of {SizeComparison(mm)}. ");
            sb.Append("We do not know yet what the spot is. ");
        }

        if (Has("82918005"))
            sb.Append("You will have one more scan. It is called a PET scan. It shows how busy the cells in the spot are. ");
        if (Has("86273004"))
            sb.Append("A doctor may take a tiny bit of the spot with a thin needle. This helps us find out what it is. ");
        if (Has("173171007"))
            sb.Append("If you need it, a surgeon may take out the part of the lung with the spot. ");
        if (Has("38341003"))
            sb.Append("Your blood pressure is a bit high. Keep taking your pills for it. ");
        if (Has("8517006"))
            sb.Append("Well done for not smoking. This is good for your lungs. ");

        sb.Append("A nurse will call you soon. They will tell you what comes next. You can ask them any questions you have.");
        return sb.ToString();
    }

    private static double? LargestMeasurement(NoteRequest request)
    {
        var caliper = request.Annotations.Where(a => a.LengthMm is not null).Select(a => a.LengthMm!.Value).DefaultIfEmpty().Max();
        if (caliper > 0) return caliper;

        // Fall back to a size mentioned in the dialogue, e.g. "measuring approximately 18 mm".
        var m = System.Text.RegularExpressions.Regex.Match(request.Transcript.FullText, @"(\d+(?:\.\d+)?)\s?mm");
        return m.Success ? double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    private static string SizeComparison(double mm) => mm switch
    {
        < 8 => "a pea",
        < 25 => "a grape",
        < 45 => "a plum",
        _ => "an egg",
    };
}

/// <summary>Formats generated notes as plain text and Markdown for display and export.</summary>
public static class NoteFormatter
{
    public static string FormatMdt(GeneratedNotes notes)
    {
        var sb = new StringBuilder();
        var m = notes.Mdt;
        sb.AppendLine("MDT CLINICAL NOTE");
        sb.AppendLine(m.Header);
        sb.AppendLine($"Attendees: {(m.Attendees.Count == 0 ? "not recorded" : string.Join(", ", m.Attendees))}");
        Section(sb, "Imaging findings", m.Findings);
        Section(sb, "Measurements", m.Measurements);
        Section(sb, "Relevant history", m.History);
        Section(sb, "Discussion", m.Discussion);
        Section(sb, "Plan / actions", m.Plan);
        Section(sb, "SNOMED CT concepts mentioned", notes.Concepts.Select(c => $"{c.PreferredTerm} — SCTID {c.ConceptId}").ToList());
        Section(sb, "NICE guidance to cross-check", notes.Guidelines.Select(g => $"{g.Code}: {g.Title}").ToList());
        sb.AppendLine();
        sb.AppendLine($"Generated {notes.GeneratedAt:dd MMM yyyy HH:mm} by {notes.EngineName}. Draft for clinician review.");
        return sb.ToString();
    }

    public static string ToMarkdown(GeneratedNotes notes)
    {
        var sb = new StringBuilder();
        var m = notes.Mdt;
        sb.AppendLine("# MDT clinical note");
        sb.AppendLine();
        sb.AppendLine($"**{m.Header}**  ");
        sb.AppendLine($"Attendees: {(m.Attendees.Count == 0 ? "not recorded" : string.Join(", ", m.Attendees))}");
        MdSection(sb, "Imaging findings", m.Findings);
        MdSection(sb, "Measurements", m.Measurements);
        MdSection(sb, "Relevant history", m.History);
        MdSection(sb, "Discussion", m.Discussion);
        MdSection(sb, "Plan / actions", m.Plan);

        sb.AppendLine();
        sb.AppendLine("## SNOMED CT concepts mentioned");
        sb.AppendLine();
        if (notes.Concepts.Count == 0) sb.AppendLine("_None identified._");
        else
        {
            sb.AppendLine("| SCTID | Preferred term | Category |");
            sb.AppendLine("|---|---|---|");
            foreach (var c in notes.Concepts) sb.AppendLine($"| {c.ConceptId} | {c.PreferredTerm} | {c.Category} |");
        }

        sb.AppendLine();
        sb.AppendLine("## NICE guidance to cross-check");
        sb.AppendLine();
        if (notes.Guidelines.Count == 0) sb.AppendLine("_None linked._");
        foreach (var g in notes.Guidelines) sb.AppendLine($"- [{g.Code}: {g.Title}]({g.Url}) — {g.Rationale}");

        sb.AppendLine();
        sb.AppendLine("# Patient summary");
        sb.AppendLine();
        sb.AppendLine(notes.PatientSummary);
        sb.AppendLine();
        sb.AppendLine($"_Estimated reading age: {notes.PatientReadability.ReadingAge:0.#} (target ≤ {ReadabilityScorer.PatientTargetReadingAge:0})._");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine($"Generated {notes.GeneratedAt:dd MMM yyyy HH:mm} by {notes.EngineName}. Draft for clinician review; not for clinical use.");
        return sb.ToString();
    }

    private static void Section(StringBuilder sb, string title, IReadOnlyList<string> items)
    {
        sb.AppendLine();
        sb.AppendLine(title.ToUpperInvariant());
        if (items.Count == 0) sb.AppendLine("  • None recorded");
        foreach (var i in items) sb.AppendLine($"  • {i}");
    }

    private static void MdSection(StringBuilder sb, string title, IReadOnlyList<string> items)
    {
        sb.AppendLine();
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        if (items.Count == 0) sb.AppendLine("_None recorded._");
        foreach (var i in items) sb.AppendLine($"- {i}");
    }
}
