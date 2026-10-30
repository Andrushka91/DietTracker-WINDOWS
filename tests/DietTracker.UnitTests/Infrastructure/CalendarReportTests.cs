using System.Globalization;
using System.Text;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using DietTracker.Infrastructure.Reporting;
using DietTracker.UnitTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DietTracker.UnitTests.Infrastructure;

/// <summary>Structure of the coloured PDF primitives the one-page calendar is built on.</summary>
public sealed class CalendarPdfPrimitivesTests
{
    [Fact]
    public void The_coloured_primitives_reach_the_content_stream()
    {
        var document = new PdfTextDocument("Colours");
        document.AddPage();
        document.FillRectangle(10, 20, 30, 40, 1, 0, 0);
        document.DrawRectangle(10, 20, 30, 40, 1, 0, 0.5, 0);
        document.DrawText(12, 30, "Hi", 8, true, 0, 0, 1);

        var text = Encoding.ASCII.GetString(document.ToArray());

        Assert.Contains("1 0 0 rg", text);
        Assert.Contains("10 20 30 40 re\nf\n", text);
        Assert.Contains("0 0.5 0 RG", text);
        Assert.Contains("10 20 30 40 re\nS\n", text);
        Assert.Contains("0 0 1 rg", text);
        Assert.Contains("(Hi) Tj", text);
    }
}

/// <summary>The one-page month calendar: same panel, same states, same counts - on a single A4 page.</summary>
public sealed class CalendarReportServiceTests
{
    [Fact]
    public async Task A_month_is_exported_as_one_page_like_the_calendar_panel()
    {
        await using var environment = await TestEnvironment.CreateAsync();

        // Day 15: all five meals eaten - the green state of the panel, "5/5" and "100 %" in the cell.
        // Every card carries a title and a description, exactly like the editor saves them.
        foreach (var key in AllSlots)
        {
            var slot = await environment.Tracking.GetOrCreateSlotAsync(new DateOnly(2024, 3, 15), key);
            slot.Title = "Rice";
            slot.Ingredients = "With milk";
            await environment.Tracking.SaveEditsAsync(slot);
            await environment.Tracking.MarkDoneAsync(slot.Id);
        }

        // Day 16: a meal explicitly missed without any content - the red state of the panel; its
        // mini card falls back to the category name and the state line, like the panel does.
        var missed = await environment.Tracking.GetOrCreateSlotAsync(new DateOnly(2024, 3, 16), MealCategoryKey.Breakfast);
        await environment.Tracking.MarkMissedAsync(missed.Id);

        // Day 17: one confirmed meal, drawn as a mini card with its title and description.
        var done = await environment.Tracking.GetOrCreateSlotAsync(new DateOnly(2024, 3, 17), MealCategoryKey.Breakfast);
        done.Title = "Oats";
        done.Ingredients = "Milk";
        await environment.Tracking.SaveEditsAsync(done);
        await environment.Tracking.MarkDoneAsync(done.Id);

        var path = Path.Combine(environment.RootPath, "calendar.pdf");
        var summary = await environment.Services
            .GetRequiredService<ICalendarReportService>()
            .ExportMonthPdfAsync(path, new DateOnly(2024, 3, 1), Options(new DateOnly(2024, 3, 15)));

        // One month is always exactly one page.
        Assert.Equal(1, summary.Pages);
        Assert.Equal(new DateOnly(2024, 3, 1), summary.Month);
        Assert.Equal(31, summary.Days);
        Assert.Equal(7, summary.MealEntries);
        Assert.Equal(6, summary.Done);

        var text = Encoding.ASCII.GetString(await File.ReadAllBytesAsync(path));
        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);

        // Header: the chosen month in the shape of the calendar page, the weekday row and the
        // summary line under the heading.
        Assert.Contains("March 2024", text);
        Assert.Contains("(Sa) Tj", text);
        Assert.Contains("(6 confirmed, 1 missed,", text);

        // The count of meals done per day, the percentage and the mini cards.
        Assert.Contains("(5/5)", text);
        Assert.Contains("(1/5)", text);
        Assert.Contains("(0/5)", text);
        Assert.Contains("100 %", text);
        Assert.Contains("20 %", text);
        Assert.Contains("(0 %)", text);
        Assert.Contains("(Rice)", text);
        Assert.Contains("(Oats)", text);
        Assert.Contains("(Milk)", text);

        // A card without content falls back to the category name and the state line of the slot.
        Assert.Contains("(Breakfast)", text);
        Assert.Contains("(Missed)", text);

        // The state colours of the panel: green frame, red frame and the accent fill of today.
        Assert.Contains("0.063 0.486 0.063 RG", text);
        Assert.Contains("0.643 0.149 0.173 RG", text);
        Assert.Contains("0.133 0.469 0.761 rg", text);

        // The squares of the grid and the legend below them.
        Assert.Contains("re\nS\n", text);
        Assert.Contains("Green: all meals eaten", text);
        Assert.Contains("Red: at least one meal missed", text);
        Assert.Contains("Gray: pending", text);
    }

    [Fact]
    public async Task An_empty_month_still_prints_the_whole_grid_on_one_page()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);

        var path = Path.Combine(environment.RootPath, "empty-calendar.pdf");
        var summary = await environment.Services
            .GetRequiredService<ICalendarReportService>()
            .ExportMonthPdfAsync(path, new DateOnly(today.Year, today.Month, 1), Options());

        Assert.Equal(1, summary.Pages);
        Assert.Equal(0, summary.MealEntries);
        Assert.Equal(0, summary.Done);
        Assert.Equal(DateTime.DaysInMonth(today.Year, today.Month), summary.Days);

        var text = Encoding.ASCII.GetString(await File.ReadAllBytesAsync(path));
        Assert.StartsWith("%PDF-1.4", text);

        // The month heading comes from the culture, exactly like the calendar page builds it.
        var monthLabel = new DateOnly(today.Year, today.Month, 1)
            .ToDateTime(TimeOnly.MinValue)
            .ToString("Y", CultureInfo.GetCultureInfo("en-US"));
        Assert.Contains(monthLabel, text);

        // An untouched day shows its number and nothing else: no count, no percentage.
        Assert.DoesNotContain("(0/5)", text);
        Assert.DoesNotContain(" %", text);
        Assert.Contains("Gray: pending", text);
    }

    [Fact]
    public async Task A_whole_year_is_one_pdf_with_exactly_one_page_per_month()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var months = Enumerable.Range(1, 12).Select(month => new DateOnly(2024, month, 1)).ToArray();

        var path = Path.Combine(environment.RootPath, "year.pdf");
        var summary = await environment.Services
            .GetRequiredService<ICalendarReportService>()
            .ExportMonthsPdfAsync(path, months, Options());

        Assert.Equal(12, summary.Pages);
        Assert.Equal(12, summary.MonthCount);
        Assert.Equal(new DateOnly(2024, 1, 1), summary.Month);
        Assert.Equal(366, summary.Days); // 2024 is a leap year
        Assert.Equal(0, summary.MealEntries);
        Assert.Equal(0, summary.Done);

        var text = Encoding.ASCII.GetString(await File.ReadAllBytesAsync(path));
        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("/Count 12", text);
        Assert.Contains("January 2024", text);
        Assert.Contains("February 2024", text);
        Assert.Contains("December 2024", text);
        Assert.EndsWith("%%EOF\n", text);
    }

    [Fact]
    public async Task Months_are_normalized_to_their_first_day_sorted_and_deduplicated()
    {
        await using var environment = await TestEnvironment.CreateAsync();

        var path = Path.Combine(environment.RootPath, "range.pdf");
        var summary = await environment.Services
            .GetRequiredService<ICalendarReportService>()
            .ExportMonthsPdfAsync(
                path,
                [new DateOnly(2024, 5, 17), new DateOnly(2024, 3, 1), new DateOnly(2024, 5, 2)],
                Options());

        // Three requested, two kept: the two May dates collapse into one month, March sorts first.
        Assert.Equal(2, summary.Pages);
        Assert.Equal(2, summary.MonthCount);
        Assert.Equal(new DateOnly(2024, 3, 1), summary.Month);
        Assert.Equal(31 + 31, summary.Days); // March and May have 31 days each

        var text = Encoding.ASCII.GetString(await File.ReadAllBytesAsync(path));
        Assert.Contains("/Count 2", text);
        Assert.Contains("March 2024", text);
        Assert.Contains("May 2024", text);
        Assert.DoesNotContain("April 2024", text);

        // Chronological order: the heading of March comes before the heading of May.
        Assert.True(
            text.IndexOf("March 2024", StringComparison.Ordinal) < text.IndexOf("May 2024", StringComparison.Ordinal),
            "March must be printed before May");
    }

    [Fact]
    public async Task An_empty_month_list_is_refused()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var path = Path.Combine(environment.RootPath, "nothing.pdf");

        await Assert.ThrowsAsync<ArgumentException>(() => environment.Services
            .GetRequiredService<ICalendarReportService>()
            .ExportMonthsPdfAsync(path, [], Options()));
    }

    /// <summary>The five permanent slots, in the order the panel draws them.</summary>
    private static readonly MealCategoryKey[] AllSlots =
    [
        MealCategoryKey.Breakfast,
        MealCategoryKey.MorningSnack,
        MealCategoryKey.Lunch,
        MealCategoryKey.AfternoonSnack,
        MealCategoryKey.Dinner,
    ];

    /// <summary>The labels the settings page passes to the calendar report service.</summary>
    private static CalendarReportOptions Options(DateOnly? today = null) => new()
    {
        CultureName = "en-US",
        DocumentTitle = "Diet Tracker - Calendar",
        SummaryFormat = "{0} confirmed, {1} missed, {2} pending",
        GeneratedFormat = "Generated on {0}",
        WeekdayNames = ["Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"],
        LegendGreen = "Green: all meals eaten",
        LegendRed = "Red: at least one meal missed",
        LegendPending = "Gray: pending",
        Today = today,
        CategoryNames = new Dictionary<MealCategoryKey, string>
        {
            [MealCategoryKey.Breakfast] = "Breakfast",
            [MealCategoryKey.MorningSnack] = "Morning snack",
            [MealCategoryKey.Lunch] = "Lunch",
            [MealCategoryKey.AfternoonSnack] = "Afternoon snack",
            [MealCategoryKey.Dinner] = "Dinner",
        },
        StatusNames = new Dictionary<MealStatus, string>
        {
            [MealStatus.Done] = "Done",
            [MealStatus.Missed] = "Missed",
            [MealStatus.Pending] = "Pending",
        },
    };
}