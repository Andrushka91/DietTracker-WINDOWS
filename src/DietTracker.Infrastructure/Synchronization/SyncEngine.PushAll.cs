using DietTracker.Core.BusinessRules;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>
/// Queues every local record and uploads it (used after switching cloud accounts and as a manual
/// "upload everything" recovery action).
/// </summary>
public sealed partial class SyncEngine
{
    private static readonly DateOnly HistoryStart = new(2000, 1, 1);

    private static readonly DateOnly HistoryEnd = new(2100, 1, 1);

    /// <inheritdoc />
    public async Task<SyncResult> PushAllAsync(IProgress<RestoreProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return SyncResult.Failure("sync.not-configured", state: SyncState.NotConfigured);
        }

        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var range = await _entries.GetForRangeAsync(profile.Id, HistoryStart, HistoryEnd, cancellationToken).ConfigureAwait(false);
        var meals = range.Values.SelectMany(v => v).Where(MealContentRules.HasUserContent).ToArray();

        var templates = await _templates.GetAllAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        var reminders = await _reminders.GetAllAsync(profile.Id, cancellationToken).ConfigureAwait(false);

        var total = meals.Length + templates.Count + reminders.Count;
        var processed = 0;

        foreach (var meal in meals)
        {
            await JournalUpsertAsync(account.Id, EntityType.MealEntry, meal.Id, meal.Revision, cancellationToken).ConfigureAwait(false);

            foreach (var photo in await _photos.GetForMealAsync(meal.Id, cancellationToken).ConfigureAwait(false))
            {
                await JournalUpsertAsync(account.Id, EntityType.MealPhoto, photo.Id, 1, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new RestoreProgress { Stage = "queue", Processed = ++processed, Total = total });
        }

        foreach (var template in templates)
        {
            await JournalUpsertAsync(account.Id, EntityType.MealTemplate, template.Id, 1, cancellationToken).ConfigureAwait(false);
            progress?.Report(new RestoreProgress { Stage = "queue", Processed = ++processed, Total = total });
        }

        foreach (var reminder in reminders)
        {
            await JournalUpsertAsync(account.Id, EntityType.ReminderSettings, reminder.Id, 1, cancellationToken).ConfigureAwait(false);
            progress?.Report(new RestoreProgress { Stage = "queue", Processed = ++processed, Total = total });
        }

        await JournalUpsertAsync(account.Id, EntityType.AppPreferences, profile.Id, 1, cancellationToken).ConfigureAwait(false);

        progress?.Report(new RestoreProgress { Stage = "upload", Processed = 0, Total = total });
        return await SynchronizeAsync(SyncTrigger.PostRestore, cancellationToken).ConfigureAwait(false);
    }
}
