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
        card.ApplyEditsTo(entry);

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
            var entry = target switch
            {
                MealStatus.Done => await _tracking.MarkDoneAsync(card.Id, cancellationToken),
                MealStatus.Missed => await _tracking.MarkMissedAsync(card.Id, cancellationToken),
                _ => await _tracking.UndoDoneAsync(card.Id, cancellationToken),
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
        foreach (var file in files)
        {
            try
            {
                await _tracking.AddPhotoAsync(card.Id, file, cancellationToken);
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
    public async Task CopyToDateAsync(MealCardViewModel card, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);

        var target = await _dialogs.PickDateAsync("Meal_CopyToDate", card.Date.AddDays(1));
        if (target is null)
        {
            return;
        }

        await _tracking.CopyMealToDateAsync(card.Id, target.Value, cancellationToken);
        StatusMessage = _localization.Format("Meal_Copied", target.Value.ToString("d"));
    }

    /// <inheritdoc />
    public async Task SaveAsTemplateAsync(MealCardViewModel card, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(card);

        var defaultName = string.IsNullOrWhiteSpace(card.Title) ? card.CategoryName : card.Title;
        var name = await _dialogs.PromptTextAsync("Template_NameTitle", defaultName);
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var entry = await _tracking.GetOrCreateSlotAsync(card.Date, card.CategoryKey, cancellationToken);
        await _tracking.SaveTemplateAsync(entry, name, cancellationToken);
        StatusMessage = _localization.Get("Template_Saved");
    }

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken = default) => LoadAsync(cancellationToken);
}
