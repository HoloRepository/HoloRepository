using HoloRepository.App.Services;
using HoloRepository.Core.Imaging;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace HoloRepository.App.Controls;

/// <summary>A projected marker in rendered-image pixel coordinates.</summary>
public sealed record VolumeMarker(double X, double Y, string Label, bool Remote);

/// <summary>Shows the 3D render; left-drag rotates, wheel zooms. Rendering itself is done by the daemon.</summary>
public sealed partial class VolumeViewport : UserControl
{
    private WriteableBitmap? _bitmap;
    private RenderedImage? _image;
    private IReadOnlyList<VolumeMarker> _markers = Array.Empty<VolumeMarker>();
    private Windows.Foundation.Point? _dragOrigin;

    private TaskCompletionSource<bool>? _niivueReady;
    private TaskCompletionSource<string>? _niivueLoad;

    public VolumeViewport()
    {
        InitializeComponent();
    }

    // ---- NiiVue (GPU) view ----------------------------------------------------------------------

    public bool IsNiiVueShown => NiiVueWeb.Visibility == Visibility.Visible;

    /// <summary>
    /// Shows the scene in NiiVue (WebGL 2 in WebView2) instead of the CPU render.
    /// Returns null on success, or a message describing why the view could not be shown.
    /// </summary>
    public async Task<string?> ShowNiiVueAsync(NiiVueSceneDescription scene, string urlPrefix, bool allowDevTools)
    {
        NiiVueWeb.Visibility = Visibility.Visible;
        RenderImage.Visibility = Overlay.Visibility = InfoBadge.Visibility = EmptyText.Visibility = Visibility.Collapsed;
        NiiVueStatus.Visibility = Visibility.Visible;
        NiiVueStatusText.Text = "Preparing 3D view…";
        try
        {
            await EnsureNiiVueAsync(allowDevTools);
            _niivueLoad = new TaskCompletionSource<string>();
            string prefix = System.Text.Json.JsonSerializer.Serialize(urlPrefix);
            await NiiVueWeb.ExecuteScriptAsync($"holo.load({scene.ToJson()}, {prefix})");
            string result = await _niivueLoad.Task.WaitAsync(TimeSpan.FromMinutes(2));
            return result == "loaded" ? null : result;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            NiiVueStatus.Visibility = Visibility.Collapsed;
        }
    }

    public async Task SetNiiVueWindowAsync(double min, double max)
    {
        if (!IsNiiVueShown || _niivueReady?.Task.IsCompletedSuccessfully != true) return;
        await NiiVueWeb.ExecuteScriptAsync(FormattableString.Invariant($"holo.setWindow({min}, {max})"));
    }

    /// <summary>Returns to the CPU-rendered 3D view.</summary>
    public void HideNiiVue()
    {
        NiiVueWeb.Visibility = NiiVueStatus.Visibility = Visibility.Collapsed;
        RenderImage.Visibility = Overlay.Visibility = Visibility.Visible;
        if (_image is null) EmptyText.Visibility = Visibility.Visible;
    }

    private async Task EnsureNiiVueAsync(bool allowDevTools)
    {
        if (_niivueReady is not null)
        {
            await _niivueReady.Task;
            return;
        }

        _niivueReady = new TaskCompletionSource<bool>();
        await Task.Run(NiiVueHost.PrepareAssets);
        var environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateWithOptionsAsync(
            null, NiiVueHost.UserDataFolder, new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions());
        await NiiVueWeb.EnsureCoreWebView2Async(environment);

        var core = NiiVueWeb.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping(NiiVueHost.HostName, NiiVueHost.Root,
            Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
        core.Settings.AreDevToolsEnabled = allowDevTools;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        // Stay on the bundled page: never navigate anywhere else.
        core.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith($"https://{NiiVueHost.HostName}/", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
        };
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.WebMessageReceived += (_, e) =>
        {
            string message = e.TryGetWebMessageAsString();
            if (message == "ready") _niivueReady.TrySetResult(true);
            else if (message == "loaded" || message.StartsWith("error:", StringComparison.Ordinal))
            {
                if (!_niivueReady.Task.IsCompleted) _niivueReady.TrySetException(new InvalidOperationException(message[6..]));
                _niivueLoad?.TrySetResult(message.StartsWith("error:", StringComparison.Ordinal) ? message[6..] : message);
            }
        };
        NiiVueWeb.Source = NiiVueHost.PageUri;
        await _niivueReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>Raised while dragging with (delta yaw, delta pitch) in degrees.</summary>
    public event EventHandler<(double Yaw, double Pitch)>? Rotated;

    public event EventHandler? RotationFinished;

    public event EventHandler<double>? Zoomed;

    public string Title
    {
        get => TitleText.Text;
        set => TitleText.Text = value;
    }

    public bool IsDragging => _dragOrigin is not null;

    /// <summary>Edge length of the square drawing area, used to pick a render resolution.</summary>
    public double DisplaySize => Math.Max(1, Math.Min(Surface.ActualWidth, Surface.ActualHeight));

    public event EventHandler? FullScreenRequested;

    public bool IsFullScreen
    {
        get => FullScreenIcon.Glyph == "";
        set
        {
            FullScreenIcon.Glyph = value ? "" : "";
            ToolTipService.SetToolTip(FullScreenButton, value ? "Exit full screen (Esc)" : "Full screen");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(FullScreenButton, value ? "Exit full screen" : "Full screen");
        }
    }

    private void FullScreen_Click(object sender, RoutedEventArgs e) => FullScreenRequested?.Invoke(this, EventArgs.Empty);

    private void Surface_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => FullScreenRequested?.Invoke(this, EventArgs.Empty);

    public void Show(RenderedImage image, string info, IReadOnlyList<VolumeMarker> markers)
    {
        _image = image;
        _markers = markers;
        _bitmap = BitmapHelper.Write(image.Width, image.Height, image.Bgra, _bitmap);
        RenderImage.Source = _bitmap;
        InfoText.Text = info;
        InfoBadge.Visibility = string.IsNullOrEmpty(info) ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Visibility = Visibility.Collapsed;
        Layout();
    }

    public void Clear()
    {
        _image = null;
        RenderImage.Source = null;
        Overlay.Children.Clear();
        InfoText.Text = string.Empty;
        EmptyText.Visibility = Visibility.Visible;
    }

    private void Layout()
    {
        double size = DisplaySize;
        RenderImage.Width = RenderImage.Height = size;
        Overlay.Width = Overlay.Height = size;
        DrawMarkers(size);
    }

    private void DrawMarkers(double size)
    {
        Overlay.Children.Clear();
        if (_image is null) return;
        double scale = size / _image.Width;
        foreach (var m in _markers)
        {
            var brush = new SolidColorBrush(m.Remote ? Color.FromArgb(255, 255, 170, 60) : Color.FromArgb(255, 79, 209, 217));
            var dot = new Ellipse { Width = 12, Height = 12, Fill = brush, Stroke = new SolidColorBrush(Colors.White), StrokeThickness = 2 };
            Canvas.SetLeft(dot, m.X * scale - 6);
            Canvas.SetTop(dot, m.Y * scale - 6);
            Overlay.Children.Add(dot);
            var label = new TextBlock { Text = m.Label, Foreground = brush, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            Canvas.SetLeft(label, m.X * scale + 9);
            Canvas.SetTop(label, m.Y * scale - 10);
            Overlay.Children.Add(label);
        }
    }

    private void Surface_SizeChanged(object sender, SizeChangedEventArgs e) => Layout();

    private void Surface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _dragOrigin = e.GetCurrentPoint(Surface).Position;
        Surface.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Surface_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragOrigin is not Windows.Foundation.Point origin) return;
        var p = e.GetCurrentPoint(Surface).Position;
        _dragOrigin = p;
        Rotated?.Invoke(this, ((p.X - origin.X) * 0.5, (p.Y - origin.Y) * 0.5));
    }

    private void Surface_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        Surface.ReleasePointerCapture(e.Pointer);
        FinishDrag();
    }

    private void Surface_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => FinishDrag();

    private void FinishDrag()
    {
        if (_dragOrigin is null) return;
        _dragOrigin = null;
        RotationFinished?.Invoke(this, EventArgs.Empty);
    }

    private void Surface_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        int delta = e.GetCurrentPoint(Surface).Properties.MouseWheelDelta;
        Zoomed?.Invoke(this, delta > 0 ? 1.1 : 1 / 1.1);
        e.Handled = true;
    }
}
