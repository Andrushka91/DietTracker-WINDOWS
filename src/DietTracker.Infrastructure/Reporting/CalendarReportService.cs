using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Reporting;

/// <summary>
/// Builds the printable one-page month calendar from the local database.
/// </summary>
/// <remarks>
/// The export mirrors the calendar screen: the same Monday first grid, the same day states, the
/// same mini cards and the same legend, so a printed month reads exactly like the application. It
/// only reads the local working copy - no account, no connection and no photograph is needed, so it
/// works in Guest Mode exactly like a backup does.
/// </remarks>
public sealed class CalendarReportService : ICalendarReportService
{
    private readonly IMealTrackingService _tracking;
    private readonly ILogger<CalendarReportService> _logger;

    public CalendarReportService(
        IMealTrackingService tracking,
        ILogger<CalendarReportService> logger)
    {
        _tracking = tracking;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<CalendarReportSummary> ExportMonthPdfAsync(
        string targetFilePath,
        DateOnly month,
        CalendarReportOptions options,
        CancellationToken cancellationToken = default)
        => ExportMonthsPdfAsync(targetFilePath, [month], options, cancellationToken);

    /// <inheritdoc />
    public async Task<CalendarReportSummary> ExportMonthsPdfAsync(
        string targetFilePath,
        IReadOnlyList<DateOnly> months,
        CalendarReportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFilePath);
        ArgumentNullException.ThrowIfNull(months);
        ArgumentNullException.ThrowIfNull(options);

        // Every month is normalized to its first day, duplicates are dropped and the list is
        // sorted, so the document always reads in chronological order no matter what the caller
        // (a date range typed backwards, for example) produced.
        var plan = months
            .Select(month => new DateOnly(month.Year, month.Month, 1))
            .Distinct()
            .OrderBy(month => month)
            .ToArray();

        if (plan.Length == 0)
        {
            throw new ArgumentException("At least one month is required.", nameof(months));
        }

        var generatedAt = DateTimeOffset.Now;
        var document = new PdfTextDocument(options.DocumentTitle);

        var days = 0;
        var meals = 0;
        var done = 0;

        // One month per page: the layout already renders a single month on a single A4 page, so the
        // document simply grows by one page per entry of the plan.
        foreach (var month in plan)
        {
            var progress = await _tracking
                .GetMonthProgressAsync(month, cancellationToken)
                .ConfigureAwait(false);

            var layout = CalendarReportLayout.Render(document, options, month, progress, generatedAt);
            days += layout.Days;
            meals += layout.Meals;
            done += progress.Completed;
        }

        var directory = Path.GetDirectoryName(targetFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllBytesAsync(targetFilePath, document.ToArray(), cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Calendar PDF with {Months} month(s), {Meals} recorded meal(s) on {Pages} page(s) written to '{Path}'.",
            plan.Length,
            meals,
            document.PageCount,
            targetFilePath);

        return new CalendarReportSummary
        {
            FilePath = targetFilePath,
            Month = plan[0],
            MonthCount = plan.Length,
            Days = days,
            MealEntries = meals,
            Done = done,
            Pages = document.PageCount,
            CreatedAt = generatedAt,
        };
    }
}