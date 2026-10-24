using DietTracker.Core.Enums;

namespace DietTracker.Core.Models;

/// <summary>Progress for a single calendar date.</summary>
public sealed record DayProgress
{
    public required DateOnly Date { get; init; }

    /// <summary>Always five: the number of permanent meal slots.</summary>
    public int TotalSlots { get; init; } = 5;

    /// <summary>Meals the user confirmed.</summary>
    public int Completed { get; init; }

    /// <summary>Meals the user explicitly marked as not eaten (the red state).</summary>
    public int Missed { get; init; }

    /// <summary>Slots that are neither confirmed nor missed: untouched placeholders and meals the
    /// user has not decided on yet. Never part of any ratio; time alone never changes this state.</summary>
    public int Pending { get; init; }

    /// <summary>The meals statistics are built from: confirmed plus missed. Untouched slots are
    /// deliberately excluded, so an untouched day contributes nothing but its day count.</summary>
    public int Decided => Completed + Missed;

    /// <summary>True when at least one meal was decided (confirmed or missed).</summary>
    public bool HasDecided => Decided > 0;

    /// <summary>Share of the decided meals that were confirmed, in the range 0..100. Zero when
    /// nothing was decided yet, so an untouched day never reports a value.</summary>
    public double Percentage => Decided == 0 ? 0d : Math.Round(Completed * 100d / Decided, 2);

    /// <summary>True when all five permanent slots were confirmed.</summary>
    public bool IsFullyCompleted => Completed == TotalSlots;

    public IReadOnlyList<MealSlotStatus> Slots { get; init; } = [];
}

/// <summary>Status of one meal slot, used for the dashboard and statistics grids.</summary>
public sealed record MealSlotStatus
{
    public required MealCategoryKey MealCategoryKey { get; init; }

    public required MealStatus Status { get; init; }

    /// <summary>True when the user explicitly marked this slot as missed - the stored state, never
    /// derived from the clock.</summary>
    public bool IsMissed => Status == MealStatus.Missed;

    public string Title { get; init; } = string.Empty;

    public TimeOnly? PlannedTime { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public int PhotoCount { get; init; }
}

/// <summary>Aggregated progress for a period (week or month) plus its trend information.</summary>
public sealed record PeriodProgress
{
    public required DateOnly From { get; init; }

    public required DateOnly To { get; init; }

    /// <summary>Permanent slots of the period (five per day) - the raw size, not a denominator.</summary>
    public int TotalSlots { get; init; }

    /// <summary>Confirmed meals of the period.</summary>
    public int Completed { get; init; }

    /// <summary>Recorded meals whose planned time passed without confirmation.</summary>
    public int Missed { get; init; }

    /// <summary>Slots that are neither confirmed nor missed (untouched or not due yet).</summary>
    public int Pending { get; init; }

    /// <summary>Confirmed plus missed - the denominator of every period ratio.</summary>
    public int Decided => Completed + Missed;

    /// <summary>True when at least one meal of the period was decided.</summary>
    public bool HasDecided => Decided > 0;

    public double Percentage => Decided == 0 ? 0d : Math.Round(Completed * 100d / Decided, 2);

    /// <summary>Number of days in the period on which all five meals were completed.</summary>
    public int FullyCompletedDays { get; init; }

    public IReadOnlyList<DayProgress> Days { get; init; } = [];
}

/// <summary>One point of a trend chart.</summary>
public sealed record TrendPoint
{
    public required DateOnly Date { get; init; }

    public required double Percentage { get; init; }

    public int Completed { get; init; }

    public int Missed { get; init; }

    /// <summary>Confirmed plus missed meals of that day; zero when the day has no decided meal.</summary>
    public int Decided => Completed + Missed;
}

/// <summary>Alert raised when a logically impossible data state is detected during an integrity check.</summary>
public sealed record DataIntegrityIssue
{
    public required string Code { get; init; }

    public required string Description { get; init; }

    public Guid? EntityId { get; init; }
}
