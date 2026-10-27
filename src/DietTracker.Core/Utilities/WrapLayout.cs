namespace DietTracker.Core.Utilities;

/// <summary>
/// Row-major flow (wrapping) layout used by the wrapping panels of the UI: items are placed from left
/// to right and a new line starts as soon as the next item no longer fits into the available width.
/// The calculation is intentionally free of any UI framework type, so the geometry that decides where
/// a thumbnail or a button lands can be unit tested on its own.
/// </summary>
public static class WrapLayout
{
    /// <summary>Calculates the line assignment, the line heights and the outer size of a wrapping row.</summary>
    /// <param name="itemWidths">Natural width of every item, in children order.</param>
    /// <param name="itemHeights">Natural height of every item, in children order.</param>
    /// <param name="availableWidth">Width the items must fit into; infinity means "never wrap".</param>
    /// <param name="horizontalSpacing">Gap between two items of the same line.</param>
    /// <param name="verticalSpacing">Gap between two lines.</param>
    /// <remarks>
    /// <para>
    /// The wrap test is <c>lineLength + width > availableWidth</c>: <c>lineLength</c> already
    /// carries the gap that follows every placed item, which is exactly the gap needed before the next
    /// one, so the gap must not be counted a second time.
    /// </para>
    /// <para>
    /// This matters because the layout is evaluated twice: once with the width the parent offers
    /// (measure) and once with <see cref="WrapLayoutResult.WidestLine"/> (arrange - a left-aligned
    /// frame shrinks its content down to the measured width). Both passes must produce the same lines,
    /// otherwise an item moves onto a line that was never measured and is painted outside the control
    /// hosting it - the "second thumbnail falls out of the gallery frame" bug.
    /// </para>
    /// </remarks>
    public static WrapLayoutResult Calculate(
        IReadOnlyList<double> itemWidths,
        IReadOnlyList<double> itemHeights,
        double availableWidth,
        double horizontalSpacing,
        double verticalSpacing)
    {
        ArgumentNullException.ThrowIfNull(itemWidths);
        ArgumentNullException.ThrowIfNull(itemHeights);

        var spacingH = Math.Max(0d, horizontalSpacing);
        var spacingV = Math.Max(0d, verticalSpacing);

        var lineOfItem = new List<int>(itemWidths.Count);
        var lineHeights = new List<double>();
        var line = 0;

        // Width of the current line so far, trailing gap of every placed item included.
        var lineLength = 0d;
        var widestLine = 0d;

        for (var index = 0; index < itemWidths.Count; index++)
        {
            var width = itemWidths[index];
            if (lineLength > 0
                && !double.IsInfinity(availableWidth)
                && lineLength + width > availableWidth)
            {
                widestLine = Math.Max(widestLine, lineLength - spacingH);
                line++;
                lineLength = 0d;
            }

            lineOfItem.Add(line);
            while (lineHeights.Count <= line)
            {
                lineHeights.Add(0d);
            }

            var height = index < itemHeights.Count ? itemHeights[index] : 0d;
            lineHeights[line] = Math.Max(lineHeights[line], height);
            lineLength += width + spacingH;
        }

        widestLine = Math.Max(widestLine, lineLength > 0 ? lineLength - spacingH : 0d);

        var totalHeight = 0d;
        for (var index = 0; index < lineHeights.Count; index++)
        {
            totalHeight += lineHeights[index] + (index > 0 ? spacingV : 0d);
        }

        return new WrapLayoutResult(lineOfItem, lineHeights, widestLine, totalHeight);
    }
}

/// <summary>Geometry of one wrapped row: where every item lands and how much space the row needs.</summary>
/// <param name="LineOfItem">Zero based line index of every item, in children order.</param>
/// <param name="LineHeights">Height of every line (the tallest item of that line).</param>
/// <param name="WidestLine">Width of the longest line; the gaps between its items counted in, the trailing gap excluded.</param>
/// <param name="TotalHeight">Height of the whole row: the line heights plus the gaps between lines.</param>
public sealed record WrapLayoutResult(
    IReadOnlyList<int> LineOfItem,
    IReadOnlyList<double> LineHeights,
    double WidestLine,
    double TotalHeight);