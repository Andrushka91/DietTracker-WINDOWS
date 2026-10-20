using DietTracker.Core.Enums;

namespace DietTracker.App.ViewModels;

/// <summary>Editor for a single ingredient line inside a meal card while the card is in edit mode.</summary>
public sealed partial class IngredientLineViewModel
{
    /// <summary>Localized name of a measurement unit.</summary>
    public string LocalizedUnit => LocalizeUnit(Unit);

    /// <summary>Resolves the localized label of a unit through the host resource keys.</summary>
    internal static string LocalizeUnit(MeasurementUnit unit)
        => LocalizationLookup?.Invoke($"Unit_{unit}") ?? unit.ToString();

    /// <summary>
    /// Lookup callback installed by the host, so this helper can stay UI agnostic while still using
    /// the application resources instead of hardcoded strings.
    /// </summary>
    internal static Func<string, string>? LocalizationLookup { get; set; }
}
