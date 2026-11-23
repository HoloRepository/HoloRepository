using HoloRepository.App.Services;
using HoloRepository.Core.Imaging;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace HoloRepository.App.Controls;

public enum OverlayKind
{
    Pin,
    Line,
    Callout,
    Crosshair,
}

/// <summary>Something to draw over a slice, in slice pixel coordinates.</summary>
public sealed record OverlayItem(OverlayKind Kind, double Col, double Row, double Col2 = 0, double Row2 = 0, string Label = "", bool Remote = false);

public sealed class SlicePointerEventArgs(double col, double row, bool isRightButton) : EventArgs
{
    public double Col { get; } = col;
    public double Row { get; } = row;
    public bool IsRightButton { get; } = isRightButton;
}

/// <summary>
/// Displays one MPR plane. Mouse wheel scrolls slices, right-drag adjusts window/level, left button
/// events are raised in slice pixel coordinates so the page can apply the active tool.
/// </summary>
public sealed partial class SliceViewport : UserControl
{
    private static readonly Color LocalColour = Color.FromArgb(255, 79, 209, 217);
    private static readonly Color RemoteColour = Color.FromArgb(255, 255, 170, 60);

    private WriteableBitmap? _bitmap;
    private byte[] _buffer = Array.Empty<byte>();
    private SliceImage? _slice;
    private IReadOnlyList<OverlayItem> _overlay = Array.Empty<OverlayItem>();
    private Windows.Foundation.Point? _rightDragOrigin;
    private bool _leftDown;

    public SliceViewport()
    {
        InitializeComponent();
    }

    public event EventHandler<int>? SliceScrolled;
    public event EventHandler<(double DeltaWidth, double DeltaCentre)>? WindowDragged;
    public event EventHandler<SlicePointerEventArgs>? ImagePressed;
    public event EventHandler<SlicePointerEventArgs>? ImageDragged;
    public event EventHandler<SlicePointerEventArgs>? ImageReleased;

    public string Title
    {
        get => TitleText.Text;
        set => TitleText.Text = value;
    }

    public SliceImage? Slice => _slice;

    /// <summary>Optional function that formats a value readout for a hovered pixel.</summary>
    public Func<double, double, string>? DescribePoint { get; set; }

    public void Show(SliceImage slice, WindowLevel window, int sliceCount)
    {
        _slice = slice;
        int needed = slice.Width * slice.Height * 4;
        if (_buffer.Length != needed) _buffer = new byte[needed];
        window.ApplyToBgra(slice.Pixels, _buffer);
        _bitmap = BitmapHelper.Write(slice.Width, slice.Height, _buffer, _bitmap);
        SliceImage.Source = _bitmap;
        EmptyText.Visibility = Visibility.Collapsed;
        InfoText.Text = $"{slice.Index + 1}/{sliceCount}  {window.Describe()}";
        Layout();
    }

    public void Clear()
    {
        _slice = null;
        SliceImage.Source = null;
        Overlay.Children.Clear();
        InfoText.Text = string.Empty;
        CursorText.Text = string.Empty;
        CursorBadge.Visibility = Visibility.Collapsed;
        EmptyText.Visibility = Visibility.Visible;
    }

    public void SetOverlay(IReadOnlyList<OverlayItem> items)
    {
        _overlay = items;
        DrawOverlay();
    }

    private (double Scale, double Width, double Height) DisplayRect()
    {
        if (_slice is null || Surface.ActualWidth <= 0 || Surface.ActualHeight <= 0) return (0, 0, 0);
        double scale = Math.Min(Surface.ActualWidth / _slice.PhysicalWidth, Surface.ActualHeight / _slice.PhysicalHeight);
        return (scale, _slice.PhysicalWidth * scale, _slice.PhysicalHeight * scale);
    }

    private void Layout()
    {
        var (_, w, h) = DisplayRect();
        if (w <= 0) return;
        var margin = new Thickness((Surface.ActualWidth - w) / 2, (Surface.ActualHeight - h) / 2, 0, 0);
        SliceImage.Width = w;
        SliceImage.Height = h;
        SliceImage.Margin = margin;
        Overlay.Width = w;
        Overlay.Height = h;
        Overlay.Margin = margin;
        DrawOverlay();
    }

    private void DrawOverlay()
    {
        Overlay.Children.Clear();
        if (_slice is null) return;
        var (_, w, h) = DisplayRect();
        if (w <= 0) return;
        double sx = w / _slice.Width, sy = h / _slice.Height;

        foreach (var item in _overlay)
        {
            var colour = new SolidColorBrush(item.Remote ? RemoteColour : LocalColour);
            double x = item.Col * sx, y = item.Row * sy;
            switch (item.Kind)
            {
                case OverlayKind.Crosshair:
                {
                    var faint = new SolidColorBrush(Color.FromArgb(110, 255, 214, 0));
                    Overlay.Children.Add(new Line { X1 = x, X2 = x, Y1 = 0, Y2 = h, Stroke = faint, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 4 } });
                    Overlay.Children.Add(new Line { X1 = 0, X2 = w, Y1 = y, Y2 = y, Stroke = faint, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 4 } });
                    break;
                }
                case OverlayKind.Pin:
                {
                    var dot = new Ellipse { Width = 12, Height = 12, Fill = colour, Stroke = new SolidColorBrush(Colors.White), StrokeThickness = 2 };
                    Canvas.SetLeft(dot, x - 6);
                    Canvas.SetTop(dot, y - 6);
                    Overlay.Children.Add(dot);
                    AddLabel(item.Label, x + 9, y - 10, colour);
                    break;
                }
                case OverlayKind.Line:
                {
                    double x2 = item.Col2 * sx, y2 = item.Row2 * sy;
                    Overlay.Children.Add(new Line { X1 = x, Y1 = y, X2 = x2, Y2 = y2, Stroke = colour, StrokeThickness = 2 });
                    foreach (var (px, py) in new[] { (x, y), (x2, y2) })
                    {
                        var end = new Ellipse { Width = 7, Height = 7, Fill = colour };
                        Canvas.SetLeft(end, px - 3.5);
                        Canvas.SetTop(end, py - 3.5);
                        Overlay.Children.Add(end);
                    }
                    AddLabel(item.Label, (x + x2) / 2 + 6, (y + y2) / 2 - 18, colour);
                    break;
                }
                case OverlayKind.Callout:
                {
                    var marker = new Rectangle { Width = 10, Height = 10, Fill = colour, RadiusX = 2, RadiusY = 2 };
                    Canvas.SetLeft(marker, x - 5);
                    Canvas.SetTop(marker, y - 5);
                    Overlay.Children.Add(marker);
                    Overlay.Children.Add(new Line { X1 = x, Y1 = y, X2 = x + 24, Y2 = y - 24, Stroke = colour, StrokeThickness = 1.5 });
                    var box = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(210, 10, 20, 30)),
                        BorderBrush = colour,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(6, 2, 6, 2),
                        Child = new TextBlock { Text = item.Label, Foreground = new SolidColorBrush(Colors.White), FontSize = 12, MaxWidth = 220, TextWrapping = TextWrapping.Wrap },
                    };
                    Canvas.SetLeft(box, x + 24);
                    Canvas.SetTop(box, y - 44);
                    Overlay.Children.Add(box);
                    break;
                }
            }
        }
    }

    private void AddLabel(string text, double x, double y, Brush brush)
    {
        if (string.IsNullOrEmpty(text)) return;
        var label = new TextBlock { Text = text, Foreground = brush, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        Canvas.SetLeft(label, x);
        Canvas.SetTop(label, y);
        Overlay.Children.Add(label);
    }

    private bool TryToSlice(PointerRoutedEventArgs e, out double col, out double row)
    {
        col = row = 0;
        if (_slice is null) return false;
        var (_, w, h) = DisplayRect();
        if (w <= 0) return false;
        var p = e.GetCurrentPoint(SliceImage).Position;
        col = p.X / w * _slice.Width;
        row = p.Y / h * _slice.Height;
        return col >= 0 && row >= 0 && col < _slice.Width && row < _slice.Height;
    }

    private void Surface_SizeChanged(object sender, SizeChangedEventArgs e) => Layout();

    private void Surface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Surface);
        Surface.CapturePointer(e.Pointer);
        if (point.Properties.IsRightButtonPressed)
        {
            _rightDragOrigin = point.Position;
        }
        else if (point.Properties.IsLeftButtonPressed && TryToSlice(e, out double c, out double r))
        {
            _leftDown = true;
            ImagePressed?.Invoke(this, new SlicePointerEventArgs(c, r, false));
        }
        e.Handled = true;
    }

    private void Surface_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Surface);
        bool inside = TryToSlice(e, out double c, out double r);
        if (inside && DescribePoint is not null)
        {
            CursorText.Text = DescribePoint(c, r);
            CursorBadge.Visibility = Visibility.Visible;
        }

        if (_rightDragOrigin is Windows.Foundation.Point origin)
        {
            double dx = point.Position.X - origin.X, dy = point.Position.Y - origin.Y;
            _rightDragOrigin = point.Position;
            WindowDragged?.Invoke(this, (dx * 4, -dy * 4));
        }
        else if (_leftDown && _slice is not null)
        {
            ImageDragged?.Invoke(this, new SlicePointerEventArgs(Math.Clamp(c, 0, _slice.Width - 1), Math.Clamp(r, 0, _slice.Height - 1), false));
        }
    }

    private void Surface_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_leftDown && _slice is not null)
        {
            TryToSlice(e, out double c, out double r);
            ImageReleased?.Invoke(this, new SlicePointerEventArgs(Math.Clamp(c, 0, _slice.Width - 1), Math.Clamp(r, 0, _slice.Height - 1), false));
        }
        EndDrag();
        Surface.ReleasePointerCapture(e.Pointer);
    }

    private void Surface_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndDrag();

    private void EndDrag()
    {
        _leftDown = false;
        _rightDragOrigin = null;
    }

    private void Surface_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        int delta = e.GetCurrentPoint(Surface).Properties.MouseWheelDelta;
        SliceScrolled?.Invoke(this, delta > 0 ? 1 : -1);
        e.Handled = true;
    }
}
