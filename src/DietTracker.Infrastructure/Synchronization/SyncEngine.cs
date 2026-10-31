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
    private Task? _requestLoop;

    /// <summary>Set when a request arrives while a cycle (or its quiet period) is running.</summary>
    private int _rerunRequested;

    /// <summary>Why the next pass of the request loop runs.</summary>
    private int _pendingTrigger = (int)SyncTrigger.LocalChange;

    /// <summary>When the last automatic download ran, for the throttle of <see cref="SyncTrigger.AutoPull"/>.</summary>
    private DateTimeOffset? _lastAutoPullAt;

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

        // Retrying by itself is what makes an offline edit eventually arrive: the journal keeps it,
        // and the moment the network returns a cycle is requested without any user action.
        _connectivity.ConnectivityChanged += OnConnectivityChanged;
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
            // The passive download never uploads: a local edit has its own cycle, and a pull at the
            // same moment would only duplicate the push it is about to run.
            return await RunCycleAsync(trigger, cancellationToken, pullOnly: trigger is SyncTrigger.AutoPull).ConfigureAwait(false);
        }
        finally
        {
            _cycleGate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<SyncResult> PushOutgoingAsync(CancellationToken cancellationToken = default)
    {
        await _cycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The push-only cycle of an account transition: the journal is drained without reading
            // the change feed, and operations that wait for their retry are uploaded immediately
            // instead of silently counting as pending while their backoff counts down.
            return await RunCycleAsync(SyncTrigger.AccountTransition, cancellationToken, pushOnly: true).ConfigureAwait(false);
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

        lock (this)
        {
            // The reason of a coalesced request is kept, but a passive download never downgrades a
            // request that carries real work: an edit that arrived together with the clock is
            // uploaded, not skipped because the tick happened to be the later caller.
            if (trigger is not SyncTrigger.AutoPull
                || (SyncTrigger)Volatile.Read(ref _pendingTrigger) is SyncTrigger.AutoPull)
            {
                Interlocked.Exchange(ref _pendingTrigger, (int)trigger);
            }

            if (_requestLoop is { IsCompleted: false })
            {
                // A cycle - or its quiet period - is running. Remember that one more pass is owed:
                // an edit committed while the journal was already being read would otherwise wait
                // for the next scheduled knock.
                Interlocked.Exchange(ref _rerunRequested, 1);
                return;
            }

            _requestLoop = Task.Run(() => RunRequestLoopAsync(_shutdown.Token));
        }
    }

    /// <summary>
    /// Runs cycles until no request arrived during the last one. Rapid edits collapse into a single
    /// pass through the debounce delay; an edit committed <em>during</em> a pass sets the rerun flag
    /// and is picked up by the next iteration instead of waiting for the hourly check.
    /// </summary>
    private async Task RunRequestLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_debounce, cancellationToken).ConfigureAwait(false);

                // Cleared before the cycle starts: anything that lands afterwards is new work.
                Interlocked.Exchange(ref _rerunRequested, 0);
                var trigger = (SyncTrigger)Volatile.Read(ref _pendingTrigger);

                try
                {
                    await SynchronizeAsync(trigger, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // A failed automatic cycle is reported through the state, never thrown here.
                    _logger.LogWarning(ex, "Background synchronization triggered by {Trigger} failed.", trigger);
                }

                if (Interlocked.CompareExchange(ref _rerunRequested, 0, 0) == 0)
                {
                    return;
                }

                _logger.LogDebug("A change arrived while the last cycle ran; one more pass follows.");
            }
        }
        catch (OperationCanceledException)
        {
            // Application is shutting down.
        }
    }

    /// <summary>
    /// A dropped connection is the most common reason a change stays local, so coming back online
    /// asks for a cycle immediately instead of waiting for the next scheduled knock.
    /// </summary>
    private void OnConnectivityChanged(object? sender, bool isOnline)
    {
        if (!isOnline)
        {
            return;
        }

        _logger.LogInformation("Connectivity returned; requesting a synchronization pass.");
        RequestSync(SyncTrigger.ConnectivityRestored);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _connectivity.ConnectivityChanged -= OnConnectivityChanged;
        await _shutdown.CancelAsync().ConfigureAwait(false);
        _shutdown.Dispose();
        _cycleGate.Dispose();
    }
}
