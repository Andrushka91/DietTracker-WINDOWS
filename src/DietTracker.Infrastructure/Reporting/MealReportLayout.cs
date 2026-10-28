using System.Globalization;
using DietTracker.Core.Enums;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Reporting;

/// <summary>One day of the report: the date and the meals of that day that carry information.</summary>
internal sealed record MealReportDay(DateOnly Date, IReadOnlyList<MealEntry> Meals);

/// <summary>What the layout painted, so the caller can report it to the user.</summary>
internal sealed record MealReportLayoutResult(int Pages, int Meals, int Done);

/// <summary>
/// A4 layout of the meal report: one header with the period and the totals, then a block per day with
/// one row per meal. Everything is measured in PDF points and laid out from top to bottom; when a row
/// does not fit, a new page starts and both the running header and the heading of the day are
/// repeated, so every page can be read on its own.
/// </summary>
internal static class MealReportLayout
{
    private const double MarginLeft = 48d;
    private const double MarginRight = 48d;
    private const double MarginTop = 54d;

    /// <summary>Lowest baseline a row may reach; below it starts the footer.</summary>
    private const double ContentBottom = 64d;

    private const double FooterBaseline = 34d;

    // Columns of a meal row, from left to right. The widths are chosen for the longest labels of the
    // three shipped languages ("Gustare de dimineață", "În așteptare", "Tentempié de la mañana").
    private const double TimeColumn = MarginLeft;
    private const double SlotColumn = 80d;
    private const double StateColumn = 200d;
    private const double TitleColumn = 290d;

    private const double RowLineHeight = 12d;
    private const double DetailLineHeight = 11d;

    public static MealReportLayoutResult Render(
        PdfTextDocument document,
        MealReportOptions options,
        IReadOnlyList<MealReportDay> days,
        DateTimeOffset generatedAt)
    {
        var culture = CultureOf(options.CultureName);
        var right = PdfTextDocument.A4Width - MarginRight;
        var textWidth = right - TitleColumn;

        var meals = days.Sum(day => day.Meals.Count);
        var done = days.Sum(day => day.Meals.Count(meal => meal.Status == MealStatus.Done));
        var missed = days.Sum(day => day.Meals.Count(meal => meal.Status == MealStatus.Missed));
        var pending = meals - done - missed;

        document.AddPage();
        var y = PdfTextDocument.A4Height - MarginTop;

        void NewPage()
        {
            document.AddPage();
            y = PdfTextDocument.A4Height - MarginTop;
            document.DrawText(MarginLeft, y, options.DocumentTitle, 9d, gray: 0.45d);
            document.DrawLine(MarginLeft, y - 7d, right, 0.5d, 0.85d);
            y -= 26d;
        }

        void StartDay(MealReportDay day)
        {
            document.DrawText(
                MarginLeft,
                y,
                day.Date.ToDateTime(TimeOnly.MinValue).ToString("D", culture),
                10.5d,
                bold: true,
                gray: 0.15d);
            y -= 16d;
        }

        // Document header: what the report covers and when it was produced.
        document.DrawText(MarginLeft, y, options.DocumentTitle, 17d, bold: true, gray: 0.1d);
        y -= 21d;

        var first = days.Count > 0 ? days[0].Date : options.From ?? DateOnly.FromDateTime(generatedAt.DateTime);
        var last = days.Count > 0 ? days[^1].Date : options.To ?? DateOnly.FromDateTime(generatedAt.DateTime);
        document.DrawText(
            MarginLeft,
            y,
            string.Format(culture, options.PeriodFormat, FormatDate(first, culture), FormatDate(last, culture)),
            9.5d,
            gray: 0.4d);
        y -= 13d;
        document.DrawText(
            MarginLeft,
            y,
            string.Format(culture, options.GeneratedFormat, generatedAt.ToString("g", culture)),
            9.5d,
            gray: 0.4d);
        y -= 18d;
        document.DrawText(
            MarginLeft,
            y,
            string.Format(culture, options.SummaryFormat, meals, done, missed, pending),
            9.5d,
            bold: true,
            gray: 0.25d);
        y -= 11d;
        document.DrawLine(MarginLeft, y, right);
        y -= 22d;

        if (days.Count == 0)
        {
            document.DrawText(MarginLeft, y, options.EmptyText, 9.5d, gray: 0.4d);
        }

        foreach (var day in days)
        {
            if (y - 34d < ContentBottom)
            {
                NewPage();
            }

            StartDay(day);

            foreach (var meal in day.Meals)
            {
                var titleLines = PdfTextDocument.Wrap(meal.Title, 9d, false, textWidth);
                var detailLines = PdfTextDocument.Wrap(meal.Ingredients, 8.5d, false, textWidth);
                var titleBlock = Math.Max(1, titleLines.Count) * RowLineHeight;
                var blockHeight = titleBlock + (detailLines.Count * DetailLineHeight) + 6d;

                // A row that does not fit starts a new page; the heading of the day is repeated there
                // so the page still says which day the meals belong to.
                if (y - blockHeight < ContentBottom)
                {
                    NewPage();
                    StartDay(day);
                }

                var slot = Fit(NameOf(options.CategoryNames, meal.MealCategoryKey), StateColumn - SlotColumn - 6d, 9d);
                var state = Fit(NameOf(options.StatusNames, meal.Status), TitleColumn - StateColumn - 8d, 9d);

                document.DrawText(TimeColumn, y, FormatTime(meal.PlannedTime, options.TimeFormat, culture), 9d, bold: true, gray: 0.15d);
                document.DrawText(SlotColumn, y, slot, 9d, gray: 0.3d);
                document.DrawText(StateColumn, y, state, 9d, gray: 0.45d);

                var lineY = y;
                foreach (var line in titleLines)
                {
                    document.DrawText(TitleColumn, lineY, line, 9d, gray: 0.1d);
                    lineY -= RowLineHeight;
                }

                lineY = y - titleBlock;
                foreach (var line in detailLines)
                {
                    document.DrawText(TitleColumn, lineY, line, 8.5d, gray: 0.4d);
                    lineY -= DetailLineHeight;
                }

                y -= blockHeight;
            }
        }

        StampFooters(document, options, culture);

        return new MealReportLayoutResult(document.PageCount, meals, done);
    }

    /// <summary>
    /// Writes "Page x of y" at the bottom of every page. Done in a second pass because the total is
    /// only known once the last row has been placed.
    /// </summary>
    private static void StampFooters(PdfTextDocument document, MealReportOptions options, CultureInfo culture)
    {
        var pages = document.PageCount;
        for (var index = 0; index < pages; index++)
        {
            var label = string.Format(culture, options.PageFormat, index + 1, pages);
            var labelWidth = PdfTextDocument.MeasureWidth(label, 8.5d);
            document.DrawTextOnPage(
                index,
                (PdfTextDocument.A4Width - labelWidth) / 2d,
                FooterBaseline,
                label,
                8.5d,
                gray: 0.45d);
        }
    }

    private static string NameOf(IReadOnlyDictionary<MealCategoryKey, string> names, MealCategoryKey key) =>
        names.TryGetValue(key, out var name) && !string.IsNullOrWhiteSpace(name) ? name : key.ToString();

    private static string NameOf(IReadOnlyDictionary<MealStatus, string> names, MealStatus status) =>
        names.TryGetValue(status, out var name) && !string.IsNullOrWhiteSpace(name) ? name : status.ToString();

    private static string FormatDate(DateOnly date, CultureInfo culture) =>
        date.ToDateTime(TimeOnly.MinValue).ToString("d", culture);

    private static string FormatTime(TimeOnly? time, string format, CultureInfo culture) =>
        time is null
            ? string.Empty
            : time.Value.ToString(string.IsNullOrWhiteSpace(format) ? "HH:mm" : format, culture);

    /// <summary>Cuts a label that is wider than its column and marks the cut with an ellipsis.</summary>
    private static string Fit(string text, double maxWidth, double fontSize)
    {
        if (string.IsNullOrEmpty(text) || PdfTextDocument.MeasureWidth(text, fontSize) <= maxWidth)
        {
            return text;
        }

        var cut = text;
        while (cut.Length > 1 && PdfTextDocument.MeasureWidth(cut + "…", fontSize) > maxWidth)
        {
            cut = cut[..^1];
        }

        return cut + "…";
    }

    private static CultureInfo CultureOf(string name)
    {
        try
        {
            return CultureInfo.GetCultureInfo(string.IsNullOrWhiteSpace(name) ? "en-US" : name);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.GetCultureInfo("en-US");
        }
    }
}
