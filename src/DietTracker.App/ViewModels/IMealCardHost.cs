using DietTracker.Core.Enums;

namespace DietTracker.App.ViewModels;

/// <summary>
/// Callbacks a meal card needs from its page: localization, the current date and the data operations
/// (persist, status transition, photographs). Keeping this contract separate means the
/// card never talks to persistence directly.
/// </summary>
public interface IMealCardHost
{
    /// <summary>Resolves a resource key for the active language.</summary>
    string Localize(string resourceKey);

    /// <summary>Today's date in the user's time zone.</summary>
    DateOnly Today { get; }

    /// <summary>
    /// Persists the edited content of a card.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the card passed validation and was written;
    /// <see langword="false"/> when the card refused the edits (a mandatory field is still empty
    /// or the typed time is invalid) - it then keeps its editor open and shows the explanation.
    /// </returns>
    Task<bool> SaveCardAsync(MealCardViewModel card, CancellationToken cancellationToken = default);

    /// <summary>Wipes the whole content of one card - text, planned time, state and photographs -
    /// after the user confirmed it in a yes/no dialog. Declining the dialog changes nothing.</summary>
    Task ClearCardAsync(MealCardViewModel card, CancellationToken cancellationToken = default);

    /// <summary>Applies a status transition (DONE or MISSED). The date rules are enforced by the
    /// service: today and past dates change, future meals stay pending.</summary>
    Task ChangeStatusAsync(MealCardViewModel card, MealStatus target, CancellationToken cancellationToken = default);

    /// <summary>Adds a photograph through the native Windows file picker.</summary>
    Task AddPhotoAsync(MealCardViewModel card, CancellationToken cancellationToken = default);

    /// <summary>Removes a photograph.</summary>
    Task RemovePhotoAsync(MealCardViewModel card, MealPhotoViewModel photo, CancellationToken cancellationToken = default);

    /// <summary>Opens a photograph full size in the shell.</summary>
    Task OpenPhotoAsync(MealPhotoViewModel photo);

    /// <summary>Reloads one card from persistence after its pending edits were discarded. Only that
    /// single slot is touched: every other card - including one that is open in inline editing with
    /// unsaved changes - keeps its state.</summary>
    Task RefreshCardAsync(MealCardViewModel card, CancellationToken cancellationToken = default);
}
