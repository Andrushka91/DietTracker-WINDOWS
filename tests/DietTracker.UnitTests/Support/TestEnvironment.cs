using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using DietTracker.Core.Services;
using DietTracker.Infrastructure;
using DietTracker.Infrastructure.Authentication;
using DietTracker.Infrastructure.Cloud;
using DietTracker.Infrastructure.Database;
using DietTracker.Infrastructure.FileStorage;
using DietTracker.Infrastructure.Repositories;
using DietTracker.Infrastructure.Security;
using DietTracker.Infrastructure.Services;
using DietTracker.Infrastructure.Synchronization;
using DietTracker.UnitTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DietTracker.UnitTests.Support;

/// <summary>
/// Builds a complete DietTracker object graph against a temporary application data directory, so the
/// tests exercise the real SQLite database, the real repositories and the real synchronization engine
/// while the cloud is replaced by an in-memory provider.
/// </summary>
public sealed class TestEnvironment : IAsyncDisposable
{
    private TestEnvironment(string rootPath, ServiceProvider services, InMemoryCloudStorageProvider cloud)
    {
        RootPath = rootPath;
        Services = services;
        Cloud = cloud;
    }

    public string RootPath { get; }

    public ServiceProvider Services { get; }

    public InMemoryCloudStorageProvider Cloud { get; }

    public IMealTrackingService Tracking => Services.GetRequiredService<IMealTrackingService>();

    public ISyncEngine Sync => Services.GetRequiredService<ISyncEngine>();

    public ICloudAccountRepository Accounts => Services.GetRequiredService<ICloudAccountRepository>();

    public IMealEntryRepository Entries => Services.GetRequiredService<IMealEntryRepository>();

    public IPhotoRepository Photos => Services.GetRequiredService<IPhotoRepository>();

    public IBackupService Backup => Services.GetRequiredService<IBackupService>();

    public IDatabaseInitializer Database => Services.GetRequiredService<IDatabaseInitializer>();

    public RecordingNotificationService Notifications => (RecordingNotificationService)Services.GetRequiredService<INotificationService>();

    /// <summary>Creates an isolated environment. Set <paramref name="connectAccount"/> to simulate a signed-in user.</summary>
    public static async Task<TestEnvironment> CreateAsync(
        bool connectAccount = false,
        ProviderType provider = ProviderType.Microsoft,
        string? rootPath = null)
    {
        var root = rootPath ?? Path.Combine(Path.GetTempPath(), "diettracker-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var cloud = new InMemoryCloudStorageProvider(provider);
        var connectivity = new TestConnectivityMonitor();
        var notifications = new RecordingNotificationService();

        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSingleton<IAppDataPaths>(new AppDataPaths(root));
        services.AddSingleton<ISqliteConnectionFactory, SqliteConnectionFactory>();
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<IDataIntegrityChecker, DataIntegrityChecker>();
        services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
        services.AddSingleton<ITokenStore, FileTokenStore>();
        services.AddSingleton<IPhotoStore, PhotoStore>();

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

        services.AddSingleton<IClock>(new SystemClock(TimeProvider.System));
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<IMealTrackingService, MealTrackingService>();
        services.AddSingleton<IAppEnvironment, AppEnvironment>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IPrivacyService, PrivacyService>();

        services.AddSingleton<IConnectivityMonitor>(connectivity);
        services.AddSingleton(connectivity);
        services.AddSingleton<INotificationService>(notifications);
        services.AddSingleton<ICloudPayloadSerializer, CloudPayloadSerializer>();
        services.AddSingleton<ICloudStorageProviderFactory>(new TestCloudStorageFactory(cloud));
        services.AddSingleton<IConflictResolver, ConflictResolver>();
        services.AddSingleton<ISyncEngine, SyncEngine>();
        services.AddSingleton<ISyncJournal, SyncJournal>();
        services.AddSingleton<IAccountSwitchCoordinator, AccountSwitchCoordinator>();
        services.AddSingleton<IUserInterfaceContext>(new TestUserInterfaceContext());
        services.AddSingleton<IAuthenticationCoordinator>(new TestAuthenticationCoordinator());

        var providerInstance = services.BuildServiceProvider();
        var environment = new TestEnvironment(root, providerInstance, cloud);

        await environment.Database.InitializeAsync();
        await environment.Tracking.GetCategoriesAsync();

        if (connectAccount)
        {
            await environment.ConnectAccountAsync(provider);
        }

        return environment;
    }

    /// <summary>Creates the active cloud account row so the journal and the engine are enabled.</summary>
    public async Task ConnectAccountAsync(ProviderType provider = ProviderType.Microsoft, string email = "user@example.com")
    {
        var account = new CloudAccount
        {
            ProviderType = provider,
            ProviderUserId = $"provider-user-{provider}",
            DisplayName = "Test User",
            Email = email,
            IsActive = true,
            SynchronizationEnabled = true,
        };

        await Accounts.UpsertActiveAsync(account);
    }

    /// <summary>Switches the simulated connectivity state.</summary>
    public void SetOnline(bool online) => Services.GetRequiredService<TestConnectivityMonitor>().IsOnlineValue = online;

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();

        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked file must not fail the test run; the temporary folder is cleaned up by Windows.
        }
    }
}

/// <summary>Connectivity monitor whose state is controlled by the test.</summary>
public sealed class TestConnectivityMonitor : IConnectivityMonitor
{
    public bool IsOnlineValue { get; set; } = true;

    public bool IsOnline => IsOnlineValue;

    public event EventHandler<bool>? ConnectivityChanged;

    public void Raise(bool online)
    {
        IsOnlineValue = online;
        ConnectivityChanged?.Invoke(this, online);
    }
}

/// <summary>Notification service that records what would have been shown.</summary>
public sealed class RecordingNotificationService : INotificationService
{
    public List<MealReminderNotification> Reminders { get; } = [];

    public List<string> Cancelled { get; } = [];

    public event EventHandler<NotificationActivatedEventArgs>? Activated;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ShowMealReminderAsync(MealReminderNotification notification, CancellationToken cancellationToken = default)
    {
        Reminders.Add(notification);
        return Task.CompletedTask;
    }

    public Task ShowInfoAsync(string title, string message, string? argument = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task CancelMealReminderAsync(Guid mealEntryId, CancellationToken cancellationToken = default)
    {
        Cancelled.Add(mealEntryId.ToString("D"));
        return Task.CompletedTask;
    }

    public Task CancelAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void RaiseActivation(string argument, string? action = null)
        => Activated?.Invoke(this, new NotificationActivatedEventArgs { Argument = argument, Action = action });
}

/// <summary>Authentication coordinator that never touches a real identity provider.</summary>
public sealed class TestAuthenticationCoordinator : IAuthenticationCoordinator
{
    public event EventHandler<ProviderType>? ActiveAccountChanged;

    public Task<CloudAccount?> GetActiveAccountAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<CloudAccount?>(null);

    public Task<AuthenticationResult> SignInAsync(ProviderType providerType, bool grantUploadConsent, CancellationToken cancellationToken = default)
        => Task.FromResult(AuthenticationResult.Ok(providerType, "test-user", "Test User", "user@example.com", silent: false));

    public Task<AuthenticationResult?> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<AuthenticationResult?>(null);

    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        ActiveAccountChanged?.Invoke(this, ProviderType.None);
        return Task.CompletedTask;
    }
}
