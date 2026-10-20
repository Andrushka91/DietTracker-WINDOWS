using DietTracker.Core.Enums;

namespace DietTracker.Core.Models;

/// <summary>Progress for a single calendar date.</summary>
public sealed record DayProgress
{
    public required DateOnly Date { get; init; }

    /// <summary>Always five: the number of permanent meal slots.</summary>
    public int TotalSlots { get; init; } = 5;

    public int Completed { get; init; }

    public int Pending { get; init; }

    public int Missed { get; init; }

    /// <summary>Completion percentage in the range 0..100.</summary>
    public double Percentage => TotalSlots == 0 ? 0d : Math.Round(Completed * 100d / TotalSlots, 2);

    public bool IsFullyCompleted => Completed == TotalSlots;

    public IReadOnlyList<MealSlotStatus> Slots { get; init; } = [];
}

/// <summary>Status of one meal slot, used for the dashboard and statistics grids.</summary>
public sealed record MealSlotStatus
{
    public required MealCategoryKey MealCategoryKey { get; init; }

    public required MealStatus Status { get; init; }

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

    public int TotalSlots { get; init; }

    public int Completed { get; init; }

    public int Missed { get; init; }

    public int Pending { get; init; }

    public double Percentage => TotalSlots == 0 ? 0d : Math.Round(Completed * 100d / TotalSlots, 2);

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
}

/// <summary>Alert raised when a logically impossible data state is detected during an integrity check.</summary>
public sealed record DataIntegrityIssue
{
    public required string Code { get; init; }

    public required string Description { get; init; }

    public Guid? EntityId { get; init; }
}
