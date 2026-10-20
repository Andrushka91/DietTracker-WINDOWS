using DietTracker.Core.Interfaces;

namespace DietTracker.Infrastructure.Notifications;

/// <summary>
/// Event driven reminder scheduler.
/// </summary>
/// <remarks>
/// The scheduler computes the next due instant instead of polling, recalculates after every relevant
/// change (settings, resume from sleep, time zone change) and relies on a durable ledger so a reminder
/// is never raised twice for the same meal slot and date - not even after a restart or a DST shift.
/// Reminders older than <see cref="StaleReminderWindow"/> are skipped, so returning to a powered-on PC
/// after a long absence does not flood the user with obsolete notifications.
/// </remarks>
public sealed partial class ReminderScheduler : IReminderScheduler, IAsyncDisposable
{
    /// <summary>Reminders older than this are considered obsolete and are not shown.</summary>
    internal static readonly TimeSpan StaleReminderWindow = TimeSpan.FromHours(3);

    /// <summary>Upper bound for a single wait, so the scheduler reacts to clock and time zone changes.</summary>
    internal static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(5);

    private readonly IReminderRepository _reminders;
    private readonly IMealEntryRepository _entries;
    private readonly IMealCategoryRepository _categories;
    private readonly IProfileService _profiles;
    private readonly IAppPreferencesRepository _preferences;
    private readonly INotificationService _notifications;
    private readonly IClock _clock;
    private readonly ILogger<ReminderScheduler> _logger;

    private readonly Dictionary<Guid, DateTimeOffset> _snoozes = [];
    private readonly CancellationTokenSource _shutdown = new();

    private Task? _loop;
    private DateTimeOffset? _nextDueAt;

    public ReminderScheduler(
        IReminderRepository reminders,
        IMealEntryRepository entries,
        IMealCategoryRepository categories,
        IProfileService profiles,
        IAppPreferencesRepository preferences,
        INotificationService notifications,
        IClock clock,
        ILogger<ReminderScheduler> logger)
    {
        _reminders = reminders;
        _entries = entries;
        _categories = categories;
        _profiles = profiles;
        _preferences = preferences;
        _notifications = notifications;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public DateTimeOffset? NextDueAt => _nextDueAt;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _notifications.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await RefreshAsync(cancellationToken).ConfigureAwait(false);

        _loop ??= Task.Run(() => RunAsync(_shutdown.Token), CancellationToken.None);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);

        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
        }
    }

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        _snoozes.Clear();
        _nextDueAt = null;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task SnoozeAsync(Guid mealEntryId, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var entry = await _entries.GetAsync(mealEntryId, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return;
        }

        // A snooze is a transient, in-memory delay: it does not change the configuration and is
        // therefore not journaled for synchronization.
        _snoozes[entry.Id] = _clock.UtcNow + duration;
        _logger.LogInformation("The reminder for meal {MealEntryId} was snoozed until {Until}.", entry.Id, _snoozes[entry.Id]);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueAsync(cancellationToken).ConfigureAwait(false);

                var delay = _nextDueAt is { } next ? next - _clock.UtcNow : MaxWait;

                if (delay < TimeSpan.FromSeconds(1))
                {
                    delay = TimeSpan.FromSeconds(1);
                }
                else if (delay > MaxWait)
                {
                    delay = MaxWait;
                }

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // A failing cycle must not kill the scheduler.
                _logger.LogWarning(ex, "The reminder scheduler cycle failed.");
                await Task.Delay(MaxWait, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _shutdown.Dispose();
    }
}
