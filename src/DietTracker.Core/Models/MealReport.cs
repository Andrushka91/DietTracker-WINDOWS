using DietTracker.Core.Enums;

namespace DietTracker.Core.Models;

/// <summary>
/// Everything the PDF report needs besides the records themselves: the period, the culture used to
/// format dates and the localized texts. The service that writes the file has no access to the
/// resource files of the interface, so the caller passes the labels it already localized; every
/// label has an English default, which keeps the report usable from a test or a headless call.
/// </summary>
public sealed record MealReportOptions
{
    /// <summary>First day of the report, or <see langword="null"/> for "since the first record".</summary>
    public DateOnly? From { get; init; }

    /// <summary>Last day of the report, or <see langword="null"/> for "up to the last record".</summary>
    public DateOnly? To { get; init; }

    /// <summary>Culture of the dates and the numbers (BCP-47 name, for example <c>ro-RO</c>).</summary>
    public string CultureName { get; init; } = "en-US";

    /// <summary>Format of the planned time of a meal (<c>HH:mm</c> or <c>h:mm tt</c>).</summary>
    public string TimeFormat { get; init; } = "HH:mm";

    /// <summary>Title of the document, repeated in the header of every page after the first.</summary>
    public string DocumentTitle { get; init; } = "Diet Tracker - Meal report";

    /// <summary>Format of the period line (<c>{0}</c> = first day, <c>{1}</c> = last day).</summary>
    public string PeriodFormat { get; init; } = "Period: {0} - {1}";

    /// <summary>Format of the generation line (<c>{0}</c> = date and time).</summary>
    public string GeneratedFormat { get; init; } = "Generated on {0}";

    /// <summary>
    /// Format of the summary line (<c>{0}</c> = meals, <c>{1}</c> = done, <c>{2}</c> = missed,
    /// <c>{3}</c> = pending).
    /// </summary>
    public string SummaryFormat { get; init; } = "{0} meals recorded: {1} done, {2} missed, {3} pending";

    /// <summary>Shown instead of the table when the period holds no meal.</summary>
    public string EmptyText { get; init; } = "No meals were recorded in this period.";

    /// <summary>Format of the page number (<c>{0}</c> = current page, <c>{1}</c> = page count).</summary>
    public string PageFormat { get; init; } = "Page {0} of {1}";

    /// <summary>Localized name of each permanent meal slot (the enum name is used when missing).</summary>
    public IReadOnlyDictionary<MealCategoryKey, string> CategoryNames { get; init; } =
        new Dictionary<MealCategoryKey, string>();

    /// <summary>Localized name of each meal state (the enum name is used when missing).</summary>
    public IReadOnlyDictionary<MealStatus, string> StatusNames { get; init; } =
        new Dictionary<MealStatus, string>();
}

/// <summary>What a generated PDF report contains.</summary>
public sealed record MealReportSummary
{
    public required string FilePath { get; init; }

    /// <summary>Days that hold at least one meal (the days printed in the report).</summary>
    public int Days { get; init; }

    /// <summary>Meals printed in the report.</summary>
    public int MealEntries { get; init; }

    /// <summary>Meals the user confirmed as eaten.</summary>
    public int Done { get; init; }

    /// <summary>Number of pages of the document.</summary>
    public int Pages { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
