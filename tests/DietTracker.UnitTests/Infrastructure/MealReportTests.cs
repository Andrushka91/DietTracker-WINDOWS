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

/// <summary>Structure of the PDF writer the printable report is built on.</summary>
public sealed class PdfTextDocumentTests
{
    [Fact]
    public void A_document_is_a_complete_pdf_file()
    {
        var document = new PdfTextDocument("Report");
        document.AddPage();
        document.DrawText(48, 800, "(Hello) world", 12);

        var text = Encoding.ASCII.GetString(document.ToArray());

        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("/Type /Catalog", text);
        Assert.Contains("/BaseFont /Helvetica", text);
        Assert.Contains(@"(\(Hello\) world) Tj", text);
        Assert.Contains("startxref", text);
        Assert.EndsWith("%%EOF\n", text);
    }

    [Fact]
    public void Every_object_has_a_cross_reference_entry()
    {
        var document = new PdfTextDocument("Report");
        document.AddPage();
        document.DrawText(48, 800, "First page", 12);
        document.AddPage();
        document.DrawText(48, 800, "Second page", 12);

        var text = Encoding.ASCII.GetString(document.ToArray());

        // 4 fixed objects, two objects per page (the page and its content stream) and the info object.
        const int Objects = 4 + (2 * 2) + 1;
        Assert.Contains("/Count 2", text);
        Assert.Contains($"/Size {Objects + 1}", text);

        var entries = text.Split("xref\n0 ")[1].Split('\n');
        Assert.Equal($"{Objects + 1}", entries[0]);
        Assert.Equal("0000000000 65535 f ", entries[1]);

        // The offsets really point at the objects they belong to.
        for (var index = 1; index <= Objects; index++)
        {
            // 19 characters plus the line feed: every entry of the table is exactly 20 bytes.
            var entry = entries[index + 1];
            Assert.Equal(19, entry.Length);
            var offset = int.Parse(entry[..10], CultureInfo.InvariantCulture);
            Assert.StartsWith($"{index} 0 obj", text[offset..], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_declared_length_of_a_content_stream_matches_its_bytes()
    {
        var document = new PdfTextDocument("Report");
        document.AddPage();
        document.DrawText(48, 800, "Cerrado: día 1", 10);

        var text = Encoding.ASCII.GetString(document.ToArray());
        var rest = text.Split("<< /Length ")[1];
        var declared = int.Parse(rest[..rest.IndexOf(' ')], CultureInfo.InvariantCulture);

        var start = text.IndexOf("stream\n", StringComparison.Ordinal) + "stream\n".Length;
        var end = text.IndexOf("\nendstream", start, StringComparison.Ordinal);

        Assert.Equal(end - start, declared);
    }

    [Fact]
    public void Latin_1_characters_are_written_as_their_win_ansi_bytes()
    {
        var document = new PdfTextDocument("Informe");
        document.AddPage();
        document.DrawText(48, 800, "Piña y café", 12);

        var text = Encoding.ASCII.GetString(document.ToArray());

        Assert.Contains(@"Pi\361a y caf\351", text);
    }

    [Fact]
    public void Letters_outside_win_ansi_are_reduced_to_their_base_letter()
    {
        var document = new PdfTextDocument("Raport");
        document.AddPage();
        document.DrawText(48, 800, "Mic dejun cu brânză și șnițel", 12);

        var text = Encoding.ASCII.GetString(document.ToArray());

        // â exists in WinAnsi and is kept as its escape; ă, ș and ț do not, so they are reduced to
        // their base letter instead of becoming "?" (brânză -> brânza, și șnițel -> si snitel).
        Assert.Contains(@"Mic dejun cu br\342nza si snitel", text);
    }

    [Theory]
    [InlineData("one two three", 200, false)]
    [InlineData("one two three four five six seven eight", 60, true)]
    public void Wrapping_keeps_every_line_inside_the_column(string value, double maxWidth, bool expectSeveralLines)
    {
        var lines = PdfTextDocument.Wrap(value, 9, bold: false, maxWidth);

        Assert.NotEmpty(lines);
        Assert.Equal(expectSeveralLines, lines.Count > 1);
        foreach (var line in lines)
        {
            Assert.True(PdfTextDocument.MeasureWidth(line, 9) <= maxWidth, $"'{line}' is wider than {maxWidth}");
        }

        Assert.Equal(value, string.Join(' ', lines));
    }
}

/// <summary>The PDF report of the stored meals, produced against the real database.</summary>
public sealed class MealReportServiceTests
{
    [Fact]
    public async Task A_meal_of_the_past_is_written_into_the_report()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var date = DateOnly.FromDateTime(DateTime.Today).AddDays(-1);

        var slot = await environment.Tracking.GetOrCreateSlotAsync(date, MealCategoryKey.Breakfast);
        slot.Title = "Porridge with berries";
        slot.Ingredients = "Oats, milk, blueberries";
        await environment.Tracking.SaveEditsAsync(slot);
        await environment.Tracking.MarkDoneAsync(slot.Id);

        var path = Path.Combine(environment.RootPath, "report.pdf");
        var summary = await environment.Services
            .GetRequiredService<IMealReportService>()
            .ExportPdfAsync(path, Options());

        Assert.True(File.Exists(path));
        Assert.Equal(1, summary.MealEntries);
        Assert.Equal(1, summary.Done);
        Assert.Equal(1, summary.Days);
        Assert.Equal(1, summary.Pages);

        var text = Encoding.ASCII.GetString(await File.ReadAllBytesAsync(path));
        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("Porridge with berries", text);
        Assert.Contains("Oats, milk, blueberries", text);
        Assert.Contains("(Done)", text);
        Assert.EndsWith("%%EOF\n", text);
    }

    [Fact]
    public async Task Untouched_days_are_not_printed()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var date = DateOnly.FromDateTime(DateTime.Today).AddDays(-3);

        // An empty slot exists in the database (the dashboard materializes the five slots), but it
        // carries no information, so the report must not list it.
        await environment.Tracking.GetOrCreateSlotAsync(date, MealCategoryKey.Lunch);

        var path = Path.Combine(environment.RootPath, "empty-report.pdf");
        var summary = await environment.Services
            .GetRequiredService<IMealReportService>()
            .ExportPdfAsync(path, Options());

        Assert.Equal(0, summary.MealEntries);
        Assert.Equal(0, summary.Days);
        Assert.Equal(1, summary.Pages);

        var text = Encoding.ASCII.GetString(await File.ReadAllBytesAsync(path));
        Assert.Contains("No meals were recorded in this period.", text);
    }

    [Fact]
    public async Task A_long_history_continues_on_more_pages_with_a_page_number()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);

        for (var day = 1; day <= 40; day++)
        {
            var slot = await environment.Tracking.GetOrCreateSlotAsync(today.AddDays(-day), MealCategoryKey.Dinner);
            slot.Title = $"Dinner number {day}";
            slot.Ingredients = "A description long enough to need its own line in the report column.";
            await environment.Tracking.SaveEditsAsync(slot);
        }

        var path = Path.Combine(environment.RootPath, "long-report.pdf");
        var summary = await environment.Services
            .GetRequiredService<IMealReportService>()
            .ExportPdfAsync(path, Options());

        Assert.Equal(40, summary.MealEntries);
        Assert.True(summary.Pages > 1, "forty days of meals do not fit on one page");

        var text = Encoding.ASCII.GetString(await File.ReadAllBytesAsync(path));
        Assert.Contains($"(Page 1 of {summary.Pages})", text);
        Assert.Contains($"(Page {summary.Pages} of {summary.Pages})", text);
    }

    /// <summary>The labels the settings page passes to the report service.</summary>
    private static MealReportOptions Options() => new()
    {
        CultureName = "en-US",
        TimeFormat = "HH:mm",
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
