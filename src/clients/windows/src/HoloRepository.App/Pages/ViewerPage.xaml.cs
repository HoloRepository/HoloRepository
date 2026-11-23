using System.Collections.ObjectModel;
using HoloRepository.App.Controls;
using HoloRepository.App.Services;
using HoloRepository.Core.Imaging;
using HoloRepository.Core.Markup;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    public string Detail => $"{Annotation.Kind} · {Annotation.Author} · {Annotation.CreatedAt.ToLocalTime():HH:mm}";
    public SolidColorBrush Brush => Annotation.IsRemote ? RemoteBrush : LocalBrush;
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

    public ViewerPage()
    {
        InitializeComponent();
        _views = new()
        {
            [SlicePlane.Axial] = AxialView,
            [SlicePlane.Coronal] = CoronalView,
            [SlicePlane.Sagittal] = SagittalView,
        };

        foreach (var (plane, view) in _views)
        {
            var p = plane;
            view.SliceScrolled += (_, delta) => Scroll(p, delta);
            view.WindowDragged += (_, d) => SetWindow(_window.Adjust(d.DeltaWidth, d.DeltaCentre), fromPreset: false);
            view.ImagePressed += (_, e) => OnPointer(p, 0, e.Col, e.Row);
            view.ImageDragged += (_, e) => OnPointer(p, 1, e.Col, e.Row);
            view.ImageReleased += (_, e) => OnPointer(p, 2, e.Col, e.Row);
            view.DescribePoint = (c, r) => Describe(p, c, r);
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

        foreach (var (name, _) in WindowLevel.Presets) PresetBox.Items.Add(name);
        _suppressPresetEvent = true;
        PresetBox.SelectedIndex = 0;
        RenderBox.SelectedIndex = 0;
        _suppressPresetEvent = false;

        AnnotationList.ItemsSource = _rows;
        Loaded += OnLoaded;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is "secondary") NewWindowButton.Visibility = Visibility.Collapsed;
        await OpenSeriesAsync(_services.CurrentSeriesId);
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
    }

    private void Services_CurrentSeriesChanged(object? sender, EventArgs e) =>
        DispatcherQueue.TryEnqueue(async () => await OpenSeriesAsync(_services.CurrentSeriesId));

    private async Task OpenSeriesAsync(string? seriesId)
    {
        bool has = seriesId is not null && _services.Daemon.LoadedSeries.Any(s => s.Id == seriesId);
        NoSeriesPanel.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        ViewportGrid.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        if (!has)
        {
            _seriesId = null;
            SeriesTitle.Text = "Viewer";
            SeriesSubtitle.Text = "No series open";
            return;
        }
        if (seriesId == _seriesId) return;

        _seriesId = seriesId;
        _geometry = _services.Daemon.GetGeometry(seriesId!);
        var info = _services.CurrentSeries ?? _services.Daemon.LoadedSeries.First(s => s.Id == seriesId);
        SeriesTitle.Text = info.Metadata.SeriesDescription;
        SeriesSubtitle.Text = $"{info.Metadata.PatientDisplayName} ·{info.Metadata.PatientId} · {info.Metadata.Modality} · {info.Dimensions} · {info.SpacingText}";
        ResetView();
        RefreshMarkup();
        await RefreshAllAsync();
    }

    private void ResetView()
    {
        if (_geometry is null) return;
        foreach (var plane in _views.Keys) _index[plane] = MprSlicer.MiddleSlice(_geometry, plane);
        _crosshair = null;
        _yaw = 25;
        _pitch = 12;
        _zoom = 1.35;
        SetWindow(WindowLevel.Lung, fromPreset: true);
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
        _views[plane].Show(slice, _window, MprSlicer.SliceCount(_geometry, plane));
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
        if (!fromPreset)
        {
            _suppressPresetEvent = true;
            PresetBox.SelectedIndex = -1;
            _suppressPresetEvent = false;
        }
        else
        {
            int i = WindowLevel.Presets.Select(p => p.Value).ToList().IndexOf(window);
            _suppressPresetEvent = true;
            PresetBox.SelectedIndex = i;
            _suppressPresetEvent = false;
        }
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
            Title = "Add callout",
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

    private async void Request3D()
    {
        if (_seriesId is null || _geometry is null) return;
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
                double scale = XamlRoot?.RasterizationScale ?? 1;
                int size = VolumeView.IsDragging ? 180 : Math.Clamp((int)(VolumeView.DisplaySize * scale * 0.75), 200, 480);
                var request = new VolumeRenderRequest(_yaw, _pitch, size, window, mode, _zoom);
                var image = await _services.Daemon.RenderVolumeAsync(_seriesId, request);

                var markers = new List<VolumeMarker>();
                foreach (var a in _services.Annotations.ForSeries(_seriesId))
                {
                    var at = a.End is Point3 e && a.Kind == AnnotationKind.Caliper
                        ? new Point3((a.Start.X + e.X) / 2, (a.Start.Y + e.Y) / 2, (a.Start.Z + e.Z) / 2)
                        : a.Start;
                    var (x, y) = VolumeRenderer.Project(_geometry, request, at);
                    markers.Add(new VolumeMarker(x, y, a.DisplayLabel, a.IsRemote));
                }

                VolumeView.Show(image, $"{name} · {image.Elapsed.TotalMilliseconds:0} ms · {size}px · CPU", markers);
            }
            while (_renderAgain);
        }
        catch (Exception ex)
        {
            SyncText.Text = $"3D render failed: {ex.Message}";
        }
        finally
        {
            _rendering = false;
        }
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
        SyncText.Text = hub.IsConnected
            ? $"Live in {hub.SessionId} · {hub.Participants.Count} participants · last sync {hub.LastRoundTripMs:0} ms (budget {LoopbackCollaborationHub.LatencyBudgetMs:0} ms)"
            : "Not in a consultation · markup stays on this device";
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
