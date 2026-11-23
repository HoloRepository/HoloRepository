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

    public VolumeViewport()
    {
        InitializeComponent();
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

    public void Show(RenderedImage image, string info, IReadOnlyList<VolumeMarker> markers)
    {
        _image = image;
        _markers = markers;
        _bitmap = BitmapHelper.Write(image.Width, image.Height, image.Bgra, _bitmap);
        RenderImage.Source = _bitmap;
        InfoText.Text = info;
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
