using HoloRepository.App.Pages;
using HoloRepository.App.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace HoloRepository.App;

public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["studies"] = typeof(StudiesPage),
        ["viewer"] = typeof(ViewerPage),
        ["consultation"] = typeof(ConsultationPage),
        ["notes"] = typeof(NotesPage),
        ["settings"] = typeof(SettingsPage),
    };

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        WindowHelper.SizeAndCentre(this, 1480, 940);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        var services = AppServices.Current;
        DaemonBadgePanel.Visibility = services.TechnicalVisibility;
        services.TechnicalDetailsChanged += (_, _) => DispatcherQueue.TryEnqueue(() => DaemonBadgePanel.Visibility = services.TechnicalVisibility);

        Navigate("studies");

        // Paths given on the command line ("Open with", or a folder dropped on the app icon) are opened straight away.
        var paths = Environment.GetCommandLineArgs().Skip(1).Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (paths.Count > 0)
        {
            RootGrid.Loaded += async (_, _) =>
            {
                if (ContentFrame.Content is StudiesPage studies) await studies.OpenFromCommandLineAsync(paths);
            };
        }
    }

    /// <summary>Selects a navigation item by tag (studies, viewer, consultation, notes, settings).</summary>
    public void Navigate(string tag)
    {
        if (tag == "settings")
        {
            Nav.SelectedItem = Nav.SettingsItem;
            return;
        }
        Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First(i => (string)i.Tag == tag);
    }

    /// <summary>Hides the title bar and navigation so a viewport can fill the screen.</summary>
    public void SetImmersive(bool immersive)
    {
        AppTitleBar.Visibility = immersive ? Visibility.Collapsed : Visibility.Visible;
        RootGrid.RowDefinitions[0].Height = immersive ? new GridLength(0) : new GridLength(40);
        Nav.IsPaneVisible = !immersive;
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string tag = args.IsSettingsSelected ? "settings" : (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "studies";
        if (Pages.TryGetValue(tag, out var page) && ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
    }
}
