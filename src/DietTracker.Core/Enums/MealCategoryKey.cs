namespace DietTracker.Core.Enums;

/// <summary>
/// The five permanent daily meal slots. These keys are part of the persisted data
/// contract and must never be renumbered or removed.
/// </summary>
public enum MealCategoryKey
{
    /// <summary>Breakfast / Desayuno / Mic dejun.</summary>
    Breakfast = 1,

    /// <summary>Morning snack / Almuerzo / Gustare de dimineață.</summary>
    MorningSnack = 2,

    /// <summary>Lunch / Comida / Prânz.</summary>
    Lunch = 3,

    /// <summary>Afternoon snack / Merienda / Gustare de după-amiază.</summary>
    AfternoonSnack = 4,

    /// <summary>Dinner / Cena / Cină.</summary>
    Dinner = 5,
}
