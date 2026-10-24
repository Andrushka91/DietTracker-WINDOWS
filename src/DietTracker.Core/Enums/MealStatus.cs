namespace DietTracker.Core.Enums;

/// <summary>
/// State of a meal slot on a specific calendar date. Exactly three states exist: PENDING is the
/// default every untouched meal keeps, DONE means the user confirmed the meal was eaten and MISSED
/// means the user explicitly marked it as not eaten. Time never changes a state on its own.
/// </summary>
public enum MealStatus
{
    /// <summary>Consumption has not been confirmed nor rejected yet (the default state).</summary>
    Pending = 0,

    /// <summary>The user explicitly confirmed consumption.</summary>
    Done = 1,

    /// <summary>The user explicitly marked the meal as not eaten. Only reachable on today or a past
    /// date - a future meal always stays PENDING.</summary>
    Missed = 2,
}
