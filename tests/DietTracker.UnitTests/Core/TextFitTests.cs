using DietTracker.Core.Utilities;
using Xunit;

namespace DietTracker.UnitTests.Core;

/// <summary>
/// Tests of the rule behind the description field: the text may never be taller than the box that shows
/// it, so the number of characters the field accepts is decided by the space available in it and not by
/// a fixed cap. The measurements themselves belong to the UI; what is decided here is when wrapped text
/// counts as overflowing and how much of a too long text has to be given up.
/// </summary>
public sealed class TextFitTests
{
    /// <summary>Height of one line of the description box, as the UI lays it out.</summary>
    private const double Line = 21d;

    /// <summary>Five lines: the room the description row of the editor gets.</summary>
    private const double Viewport = 5 * Line;

    [Fact]
    public void Text_exactly_as_tall_as_the_viewport_does_not_overflow()
    {
        Assert.False(TextFit.Overflows(Viewport, Viewport));
    }

    [Fact]
    public void Shorter_text_does_not_overflow()
    {
        Assert.False(TextFit.Overflows(3 * Line, Viewport));
    }

    [Fact]
    public void One_more_line_overflows()
    {
        Assert.True(TextFit.Overflows(6 * Line, Viewport));
    }

    /// <summary>The per-line heights are rounded on their own, so a visible pixel-sum can be a lie.</summary>
    [Fact]
    public void A_fraction_of_a_pixel_is_not_an_overflow()
    {
        Assert.False(TextFit.Overflows(Viewport + (TextFit.OverflowTolerance / 2), Viewport));
        Assert.True(TextFit.Overflows(Viewport + (TextFit.OverflowTolerance * 10), Viewport));
    }

    [Fact]
    public void A_single_character_change_is_a_keystroke_a_paste_is_not()
    {
        Assert.True(TextFit.IsSmallEdit(new string('a', 100), new string('a', 101)));
        Assert.True(TextFit.IsSmallEdit(new string('a', 100), new string('a', 99)));
        Assert.False(TextFit.IsSmallEdit(new string('a', 100), new string('a', 100 + TextFit.SmallEditDelta + 1)));
    }

    [Fact]
    public void A_text_that_fits_is_kept_whole()
    {
        var length = TextFit.LongestFittingLength(240, _ => true);

        Assert.Equal(240, length);
    }

    [Fact]
    public void The_longest_leading_part_of_a_paste_is_the_boundary_of_the_predicate()
    {
        // The box shows 200 characters of this text; everything past that needs another line.
        var length = TextFit.LongestFittingLength(500, size => size <= 200);

        Assert.Equal(200, length);
    }

    [Fact]
    public void A_text_that_starts_too_long_and_only_fits_when_emptied_returns_the_empty_text()
    {
        var length = TextFit.LongestFittingLength(300, size => size == 0);

        Assert.Equal(0, length);
    }

    [Fact]
    public void An_empty_candidate_needs_no_probe_at_all()
    {
        var probes = 0;
        var length = TextFit.LongestFittingLength(0, _ => { probes++; return true; });

        Assert.Equal(0, length);
        Assert.Equal(0, probes);
    }

    /// <summary>
    /// The search runs while the real text box is being measured, so every probe is a layout pass: it
    /// must stay logarithmic, otherwise pasting would visibly freeze the card.
    /// </summary>
    [Fact]
    public void Cutting_a_long_paste_measures_only_a_handful_of_lengths()
    {
        const int Candidate = 2000;
        var probes = 0;
        var length = TextFit.LongestFittingLength(Candidate, size => { probes++; return size <= 272; });

        Assert.Equal(272, length);
        Assert.InRange(probes, 1, 12);
    }

    [Fact]
    public void Every_probed_length_is_a_prefix_of_the_candidate_so_the_wrap_stays_monotonic()
    {
        var probed = new List<int>();
        TextFit.LongestFittingLength(1000, size => { probed.Add(size); return size <= 450; });

        Assert.All(probed, size => Assert.InRange(size, 0, 1000));
        Assert.Contains(450, probed);
    }

    /// <summary>
    /// The capacity of the wrapped description is the wrapped counterpart of the title rule: the rows
    /// still free are filled at the character density of the complete rows already typed.
    /// </summary>
    [Fact]
    public void The_free_rows_are_filled_at_the_density_of_the_complete_rows()
    {
        // 100 characters over 3 rows, the last one starting at index 80: the two complete rows hold 40
        // characters each, and the box can show 5 rows.
        var capacity = TextFit.WrappedCapacity(
            length: 100,
            usedRows: 3,
            firstCharacterOfLastRow: 80,
            visibleRows: 5,
            maxLength: 2000);

        Assert.Equal(200, capacity);
    }

    /// <summary>The partial last row must not lower the density of the rows above it.</summary>
    [Fact]
    public void A_short_last_row_does_not_lower_the_row_density()
    {
        // The last of the three rows only has 10 characters; the two complete rows still hold 40 each.
        var capacity = TextFit.WrappedCapacity(
            length: 90,
            usedRows: 3,
            firstCharacterOfLastRow: 80,
            visibleRows: 4,
            maxLength: 2000);

        Assert.Equal(160, capacity);
    }

    [Fact]
    public void A_box_whose_rows_are_all_full_does_not_grow()
    {
        var capacity = TextFit.WrappedCapacity(
            length: 200,
            usedRows: 5,
            firstCharacterOfLastRow: 160,
            visibleRows: 5,
            maxLength: 2000);

        Assert.Equal(200, capacity);
    }

    [Fact]
    public void A_box_that_cannot_show_the_rows_it_already_has_does_not_grow()
    {
        var capacity = TextFit.WrappedCapacity(
            length: 150,
            usedRows: 4,
            firstCharacterOfLastRow: 120,
            visibleRows: 3,
            maxLength: 2000);

        Assert.Equal(150, capacity);
    }

    /// <summary>A single row offers no complete row to measure, so the cap leaves the text alone.</summary>
    [Fact]
    public void A_single_row_is_not_capped_by_a_guess()
    {
        var capacity = TextFit.WrappedCapacity(
            length: 10,
            usedRows: 1,
            firstCharacterOfLastRow: 0,
            visibleRows: 5,
            maxLength: 2000);

        Assert.Equal(10, capacity);
    }

    [Fact]
    public void The_capacity_never_exceeds_the_validator_ceiling()
    {
        var capacity = TextFit.WrappedCapacity(
            length: 1990,
            usedRows: 2,
            firstCharacterOfLastRow: 1000,
            visibleRows: 5,
            maxLength: 2000);

        Assert.Equal(2000, capacity);
    }

    [Fact]
    public void The_capacity_never_falls_below_the_text_already_in_the_box()
    {
        var capacity = TextFit.WrappedCapacity(
            length: 150,
            usedRows: 3,
            firstCharacterOfLastRow: 120,
            visibleRows: 2,
            maxLength: 2000);

        Assert.Equal(150, capacity);
    }
}
