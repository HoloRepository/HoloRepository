using HoloRepository.App.Services;
using HoloRepository.Core.Daemon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using HoloRepository.Core.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace HoloRepository.App.Pages;

public sealed partial class StudiesPage : Page
{
    private readonly AppServices _services = AppServices.Current;

    public StudiesPage()
    {
        InitializeComponent();
    }

    public static double OpacityFor(bool supported) => supported ? 1 : 0.5;

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
        await OpenPathsAsync([folder.Path], folder.Name);
    }

    private async void OpenFiles_Click(object sender, RoutedEventArgs e)
    {
        var picker = WindowHelper.WithOwner(new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary });
        // Many DICOM files have no extension, so allow everything; the loader skips files that are not DICOM.
        picker.FileTypeFilter.Add("*");
        picker.FileTypeFilter.Add(".dcm");
        picker.FileTypeFilter.Add(".zip");
        var files = await picker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0) return;
        await OpenPathsAsync(files.Select(f => f.Path).ToList(), files.Count == 1 ? files[0].Name : $"{files.Count} files");
    }

    private void Page_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Open scan";
    }

    private async void Page_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        var paths = items.Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
        if (paths.Count > 0) await OpenPathsAsync(paths, paths.Count == 1 ? Path.GetFileName(paths[0]) : $"{paths.Count} items");
    }

    /// <summary>Opens scans passed on the command line (e.g. "Open with", or a folder dropped on the app icon).</summary>
    public Task OpenFromCommandLineAsync(IReadOnlyList<string> paths) =>
        OpenPathsAsync(paths, paths.Count == 1 ? Path.GetFileName(paths[0].TrimEnd('\\', '/')) : $"{paths.Count} items");

    /// <summary>Scans files, folders or ZIP archives; asks which series to open when there is more than one.</summary>
    private async Task OpenPathsAsync(IReadOnlyList<string> paths, string name)
    {
        DicomScanResult? scan = null;
        await RunBusyAsync($"Looking for scans in {name}…", async () =>
        {
            Progress.IsIndeterminate = false;
            var progress = new Progress<double>(p => Progress.Value = p);
            scan = await _services.Daemon.ScanDicomAsync(paths, progress);
        });
        if (scan is null) return;

        var supported = scan.Series.Where(s => s.IsSupported).ToList();
        if (supported.Count == 0)
        {
            ShowStatus(InfoBarSeverity.Error, "Could not open the scan",
                "The DICOM images found are not supported: " + scan.Series[0].UnsupportedReason + ".");
            return;
        }

        var chosen = scan.Series.Count == 1 ? supported[0] : await ChooseSeriesAsync(scan);
        if (chosen is null)
        {
            Status.IsOpen = false;
            return;
        }

        await RunBusyAsync($"Opening {chosen.Title}…", async () =>
        {
            Progress.IsIndeterminate = false;
            var progress = new Progress<double>(p => Progress.Value = p);
            var info = await _services.Daemon.LoadDicomSeriesAsync(scan, chosen.SeriesInstanceUid, progress);
            Select(info);
            string warnings = info.Warnings.Count > 0 ? " " + string.Join(" ", info.Warnings) : string.Empty;
            string detail = _services.ShowTechnicalDetails
                ? $"{info.Metadata.SeriesDescription} · {info.Dimensions} · {info.SpacingText}."
                : $"{info.Metadata.PatientDisplayName} · {chosen.Title} · {info.Depth} images.";
            ShowStatus(info.Warnings.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success, "Scan opened", detail + warnings);
        });
    }

    private async Task<DicomSeriesSummary?> ChooseSeriesAsync(DicomScanResult scan)
    {
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 360,
            ItemsSource = scan.Series,
            ItemTemplate = (DataTemplate)Resources["SeriesChoiceTemplate"],
            SelectedItem = scan.Largest,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Choose a series",
            Content = new StackPanel
            {
                Spacing = 8,
                MinWidth = 460,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"{scan.Series.Count} series were found. Which one should be opened?",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    list,
                },
            },
            PrimaryButtonText = "Open",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        list.SelectionChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = list.SelectedItem is DicomSeriesSummary { IsSupported: true };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        return list.SelectedItem as DicomSeriesSummary;
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
        LoadSampleButton.IsEnabled = OpenFolderButton.IsEnabled = OpenFilesButton.IsEnabled = false;
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
            LoadSampleButton.IsEnabled = OpenFolderButton.IsEnabled = OpenFilesButton.IsEnabled = true;
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
