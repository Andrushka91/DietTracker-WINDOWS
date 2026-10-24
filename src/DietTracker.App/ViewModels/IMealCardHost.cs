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

    /// <summary>Persists the edited content of a card.</summary>
    Task SaveCardAsync(MealCardViewModel card, CancellationToken cancellationToken = default);

    /// <summary>Applies a status transition (DONE or MISSED). The date rules are enforced by the
    /// service: today and past dates change, future meals stay pending.</summary>
    Task ChangeStatusAsync(MealCardViewModel card, MealStatus target, CancellationToken cancellationToken = default);

    /// <summary>Adds a photograph through the native Windows file picker.</summary>
    Task AddPhotoAsync(MealCardViewModel card, CancellationToken cancellationToken = default);

    /// <summary>Removes a photograph.</summary>
    Task RemovePhotoAsync(MealCardViewModel card, MealPhotoViewModel photo, CancellationToken cancellationToken = default);

    /// <summary>Opens a photograph full size in the shell.</summary>
    Task OpenPhotoAsync(MealPhotoViewModel photo);

    /// <summary>Refreshes the host after a change (progress indicator, statistics).</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
