using HoloRepository.Core.Daemon;
using HoloRepository.Core.Imaging;
using HoloRepository.Core.Markup;
using HoloRepository.Core.Notes;
using HoloRepository.Core.Segmentation;
using Microsoft.UI.Xaml;

namespace HoloRepository.App.Services;

/// <summary>
/// Composition root for the client. Swap <see cref="Daemon"/>, <see cref="Hub"/> or <see cref="NoteGenerator"/>
/// for networked implementations without touching the pages.
/// </summary>
public sealed class AppServices
{
    private readonly Dictionary<string, SegmentationResult> _segmentations = new();
    private readonly DemoSegmentationService _demoSegmentation = new();
    private string? _currentSeriesId;
    private HoloRepositoryApiClient? _api;

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

    public event EventHandler? TechnicalDetailsChanged;

    public event EventHandler<string>? SegmentationChanged;

    public event EventHandler? AccountChanged;

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

    // ---- Technical details --------------------------------------------------------------------

    public bool ShowTechnicalDetails
    {
        get => Settings.ShowTechnicalDetails;
        set
        {
            if (Settings.ShowTechnicalDetails == value) return;
            Settings.ShowTechnicalDetails = value;
            Settings.Save();
            TechnicalDetailsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Visibility TechnicalVisibility => ShowTechnicalDetails ? Visibility.Visible : Visibility.Collapsed;

    // ---- HoloRepository server ----------------------------------------------------------------

    public bool IsSignedIn => _api?.IsSignedIn == true;

    public string? SignedInUser { get; private set; }

    public async Task SignInAsync(string username, string password, bool createAccount)
    {
        var api = CreateApi();
        if (createAccount) await api.SignUpAsync(username, password);
        var user = await api.SignInAsync(username, password);
        _api?.Dispose();
        _api = api;
        SignedInUser = user.Username;
        Settings.ServerUsername = user.Username;
        Settings.Save();
        AccountChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut()
    {
        _api?.Dispose();
        _api = null;
        SignedInUser = null;
        AccountChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The server when signed in, otherwise the offline demo.</summary>
    public ISegmentationService Segmentation => _api is { IsSignedIn: true } api
        ? new RemoteSegmentationService(api)
        : _demoSegmentation;

    private HoloRepositoryApiClient CreateApi()
    {
        string address = Settings.ServerAddress.Trim();
        if (!address.EndsWith('/')) address += "/";
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("The server address is not valid. Check it under Settings › Advanced.");
        return new HoloRepositoryApiClient(uri);
    }

    // ---- Segmentation results ------------------------------------------------------------------

    public SegmentationResult? SegmentationFor(string? seriesId) =>
        seriesId is not null && _segmentations.TryGetValue(seriesId, out var r) ? r : null;

    public void SetSegmentation(SegmentationResult result)
    {
        _segmentations[result.SeriesId] = result;
        SegmentationChanged?.Invoke(this, result.SeriesId);
    }

    public LoadedSeries GetLoadedSeries(string seriesId)
    {
        var info = Daemon.LoadedSeries.First(s => s.Id == seriesId);
        return new LoadedSeries(info.Metadata, Daemon.GetGeometry(seriesId));
    }
}
