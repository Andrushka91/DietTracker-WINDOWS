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
