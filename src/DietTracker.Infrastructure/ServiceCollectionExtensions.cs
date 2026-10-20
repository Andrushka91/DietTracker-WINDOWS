using DietTracker.Core.Interfaces;
using DietTracker.Infrastructure.Authentication;
using DietTracker.Infrastructure.Authentication.Google;
using DietTracker.Infrastructure.Authentication.Microsoft;
using DietTracker.Infrastructure.Cloud;
using DietTracker.Infrastructure.Diagnostics;
using DietTracker.Infrastructure.FileStorage;
using DietTracker.Infrastructure.Notifications;
using DietTracker.Infrastructure.Repositories;
using DietTracker.Infrastructure.Security;
using DietTracker.Infrastructure.Services;
using DietTracker.Infrastructure.Shell;
using DietTracker.Infrastructure.Synchronization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DietTracker.Infrastructure;

/// <summary>
/// Registers every infrastructure service. The application composes its object graph with this single
/// call, which keeps the dependency graph explicit and testable.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Adds the DietTracker infrastructure (persistence, cloud, authentication, reminders).</summary>
    public static IServiceCollection AddDietTrackerInfrastructure(
        this IServiceCollection services,
        Action<MicrosoftAuthenticationOptions>? configureMicrosoft = null,
        Action<GoogleAuthenticationOptions>? configureGoogle = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Configuration of the identity providers (client ids come from the application, never secrets).
        services.AddOptions<MicrosoftAuthenticationOptions>();
        services.AddOptions<GoogleAuthenticationOptions>();

        if (configureMicrosoft is not null)
        {
            services.Configure(configureMicrosoft);
        }

        if (configureGoogle is not null)
        {
            services.Configure(configureGoogle);
        }

        // Local storage and security
        services.AddSingleton<IAppDataPaths, AppDataPaths>();
        services.AddSingleton<ISqliteConnectionFactory, SqliteConnectionFactory>();
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<IDataIntegrityChecker, DataIntegrityChecker>();
        services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        services.AddSingleton<ITokenStore, FileTokenStore>();
        services.AddSingleton<IPhotoStore, PhotoStore>();

        // Repositories
        services.AddSingleton<IMealCategoryRepository, SqliteMealCategoryRepository>();
        services.AddSingleton<IMealEntryRepository, SqliteMealEntryRepository>();
        services.AddSingleton<IPhotoRepository, SqlitePhotoRepository>();
        services.AddSingleton<IMealTemplateRepository, SqliteMealTemplateRepository>();
        services.AddSingleton<IReminderRepository, SqliteReminderRepository>();
        services.AddSingleton<IProfileRepository, SqliteProfileRepository>();
        services.AddSingleton<IAppPreferencesRepository, SqliteAppPreferencesRepository>();
        services.AddSingleton<ICloudAccountRepository, SqliteCloudAccountRepository>();
        services.AddSingleton<ISyncOperationRepository, SqliteSyncOperationRepository>();
        services.AddSingleton<ISyncMetadataRepository, SqliteSyncMetadataRepository>();
        services.AddSingleton<IConflictRepository, SqliteConflictRepository>();
        services.AddSingleton<ITombstoneRepository, SqliteTombstoneRepository>();

        // Domain services
        services.AddSingleton<IClock>(_ => new SystemClock(TimeProvider.System));
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<IMealTrackingService, Core.Services.MealTrackingService>();
        services.AddSingleton<IAppEnvironment>(provider => new AppEnvironment(provider.GetRequiredService<IAppDataPaths>()));
        services.AddSingleton<IConnectivityMonitor, ConnectivityMonitor>();
        services.AddSingleton<IBackupService, BackupService>();

        // Authentication
        services.AddSingleton<IUserInterfaceContext, HeadlessUserInterfaceContext>();
        services.AddSingleton<MsalAuthenticationProvider>();
        services.AddSingleton<GoogleAuthenticationProvider>();
        services.AddSingleton<IAuthenticationProviderFactory, AuthenticationProviderFactory>();
        services.AddSingleton<IAuthenticationCoordinator, AuthenticationCoordinator>();

        // Cloud storage and synchronization
        services.AddHttpClient("cloud", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DietTracker/1.0");
        });

        services.AddSingleton<ICloudPayloadSerializer, CloudPayloadSerializer>();
        services.AddSingleton<ICloudStorageProviderFactory, CloudStorageProviderFactory>();
        services.AddSingleton<IConflictResolver, ConflictResolver>();
        services.AddSingleton<ISyncEngine, SyncEngine>();
        services.AddSingleton<ISyncJournal, SyncJournal>();
        services.AddSingleton<IAccountSwitchCoordinator, AccountSwitchCoordinator>();
        services.AddSingleton<IPrivacyService, PrivacyService>();

        // Notifications and Windows integration
        services.AddSingleton<INotificationService, WindowsNotificationService>();
        services.AddSingleton<IReminderScheduler, ReminderScheduler>();
        services.AddSingleton<IStartupTaskService, StartupTaskService>();

        return services;
    }

    /// <summary>Creates the logging pipeline used by the desktop application.</summary>
    /// <param name="builder">Logging builder to extend.</param>
    /// <param name="logDirectory">
    /// Folder that receives a readable log file (normally <c>IAppDataPaths.LogDirectory</c>). When it is
    /// <see langword="null"/> or empty only the debugger output is used.
    /// </param>
    /// <param name="minimumLevel">Lowest level that is written.</param>
    /// <remarks>
    /// Two sinks on purpose: the Debug provider for a debugger session, and a text file in the private
    /// application data folder, which is what a user or a tester can inspect after a run (see the
    /// "Test the app" section of the README). Log files contain technical detail only.
    /// </remarks>
    public static ILoggingBuilder AddDietTrackerLogging(
        this ILoggingBuilder builder,
        string? logDirectory = null,
        LogLevel minimumLevel = LogLevel.Information)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.SetMinimumLevel(minimumLevel);
        builder.AddDebug();

        if (!string.IsNullOrWhiteSpace(logDirectory))
        {
            builder.AddProvider(new FileLoggerProvider(logDirectory, minimumLevel));
        }

        return builder;
    }
}
