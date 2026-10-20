using DietTracker.Core.Models;

namespace DietTracker.Core.BusinessRules;

/// <summary>
/// Decides whether a meal slot carries user content. Placeholder rows (materialized empty cards)
/// carry no content and are therefore not uploaded to the cloud, which keeps cloud traffic and
/// the journal small.
/// </summary>
public static class MealContentRules
{
    /// <summary>True when the slot holds anything the user actually typed, attached or confirmed.</summary>
    public static bool HasUserContent(MealEntry? entry)
    {
        if (entry is null)
        {
            return false;
        }

        return entry.Status != Enums.MealStatus.Pending
               || entry.CompletedAt is not null
               || !string.IsNullOrWhiteSpace(entry.Title)
               || !string.IsNullOrWhiteSpace(entry.Ingredients)
               || !string.IsNullOrWhiteSpace(entry.Notes)
               || entry.Quantities.Count > 0
               || entry.Photos.Count > 0
               || entry.PlannedTime is not null;
    }

    /// <summary>True when the slot is a pure placeholder and may be skipped during synchronization.</summary>
    public static bool IsPlaceholder(MealEntry? entry) => !HasUserContent(entry);
}

/// <summary>Thrown when a domain object fails validation before it is persisted.</summary>
public sealed class ValidationException : Exception
{
    public ValidationException(Validation.ValidationResult result)
        : base(result.ToLogString())
        => Result = result;

    /// <summary>Detailed validation failures.</summary>
    public Validation.ValidationResult Result { get; }
}
