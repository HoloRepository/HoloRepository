using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace HoloRepository.App.Services;

/// <summary>Tracks open windows so theme changes apply everywhere, and helps with pickers in desktop apps.</summary>
public static class WindowHelper
{
    private static readonly List<Window> OpenWindows = new();

    public static MainWindow? Main { get; set; }

    public static IReadOnlyList<Window> Windows => OpenWindows;

    public static void Track(Window window)
    {
        OpenWindows.Add(window);
        window.Closed += (_, _) => OpenWindows.Remove(window);
        ApplyTheme(window, AppServices.Current.Settings.Theme);
    }

    public static ElementTheme ParseTheme(string theme) => theme switch
    {
        "Light" => ElementTheme.Light,
        "Dark" => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    public static void ApplyThemeEverywhere(string theme)
    {
        foreach (var w in OpenWindows) ApplyTheme(w, theme);
    }

    private static void ApplyTheme(Window window, string theme)
    {
        if (window.Content is FrameworkElement root) root.RequestedTheme = ParseTheme(theme);
    }

    public static nint GetHandle(Window window) => WinRT.Interop.WindowNative.GetWindowHandle(window);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    /// <summary>Sizes a window in effective (DPI-independent) pixels, centred and clamped to the work area.</summary>
    public static void SizeAndCentre(Window window, double width, double height)
    {
        var appWindow = window.AppWindow;
        var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(appWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary).WorkArea;
        double scale = Math.Max(1, GetDpiForWindow(GetHandle(window)) / 96.0);
        int w = (int)Math.Min(width * scale, area.Width * 0.94);
        int h = (int)Math.Min(height * scale, area.Height * 0.94);
        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));
    }

    /// <summary>Pickers need an owner window when the app is a desktop (non-UWP) process.</summary>
    public static T WithOwner<T>(T picker, Window? window = null)
    {
        var owner = window ?? Main ?? throw new InvalidOperationException("No window available.");
        WinRT.Interop.InitializeWithWindow.Initialize(picker!, GetHandle(owner));
        return picker;
    }

    public static Window? WindowFor(UIElement element) =>
        OpenWindows.FirstOrDefault(w => w.Content?.XamlRoot == element.XamlRoot);
}

public static class BitmapHelper
{
    /// <summary>Copies opaque BGRA pixels into a bitmap, reusing it when the size matches.</summary>
    public static WriteableBitmap Write(int width, int height, byte[] bgra, WriteableBitmap? reuse = null)
    {
        var bitmap = reuse is not null && reuse.PixelWidth == width && reuse.PixelHeight == height
            ? reuse
            : new WriteableBitmap(width, height);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Seek(0, SeekOrigin.Begin);
            stream.Write(bgra, 0, Math.Min(bgra.Length, width * height * 4));
        }
        bitmap.Invalidate();
        return bitmap;
    }
}
