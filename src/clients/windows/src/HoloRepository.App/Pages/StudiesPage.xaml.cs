using HoloRepository.App.Services;
using HoloRepository.Core.Daemon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;

namespace HoloRepository.App.Pages;

public sealed partial class StudiesPage : Page
{
    private readonly AppServices _services = AppServices.Current;

    public StudiesPage()
    {
        InitializeComponent();
    }

    public static string GlyphFor(bool detected) => detected ? "" : "";

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyTechnicalDetails();
        RefreshList();
        await RefreshStatusAsync();
    }

    /// <summary>Engineering details (compute daemon, DICOM export) are hidden unless enabled in Settings.</summary>
    private void ApplyTechnicalDetails()
    {
        bool technical = _services.ShowTechnicalDetails;
        DaemonCard.Visibility = technical ? Visibility.Visible : Visibility.Collapsed;
        DaemonColumn.Width = technical ? new GridLength(360) : new GridLength(0);
        ContentGrid.ColumnSpacing = technical ? 16 : 0;
        ExportButton.Visibility = technical ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshList()
    {
        var items = _services.Daemon.LoadedSeries;
        SeriesList.ItemsSource = items;
        EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var current = items.FirstOrDefault(s => s.Id == _services.CurrentSeriesId);
        if (current is not null) SeriesList.SelectedItem = current;
    }

    private async Task RefreshStatusAsync()
    {
        var status = await _services.Daemon.GetStatusAsync();
        ModeText.Text = $"{status.Mode} · {(status.Online ? "online" : "offline")}";
        EndpointText.Text = status.Endpoint;
        PrivacyText.Text = status.CloudEgressDisabled ? "Cloud egress disabled · all pixel data stays local" : "Cloud egress enabled";
        BackendList.ItemsSource = status.Backends;
    }

    private async void LoadSample_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync("Opening the sample chest CT…", async () =>
        {
            var info = await _services.Daemon.LoadSampleAsync();
            Select(info);
            ShowStatus(InfoBarSeverity.Success, "Sample opened", "This is a computer-generated chest CT, not a real patient.");
        });
    }

    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = WindowHelper.WithOwner(new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary });
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        await RunBusyAsync($"Reading the scan in {folder.Name}…", async () =>
        {
            var progress = new Progress<double>(p => Progress.Value = p);
            var info = await _services.Daemon.LoadDicomDirectoryAsync(folder.Path, progress);
            Select(info);
            string warnings = info.Warnings.Count > 0 ? " " + string.Join(" ", info.Warnings) : string.Empty;
            string detail = _services.ShowTechnicalDetails
                ? $"{info.Metadata.SeriesDescription} · {info.Dimensions} · {info.SpacingText}."
                : $"{info.Metadata.PatientDisplayName} · {info.Metadata.SeriesDescription} · {info.Depth} images.";
            ShowStatus(info.Warnings.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success, "Scan opened", detail + warnings);
        });
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (SeriesList.SelectedItem is not SeriesInfo info) return;
        var picker = WindowHelper.WithOwner(new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary });
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        string target = Path.Combine(folder.Path, $"HoloRepository-{info.Metadata.PatientId}-{DateTime.Now:yyyyMMdd-HHmmss}");
        await RunBusyAsync("Writing DICOM series…", async () =>
        {
            await _services.Daemon.ExportSeriesAsDicomAsync(info.Id, target);
            ShowStatus(InfoBarSeverity.Success, "Exported", $"{info.Depth} DICOM files written to {target}");
        });
    }

    private void Select(SeriesInfo info)
    {
        _services.CurrentSeriesId = info.Id;
        RefreshList();
        _ = RefreshStatusAsync();
    }

    private async Task RunBusyAsync(string message, Func<Task> work)
    {
        LoadSampleButton.IsEnabled = OpenFolderButton.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        Progress.IsIndeterminate = true;
        ShowStatus(InfoBarSeverity.Informational, "Working", message);
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Could not complete", ex.Message);
        }
        finally
        {
            LoadSampleButton.IsEnabled = OpenFolderButton.IsEnabled = true;
            Progress.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        Progress.IsIndeterminate = severity == InfoBarSeverity.Informational && Progress.Value == 0;
        Status.Severity = severity;
        Status.Title = title;
        Status.Message = message;
        Status.IsOpen = true;
    }

    private void SeriesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool selected = SeriesList.SelectedItem is SeriesInfo;
        ExportButton.IsEnabled = OpenViewerButton.IsEnabled = selected;
        if (SeriesList.SelectedItem is SeriesInfo info) _services.CurrentSeriesId = info.Id;
    }

    private void SeriesList_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => OpenViewer_Click(sender, e);

    private void OpenViewer_Click(object sender, RoutedEventArgs e)
    {
        if (SeriesList.SelectedItem is SeriesInfo) WindowHelper.Main?.Navigate("viewer");
    }
}
