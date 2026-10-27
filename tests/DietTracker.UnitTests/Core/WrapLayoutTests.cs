using DietTracker.Core.Utilities;
using Xunit;

namespace DietTracker.UnitTests.Core;

/// <summary>
/// Tests of the flow layout behind <c>UniformWrapPanel</c> - the panel that lays out the photograph
/// gallery of a meal card (104 px tiles, 6 px gaps) and the card action rows.
/// The invariant that matters is the round trip: the panel is measured with the width its parent
/// offers and then arranged with the width this calculation returns (a left-aligned frame shrinks its
/// content to the measured width). The second pass must reproduce the first one; when it wraps an
/// extra line, the last tile is painted outside the grey gallery frame, on top of the action row.
/// </summary>
public sealed class WrapLayoutTests
{
    /// <summary>Width and height of one photo tile of the gallery.</summary>
    private const double Tile = 104d;

    /// <summary>Gap the gallery puts between tiles.</summary>
    private const double Gap = 6d;

    [Fact]
    public void Three_tiles_and_their_gaps_fit_on_one_line()
    {
        var layout = WrapLayout.Calculate([Tile, Tile, Tile], [Tile, Tile, Tile], 336d, Gap, Gap);

        Assert.Equal([0, 0, 0], layout.LineOfItem);
        Assert.Equal((3 * Tile) + (2 * Gap), layout.WidestLine);
        Assert.Equal([Tile], layout.LineHeights);
        Assert.Equal(Tile, layout.TotalHeight);
    }

    /// <summary>
    /// The regression: arranging at the measured width used to wrap the third tile onto a second line
    /// that had never been measured, so it was drawn below the frame that only had room for one.
    /// </summary>
    [Fact]
    public void Arranging_at_the_measured_width_reproduces_the_measured_layout_of_three_tiles()
    {
        var measured = WrapLayout.Calculate([Tile, Tile, Tile], [Tile, Tile, Tile], 336d, Gap, Gap);
        var arranged = WrapLayout.Calculate([Tile, Tile, Tile], [Tile, Tile, Tile], measured.WidestLine, Gap, Gap);

        Assert.Equal(measured.LineOfItem, arranged.LineOfItem);
        Assert.Equal(measured.LineHeights, arranged.LineHeights);
        Assert.Equal(measured.WidestLine, arranged.WidestLine);
        Assert.Equal(measured.TotalHeight, arranged.TotalHeight);
    }

    /// <summary>The same round trip for two tiles - the case that used to drop the second photo.</summary>
    [Fact]
    public void Two_tiles_stay_side_by_side_when_the_row_is_arranged_at_its_measured_width()
    {
        var measured = WrapLayout.Calculate([Tile, Tile], [Tile, Tile], 336d, Gap, Gap);
        var arranged = WrapLayout.Calculate([Tile, Tile], [Tile, Tile], measured.WidestLine, Gap, Gap);

        Assert.Equal((2 * Tile) + Gap, measured.WidestLine);
        Assert.Equal([0, 0], arranged.LineOfItem);
        Assert.Equal(Tile, arranged.TotalHeight);
    }

    [Fact]
    public void Items_that_do_not_fit_wrap_onto_the_next_line_and_the_round_trip_survives_it()
    {
        // Four tiles cannot fit into 336 px: three stay on the first line, the fourth wraps.
        var measured = WrapLayout.Calculate(
            [Tile, Tile, Tile, Tile], [Tile, Tile, Tile, Tile], 336d, Gap, Gap);

        Assert.Equal([0, 0, 0, 1], measured.LineOfItem);
        Assert.Equal((3 * Tile) + (2 * Gap), measured.WidestLine);
        Assert.Equal(Tile + Tile + Gap, measured.TotalHeight);

        var arranged = WrapLayout.Calculate(
            [Tile, Tile, Tile, Tile], [Tile, Tile, Tile, Tile], measured.WidestLine, Gap, Gap);

        Assert.Equal(measured.LineOfItem, arranged.LineOfItem);
        Assert.Equal(measured.TotalHeight, arranged.TotalHeight);
    }

    [Fact]
    public void Every_line_is_as_tall_as_its_tallest_item()
    {
        // The first line holds a short and a tall item and must take the tall one's height.
        var layout = WrapLayout.Calculate([100d, 100d, 150d], [20d, 60d, 30d], 220d, 10d, 4d);

        Assert.Equal([0, 0, 1], layout.LineOfItem);
        Assert.Equal([60d, 30d], layout.LineHeights);
        Assert.Equal(210d, layout.WidestLine);
        Assert.Equal(94d, layout.TotalHeight);
    }

    [Fact]
    public void An_unlimited_width_never_wraps()
    {
        var layout = WrapLayout.Calculate([Tile, Tile, Tile], [Tile, Tile, Tile], double.PositiveInfinity, Gap, Gap);

        Assert.Equal([0, 0, 0], layout.LineOfItem);
        Assert.Single(layout.LineHeights);
        Assert.Equal((3 * Tile) + (2 * Gap), layout.WidestLine);
    }

    [Fact]
    public void An_empty_row_costs_no_space()
    {
        var layout = WrapLayout.Calculate([], [], 336d, Gap, Gap);

        Assert.Empty(layout.LineOfItem);
        Assert.Empty(layout.LineHeights);
        Assert.Equal(0d, layout.WidestLine);
        Assert.Equal(0d, layout.TotalHeight);
    }
}