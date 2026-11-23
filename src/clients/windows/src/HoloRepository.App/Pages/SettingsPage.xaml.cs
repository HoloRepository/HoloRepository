using HoloRepository.App.Services;
using HoloRepository.Core.Daemon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HoloRepository.App.Pages;

public sealed partial class SettingsPage : Page
{
    private static readonly string[] Themes = ["Dark", "Light", "Default"];
    private readonly AppServices _services = AppServices.Current;
    private bool _loading = true;

    public SettingsPage()
    {
        InitializeComponent();
        var s = _services.Settings;

        ThemeButtons.SelectedIndex = Math.Max(0, Array.IndexOf(Themes, s.Theme));
        foreach (var b in Enum.GetValues<ComputeBackend>()) BackendBox.Items.Add(b.DisplayName());
        BackendBox.SelectedIndex = (int)s.Backend;
        EndpointBox.Text = s.DaemonEndpoint;
        DisplayNameBox.Text = s.DisplayName;

        var version = typeof(App).Assembly.GetName().Version;
        VersionText.Text = $"HoloRepository for Windows {version?.ToString(3)} · .NET {Environment.Version} · {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}";
        _loading = false;
    }

    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeButtons.SelectedIndex < 0) return;
        _services.Settings.Theme = Themes[ThemeButtons.SelectedIndex];
        _services.Settings.Save();
        WindowHelper.ApplyThemeEverywhere(_services.Settings.Theme);
    }

    private void Backend_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || BackendBox.SelectedIndex < 0) return;
        _services.Settings.Backend = (ComputeBackend)BackendBox.SelectedIndex;
        _services.Daemon.PreferredBackend = _services.Settings.Backend;
        _services.Settings.Save();
    }

    private void Endpoint_LostFocus(object sender, RoutedEventArgs e)
    {
        _services.Settings.DaemonEndpoint = EndpointBox.Text.Trim();
        _services.Settings.Save();
    }

    private void DisplayName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DisplayNameBox.Text)) return;
        _services.Settings.DisplayName = DisplayNameBox.Text.Trim();
        _services.Settings.Save();
    }
}
