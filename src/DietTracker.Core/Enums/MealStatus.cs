namespace DietTracker.Core.Enums;

/// <summary>State of a meal slot on a specific calendar date.</summary>
public enum MealStatus
{
    /// <summary>Consumption has not been confirmed yet.</summary>
    Pending = 0,

    /// <summary>The user explicitly confirmed consumption.</summary>
    Done = 1,

    /// <summary>The user explicitly marked the meal as missed (never set automatically for future dates).</summary>
    Missed = 2,
}
