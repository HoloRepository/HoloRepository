using System.Collections.ObjectModel;
using HoloRepository.App.Controls;
using HoloRepository.App.Services;
using HoloRepository.Core.Imaging;
using HoloRepository.Core.Markup;
using HoloRepository.Core.Segmentation;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;

namespace HoloRepository.App.Pages;

/// <summary>Row shown in the markup list.</summary>
public sealed class AnnotationRow(Annotation annotation)
{
    private static readonly SolidColorBrush LocalBrush = new(Color.FromArgb(255, 79, 209, 217));
    private static readonly SolidColorBrush RemoteBrush = new(Color.FromArgb(255, 255, 170, 60));

    public Annotation Annotation { get; } = annotation;
    public string Id => Annotation.Id.ToString();
    public string Title => Annotation.Summary;
    public string Detail => $"{KindName(Annotation.Kind)} · {Annotation.Author} · {Annotation.CreatedAt.ToLocalTime():HH:mm}";
    public SolidColorBrush Brush => Annotation.IsRemote ? RemoteBrush : LocalBrush;

    public static string KindName(AnnotationKind kind) => kind switch
    {
        AnnotationKind.Caliper => "Measurement",
        AnnotationKind.Callout => "Note",
        _ => "Pin",
    };
}

/// <summary>Row shown in the organs list.</summary>
public sealed class StructureRow(SegmentedStructure structure, bool visible)
{
    public byte Label => structure.Label;
    public string Name => structure.Name;
    public string VolumeText => structure.VoxelCount > 0 ? structure.VolumeText : string.Empty;
    public bool IsVisible { get; } = visible;
    public SolidColorBrush Brush { get; } = new(Color.FromArgb(255, structure.Colour.R, structure.Colour.G, structure.Colour.B));
}

/// <summary>
/// Multi-planar + 3D workspace. Each instance (main window or a pop-out window) keeps its own camera,
/// while markup is shared through the annotation store and the consultation hub.
/// </summary>
public sealed partial class ViewerPage : Page
{
    private enum Tool
    {
        Navigate,
        Pin,
        Measure,
        Callout,
    }

    private static readonly (string Name, WindowLevel Window, RenderMode Mode)[] RenderModes =
    [
        ("Volume", new WindowLevel(300, 1000), RenderMode.Composite),
        ("Bone", new WindowLevel(700, 1000), RenderMode.Composite),
        ("MIP", new WindowLevel(150, 1400), RenderMode.MaximumIntensity),
    ];

    private readonly AppServices _services = AppServices.Current;
    private readonly Dictionary<SlicePlane, SliceViewport> _views;
    private readonly Dictionary<SlicePlane, int> _index = new();
    private readonly Dictionary<SlicePlane, int> _requestVersion = new();
    private readonly ObservableCollection<AnnotationRow> _rows = new();
    private readonly HashSet<byte> _hiddenStructures = new();
    private readonly Dictionary<FrameworkElement, (int Row, int Column)> _homeCells;

    private string? _seriesId;
    private Volume? _geometry;
    private WindowLevel _window = WindowLevel.Lung;
    private Tool _tool = Tool.Navigate;
    private int _renderMode;
    private double _yaw = 25, _pitch = 12, _zoom = 1.35;
    private Point3? _crosshair;
    private (SlicePlane Plane, Point3 Start, Point3 End)? _caliperDraft;
    private bool _rendering, _renderAgain, _subscribed, _suppressPresetEvent;
    private DateTime _lastViewportPublish = DateTime.MinValue;
    private CancellationTokenSource? _segmentCts;
    private FrameworkElement? _fullScreenView;
    private WindowLevel? _defaultWindow;
    private bool _isCt = true;

    public ViewerPage()
    {
        InitializeComponent();
        _views = new()
        {
            [SlicePlane.Axial] = AxialView,
            [SlicePlane.Coronal] = CoronalView,
            [SlicePlane.Sagittal] = SagittalView,
        };
        _homeCells = new()
        {
            [AxialView] = (0, 0),
            [CoronalView] = (0, 1),
            [SagittalView] = (1, 0),
            [VolumeView] = (1, 1),
        };

        foreach (var (plane, view) in _views)
        {
            var p = plane;
            view.SliceScrolled += (_, delta) => Scroll(p, delta);
            view.WindowDragged += (_, d) => SetWindow(_window.Adjust(d.DeltaWidth, d.DeltaCentre), fromPreset: false);
            view.ImagePressed += (_, e) => OnPointer(p, 0, e.Col, e.Row);
            view.ImageDragged += (_, e) => OnPointer(p, 1, e.Col, e.Row);
            view.ImageReleased += (_, e) => OnPointer(p, 2, e.Col, e.Row);
            view.FullScreenRequested += (_, _) => ToggleFullScreen(view);
        }

        VolumeView.Rotated += (_, d) =>
        {
            _yaw = (_yaw + d.Yaw) % 360;
            _pitch = Math.Clamp(_pitch - d.Pitch, -89, 89);
            Request3D();
        };
        VolumeView.RotationFinished += (_, _) => Request3D();
        VolumeView.Zoomed += (_, factor) =>
        {
            _zoom = Math.Clamp(_zoom * factor, 0.6, 4);
            Request3D();
        };
        VolumeView.FullScreenRequested += (_, _) => ToggleFullScreen(VolumeView);

        foreach (var (name, _) in WindowLevel.Presets) PresetBox.Items.Add(name);
        _suppressPresetEvent = true;
        PresetBox.SelectedIndex = 0;
        RenderBox.SelectedIndex = 0;
        _suppressPresetEvent = false;

        AnnotationList.ItemsSource = _rows;
        ApplyTechnicalDetails();
        Loaded += OnLoaded;
    }

    private bool Technical => _services.ShowTechnicalDetails;

    private bool OrgansVisible => OrgansToggle.IsChecked == true;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is "secondary") NewWindowButton.Visibility = Visibility.Collapsed;
        await OpenSeriesAsync(_services.CurrentSeriesId);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        if (_fullScreenView is not null) ExitFullScreen();
    }

    // The page is cached while other pages are shown, so it stays subscribed and keeps following
    // the shared viewport. Subscriptions are only dropped when the hosting window closes.
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateSyncText();
        if (_subscribed) return;
        _subscribed = true;
        _services.Annotations.Changed += Annotations_Changed;
        _services.Hub.ViewportReceived += Hub_ViewportReceived;
        _services.Hub.Activity += Hub_Activity;
        _services.CurrentSeriesChanged += Services_CurrentSeriesChanged;
        _services.TechnicalDetailsChanged += Services_TechnicalDetailsChanged;
        _services.SegmentationChanged += Services_SegmentationChanged;
        RefreshMarkup();
    }

    public void Detach()
    {
        if (!_subscribed) return;
        _subscribed = false;
        _services.Annotations.Changed -= Annotations_Changed;
        _services.Hub.ViewportReceived -= Hub_ViewportReceived;
        _services.Hub.Activity -= Hub_Activity;
        _services.CurrentSeriesChanged -= Services_CurrentSeriesChanged;
        _services.TechnicalDetailsChanged -= Services_TechnicalDetailsChanged;
        _services.SegmentationChanged -= Services_SegmentationChanged;
        _segmentCts?.Cancel();
    }

    private void Services_CurrentSeriesChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(async () => await OpenSeriesAsync(_services.CurrentSeriesId));

    private void Services_TechnicalDetailsChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(async () =>
    {
        ApplyTechnicalDetails();
        UpdateSubtitle();
        UpdateSyncText();
        RefreshSegmentationUi();
        await RefreshAllAsync();
    });

    private void Services_SegmentationChanged(object? sender, string seriesId) => DispatcherQueue.TryEnqueue(async () =>
    {
        if (seriesId != _seriesId) return;
        _hiddenStructures.Clear();
        OrgansToggle.IsChecked = true;
        RefreshSegmentationUi();
        await RefreshAllAsync();
    });

    private void ApplyTechnicalDetails()
    {
        foreach (var (plane, view) in _views)
        {
            var p = plane;
            view.DescribePoint = Technical ? (c, r) => Describe(p, c, r) : null;
        }
    }

    private async Task OpenSeriesAsync(string? seriesId)
    {
        bool has = seriesId is not null && _services.Daemon.LoadedSeries.Any(s => s.Id == seriesId);
        NoSeriesPanel.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        ViewportGrid.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        FindOrgansButton.IsEnabled = has && _segmentCts is null;
        if (!has)
        {
            _seriesId = null;
            SeriesTitle.Text = "Viewer";
            SeriesSubtitle.Text = "No scan open";
            RefreshSegmentationUi();
            return;
        }
        if (seriesId == _seriesId) return;

        _seriesId = seriesId;
        _geometry = _services.Daemon.GetGeometry(seriesId!);
        _hiddenStructures.Clear();
        var metadata = _services.Daemon.LoadedSeries.First(s => s.Id == seriesId).Metadata;
        _defaultWindow = metadata.DefaultWindow;
        _isCt = metadata.Modality == "CT";
        // HU-based 3D styles only make sense for CT; other modalities start in MIP with their own window.
        RenderBox.SelectedIndex = _renderMode == NiiVueMode ? NiiVueMode : _isCt ? Math.Min(_renderMode, 1) : 2;
        UpdateSubtitle();
        ResetView();
        RefreshMarkup();
        RefreshSegmentationUi();
        await RefreshAllAsync();
    }

    private void UpdateSubtitle()
    {
        var info = _services.Daemon.LoadedSeries.FirstOrDefault(s => s.Id == _seriesId);
        if (info is null) return;
        var m = info.Metadata;
        SeriesTitle.Text = string.IsNullOrWhiteSpace(m.SeriesDescription) ? m.StudyDescription : m.SeriesDescription;
        string date = m.StudyDate is DateTime d ? $" · {d:dd MMM yyyy}" : string.Empty;
        SeriesSubtitle.Text = Technical
            ? $"{m.PatientDisplayName} · {m.PatientId} · {m.Modality} · {info.Dimensions} · {info.SpacingText}"
            : $"{m.PatientDisplayName} · {m.PatientId} · {m.Modality}{date}";
    }

    private void ResetView()
    {
        if (_geometry is null) return;
        foreach (var plane in _views.Keys) _index[plane] = MprSlicer.MiddleSlice(_geometry, plane);
        _crosshair = null;
        _yaw = 25;
        _pitch = 12;
        _zoom = 1.35;
        SetWindow(_defaultWindow ?? WindowLevel.Lung, fromPreset: true);
    }

    private async Task RefreshAllAsync()
    {
        await Task.WhenAll(_views.Keys.Select(RefreshPlaneAsync));
        Request3D();
    }

    private async Task RefreshPlaneAsync(SlicePlane plane)
    {
        if (_seriesId is null || _geometry is null) return;
        int version = _requestVersion[plane] = _requestVersion.GetValueOrDefault(plane) + 1;
        var slice = await _services.Daemon.GetSliceAsync(_seriesId, plane, _index[plane]);
        if (_requestVersion[plane] != version) return; // a newer request superseded this one

        var segmentation = OrgansVisible ? _services.SegmentationFor(_seriesId) : null;
        byte[]? labels = segmentation?.Labels is { } all ? MprSlicer.ExtractLabels(_geometry, all, plane, slice.Index) : null;
        _views[plane].Show(slice, _window, MprSlicer.SliceCount(_geometry, plane), Technical, labels,
            label => _hiddenStructures.Contains(label) ? null : segmentation?.ColourOf(label));
        RefreshOverlay(plane);
    }

    private void Scroll(SlicePlane plane, int delta)
    {
        if (_geometry is null) return;
        _index[plane] = Math.Clamp(_index[plane] + delta, 0, MprSlicer.SliceCount(_geometry, plane) - 1);
        _ = RefreshPlaneAsync(plane);
        PublishViewport(plane);
    }

    private void SetWindow(WindowLevel window, bool fromPreset)
    {
        _window = window;
        _suppressPresetEvent = true;
        PresetBox.SelectedIndex = fromPreset ? WindowLevel.Presets.Select(p => p.Value).ToList().IndexOf(window) : -1;
        _suppressPresetEvent = false;
        foreach (var plane in _views.Keys) _ = RefreshPlaneAsync(plane);
    }

    private string Describe(SlicePlane plane, double col, double row)
    {
        if (_geometry is null) return string.Empty;
        var p = MprSlicer.SliceToPoint(_geometry, plane, _index[plane], col, row);
        return $"{p.X:0},{p.Y:0},{p.Z:0} mm  {_geometry.SampleMm(p.X, p.Y, p.Z)} HU";
    }

    // ---- Tools -------------------------------------------------------------------------------

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not AppBarToggleButton button) return;
        _tool = Enum.Parse<Tool>((string)button.Tag);
        foreach (var b in new[] { NavigateTool, PinTool, MeasureTool, CalloutTool }) b.IsChecked = b == button;
    }

    private void OnPointer(SlicePlane plane, int phase, double col, double row)
    {
        if (_geometry is null || _seriesId is null) return;
        var p = MprSlicer.SliceToPoint(_geometry, plane, _index[plane], col, row);

        switch (_tool)
        {
            case Tool.Navigate:
                SetCrosshair(p, plane);
                break;

            case Tool.Pin when phase == 0:
                Add(new Annotation
                {
                    SeriesId = _seriesId, Kind = AnnotationKind.Pin, Start = p,
                    Label = $"Pin {_services.Annotations.NextNumber(AnnotationKind.Pin)}", Author = _services.Settings.DisplayName,
                });
                break;

            case Tool.Measure:
                if (phase == 0) _caliperDraft = (plane, p, p);
                else if (_caliperDraft is { } d && d.Plane == plane)
                {
                    _caliperDraft = (plane, d.Start, p);
                    if (phase == 2)
                    {
                        _caliperDraft = null;
                        if (d.Start.DistanceTo(p) >= 1)
                        {
                            Add(new Annotation
                            {
                                SeriesId = _seriesId, Kind = AnnotationKind.Caliper, Start = d.Start, End = p,
                                Label = $"Measurement {_services.Annotations.NextNumber(AnnotationKind.Caliper)}",
                                Author = _services.Settings.DisplayName,
                            });
                        }
                    }
                }
                RefreshOverlay(plane);
                break;

            case Tool.Callout when phase == 0:
                _ = AddCalloutAsync(p);
                break;
        }
    }

    private async Task AddCalloutAsync(Point3 p)
    {
        var box = new TextBox { PlaceholderText = "e.g. Spiculated margin – suspicious", AcceptsReturn = false, MinWidth = 320 };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Add a note",
            Content = box,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(box.Text) || _seriesId is null) return;
        Add(new Annotation
        {
            SeriesId = _seriesId, Kind = AnnotationKind.Callout, Start = p, Label = box.Text.Trim(), Author = _services.Settings.DisplayName,
        });
    }

    private void Add(Annotation annotation)
    {
        _services.Annotations.Add(annotation);
        if (_services.Hub.IsConnected) _ = PublishAsync(() => _services.Hub.PublishAnnotationAsync(annotation));
    }

    private void SetCrosshair(Point3 p, SlicePlane? source)
    {
        if (_geometry is null) return;
        _crosshair = p;
        var indices = MprSlicer.CrosshairIndices(_geometry, p);
        foreach (var plane in _views.Keys)
        {
            if (plane == source)
            {
                RefreshOverlay(plane);
                continue;
            }
            _index[plane] = indices[plane];
            _ = RefreshPlaneAsync(plane);
        }
        PublishViewport(source ?? SlicePlane.Axial);
    }

    // ---- Overlays ----------------------------------------------------------------------------

    private void RefreshOverlay(SlicePlane plane)
    {
        if (_geometry is null || _seriesId is null) return;
        var items = new List<OverlayItem>();
        int current = _index.GetValueOrDefault(plane);

        if (_crosshair is Point3 ch)
        {
            var (c, r, _) = MprSlicer.PointToSlice(_geometry, plane, ch);
            items.Add(new OverlayItem(OverlayKind.Crosshair, c, r));
        }

        foreach (var a in _services.Annotations.ForSeries(_seriesId))
        {
            var (c, r, idx) = MprSlicer.PointToSlice(_geometry, plane, a.Start);
            bool near = Math.Abs(idx - current) <= 1;
            switch (a.Kind)
            {
                case AnnotationKind.Pin when near:
                    items.Add(new OverlayItem(OverlayKind.Pin, c, r, Label: a.DisplayLabel, Remote: a.IsRemote));
                    break;
                case AnnotationKind.Callout when near:
                    items.Add(new OverlayItem(OverlayKind.Callout, c, r, Label: a.DisplayLabel, Remote: a.IsRemote));
                    break;
                case AnnotationKind.Caliper when a.End is Point3 end:
                {
                    var (c2, r2, idx2) = MprSlicer.PointToSlice(_geometry, plane, end);
                    if (near || Math.Abs(idx2 - current) <= 1)
                        items.Add(new OverlayItem(OverlayKind.Line, c, r, c2, r2, $"{a.LengthMm:0.0} mm", a.IsRemote));
                    break;
                }
            }
        }

        if (_caliperDraft is { } d && d.Plane == plane)
        {
            var (c1, r1, _) = MprSlicer.PointToSlice(_geometry, plane, d.Start);
            var (c2, r2, _) = MprSlicer.PointToSlice(_geometry, plane, d.End);
            items.Add(new OverlayItem(OverlayKind.Line, c1, r1, c2, r2, $"{d.Start.DistanceTo(d.End):0.0} mm"));
        }

        _views[plane].SetOverlay(items);
    }

    private void RefreshMarkup()
    {
        _rows.Clear();
        if (_seriesId is not null)
            foreach (var a in _services.Annotations.ForSeries(_seriesId).OrderByDescending(a => a.CreatedAt))
                _rows.Add(new AnnotationRow(a));
        EmptyMarkupText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var plane in _views.Keys) RefreshOverlay(plane);
        Request3D();
    }

    // ---- 3D ----------------------------------------------------------------------------------

    /// <summary>Index of the NiiVue (GPU) entry in the 3D style list.</summary>
    private const int NiiVueMode = 3;

    private bool _niivueBusy, _niivueAgain;
    private string? _niivueKey;

    /// <summary>
    /// Shows the series, its organ overlay and surfaces in NiiVue. The scene is only re-exported when the
    /// series, segmentation or visible structures change; camera changes are handled by NiiVue itself.
    /// </summary>
    private async Task UpdateNiiVueAsync()
    {
        if (_seriesId is null || _geometry is null) return;
        if (_niivueBusy)
        {
            _niivueAgain = true;
            return;
        }

        _niivueBusy = true;
        try
        {
            do
            {
                _niivueAgain = false;
                var segmentation = OrgansVisible ? _services.SegmentationFor(_seriesId) : null;
                string key = $"{_seriesId}|{segmentation?.CompletedAt.UtcTicks}|{string.Join(',', _hiddenStructures.Order())}";
                if (key == _niivueKey && VolumeView.IsNiiVueShown) continue;

                var window = _isCt ? RenderModes[0].Window : _defaultWindow ?? _window;
                var (folder, prefix) = NiiVueHost.NewScene();
                var geometry = _geometry;
                var hidden = _hiddenStructures.ToHashSet();
                var scene = await Task.Run(() => NiiVueSceneWriter.Write(folder, geometry, window, segmentation, hidden));
                string? error = await VolumeView.ShowNiiVueAsync(scene, prefix, Technical);
                if (error is null)
                {
                    _niivueKey = key;
                    NiiVueHost.DeleteScenes(keep: folder);
                }
                else
                {
                    _niivueKey = null;
                    SyncText.Text = Technical ? $"NiiVue could not show the scan: {error}" : "The detailed 3D view could not be shown on this computer.";
                }
            }
            while (_niivueAgain);
        }
        catch (Exception ex)
        {
            SyncText.Text = Technical ? $"NiiVue could not show the scan: {ex.Message}" : "The detailed 3D view could not be shown on this computer.";
        }
        finally
        {
            _niivueBusy = false;
        }
    }

    private async void Request3D()
    {
        if (_seriesId is null || _geometry is null) return;
        if (_renderMode == NiiVueMode)
        {
            await UpdateNiiVueAsync();
            return;
        }
        if (VolumeView.IsNiiVueShown)
        {
            VolumeView.HideNiiVue();
            _niivueKey = null;
        }
        if (_rendering)
        {
            _renderAgain = true;
            return;
        }

        _rendering = true;
        try
        {
            do
            {
                _renderAgain = false;
                var (name, window, mode) = RenderModes[_renderMode];
                if (!_isCt && _defaultWindow is WindowLevel own) window = own;
                double scale = XamlRoot?.RasterizationScale ?? 1;
                int max = _fullScreenView == VolumeView ? 720 : 480;
                int size = VolumeView.IsDragging ? 180 : Math.Clamp((int)(VolumeView.DisplaySize * scale * 0.75), 200, max);
                var request = new VolumeRenderRequest(_yaw, _pitch, size, window, mode, _zoom);
                var image = await _services.Daemon.RenderVolumeAsync(_seriesId, request);

                var segmentation = OrgansVisible ? _services.SegmentationFor(_seriesId) : null;
                if (segmentation is not null)
                {
                    var layers = segmentation.Structures
                        .Where(s => s.Mesh is not null && !_hiddenStructures.Contains(s.Label))
                        .Select(s => new MeshLayer(s.Mesh!, s.Colour))
                        .ToList();
                    var geometry = _geometry;
                    await Task.Run(() => MeshRenderer.Composite(geometry, request, layers, image.Bgra));
                }

                var markers = new List<VolumeMarker>();
                foreach (var a in _services.Annotations.ForSeries(_seriesId))
                {
                    var at = a.End is Point3 e && a.Kind == AnnotationKind.Caliper
                        ? new Point3((a.Start.X + e.X) / 2, (a.Start.Y + e.Y) / 2, (a.Start.Z + e.Z) / 2)
                        : a.Start;
                    var (x, y) = VolumeRenderer.Project(_geometry, request, at);
                    markers.Add(new VolumeMarker(x, y, a.DisplayLabel, a.IsRemote));
                }

                string info = Technical ? $"{name} · {image.Elapsed.TotalMilliseconds:0} ms · {size}px · CPU" : string.Empty;
                VolumeView.Show(image, info, markers);
            }
            while (_renderAgain);
        }
        catch (Exception ex)
        {
            SyncText.Text = Technical ? $"3D render failed: {ex.Message}" : "The 3D view could not be drawn.";
        }
        finally
        {
            _rendering = false;
        }
    }

    // ---- Organ segmentation ------------------------------------------------------------------

    private async void FindOrgans_Click(object sender, RoutedEventArgs e)
    {
        if (_seriesId is null || _segmentCts is not null) return;
        var models = SegmentationModelInfo.All;
        var choices = new RadioButtons { Header = "What should be outlined?", SelectedIndex = 0 };
        foreach (var m in models) choices.Items.Add(m.DisplayName);
        var description = new TextBlock
        {
            Text = models[0].Description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };
        choices.SelectionChanged += (_, _) =>
        {
            if (choices.SelectedIndex >= 0) description.Text = models[choices.SelectedIndex].Description;
        };
        var source = new InfoBar
        {
            IsOpen = true,
            IsClosable = false,
            Severity = _services.IsSignedIn ? InfoBarSeverity.Informational : InfoBarSeverity.Warning,
            Title = _services.IsSignedIn ? "HoloRepository server" : "Offline demo",
            Message = _services.IsSignedIn
                ? "The scan is sent to your hospital's HoloRepository server for analysis. Results come back to this computer."
                : "You are not signed in to the HoloRepository server, so a simple offline demo will be used instead of the hospital's models. Sign in under Settings.",
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Find organs",
            Content = new StackPanel { Spacing = 12, MinWidth = 420, Children = { choices, description, source } },
            PrimaryButtonText = "Start",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || choices.SelectedIndex < 0) return;
        await RunSegmentationAsync(models[choices.SelectedIndex], _services.Segmentation);
    }

    private async Task RunSegmentationAsync(SegmentationModelInfo model, ISegmentationService service)
    {
        if (_seriesId is null) return;
        string seriesId = _seriesId;
        var series = _services.GetLoadedSeries(seriesId);
        _segmentCts = new CancellationTokenSource();
        FindOrgansButton.IsEnabled = false;
        OrgansPanel.Visibility = Visibility.Visible;
        SegmentProgressPanel.Visibility = Visibility.Visible;
        SegmentProgressText.Text = "Starting…";
        SegmentNoteText.Text = string.Empty;
        var progress = new Progress<SegmentationProgress>(p => SegmentProgressText.Text = p.Message);

        try
        {
            var result = await service.SegmentAsync(new SegmentationRequest(seriesId, series, model), progress, _segmentCts.Token);
            _services.SetSegmentation(result);
        }
        catch (OperationCanceledException)
        {
            SegmentNoteText.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            SegmentNoteText.Text = $"Could not find organs. {ex.Message}";
        }
        finally
        {
            _segmentCts.Dispose();
            _segmentCts = null;
            SegmentProgressPanel.Visibility = Visibility.Collapsed;
            FindOrgansButton.IsEnabled = _seriesId is not null;
        }
    }

    private void CancelSegmentation_Click(object sender, RoutedEventArgs e) => _segmentCts?.Cancel();

    private void RefreshSegmentationUi()
    {
        var result = _services.SegmentationFor(_seriesId);
        OrgansToggle.IsEnabled = result is not null;
        if (result is null)
        {
            StructureList.ItemsSource = null;
            if (_segmentCts is null)
            {
                OrgansPanel.Visibility = Visibility.Collapsed;
                SegmentNoteText.Text = string.Empty;
            }
            return;
        }

        OrgansPanel.Visibility = Visibility.Visible;
        StructureList.ItemsSource = result.Structures.Select(s => new StructureRow(s, !_hiddenStructures.Contains(s.Label))).ToList();
        string header = $"{result.Model.DisplayName} · {result.Source} · {result.CompletedAt.ToLocalTime():HH:mm}";
        if (Technical) header += $" · model {result.Model.Id}";
        SegmentNoteText.Text = string.IsNullOrEmpty(result.Note) ? header : $"{header}\n{result.Note}";
    }

    private async void StructureCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: byte label } box) return;
        if (box.IsChecked == true) _hiddenStructures.Remove(label);
        else _hiddenStructures.Add(label);
        await RefreshAllAsync();
    }

    private async void OrgansToggle_Click(object sender, RoutedEventArgs e) => await RefreshAllAsync();

    // ---- Full screen -------------------------------------------------------------------------

    private void ToggleFullScreen(FrameworkElement view)
    {
        if (_fullScreenView == view)
        {
            ExitFullScreen();
            return;
        }
        if (_fullScreenView is not null) ExitFullScreen();

        _fullScreenView = view;
        foreach (var child in _homeCells.Keys) child.Visibility = child == view ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetRow(view, 0);
        Grid.SetColumn(view, 0);
        Grid.SetRowSpan(view, 2);
        Grid.SetColumnSpan(view, 2);
        SetFullScreenIcon(view, true);

        HeaderGrid.Visibility = Visibility.Collapsed;
        SidePanel.Visibility = Visibility.Collapsed;
        SideColumn.Width = new GridLength(0);
        BodyGrid.ColumnSpacing = 0;
        RootGrid.Padding = new Thickness(0);
        RootGrid.RowSpacing = 0;

        var window = WindowHelper.WindowFor(this);
        window?.AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        (window as MainWindow)?.SetImmersive(true);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Request3D);
    }

    private void ExitFullScreen()
    {
        if (_fullScreenView is not { } view) return;
        _fullScreenView = null;
        var (row, column) = _homeCells[view];
        Grid.SetRow(view, row);
        Grid.SetColumn(view, column);
        Grid.SetRowSpan(view, 1);
        Grid.SetColumnSpan(view, 1);
        foreach (var child in _homeCells.Keys) child.Visibility = Visibility.Visible;
        SetFullScreenIcon(view, false);

        HeaderGrid.Visibility = Visibility.Visible;
        SidePanel.Visibility = Visibility.Visible;
        SideColumn.Width = new GridLength(300);
        BodyGrid.ColumnSpacing = 12;
        RootGrid.Padding = new Thickness(16, 8, 16, 16);
        RootGrid.RowSpacing = 8;

        var window = WindowHelper.WindowFor(this);
        window?.AppWindow.SetPresenter(AppWindowPresenterKind.Default);
        (window as MainWindow)?.SetImmersive(false);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Request3D);
    }

    private static void SetFullScreenIcon(FrameworkElement view, bool on)
    {
        if (view is SliceViewport s) s.IsFullScreen = on;
        if (view is VolumeViewport v) v.IsFullScreen = on;
    }

    private void Escape_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_fullScreenView is null) return;
        ExitFullScreen();
        args.Handled = true;
    }

    // ---- Collaboration -----------------------------------------------------------------------

    private void PublishViewport(SlicePlane plane)
    {
        if (!_services.Hub.IsConnected || !_services.ShareViewport || _seriesId is null) return;
        if ((DateTime.UtcNow - _lastViewportPublish).TotalMilliseconds < 250) return;
        _lastViewportPublish = DateTime.UtcNow;
        var state = new ViewportState(_seriesId, plane, _index[plane], _window, _yaw, _pitch, _services.Settings.DisplayName);
        _ = PublishAsync(() => _services.Hub.PublishViewportAsync(state));
    }

    private async Task PublishAsync(Func<Task> publish)
    {
        try
        {
            await publish();
        }
        catch (InvalidOperationException)
        {
            // Consultation ended between the action and the publish.
        }
    }

    private void Hub_ViewportReceived(object? sender, ViewportState state) => DispatcherQueue.TryEnqueue(() =>
    {
        if (state.SeriesId != _seriesId || _geometry is null) return;
        _index[state.Plane] = Math.Clamp(state.SliceIndex, 0, MprSlicer.SliceCount(_geometry, state.Plane) - 1);
        _yaw = state.YawDegrees;
        _pitch = state.PitchDegrees;
        SetWindow(state.Window, fromPreset: WindowLevel.Presets.Any(p => p.Value == state.Window));
        Request3D();
    });

    private void Hub_Activity(object? sender, SyncEvent e) => DispatcherQueue.TryEnqueue(UpdateSyncText);

    private void Annotations_Changed(object? sender, AnnotationChangedEventArgs e) => DispatcherQueue.TryEnqueue(RefreshMarkup);

    private void UpdateSyncText()
    {
        var hub = _services.Hub;
        if (Technical)
        {
            SyncText.Text = hub.IsConnected
                ? $"Live in {hub.SessionId} · {hub.Participants.Count} participants · last sync {hub.LastRoundTripMs:0} ms (budget {LoopbackCollaborationHub.LatencyBudgetMs:0} ms)"
                : "Not in a consultation · markup stays on this device";
        }
        else
        {
            SyncText.Text = hub.IsConnected
                ? $"In consultation with {Math.Max(0, hub.Participants.Count - 1)} colleagues"
                : "Not in a consultation";
        }
    }

    // ---- Commands ----------------------------------------------------------------------------

    private void PresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPresetEvent || PresetBox.SelectedIndex < 0) return;
        SetWindow(WindowLevel.Presets[PresetBox.SelectedIndex].Value, fromPreset: true);
    }

    private void RenderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RenderBox.SelectedIndex < 0) return;
        _renderMode = RenderBox.SelectedIndex;
        Request3D();
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        ResetView();
        await RefreshAllAsync();
    }

    private void AnnotationList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AnnotationRow row) SetCrosshair(row.Annotation.Start, null);
    }

    private void DeleteAnnotation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id } && Guid.TryParse(id, out var guid)) _services.Annotations.Remove(guid);
    }

    private void ClearMarkup_Click(object sender, RoutedEventArgs e)
    {
        if (_seriesId is null) return;
        foreach (var a in _services.Annotations.ForSeries(_seriesId)) _services.Annotations.Remove(a.Id);
    }

    private async void LoadSample_Click(object sender, RoutedEventArgs e)
    {
        var info = await _services.Daemon.LoadSampleAsync();
        _services.CurrentSeriesId = info.Id;
        await OpenSeriesAsync(info.Id);
    }

    private void NewWindow_Click(object sender, RoutedEventArgs e)
    {
        var frame = new Frame();
        var window = new Window
        {
            Title = "HoloRepository · Viewer",
            Content = frame,
            SystemBackdrop = new MicaBackdrop(),
        };
        frame.Navigate(typeof(ViewerPage), "secondary");
        WindowHelper.SizeAndCentre(window, 1280, 860);
        window.Closed += (_, _) => (frame.Content as ViewerPage)?.Detach();
        WindowHelper.Track(window);
        window.Activate();
    }
}
