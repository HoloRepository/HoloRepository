using HoloRepository.Core.Daemon;
using HoloRepository.Core.Markup;
using HoloRepository.Core.Notes;

namespace HoloRepository.App.Services;

/// <summary>
/// Composition root for the client. Swap <see cref="Daemon"/>, <see cref="Hub"/> or <see cref="NoteGenerator"/>
/// for networked implementations without touching the pages.
/// </summary>
public sealed class AppServices
{
    private string? _currentSeriesId;

    private AppServices()
    {
        Settings = AppSettings.Load();
        Daemon = new InProcessDaemonClient { PreferredBackend = Settings.Backend };
        Hub.AnnotationReceived += (_, annotation) => Annotations.Add(annotation);
    }

    public static AppServices Current { get; } = new();

    public AppSettings Settings { get; }
    public IComputeDaemonClient Daemon { get; }
    public AnnotationStore Annotations { get; } = new();
    public LoopbackCollaborationHub Hub { get; } = new();
    public ConsultationTranscript Transcript { get; } = new();
    public INoteGenerator NoteGenerator { get; } = new TemplateNoteGenerator();

    /// <summary>Whether local viewport changes are broadcast to the consultation.</summary>
    public bool ShareViewport { get; set; } = true;

    public GeneratedNotes? LastNotes { get; set; }

    public event EventHandler? CurrentSeriesChanged;

    public string? CurrentSeriesId
    {
        get => _currentSeriesId;
        set
        {
            if (_currentSeriesId == value) return;
            _currentSeriesId = value;
            CurrentSeriesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public SeriesInfo? CurrentSeries => Daemon.LoadedSeries.FirstOrDefault(s => s.Id == CurrentSeriesId);
}
