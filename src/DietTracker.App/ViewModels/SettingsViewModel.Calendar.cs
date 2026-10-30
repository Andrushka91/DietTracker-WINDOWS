using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using DietTracker.Core.BusinessRules;
using DietTracker.Core.Interfaces;

namespace DietTracker.App.ViewModels;

/// <summary>
/// Calendar export of the settings screen: the "Export calendar" button of the synchronization card
/// writes the month the user chooses as a single A4 page in the layout of the calendar screen.
/// </summary>
/// <remarks>
/// One popup offers everything (current month, the whole year, a single date field and a date
/// range field) and the save dialog then decides where the file lands - the same shape as the
/// report export, and just as non destructive: the PDF is written from scratch and nothing in the
/// database is touched.
/// </remarks>
public sealed partial class SettingsViewModel
{
    /// <summary>Extension of the calendar PDF written by <see cref="ICalendarReportService"/>.</summary>
    private const string CalendarPdfExtension = ".pdf";

    /// <summary>
    /// Asks - in one single popup - what to export (the current month, every month of the year,
    /// the month of a single date or every month between two dates), asks where to save the file
    /// and writes the calendar PDF there: one A4 page per month.
    /// </summary>
    [RelayCommand]
    private async Task ExportCalendarAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        // The popup carries all four options and their date fields; there is no follow-up dialog,
        // and cancelling it cancels the export.
        var choice = await _dialogs.ChooseCalendarExportAsync(today);
        if (choice is null)
        {
            return;
        }

        var months = MonthsOf(choice, today);
        if (months.Count == 0)
        {
            return;
        }

        var path = await _filePicker.PickSaveFileAsync(
            SuggestedCalendarName(months),
            [new SaveFileChoice(_localization.Get("Settings_ExportFormatPdf"), CalendarPdfExtension)]);

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        BackupStatusMessage = string.Empty;
        try
        {
            var summary = await _calendarReports.ExportMonthsPdfAsync(path, months, BuildCalendarReportOptions());
            BackupStatusMessage = summary.MonthCount == 1
                ? _localization.Format("Settings_CalendarPdfCreated", MonthLabel(summary.Month))
                : _localization.Format("Settings_CalendarPdfCreatedMany", summary.MonthCount);
        }
        catch (Exception)
        {
            // Like the other exports, a failure is reported instead of leaving an empty status line.
            BackupStatusMessage = _localization.Get("Settings_ExportFailed");
        }
    }

    /// <summary>
    /// Turns the choice of the popup into the months to print: the month of today, the twelve
    /// months of the year of today, the month that contains the single date, or every month the
    /// range touches (inclusive and in either order - a range typed backwards is swapped).
    /// </summary>
    private static IReadOnlyList<DateOnly> MonthsOf(CalendarExportChoice choice, DateOnly today)
    {
        switch (choice.Scope)
        {
            case CalendarExportScope.CurrentMonth:
                return [FirstOfMonth(today)];

            case CalendarExportScope.WholeYear:
            {
                var year = new List<DateOnly>(12);
                for (var month = 1; month <= 12; month++)
                {
                    year.Add(new DateOnly(today.Year, month, 1));
                }

                return year;
            }

            case CalendarExportScope.SingleDate:
                return [FirstOfMonth(choice.SingleDate)];

            default:
            {
                var start = FirstOfMonth(choice.RangeStart);
                var end = FirstOfMonth(choice.RangeEnd);
                if (start > end)
                {
                    (start, end) = (end, start);
                }

                var range = new List<DateOnly>();
                for (var month = start; month <= end; month = month.AddMonths(1))
                {
                    range.Add(month);
                }

                return range;
            }
        }
    }

    private static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    /// <summary>The month as the calendar page shows it ("October 2026").</summary>
    private static string MonthLabel(DateOnly month) =>
        month.ToDateTime(TimeOnly.MinValue).ToString("Y", CultureInfo.CurrentCulture);

    /// <summary>
    /// Suggested file name: one month keeps its number ("...-2026-10"), a whole year its year
    /// ("...-2026") and a range both of its ends ("...-2026-01_2026-06").
    /// </summary>
    private static string SuggestedCalendarName(IReadOnlyList<DateOnly> months) =>
        months.Count == 1
            ? $"diettracker-calendar-{months[0]:yyyy-MM}"
            : months.All(month => month.Year == months[0].Year)
                ? $"diettracker-calendar-{months[0]:yyyy}"
                : $"diettracker-calendar-{months[0]:yyyy-MM}_{months[^1]:yyyy-MM}";

    /// <summary>
    /// Collects the localized texts and the culture of the calendar PDF. The report service has no
    /// access to the resource files, so the weekday row, the month summary, the legend and the mini
    /// card labels arrive from this layer - which keeps exactly the texts the calendar page shows.
    /// </summary>
    private CalendarReportOptions BuildCalendarReportOptions() => new()
    {
        CultureName = CultureInfo.CurrentCulture.Name,
        DocumentTitle = _localization.Get("Report_CalendarTitle"),
        SummaryFormat = _localization.Get("Calendar_DaySummary"),
        GeneratedFormat = _localization.Get("Report_Generated"),
        // The same seven letters the calendar page puts above its grid, Monday first.
        WeekdayNames = _localization
            .Get("Calendar_Weekdays")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        LegendGreen = _localization.Get("Calendar_LegendGreen"),
        LegendRed = _localization.Get("Calendar_LegendRed"),
        LegendPending = _localization.Get("Calendar_LegendPending"),
        Today = DateOnly.FromDateTime(DateTime.Today),
        CategoryNames = MealSlotRules.AllSlots.ToDictionary(
            key => key,
            key => _localization.Get($"Meal_{key}_Name")),
        StatusNames = new Dictionary<MealStatus, string>
        {
            [MealStatus.Done] = _localization.Get("Status_Done"),
            [MealStatus.Missed] = _localization.Get("Status_Missed"),
            [MealStatus.Pending] = _localization.Get("Status_Pending"),
        },
    };
}