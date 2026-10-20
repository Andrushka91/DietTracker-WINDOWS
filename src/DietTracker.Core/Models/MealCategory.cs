using DietTracker.Core.Enums;

namespace DietTracker.Core.Models;

/// <summary>
/// One of the five permanent meal categories. Categories are configuration data,
/// separate from the daily <see cref="MealEntry"/> instances, and can never be deleted
/// by the user (see <c>MealSlotRules</c>).
/// </summary>
public sealed class MealCategory
{
    /// <summary>Stable identifier (GUID, persisted as TEXT).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Fixed logical key of one of the five permanent slots.</summary>
    public MealCategoryKey FixedCategoryKey { get; set; }

    /// <summary>User editable display name. Empty means "use the localized default".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Card background color in <c>#AARRGGBB</c> form.</summary>
    public string Color { get; set; } = "#FF107C10";

    /// <summary>Icon glyph (Segoe Fluent Icons / Segoe MDL2 Assets code point) shown on the card.</summary>
    public string Icon { get; set; } = "\uE7BF";

    /// <summary>Default planned time of day for this slot.</summary>
    public TimeOnly DefaultTime { get; set; }

    /// <summary>Ascending display order of the five cards on the dashboard.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>True for Breakfast / Lunch / Dinner: rendered with the larger "main meal" card.</summary>
    public bool IsMainMeal => FixedCategoryKey is MealCategoryKey.Breakfast or MealCategoryKey.Lunch or MealCategoryKey.Dinner;
}
