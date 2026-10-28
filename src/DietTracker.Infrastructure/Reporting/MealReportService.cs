using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Reporting;

/// <summary>
/// Builds the printable PDF report from the local database.
/// </summary>
/// <remarks>
/// The report is read-only: it enumerates the meals of the active profile, keeps the ones that carry
/// information (a title, a description or a state the user chose) and lays them out day by day. The
/// days a user never touched are therefore not printed as empty rows, so a report of a month stays a
/// report of the meals that were actually planned or eaten.
/// </remarks>
public sealed class MealReportService : IMealReportService
{
    /// <summary>Range used when the caller does not narrow the period (first and last possible day).</summary>
    private static readonly DateOnly FirstPossibleDay = new(2000, 1, 1);

    private static readonly DateOnly LastPossibleDay = new(2100, 1, 1);

    private readonly IMealEntryRepository _entries;
    private readonly IProfileService _profiles;
    private readonly ILogger<MealReportService> _logger;

    public MealReportService(
        IMealEntryRepository entries,
        IProfileService profiles,
        ILogger<MealReportService> logger)
    {
        _entries = entries;
        _profiles = profiles;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<MealReportSummary> ExportPdfAsync(
        string targetFilePath,
        MealReportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFilePath);
        ArgumentNullException.ThrowIfNull(options);

        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var range = await _entries
            .GetForRangeAsync(
                profile.Id,
                options.From ?? FirstPossibleDay,
                options.To ?? LastPossibleDay,
                cancellationToken)
            .ConfigureAwait(false);

        var days = range
            .Select(pair => new MealReportDay(
                pair.Key,
                pair.Value.Where(HasContent).OrderBy(meal => meal.MealCategoryKey).ToArray()))
            .Where(day => day.Meals.Count > 0)
            .OrderBy(day => day.Date)
            .ToArray();

        var generatedAt = DateTimeOffset.Now;
        var document = new PdfTextDocument(options.DocumentTitle);
        var layout = MealReportLayout.Render(document, options, days, generatedAt);

        var directory = Path.GetDirectoryName(targetFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllBytesAsync(targetFilePath, document.ToArray(), cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "PDF report with {Meals} meal(s) on {Pages} page(s) written to '{Path}'.",
            layout.Meals,
            layout.Pages,
            targetFilePath);

        return new MealReportSummary
        {
            FilePath = targetFilePath,
            Days = days.Length,
            MealEntries = layout.Meals,
            Done = layout.Done,
            Pages = layout.Pages,
            CreatedAt = generatedAt,
        };
    }

    /// <summary>
    /// True when a slot carries something worth printing: a title, a description or a state the user
    /// chose. The rows the dashboard creates lazily for an untouched day stay out of the report.
    /// </summary>
    private static bool HasContent(MealEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.Title)
        || !string.IsNullOrWhiteSpace(entry.Ingredients)
        || entry.Status != MealStatus.Pending;
}
