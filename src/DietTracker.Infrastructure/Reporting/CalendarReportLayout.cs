using System.Globalization;
using DietTracker.Core.Enums;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Reporting;

/// <summary>What the calendar layout painted, so the caller can report it to the user.</summary>
internal sealed record CalendarReportLayoutResult(int Pages, int Days, int Meals);

/// <summary>
/// A4 layout of one month in the shape of the calendar screen: the month heading, the summary line,
/// the Monday first weekday row, a grid of square day cells (exactly the rows the month spans), the
/// legend and the generation line - all on a single page. Every square carries the day number, the
/// percentage and the "done / five" count of the day, the mini cards of its recorded meals in the
/// same fixed two-column grid, and the state colour of the panel (green when every meal was eaten,
/// red when at least one was missed, gray while nothing was decided; today keeps the accent fill,
/// the padding cells of the neighbouring months are dimmed). Everything is measured in PDF points
/// from the bottom left corner of the page.
/// </summary>
internal static class CalendarReportLayout
{
    /// <summary>Side margins of the page; the seven columns share what they leave.</summary>
    private const double MarginSide = 36d;

    /// <summary>Gap between two columns and between two week rows (the panel's spacing).</summary>
    private const double GridGap = 6d;

    private const double MarginTop = 46d;

    /// <summary>Distance between the baseline of the weekday row and the top edge of the grid.</summary>
    private const double WeekdayToGrid = 14d;

    /// <summary>Baseline of the generation line at the bottom of the page.</summary>
    private const double FooterBaseline = 34d;

    private static readonly double GridWidth = PdfTextDocument.A4Width - (2 * MarginSide);

    /// <summary>Side of a square: the seven columns share the grid width, the rows never grow past it.</summary>
    private static readonly double CellSize = (GridWidth - (6 * GridGap)) / 7d;

    // --- The colours of the calendar panel, as RGB triples ---

    private static readonly Rgb Green = new(16 / 255d, 124 / 255d, 16 / 255d);
    private static readonly Rgb Red = new(164 / 255d, 38 / 255d, 44 / 255d);
    private static readonly Rgb Gray = new(138 / 255d, 136 / 255d, 134 / 255d);
    private static readonly Rgb TodayAccent = new(15 / 255d, 108 / 255d, 189 / 255d);
    private static readonly Rgb NumberInk = new(0.13, 0.13, 0.13);
    private static readonly Rgb White = new(1, 1, 1);

    // The background tints of the panel: the state colour at the opacity its brush carries (26/255
    // for green and red, 18/255 for gray) over the white of the page; today uses its 235/255 fill.
    private static readonly Rgb GreenTint = Green.OverWhite(26 / 255d);
    private static readonly Rgb RedTint = Red.OverWhite(26 / 255d);
    private static readonly Rgb GrayTint = Gray.OverWhite(18 / 255d);
    private static readonly Rgb TodayFill = TodayAccent.OverWhite(235 / 255d);

    /// <summary>Renders the whole month on one A4 page and reports what was painted.</summary>
    public static CalendarReportLayoutResult Render(
        PdfTextDocument document,
        CalendarReportOptions options,
        DateOnly month,
        PeriodProgress progress,
        DateTimeOffset generatedAt)
    {
        var culture = CultureOf(options.CultureName);
        var byDate = progress.Days.ToDictionary(day => day.Date, day => day);

        var first = new DateOnly(month.Year, month.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(first.Year, first.Month);

        // The grid starts on Monday and holds exactly the rows the month needs - the same rule the
        // calendar page applies, so the squares land on the same weekdays as in the application.
        var leading = ((int)first.DayOfWeek + 6) % 7;
        var rows = Math.Max(1, (leading + daysInMonth + 6) / 7);
        var gridStart = first.AddDays(-leading);

        var left = MarginSide;
        var top = PdfTextDocument.A4Height - MarginTop;

        document.AddPage();

        // Header: the chosen month exactly as the calendar page shows it, then the summary of the
        // month in the shape of the panel's day summary.
        var monthLabel = first.ToDateTime(TimeOnly.MinValue).ToString("Y", culture);
        document.DrawText(left, top, monthLabel, 17d, bold: true, gray: 0.1d);
        document.DrawText(
            left,
            top - 20d,
            string.Format(culture, options.SummaryFormat, progress.Completed, progress.Missed, progress.Pending),
            9d,
            gray: 0.4d);

        // Weekday row, Monday first, one centered label over each column.
        var weekdays = WeekdaysOf(options, culture);
        var weekdayBaseline = top - 46d;
        for (var column = 0; column < 7; column++)
        {
            var label = weekdays[column];
            var center = left + (column * (CellSize + GridGap)) + (CellSize / 2d);
            var centerOffset = PdfTextDocument.MeasureWidth(label, 8.5d, bold: true) / 2d;
            document.DrawText(center - centerOffset, weekdayBaseline, label, 8.5d, bold: true, gray: 0.35d);
        }

        var gridTop = weekdayBaseline - WeekdayToGrid;
        var recordedMeals = 0;

        // The day cells: seven per row, one block of squares that never grows taller than wide.
        for (var index = 0; index < rows * 7; index++)
        {
            var column = index % 7;
            var row = index / 7;
            var cellLeft = left + (column * (CellSize + GridGap));
            var cellTop = gridTop - (row * (CellSize + GridGap));
            var date = gridStart.AddDays(index);

            byDate.TryGetValue(date, out var day);
            recordedMeals += day?.Slots.Count(IsRecorded) ?? 0;

            DrawDay(
                document,
                options,
                culture,
                date,
                day,
                cellLeft,
                cellTop,
                isToday: options.Today == date,
                isCurrentMonth: date.Year == first.Year && date.Month == first.Month);
        }

        var gridBottom = gridTop - (rows * CellSize) - ((rows - 1) * GridGap);
        DrawLegend(document, options, left, gridBottom - 20d);
        DrawFooter(document, options, culture, left, generatedAt);

        return new CalendarReportLayoutResult(document.PageCount, daysInMonth, recordedMeals);
    }

    /// <summary>
    /// Draws one square of the grid: the state tint and frame, the day number, the done count next
    /// to it and the percentage on the right - then the mini cards of the recorded meals. An
    /// untouched day shows its number and nothing else, exactly like the panel.
    /// </summary>
    private static void DrawDay(
        PdfTextDocument document,
        CalendarReportOptions options,
        CultureInfo culture,
        DateOnly date,
        DayProgress? day,
        double cellLeft,
        double cellTop,
        bool isToday,
        bool isCurrentMonth)
    {
        // Padding cells of the neighbouring months are dimmed exactly like the panel dims them
        // (opacity 0.35); everything else is printed at full strength.
        var dim = isCurrentMonth ? 1d : 0.35d;

        var border = StateBorder(day).OverWhite(dim);
        var fill = isToday ? TodayFill : StateTint(day).OverWhite(dim);
        document.FillRectangle(cellLeft, cellTop - CellSize, CellSize, CellSize, fill.R, fill.G, fill.B);
        document.DrawRectangle(cellLeft, cellTop - CellSize, CellSize, CellSize, 1d, border.R, border.G, border.B);

        var numberInk = isToday ? White : NumberInk.OverWhite(dim);
        var numberText = date.Day.ToString(culture);
        var numberBaseline = cellTop - 14.5d;
        document.DrawText(cellLeft + 5d, numberBaseline, numberText, 13d, bold: true, numberInk.R, numberInk.G, numberInk.B);

        if (day is not null && day.HasDecided)
        {
            // The percentage sits where the panel puts it, top right of the same row.
            var subtle = isToday ? White : Gray.OverWhite(dim);
            var percentage = FormattableString.Invariant($"{day.Percentage:0.#} %");
            DrawRightAligned(document, cellLeft + CellSize - 5d, cellTop - 13d, percentage, 6.5d, bold: false, ink: subtle);

            // The count of meals done that day ("4/5") follows the day number, in the state colour
            // so the figure repeats what the frame of the square already says.
            var count = FormattableString.Invariant($"{day.Completed}/{day.TotalSlots}");
            var countInk = isToday ? White : StateBorder(day).OverWhite(dim);
            var countX = cellLeft + 5d + PdfTextDocument.MeasureWidth(numberText, 13d, bold: true) + 5d;
            document.DrawText(countX, numberBaseline, count, 7d, bold: true, countInk.R, countInk.G, countInk.B);
        }

        if (day is not null)
        {
            DrawChips(document, options, day, cellLeft, cellTop, isToday, dim);
        }
    }

    /// <summary>
    /// Draws the mini cards of one day: a fixed grid of two columns and three rows under the day
    /// number, one frame per permanent slot with the state colour of the slot - green when the meal
    /// was eaten, red when it was missed and gray while it is still pending. A slot the user never
    /// recorded is skipped but keeps its place, so the cards sit exactly where the panel puts them.
    /// The two lines of every card are the ones the panel draws: the title on top, the description
    /// (or the state of the slot when there is none) at the bottom.
    /// </summary>
    private static void DrawChips(
        PdfTextDocument document,
        CalendarReportOptions options,
        DayProgress day,
        double cellLeft,
        double cellTop,
        bool isToday,
        double dim)
    {
        const double InnerPadding = 4d;
        const double ChipGap = 3d;
        const double TopChrome = 21d; // day number, count and percentage, plus the air below them

        var areaLeft = cellLeft + InnerPadding;
        var areaWidth = CellSize - (2 * InnerPadding);
        var areaTop = cellTop - TopChrome;
        var areaHeight = CellSize - TopChrome - InnerPadding;
        var chipWidth = (areaWidth - ChipGap) / 2d;
        var chipHeight = (areaHeight - (2 * ChipGap)) / 3d;

        var textInk = isToday ? White : NumberInk.OverWhite(dim);
        var bottomInk = isToday ? White : Gray.OverWhite(dim);

        for (var slotIndex = 0; slotIndex < day.Slots.Count; slotIndex++)
        {
            var slot = day.Slots[slotIndex];
            if (!IsRecorded(slot))
            {
                continue;
            }

            var chipLeft = areaLeft + ((slotIndex % 2) * (chipWidth + ChipGap));
            var chipTop = areaTop - ((slotIndex / 2) * (chipHeight + ChipGap));

            var border = BorderOf(slot.Status).OverWhite(dim);
            var tint = TintOf(slot.Status).OverWhite(dim);
            document.FillRectangle(chipLeft, chipTop - chipHeight, chipWidth, chipHeight, tint.R, tint.G, tint.B);
            document.DrawRectangle(chipLeft, chipTop - chipHeight, chipWidth, chipHeight, 0.75d, border.R, border.G, border.B);

            var title = string.IsNullOrWhiteSpace(slot.Title)
                ? NameOf(options.CategoryNames, slot.MealCategoryKey)
                : slot.Title;
            var bottom = string.IsNullOrWhiteSpace(slot.Ingredients)
                ? NameOf(options.StatusNames, slot.Status)
                : slot.Ingredients;

            var textWidth = chipWidth - 4d;
            document.DrawText(chipLeft + 2d, chipTop - 5.5d, Fit(title, textWidth, 5d), 5d, bold: true, textInk.R, textInk.G, textInk.B);
            document.DrawText(
                chipLeft + 2d,
                chipTop - chipHeight + 2.2d,
                Fit(bottom, textWidth, 4.5d),
                4.5d,
                bold: false,
                bottomInk.R,
                bottomInk.G,
                bottomInk.B);
        }
    }

    /// <summary>Draws the legend under the grid: a miniature square with the state colour plus its text.</summary>
    private static void DrawLegend(PdfTextDocument document, CalendarReportOptions options, double left, double top)
    {
        foreach (var (border, tint, label) in new (Rgb Border, Rgb Tint, string Label)[]
        {
            (Green, GreenTint, options.LegendGreen),
            (Red, RedTint, options.LegendRed),
            (Gray, GrayTint, options.LegendPending),
        })
        {
            document.FillRectangle(left, top - 10d, 10d, 10d, tint.R, tint.G, tint.B);
            document.DrawRectangle(left, top - 10d, 10d, 10d, 1.2d, border.R, border.G, border.B);
            document.DrawText(left + 15d, top - 8d, label, 8d, bold: false, gray: 0.35d);
            left += 15d + PdfTextDocument.MeasureWidth(label, 8d) + 18d;
        }
    }

    /// <summary>Writes the generation line at the bottom of the page.</summary>
    private static void DrawFooter(
        PdfTextDocument document,
        CalendarReportOptions options,
        CultureInfo culture,
        double left,
        DateTimeOffset generatedAt)
    {
        if (string.IsNullOrWhiteSpace(options.GeneratedFormat))
        {
            return;
        }

        document.DrawText(
            left,
            FooterBaseline,
            string.Format(culture, options.GeneratedFormat, generatedAt.ToString("g", culture)),
            8.5d,
            gray: 0.5d);
    }

    /// <summary>Draws a line so that it ends at <paramref name="rightEdge"/>.</summary>
    private static void DrawRightAligned(PdfTextDocument document, double rightEdge, double baseline, string text, double fontSize, bool bold, Rgb ink)
    {
        document.DrawText(rightEdge - PdfTextDocument.MeasureWidth(text, fontSize, bold), baseline, text, fontSize, bold, ink.R, ink.G, ink.B);
    }

    private static Rgb StateBorder(DayProgress? day) =>
        day is null ? Gray : day.IsFullyCompleted ? Green : day.Missed > 0 ? Red : Gray;

    private static Rgb StateTint(DayProgress? day) =>
        day is null ? GrayTint : day.IsFullyCompleted ? GreenTint : day.Missed > 0 ? RedTint : GrayTint;

    private static Rgb BorderOf(MealStatus status) => status switch
    {
        MealStatus.Done => Green,
        MealStatus.Missed => Red,
        _ => Gray,
    };

    private static Rgb TintOf(MealStatus status) => status switch
    {
        MealStatus.Done => GreenTint,
        MealStatus.Missed => RedTint,
        _ => GrayTint,
    };

    /// <summary>True when a slot carries something worth drawing (the rule the calendar panel uses).</summary>
    private static bool IsRecorded(MealSlotStatus slot) =>
        slot.Status != MealStatus.Pending
        || !string.IsNullOrWhiteSpace(slot.Title)
        || slot.PhotoCount > 0;

    /// <summary>The seven weekday names, Monday first; the culture supplies them when the caller has none.</summary>
    private static IReadOnlyList<string> WeekdaysOf(CalendarReportOptions options, CultureInfo culture)
    {
        var names = options.WeekdayNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToArray();

        if (names.Length == 7)
        {
            return names;
        }

        var cultureNames = culture.DateTimeFormat.AbbreviatedDayNames;
        return Enumerable.Range(0, 7).Select(index => cultureNames[(index + 1) % 7]).ToArray();
    }

    private static string NameOf(IReadOnlyDictionary<MealCategoryKey, string> names, MealCategoryKey key) =>
        names.TryGetValue(key, out var name) && !string.IsNullOrWhiteSpace(name) ? name : key.ToString();

    private static string NameOf(IReadOnlyDictionary<MealStatus, string> names, MealStatus status) =>
        names.TryGetValue(status, out var name) && !string.IsNullOrWhiteSpace(name) ? name : status.ToString();

    /// <summary>Cuts a label that is wider than its frame and marks the cut with an ellipsis.</summary>
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

    /// <summary>
    /// One RGB colour (0..1 per channel) with the translation the panel's brushes need:
    /// <see cref="OverWhite"/> paints the colour over the white page at the given opacity, which is
    /// both how the translucent tints of the panel look on paper and how a dimmed padding cell is
    /// produced.
    /// </summary>
    private readonly record struct Rgb(double R, double G, double B)
    {
        public Rgb OverWhite(double opacity) => new(
            (opacity * R) + (1d - opacity),
            (opacity * G) + (1d - opacity),
            (opacity * B) + (1d - opacity));
    }
}