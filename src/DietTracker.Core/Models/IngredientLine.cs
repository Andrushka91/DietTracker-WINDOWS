using DietTracker.Core.Enums;

namespace DietTracker.Core.Models;

/// <summary>
/// A single ingredient line of a meal: what it is, how much and in which unit.
/// Serialized to JSON inside <see cref="MealEntry.Quantities"/> so that the relational
/// schema stays simple while remaining fully queryable through the aggregate root.
/// </summary>
public sealed record IngredientLine
{
    /// <summary>Ingredient name, for example "Oat flakes".</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Numeric amount; <see langword="null"/> when the amount is not relevant (for example "salt to taste").</summary>
    public decimal? Amount { get; init; }

    /// <summary>Measurement unit for <see cref="Amount"/>.</summary>
    public MeasurementUnit Unit { get; init; } = MeasurementUnit.None;

    /// <summary>Free-form remarks, for example "finely chopped".</summary>
    public string? Note { get; init; }

    /// <summary>Creates an <see cref="IngredientLine"/> with a display friendly shape.</summary>
    public static IngredientLine Create(string name, decimal? amount = null, MeasurementUnit unit = MeasurementUnit.None, string? note = null)
        => new() { Name = name, Amount = amount, Unit = unit, Note = note };
}
