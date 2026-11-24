namespace HoloRepository.Core.Notes;

public sealed record TranscriptLine(string Speaker, string Text, TimeSpan Offset);

/// <summary>Captured dialogue from an MDT consultation (stand-in for ClinicAVT speech-to-text output).</summary>
public sealed class ConsultationTranscript
{
    private readonly List<TranscriptLine> _lines = new();
    private readonly object _gate = new();

    public event EventHandler<TranscriptLine>? LineAdded;
    public event EventHandler? Cleared;

    public IReadOnlyList<TranscriptLine> Lines
    {
        get
        {
            lock (_gate) return _lines.ToList();
        }
    }

    public bool IsEmpty
    {
        get
        {
            lock (_gate) return _lines.Count == 0;
        }
    }

    public IReadOnlyList<string> Speakers => Lines.Select(l => l.Speaker).Distinct().ToList();

    public string FullText => string.Join(Environment.NewLine, Lines.Select(l => $"{l.Speaker}: {l.Text}"));

    public TranscriptLine Add(string speaker, string text, TimeSpan? offset = null)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Text is required.", nameof(text));
        TranscriptLine line;
        lock (_gate)
        {
            var at = offset ?? (_lines.Count == 0 ? TimeSpan.Zero : _lines[^1].Offset + TimeSpan.FromSeconds(12));
            line = new TranscriptLine(string.IsNullOrWhiteSpace(speaker) ? "Unknown" : speaker.Trim(), text.Trim(), at);
            _lines.Add(line);
        }
        LineAdded?.Invoke(this, line);
        return line;
    }

    public void Clear()
    {
        lock (_gate) _lines.Clear();
        Cleared?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A fictional MDT discussion that matches the built-in chest phantom.</summary>
    public static IReadOnlyList<(string Speaker, string Text)> SampleDialogue { get; } =
    [
        ("Consultant Radiologist", "This is the contrast CT thorax for the synthetic test patient, a 64 year old ex-smoker."),
        ("Consultant Radiologist", "There is a spiculated nodule in the right upper lobe measuring approximately 18 mm."),
        ("Consultant Radiologist", "No pleural effusion. No obvious mediastinal lymph node enlargement on this study."),
        ("Thoracic Surgeon", "Background of hypertension, well controlled. Performance status looks good."),
        ("Clinical Oncologist", "Given the size and spiculation we need to exclude lung cancer."),
        ("Clinical Oncologist", "I recommend a PET-CT for staging before any decision."),
        ("Thoracic Surgeon", "Plan for CT guided biopsy of the nodule after the PET-CT."),
        ("Thoracic Surgeon", "If confirmed and the disease is localised we will discuss a lobectomy at the next MDT."),
        ("Clinical Nurse Specialist", "I will contact the patient this week to explain the next steps and arrange the scans."),
    ];

    public void LoadSample()
    {
        Clear();
        foreach (var (speaker, text) in SampleDialogue) Add(speaker, text);
    }
}
