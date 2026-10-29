using DietTracker.App.Services;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.App.ViewModels;

/// <summary>
/// <see cref="IMealCardHost"/> implementation of the dashboard: the only place where card actions
/// reach persistence, the file picker and the synchronization engine.
/// </summary>
public sealed partial class DashboardViewModel
{
    /// <inheritdoc />
    public string Localize(string resourceKey) => _localization.Get(resourceKey);

    /// <inheritdoc />
    public DateOnly Today => _clock.Today;

    /// <inheritdoc />
    public async Task<bool> SaveCardAsync(MealCardViewModel card, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);

        var saved = await PersistCardAsync(card, cancellationToken);
        if (saved)
        {
            // Only a write changes the day summary and asks the synchronization engine for a cycle.
            await RefreshProgressAsync(cancellationToken);
            _syncEngine.RequestSync(SyncTrigger.LocalChange);
        }

        return saved;
    }

    /// <summary>
    /// Validates and writes one card, without refreshing the day summary or the synchronization:
    /// the single Save button wraps this with both, while <c>SaveAllAsync</c> runs it for every
    /// open card and pays the two shared costs once for the whole batch.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the card refused the edits (an empty mandatory field or an
    /// invalid time): the card keeps its editor open and shows the explanation, and nothing of it
    /// was written.
    /// </returns>
    private async Task<bool> PersistCardAsync(MealCardViewModel card, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(card);

        var entry = await _tracking.GetOrCreateSlotAsync(card.Date, card.CategoryKey, cancellationToken);
        if (!card.ApplyEditsTo(entry))
        {
            // Invalid planned time typed by the user; the card shows the explanation.
            return false;
        }

        var validation = await _tracking.SaveEditsAsync(entry, cancellationToken);
        if (!validation.IsValid)
        {
            // Every failure is mapped to its localized message in one place, so the mandatory
            // fields ("The title is required.", "The description is required.") and the length
            // rules all reach the card's own error line.
            card.ErrorMessage = string.Join(
                Environment.NewLine,
                validation.Failures.Select(f => _localization.Get(LocalizationKeys.ForValidation(f.Code))));
            return false;
        }

        // The saved card adopts the persisted entry (and closes its editor). The collection is
        // deliberately NOT rebuilt: clearing it would throw away every other card that is open in
        // inline editing with unsaved changes - only the summary is refreshed, by the caller.
        card.Reload(entry);
        return true;
    }

    /// <inheritdoc />
    public async Task ClearCardAsync(MealCardViewModel card, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);

        // Nothing is wiped without an explicit "Yes": the dialog opens with "No" as its default
        // button, so an accidental press or the escape key always lands on the safe answer. The
        // category name in the dialog spells out which of the five cards is about to be cleared.
        var confirmed = await _dialogs.ConfirmAsync("Meal_ClearTitle", "Meal_ClearMessage", card.CategoryName);
        if (!confirmed)
        {
            return;
        }

        var entry = await _tracking.ClearMealAsync(card.Id, cancellationToken);
        card.Reload(entry);

        // The photographs were wiped together with the text: the gallery of this card starts empty
        // again while every other card keeps its own pictures.
        await AttachPhotosAsync(card, cancellationToken);
        await RefreshProgressAsync(cancellationToken);
        _syncEngine.RequestSync(SyncTrigger.LocalChange);
    }

    /// <summary>
    /// Re-reads only the progress figures of the selected day. Used after a save or a status
    /// change, both of which touch exactly one card: a full <see cref="LoadAsync"/> clears the card
    /// collection and would wipe the unsaved edits of every card that is currently open in inline
    /// editing.
    /// </summary>
    private async Task RefreshProgressAsync(CancellationToken cancellationToken = default)
    {
        var progress = await _tracking.GetDayProgressAsync(SelectedDate, cancellationToken);
        Percentage = progress.Percentage;
        DoneCount = progress.Completed;
        MissedCount = progress.Missed;
        PendingCount = progress.Pending;

        // The two day-wide buttons (mark all done / mark all missed) enable and disable with the cards.
        OnPropertyChanged(nameof(CanMarkAllDone));
        OnPropertyChanged(nameof(CanMarkAllMissed));
    }

    /// <inheritdoc />
    public async Task RefreshCardAsync(MealCardViewModel card, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);

        // Only this slot is re-read; the other cards keep whatever state they hold right now.
        var entry = await _tracking.GetOrCreateSlotAsync(card.Date, card.CategoryKey, cancellationToken);
        card.Reload(entry);
    }

    /// <inheritdoc />
    public async Task ChangeStatusAsync(MealCardViewModel card, MealStatus target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);

        // A meal with neither a title nor a description may not be confirmed (CompletionRules):
        // the dialog says why and offers the way out - "Edit" opens the inline editor right away,
        // "OK" dismisses and leaves the meal pending. Nothing is marked before the card has content.
        if (target == MealStatus.Done
            && string.IsNullOrWhiteSpace(card.Title)
            && string.IsNullOrWhiteSpace(card.Ingredients))
        {
            var editRequested = await _dialogs.OfferAsync(
                "Meal_EmptyDoneTitle",
                "Meal_EmptyDoneMessage",
                "Common_Edit",
                card.CategoryName);
            if (editRequested)
            {
                card.EditCommand.Execute(null);
            }

            return;
        }

        card.IsBusy = true;
        try
        {
            // The service enforces the date rules (future meals stay pending), so a click on a
            // locked card resolves to a harmless no-op inside CompletionRules.
            var entry = target switch
            {
                MealStatus.Done => await _tracking.MarkDoneAsync(card.Id, cancellationToken),
                MealStatus.Missed => await _tracking.MarkMissedAsync(card.Id, cancellationToken),
                _ => throw new InvalidOperationException("Pending is the default state, not a transition target."),
            };

            card.Reload(entry);
            await RefreshProgressAsync(cancellationToken);
        }
        finally
        {
            card.IsBusy = false;
        }
    }

    /// <inheritdoc />
    public async Task AddPhotoAsync(MealCardViewModel card, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);

        var remaining = Core.BusinessRules.PhotoRules.MaxPhotosPerMeal - card.Photos.Count;
        if (remaining <= 0)
        {
            card.ErrorMessage = _localization.Get("Photo_LimitReached");
            return;
        }

        var files = await _filePicker.PickImagesAsync(remaining, cancellationToken);
        card.ErrorMessage = null;

        foreach (var file in files)
        {
            try
            {
                await _tracking.AddPhotoAsync(card.Id, file, cancellationToken);
            }
            catch (Core.BusinessRules.ValidationException ex)
            {
                // The reason code is a dotted domain code ("photo.duplicate"); it becomes the resource
                // key of the explanation through the single mapping used for every validation message.
                card.ErrorMessage = _localization.Get(LocalizationKeys.ForValidation(ex.Result.Failures[0].Code));
            }
        }

        await AttachPhotosAsync(card, cancellationToken);
        OnPropertyChanged(nameof(card.HasPhotos));
        OnPropertyChanged(nameof(card.CanAddPhoto));
        OnPropertyChanged(nameof(card.PhotoLimitText));
    }

    /// <inheritdoc />
    public async Task RemovePhotoAsync(MealCardViewModel card, MealPhotoViewModel photo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(photo);

        var confirmed = await _dialogs.ConfirmAsync("Photo_RemoveTitle", "Photo_RemoveMessage");
        if (!confirmed)
        {
            return;
        }

        await _tracking.RemovePhotoAsync(photo.Id, cancellationToken);
        await AttachPhotosAsync(card, cancellationToken);
        OnPropertyChanged(nameof(card.HasPhotos));
        OnPropertyChanged(nameof(card.CanAddPhoto));
        OnPropertyChanged(nameof(card.PhotoLimitText));
    }

    /// <inheritdoc />
    public Task OpenPhotoAsync(MealPhotoViewModel photo)
    {
        ArgumentNullException.ThrowIfNull(photo);
        return _filePicker.OpenWithShellAsync(photo.FullSizePath ?? photo.LocalPath ?? string.Empty);
    }

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken = default) => LoadAsync(cancellationToken);
}
