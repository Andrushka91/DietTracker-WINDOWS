using DietTracker.Core.Enums;

namespace DietTracker.Core.Models;

/// <summary>
/// Everything the calendar PDF needs besides the records themselves: the culture, the localized
/// texts of the header, the weekday row, the legend and the mini cards. The service that writes the
/// file has no access to the resource files of the interface, so the caller passes the labels it
/// already localized; every label has an English default, which keeps the export usable from a test
/// or a headless call.
/// </summary>
public sealed record CalendarReportOptions
{
    /// <summary>Culture of the dates and the numbers (BCP-47 name, for example <c>ro-RO</c>).</summary>
    public string CultureName { get; init; } = "en-US";

    /// <summary>Title of the document (also the metadata title of the PDF file).</summary>
    public string DocumentTitle { get; init; } = "Diet Tracker - Calendar";

    /// <summary>
    /// Format of the summary line under the month heading ({0} confirmed, {1} missed, {2} pending) -
    /// the same shape the calendar page shows under its header.
    /// </summary>
    public string SummaryFormat { get; init; } = "{0} confirmed, {1} missed, {2} pending";

    /// <summary>Format of the footer line ({0} = the moment the file was produced).</summary>
    public string GeneratedFormat { get; init; } = "Generated on {0}";

    /// <summary>The seven localized weekday names, Monday first (the row above the panel's grid).</summary>
    public IReadOnlyList<string> WeekdayNames { get; init; } = [];

    /// <summary>Legend under the grid: the day where every meal was eaten.</summary>
    public string LegendGreen { get; init; } = "Green: all meals eaten";

    /// <summary>Legend under the grid: the day where at least one meal was missed.</summary>
    public string LegendRed { get; init; } = "Red: at least one meal missed";

    /// <summary>Legend under the grid: the day the user has not decided on yet.</summary>
    public string LegendPending { get; init; } = "Gray: pending";

    /// <summary>The date the calendar page would mark as "today" (null = no highlight).</summary>
    public DateOnly? Today { get; init; }

    /// <summary>Localized name of each permanent meal slot (the enum name is used when missing).</summary>
    public IReadOnlyDictionary<MealCategoryKey, string> CategoryNames { get; init; } =
        new Dictionary<MealCategoryKey, string>();

    /// <summary>Localized name of each meal state (the enum name is used when missing).</summary>
    public IReadOnlyDictionary<MealStatus, string> StatusNames { get; init; } =
        new Dictionary<MealStatus, string>();
}

/// <summary>What a generated calendar PDF contains.</summary>
public sealed record CalendarReportSummary
{
    public required string FilePath { get; init; }

    /// <summary>First day of the earliest exported month.</summary>
    public required DateOnly Month { get; init; }

    /// <summary>Number of months in the document (always one page per month).</summary>
    public int MonthCount { get; init; } = 1;

    /// <summary>Days of all exported months together (the squares of every grid of the document).</summary>
    public int Days { get; init; }

    /// <summary>Meals the user recorded in the month (the mini cards drawn in the squares).</summary>
    public int MealEntries { get; init; }

    /// <summary>Meals the user confirmed as eaten in the month.</summary>
    public int Done { get; init; }

    /// <summary>Always one: the whole month fits a single A4 page.</summary>
    public int Pages { get; init; } = 1;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}