using DietTracker.Core.Enums;
using DietTracker.Core.Models;

namespace DietTracker.Core.BusinessRules;

/// <summary>
/// Mandatory business rules for the five permanent daily meal cards.
/// </summary>
/// <remarks>
/// The five meal categories (Breakfast, Morning Snack, Lunch, Afternoon Snack, Dinner) are
/// immutable in number and identity. Users may change their content, colour, icon, default time
/// and display order, but they can never delete a card, and every calendar date always exposes
/// exactly five slots.
/// </remarks>
public static class MealSlotRules
{
    /// <summary>Number of permanent meal slots per day (always five).</summary>
    public const int SlotCount = 5;

    private static readonly MealCategoryKey[] OrderedKeys =
    [
        MealCategoryKey.Breakfast,
        MealCategoryKey.MorningSnack,
        MealCategoryKey.Lunch,
        MealCategoryKey.AfternoonSnack,
        MealCategoryKey.Dinner,
    ];

    /// <summary>The five permanent slot keys in canonical display order.</summary>
    public static IReadOnlyList<MealCategoryKey> AllSlots => OrderedKeys;

    /// <summary>Slots rendered as full size "main meal" cards.</summary>
    public static IReadOnlyList<MealCategoryKey> MainMealSlots { get; } =
    [
        MealCategoryKey.Breakfast,
        MealCategoryKey.Lunch,
        MealCategoryKey.Dinner,
    ];

    /// <summary>Slots rendered as compact snack cards.</summary>
    public static IReadOnlyList<MealCategoryKey> SnackSlots { get; } =
    [
        MealCategoryKey.MorningSnack,
        MealCategoryKey.AfternoonSnack,
    ];

    /// <summary>True when the key is one of the five permanent slots (always true for valid enum values).</summary>
    public static bool IsPermanent(MealCategoryKey key) => OrderedKeys.Contains(key);

    /// <summary>True when the slot is a main meal and therefore uses the larger card layout.</summary>
    public static bool IsMainMeal(MealCategoryKey key) => MainMealSlots.Contains(key);

    /// <summary>True when the slot is a compact snack slot.</summary>
    public static bool IsSnack(MealCategoryKey key) => SnackSlots.Contains(key);

    /// <summary>
    /// Creates the default definition of the five permanent categories. Never returns fewer or
    /// more than five items. The default pictograms are food emoji - a croissant for breakfast, a
    /// plate with cutlery for lunch, a cookie for the two snack cards and a pot of food for dinner -
    /// because the Segoe Fluent icon font contains no food glyphs.
    /// </summary>
    public static IReadOnlyList<MealCategory> CreateDefaults() =>
    [
        new MealCategory
        {
            FixedCategoryKey = MealCategoryKey.Breakfast,
            Name = string.Empty,
            Color = "#FF0F6CBD",
            Icon = "\U0001F950\uFE0F",
            DefaultTime = new TimeOnly(7, 30),
            DisplayOrder = 0,
        },
        new MealCategory
        {
            FixedCategoryKey = MealCategoryKey.MorningSnack,
            Name = string.Empty,
            Color = "#FF8764B8",
            Icon = "\U0001F36A\uFE0F",
            DefaultTime = new TimeOnly(10, 30),
            DisplayOrder = 1,
        },
        new MealCategory
        {
            FixedCategoryKey = MealCategoryKey.Lunch,
            Name = string.Empty,
            Color = "#FF107C10",
            Icon = "\U0001F37D\uFE0F",
            DefaultTime = new TimeOnly(13, 30),
            DisplayOrder = 2,
        },
        new MealCategory
        {
            FixedCategoryKey = MealCategoryKey.AfternoonSnack,
            Name = string.Empty,
            Color = "#FFCA5010",
            Icon = "\U0001F36A\uFE0F",
            DefaultTime = new TimeOnly(17, 0),
            DisplayOrder = 3,
        },
        new MealCategory
        {
            FixedCategoryKey = MealCategoryKey.Dinner,
            Name = string.Empty,
            Color = "#FFB146C2",
            Icon = "\U0001F372\uFE0F",
            DefaultTime = new TimeOnly(20, 0),
            DisplayOrder = 4,
        },
    ];

    /// <summary>
    /// Verifies that a category collection is still complete and reports the missing permanent
    /// slots. Used by the integrity checker and by the tests that prove the cards cannot be removed.
    /// </summary>
    public static IReadOnlyList<MealCategoryKey> FindMissingSlots(IEnumerable<MealCategory> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);
        var present = categories.Select(c => c.FixedCategoryKey).ToHashSet();
        return OrderedKeys.Where(key => !present.Contains(key)).ToArray();
    }

    /// <summary>
    /// Merges persisted categories with the permanent defaults so that a database which lost a
    /// category row (for example after a partial restore) is healed instead of showing fewer than
    /// five cards. Existing user customizations are preserved.
    /// </summary>
    public static IReadOnlyList<MealCategory> Normalize(IEnumerable<MealCategory>? persisted)
    {
        var existing = (persisted ?? []).ToList();
        foreach (var defaultCategory in CreateDefaults())
        {
            if (existing.Any(c => c.FixedCategoryKey == defaultCategory.FixedCategoryKey))
            {
                continue;
            }

            existing.Add(defaultCategory);
        }

        return existing
            .Where(c => IsPermanent(c.FixedCategoryKey))
            .GroupBy(c => c.FixedCategoryKey)
            .Select(g => g.First())
            .OrderBy(c => c.FixedCategoryKey)
            .ToArray();
    }

    /// <summary>
    /// Returns the meal category key that follows the supplied one in display order (wraps around).
    /// </summary>
    public static MealCategoryKey Next(MealCategoryKey current)
    {
        var index = Array.IndexOf(OrderedKeys, current);
        return OrderedKeys[(index + 1) % OrderedKeys.Length];
    }
}
