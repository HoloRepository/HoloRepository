using HoloRepository.App.Services;
using Microsoft.UI.Xaml;

namespace HoloRepository.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HoloRepository", "logs", "app.log");

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        WindowHelper.Main = _window;
        WindowHelper.Track(_window);
        _window.Activate();
    }

    /// <summary>Logs unexpected errors locally and keeps the session alive where possible.</summary>
    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} {e.Exception}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        e.Handled = true;
    }
}
