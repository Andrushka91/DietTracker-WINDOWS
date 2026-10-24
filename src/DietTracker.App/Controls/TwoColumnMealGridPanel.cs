using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace DietTracker.App.Controls;

/// <summary>
/// Layout of the five permanent meal cards: always two cards per row in card order - Breakfast with
/// morning snack, lunch with afternoon snack - and a final odd card spans both columns, so the
/// dinner row is symmetric with the rows above it. Every card shares the height of the tallest card,
/// which keeps the whole block aligned vertically regardless of the window width.
/// </summary>
public sealed class TwoColumnMealGridPanel : Panel
{
    /// <summary>Identifies the <see cref="HorizontalSpacing"/> dependency property.</summary>
    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
        nameof(HorizontalSpacing),
        typeof(double),
        typeof(TwoColumnMealGridPanel),
        new PropertyMetadata(16d, OnLayoutPropertyChanged));

    /// <summary>Identifies the <see cref="VerticalSpacing"/> dependency property.</summary>
    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
        nameof(VerticalSpacing),
        typeof(double),
        typeof(TwoColumnMealGridPanel),
        new PropertyMetadata(16d, OnLayoutPropertyChanged));

    /// <summary>Horizontal gap between the two columns.</summary>
    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    /// <summary>Vertical gap between the card rows.</summary>
    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => (d as TwoColumnMealGridPanel)?.InvalidateMeasure();

    private double _uniformRowHeight;

    /// <inheritdoc />
    /// <remarks>Defensive against the very first layout pass: the available width can legitimately be
    /// zero (before bindings resolve the content cap) or infinite (inside a ScrollViewer). Negative or
    /// infinite sizes thrown at the children crash the XAML layout, so every computed width is clamped
    /// to a finite, non-negative value.</remarks>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0)
        {
            _uniformRowHeight = 0d;
            return new Size(0d, 0d);
        }

        var spacingH = Math.Max(0d, HorizontalSpacing);
        var spacingV = Math.Max(0d, VerticalSpacing);
        var infiniteWidth = double.IsInfinity(availableSize.Width);
        var fullWidth = infiniteWidth ? double.PositiveInfinity : Math.Max(0d, availableSize.Width);
        var columnWidth = infiniteWidth
            ? double.PositiveInfinity
            : Math.Max(0d, (availableSize.Width - spacingH) / 2d);

        // Two children per row; a leftover single child (the dinner card) measures at the full width.
        var rowHeights = new List<double>();
        var widestChild = 0d;
        var index = 0;
        while (index < Children.Count)
        {
            var remaining = Children.Count - index;
            var isFullSpan = remaining == 1;
            var cellWidth = isFullSpan ? fullWidth : columnWidth;

            var count = isFullSpan ? 1 : Math.Min(2, remaining);
            var rowHeight = 0d;
            for (var offset = 0; offset < count; offset++)
            {
                Children[index + offset].Measure(new Size(cellWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, Children[index + offset].DesiredSize.Height);
                widestChild = Math.Max(widestChild, Children[index + offset].DesiredSize.Width);
            }

            rowHeights.Add(rowHeight);
            index += count;
        }

        // One height for the whole grid: the tallest card decides, so every row lines up vertically.
        _uniformRowHeight = rowHeights.Max();
        var totalHeight = (_uniformRowHeight * rowHeights.Count) + (spacingV * Math.Max(0, rowHeights.Count - 1));

        // Never report an infinite size: the caller crashes on it. When the available width was
        // infinite, the widest child is the honest finite result.
        var resultWidth = infiniteWidth ? widestChild : Math.Max(0d, availableSize.Width);
        return new Size(resultWidth, totalHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0)
        {
            return finalSize;
        }

        var spacingH = Math.Max(0d, HorizontalSpacing);
        var spacingV = Math.Max(0d, VerticalSpacing);
        var infiniteWidth = double.IsInfinity(finalSize.Width);
        var fullWidth = infiniteWidth ? 0d : Math.Max(0d, finalSize.Width);
        var columnWidth = infiniteWidth
            ? 0d
            : Math.Max(0d, (finalSize.Width - spacingH) / 2d);

        var index = 0;
        var y = 0d;
        while (index < Children.Count)
        {
            var remaining = Children.Count - index;
            if (remaining == 1)
            {
                // The last odd card spans both columns to keep the block symmetric.
                var spanWidth = infiniteWidth ? Children[index].DesiredSize.Width : fullWidth;
                Children[index].Arrange(new Rect(0d, y, Math.Max(0d, spanWidth), Math.Max(0d, _uniformRowHeight)));
                index += 1;
            }
            else
            {
                for (var column = 0; column < 2; column++)
                {
                    var x = column * (columnWidth + spacingH);
                    Children[index + column].Arrange(new Rect(
                        Math.Max(0d, x),
                        y,
                        Math.Max(0d, columnWidth),
                        Math.Max(0d, _uniformRowHeight)));
                }

                index += 2;
            }

            y += _uniformRowHeight + spacingV;
        }

        return finalSize;
    }
}