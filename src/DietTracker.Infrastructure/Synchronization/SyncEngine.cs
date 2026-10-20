using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>
/// Offline-first synchronization engine shared by OneDrive and Google Drive.
/// </summary>
/// <remarks>
/// Local SQLite is authoritative for the user experience: the engine only pushes changes that are
/// already committed locally, and a failing provider can never corrupt or block local data. A cycle
/// is coalesced through a semaphore, so concurrent triggers (startup, local change, connectivity
/// restored, periodic, manual) result in a single run.
/// </remarks>
public sealed partial class SyncEngine : ISyncEngine, IAsyncDisposable
{
    private readonly ICloudAccountRepository _accounts;
    private readonly ICloudStorageProviderFactory _providers;
    private readonly ICloudPayloadSerializer _serializer;
    private readonly ISyncOperationRepository _operations;
    private readonly ISyncMetadataRepository _metadata;
    private readonly ITombstoneRepository _tombstones;
    private readonly IMealEntryRepository _entries;
    private readonly IMealCategoryRepository _categories;
    private readonly IMealTemplateRepository _templates;
    private readonly IPhotoRepository _photos;
    private readonly IPhotoStore _photoStore;
    private readonly IReminderRepository _reminders;
    private readonly IProfileService _profiles;
    private readonly IAppPreferencesRepository _preferences;
    private readonly IConflictRepository _conflicts;
    private readonly IConflictResolver _conflictResolver;
    private readonly IConnectivityMonitor _connectivity;
    private readonly IClock _clock;
    private readonly IAppEnvironment _environment;
    private readonly ILogger<SyncEngine> _logger;

    private readonly SemaphoreSlim _cycleGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TimeSpan _debounce = TimeSpan.FromSeconds(3);

    private SyncState _state = SyncState.NotConfigured;
    private DateTimeOffset? _lastSuccessfulSyncAt;
    private Task? _debounceTask;

    public SyncEngine(
        ICloudAccountRepository accounts,
        ICloudStorageProviderFactory providers,
        ICloudPayloadSerializer serializer,
        ISyncOperationRepository operations,
        ISyncMetadataRepository metadata,
        ITombstoneRepository tombstones,
        IMealEntryRepository entries,
        IMealCategoryRepository categories,
        IMealTemplateRepository templates,
        IPhotoRepository photos,
        IPhotoStore photoStore,
        IReminderRepository reminders,
        IProfileService profiles,
        IAppPreferencesRepository preferences,
        IConflictRepository conflicts,
        IConflictResolver conflictResolver,
        IConnectivityMonitor connectivity,
        IClock clock,
        IAppEnvironment environment,
        ILogger<SyncEngine> logger)
    {
        _accounts = accounts;
        _providers = providers;
        _serializer = serializer;
        _operations = operations;
        _metadata = metadata;
        _tombstones = tombstones;
        _entries = entries;
        _categories = categories;
        _templates = templates;
        _photos = photos;
        _photoStore = photoStore;
        _reminders = reminders;
        _profiles = profiles;
        _preferences = preferences;
        _conflicts = conflicts;
        _conflictResolver = conflictResolver;
        _connectivity = connectivity;
        _clock = clock;
        _environment = environment;
        _logger = logger;
    }

    /// <inheritdoc />
    public SyncState State => _state;

    /// <inheritdoc />
    public DateTimeOffset? LastSuccessfulSyncAt => _lastSuccessfulSyncAt;

    /// <inheritdoc />
    public event EventHandler<SyncStateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public async Task<SyncResult> SynchronizeAsync(SyncTrigger trigger, CancellationToken cancellationToken = default)
    {
        await _cycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RunCycleAsync(trigger, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _cycleGate.Release();
        }
    }

    /// <inheritdoc />
    public void RequestSync(SyncTrigger trigger)
    {
        if (_shutdown.IsCancellationRequested)
        {
            return;
        }

        // Debounced: rapid edits collapse into one cycle without blocking the caller.
        lock (this)
        {
            if (_debounceTask is { IsCompleted: false })
            {
                return;
            }

            _debounceTask = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_debounce, _shutdown.Token).ConfigureAwait(false);
                    await SynchronizeAsync(trigger, _shutdown.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Application is shutting down.
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Background synchronization triggered by {Trigger} failed.", trigger);
                }
            });
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        _shutdown.Dispose();
        _cycleGate.Dispose();
    }
}
