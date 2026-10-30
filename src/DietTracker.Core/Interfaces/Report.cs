using DietTracker.Core.Models;

namespace DietTracker.Core.Interfaces;

/// <summary>
/// Writes a printable PDF report of the stored meal records. The report is produced from the local
/// database only: no account, no connection and no photograph is needed, so it works in Guest Mode
/// exactly like a backup does.
/// </summary>
public interface IMealReportService
{
    /// <summary>
    /// Exports the meals of the requested period (the whole history when no period is given) into a
    /// PDF document. The file is created from scratch; nothing in the database is modified.
    /// </summary>
    Task<MealReportSummary> ExportPdfAsync(
        string targetFilePath,
        MealReportOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Writes a printable PDF of one month in the layout of the calendar screen: the seven columns of
/// the weekdays, one square per day with the meals that were done on it and the legend of the day
/// states. The whole month fits a single A4 page. Like the meal report, the export only reads the
/// local database.
/// </summary>
public interface ICalendarReportService
{
    /// <summary>
    /// Exports the calendar of <paramref name="month"/> (any day of the month; the year and month
    /// decide the grid) into a one-page PDF document. The file is created from scratch; nothing in
    /// the database is modified.
    /// </summary>
    Task<CalendarReportSummary> ExportMonthPdfAsync(
        string targetFilePath,
        DateOnly month,
        CalendarReportOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports several months into a single PDF document with exactly one page per month, in
    /// chronological order. The months may arrive in any order and may repeat: every month is
    /// normalized to its first day, duplicates are dropped and the list is sorted, so the document
    /// always reads January to December. Nothing in the database is modified.
    /// </summary>
    Task<CalendarReportSummary> ExportMonthsPdfAsync(
        string targetFilePath,
        IReadOnlyList<DateOnly> months,
        CalendarReportOptions options,
        CancellationToken cancellationToken = default);
}
