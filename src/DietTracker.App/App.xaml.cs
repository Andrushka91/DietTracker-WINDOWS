using DietTracker.App.Services;
using DietTracker.App.ViewModels;
using DietTracker.App.Views;
using DietTracker.Core.Interfaces;
using DietTracker.Infrastructure;
using DietTracker.Infrastructure.FileStorage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace DietTracker.App;

/// <summary>
/// Application entry point: composes the object graph, prepares local persistence and shows the shell.
/// </summary>
/// <remarks>
/// Startup deliberately does only what is required for the first frame: the database is initialized,
/// the window is shown and the dashboard loads its data. Authentication restoration and the first
/// synchronization cycle run asynchronously afterwards, so neither the cloud nor the network can delay
/// or block the user interface.
/// </remarks>
public partial class App : Application
{
    private Window? _window;
    private ServiceProvider? _services;

    public App() => InitializeComponent();

    /// <summary>Service provider of the running application (used by pages and the tray icon).</summary>
    internal static IServiceProvider Services => Current is App app && app._services is not null
        ? app._services
        : throw new InvalidOperationException("The application services are not available yet.");

    /// <summary>Main window of the application, or <see langword="null"/> before it is created.</summary>
    internal static Window? MainAppWindow => (Current as App)?._window;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        UnhandledException += OnUnhandledException;

        _services = BuildServices();

        // Local persistence first: DietTracker must be fully usable without an account.
        var initializer = _services.GetRequiredService<IDatabaseInitializer>();
        try
        {
            await initializer.InitializeAsync();
        }
        catch (Exception ex)
        {
            await ShowStartupFailureAsync(ex).ConfigureAwait(true);
            return;
        }

        _window = new MainWindow();
        _window.Activate();

        // Everything below happens in the background and never blocks the first frame.
        _ = StartBackgroundServicesAsync();
    }

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        // Logging goes to the debugger output and to a readable text file in the private application data
        // folder (AppDataPaths is a pure path resolver, so this instance resolves the same folder as the
        // one registered in the container).
        var paths = new AppDataPaths();
        services.AddLogging(builder => builder.AddDietTrackerLogging(paths.LogDirectory));

        var providerSettings = ProviderSettings.Load();
        services.AddDietTrackerInfrastructure(
            microsoft =>
            {
                microsoft.ClientId = providerSettings.MicrosoftClientId;
                microsoft.Authority = providerSettings.MicrosoftAuthority;
            },
            google => google.ClientId = providerSettings.GoogleClientId);

        // Windows integration and UI services
        services.AddSingleton<IUserInterfaceContext, WindowHandleProvider>();
        services.AddSingleton<ILocalizationService, ResourceLocalizationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<ITrayIconService, TrayIconService>();

        // View models
        services.AddTransient<ShellViewModel>();
        services.AddTransient<OnboardingViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<CalendarViewModel>();
        services.AddTransient<StatisticsViewModel>();
        services.AddTransient<TemplatesViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AccountSyncViewModel>();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = false,
            ValidateScopes = false,
        });

        // The ingredient editor exposes localization through a lookup so that the view models stay free
        // of XAML types while still using the application resources instead of hardcoded strings.
        IngredientLineViewModel.LocalizationLookup = key => provider.GetRequiredService<ILocalizationService>().Get(key);

        return provider;
    }

    private async Task StartBackgroundServicesAsync()
    {
        if (_services is null)
        {
            return;
        }

        try
        {
            // Reminders and notifications.
            await _services.GetRequiredService<IReminderScheduler>().StartAsync();

            // Restore the previous provider session silently and synchronize when allowed.
            var authentication = _services.GetRequiredService<IAuthenticationCoordinator>();
            var restored = await authentication.TryRestoreSessionAsync();

            if (restored is { Succeeded: true })
            {
                _services.GetRequiredService<ISyncEngine>().RequestSync(SyncTrigger.PostSignIn);
            }
        }
        catch (Exception ex)
        {
            _services.GetRequiredService<ILogger<App>>()
                .LogWarning(ex, "Background initialization failed; the application keeps working offline.");
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Never let an unexpected error take the whole session down: the user is informed and local
        // data stays intact because every write is transactional.
        _services?.GetService<ILogger<App>>()?.LogError(e.Exception, "An unhandled application error occurred.");
        e.Handled = true;
    }

    private static async Task ShowStartupFailureAsync(Exception exception)
    {
        var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = "DietTracker",
            Content = exception is Infrastructure.Database.DatabaseSchemaTooNewException
                ? exception.Message
                : "The local database could not be opened. Install the latest version of DietTracker or restore a backup.",
            CloseButtonText = "Close",
            XamlRoot = MainAppWindow?.Content.XamlRoot,
        };

        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception)
        {
            // The dialog itself is best effort; the process then exits without touching user data.
        }

        Current.Exit();
    }
}
