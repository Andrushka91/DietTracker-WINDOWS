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
    public async Task SaveCardAsync(MealCardViewModel card, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);

        var entry = await _tracking.GetOrCreateSlotAsync(card.Date, card.CategoryKey, cancellationToken);
        if (!card.ApplyEditsTo(entry))
        {
            // Invalid planned time typed by the user; the card shows the explanation.
            return;
        }

        var validation = await _tracking.SaveEditsAsync(entry, cancellationToken);
        if (!validation.IsValid)
        {
            card.ErrorMessage = string.Join(
                Environment.NewLine,
                validation.Failures.Select(f => _localization.Get(f.Code)));
            return;
        }

        card.Reload(entry);
        await LoadAsync(cancellationToken);
        _syncEngine.RequestSync(SyncTrigger.LocalChange);
    }

    /// <inheritdoc />
    public async Task ChangeStatusAsync(MealCardViewModel card, MealStatus target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);

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
            await LoadAsync(cancellationToken);
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

        // The service silently keeps a single copy when identical bytes are already attached to
        // this meal (dedupe by content hash); track what the card knows so the user gets told.
        var known = card.Photos.Select(photo => photo.Id).ToHashSet();
        foreach (var file in files)
        {
            try
            {
                var imported = await _tracking.AddPhotoAsync(card.Id, file, cancellationToken);
                if (!known.Add(imported.Id))
                {
                    card.ErrorMessage = _localization.Get("Photo_AlreadyUploaded");
                }
            }
            catch (Core.BusinessRules.ValidationException ex)
            {
                card.ErrorMessage = _localization.Get(ex.Result.Failures[0].Code);
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
