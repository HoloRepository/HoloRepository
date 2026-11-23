using HoloRepository.App.Services;
using HoloRepository.Core.Notes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;

namespace HoloRepository.App.Pages;

public sealed partial class NotesPage : Page
{
    private readonly AppServices _services = AppServices.Current;

    public NotesPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        EngineText.Text = $"Dual-tier notes from the consultation · engine: {_services.NoteGenerator.Name}";
        if (_services.Transcript.IsEmpty)
        {
            ShowStatus(InfoBarSeverity.Informational, "No consultation captured yet",
                "Capture or type the discussion on the Consultation page, or use the sample consultation.", showSample: true);
            if (_services.LastNotes is not null) Show(_services.LastNotes);
            return;
        }
        await GenerateAsync();
    }

    private async Task GenerateAsync()
    {
        GenerateButton.IsEnabled = false;
        try
        {
            var series = _services.CurrentSeries;
            var annotations = series is null ? Array.Empty<HoloRepository.Core.Markup.Annotation>() : _services.Annotations.ForSeries(series.Id);
            var request = new NoteRequest(_services.Transcript, annotations, series?.Metadata);
            var notes = await _services.NoteGenerator.GenerateAsync(request);
            _services.LastNotes = notes;
            Show(notes);
            Status.IsOpen = false;
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Could not generate notes", ex.Message, showSample: false);
        }
        finally
        {
            GenerateButton.IsEnabled = true;
        }
    }

    private void Show(GeneratedNotes notes)
    {
        MdtText.Text = notes.MdtText;
        MdtText.ClearValue(TextBlock.ForegroundProperty);
        ConceptList.ItemsSource = notes.Concepts;
        GuidelineList.ItemsSource = notes.Guidelines;
        CodesPanel.Visibility = Visibility.Visible;

        PatientText.Text = notes.PatientSummary;
        PatientText.ClearValue(TextBlock.ForegroundProperty);
        var r = notes.PatientReadability;
        bool ok = r.MeetsTarget(ReadabilityScorer.PatientTargetReadingAge);
        ReadingBadge.Visibility = Visibility.Visible;
        ReadingBadge.Background = new SolidColorBrush(ok ? Color.FromArgb(255, 16, 124, 16) : Color.FromArgb(255, 157, 93, 0));
        ReadingBadgeText.Text = $"Reading age {r.ReadingAge:0.#} {(ok ? "✓" : "– simplify")}";
        ReadabilityDetail.Text = $"Flesch–Kincaid grade {r.FleschKincaidGrade:0.#} · {r.Words} words · {r.Sentences} sentences · {r.Syllables} syllables";

        CopyButton.IsEnabled = ExportButton.IsEnabled = true;
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message, bool showSample)
    {
        Status.Severity = severity;
        Status.Title = title;
        Status.Message = message;
        UseSampleButton.Visibility = showSample ? Visibility.Visible : Visibility.Collapsed;
        Status.IsOpen = true;
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (_services.Transcript.IsEmpty)
        {
            ShowStatus(InfoBarSeverity.Warning, "Nothing to summarise", "The consultation transcript is empty.", showSample: true);
            return;
        }
        await GenerateAsync();
    }

    private async void UseSample_Click(object sender, RoutedEventArgs e)
    {
        _services.Transcript.LoadSample();
        await GenerateAsync();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_services.LastNotes is null) return;
        var package = new DataPackage();
        package.SetText(NoteFormatter.ToMarkdown(_services.LastNotes));
        Clipboard.SetContent(package);
        ShowStatus(InfoBarSeverity.Success, "Copied", "Both notes were copied to the clipboard as Markdown.", showSample: false);
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_services.LastNotes is null) return;
        var picker = WindowHelper.WithOwner(new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"MDT-note-{DateTime.Now:yyyyMMdd-HHmm}",
        }, WindowHelper.WindowFor(this));
        picker.FileTypeChoices.Add("Markdown", new List<string> { ".md" });
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        await FileIO.WriteTextAsync(file, NoteFormatter.ToMarkdown(_services.LastNotes));
        ShowStatus(InfoBarSeverity.Success, "Exported", $"Saved to {file.Path}", showSample: false);
    }
}
