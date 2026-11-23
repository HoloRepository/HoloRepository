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

        string icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.png");
        if (File.Exists(icon))
        {
            try { AppWindow.SetIcon(icon); } catch (ArgumentException) { }
        }

        Navigate("studies");
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

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        string tag = args.IsSettingsSelected ? "settings" : (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "studies";
        if (Pages.TryGetValue(tag, out var page) && ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
    }
}
