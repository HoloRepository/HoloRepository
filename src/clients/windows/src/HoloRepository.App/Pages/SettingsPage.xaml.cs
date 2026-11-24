using System.Net.Http;
using HoloRepository.App.Services;
using HoloRepository.Core.Daemon;
using HoloRepository.Core.Segmentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

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
        ServerBox.Text = s.ServerAddress;
        DisplayNameBox.Text = s.DisplayName;
        UsernameBox.Text = s.ServerUsername;
        TechnicalToggle.IsOn = s.ShowTechnicalDetails;
        TechnicalPanel.Visibility = _services.TechnicalVisibility;

        var version = typeof(App).Assembly.GetName().Version;
        VersionText.Text = $"HoloRepository for Windows {version?.ToString(3)}";
        UpdateAccount();
        _loading = false;
    }

    private void UpdateAccount()
    {
        bool signedIn = _services.IsSignedIn;
        SignInPanel.Visibility = signedIn ? Visibility.Collapsed : Visibility.Visible;
        SignOutButton.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        AccountStatus.Severity = signedIn ? InfoBarSeverity.Success : InfoBarSeverity.Informational;
        AccountStatus.Title = signedIn ? $"Signed in as {_services.SignedInUser}" : "Not signed in";
        AccountStatus.Message = signedIn ? "Find organs will use the HoloRepository server." : "Find organs will use the offline demo.";
    }

    private async Task SignInAsync(bool createAccount)
    {
        if (string.IsNullOrWhiteSpace(UsernameBox.Text) || string.IsNullOrEmpty(PasswordBox.Password))
        {
            ShowAccountError("Enter your username and password.");
            return;
        }

        SignInButton.IsEnabled = CreateAccountButton.IsEnabled = false;
        AccountProgress.IsActive = true;
        try
        {
            await _services.SignInAsync(UsernameBox.Text.Trim(), PasswordBox.Password, createAccount);
            PasswordBox.Password = string.Empty;
            UpdateAccount();
        }
        catch (HoloRepositoryApiException ex)
        {
            ShowAccountError(ex.Message);
        }
        catch (HttpRequestException)
        {
            ShowAccountError("Could not reach the HoloRepository server. Check that it is running, or ask IT for the address.");
        }
        catch (TaskCanceledException)
        {
            ShowAccountError("The HoloRepository server did not respond in time.");
        }
        catch (InvalidOperationException ex)
        {
            ShowAccountError(ex.Message);
        }
        finally
        {
            SignInButton.IsEnabled = CreateAccountButton.IsEnabled = true;
            AccountProgress.IsActive = false;
        }
    }

    private void ShowAccountError(string message)
    {
        AccountStatus.Severity = InfoBarSeverity.Error;
        AccountStatus.Title = "Could not sign in";
        AccountStatus.Message = message;
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e) => await SignInAsync(createAccount: false);

    private async void CreateAccount_Click(object sender, RoutedEventArgs e) => await SignInAsync(createAccount: true);

    private async void PasswordBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        await SignInAsync(createAccount: false);
    }

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        _services.SignOut();
        UpdateAccount();
    }

    private void TechnicalToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _services.ShowTechnicalDetails = TechnicalToggle.IsOn;
        TechnicalPanel.Visibility = _services.TechnicalVisibility;
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

    private void Server_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ServerBox.Text)) return;
        _services.Settings.ServerAddress = ServerBox.Text.Trim();
        _services.Settings.Save();
    }

    private void DisplayName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DisplayNameBox.Text)) return;
        _services.Settings.DisplayName = DisplayNameBox.Text.Trim();
        _services.Settings.Save();
    }
}
