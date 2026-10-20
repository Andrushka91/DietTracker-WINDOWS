using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DietTracker.App;

/// <summary>
/// Shell window: navigation between the five application areas, the synchronization status strip,
/// the notification area presence and notification deep links.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly ILocalizationService _localization;
    private readonly ISyncEngine _syncEngine;
    private readonly ITrayIconService _trayIcon;
    private readonly IMealTrackingService _mealTracking;
    private bool _exitRequested;

    public MainWindow()
    {
        InitializeComponent();

        _services = App.Services;
        _localization = _services.GetRequiredService<ILocalizationService>();
        _syncEngine = _services.GetRequiredService<ISyncEngine>();
        _trayIcon = _services.GetRequiredService<ITrayIconService>();
        _mealTracking = _services.GetRequiredService<IMealTrackingService>();

        Title = _localization.Get("App_DisplayName");
        AppWindow.Title = Title;

        // Opens sized so all five meal cards fit, and limits resizing to the space the content uses.
        InitializeWindowSize();

        _syncEngine.StateChanged += OnSyncStateChanged;
        _services.GetRequiredService<INotificationService>().Activated += OnNotificationActivated;

        _trayIcon.RestoreRequested += (_, _) => RestoreFromTray();
        _trayIcon.ExitRequested += (_, _) => ExitApplication();

        AppWindow.Closing += OnWindowClosing;

        Navigate("dashboard");
    }

    /// <summary>Navigates to a page by tag, keeping the navigation item selection in sync.</summary>
    public void Navigate(string tag, object? parameter = null)
    {
        var pageType = tag switch
        {
            "calendar" => typeof(Views.CalendarPage),
            "statistics" => typeof(Views.StatisticsPage),
            "templates" => typeof(Views.TemplatesPage),
            "account" => typeof(Views.AccountSyncPage),
            "settings" => typeof(Views.SettingsPage),
            "onboarding" => typeof(Views.OnboardingPage),
            _ => typeof(Views.DashboardPage),
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType, parameter);
        }

        foreach (var item in Navigation.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag is string itemTag && itemTag == tag)
            {
                Navigation.SelectedItem = item;
            }
        }
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            Navigate(tag);
        }
    }

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exitRequested || !MinimizeToTrayEnabled())
        {
            _trayIcon.HideAsync().GetAwaiter().GetResult();
            return;
        }

        // Closing the window keeps reminders working from the notification area; the user exits
        // explicitly through the tray menu or the Settings page.
        args.Cancel = true;
        AppWindow.Hide();
        _trayIcon.ShowAsync().GetAwaiter().GetResult();
    }

    private bool MinimizeToTrayEnabled()
    {
        try
        {
            var profiles = _services.GetRequiredService<IProfileService>();
            return profiles.GetPreferencesAsync().GetAwaiter().GetResult().MinimizeToTray;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void RestoreFromTray()
    {
        AppWindow.Show();
        Activate();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        _trayIcon.HideAsync().GetAwaiter().GetResult();
        Application.Current.Exit();
    }
}
