using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace DietTracker.App.Controls;

/// <summary>
/// Responsive wrap panel used for the meal cards and for button rows.
/// Children flow from left to right in their natural order and wrap to the next line when the available
/// width no longer fits <see cref="MinItemWidth"/>, so the layout reflows automatically when the window
/// is resized while the order of the children (the five meals) is always preserved (row-major).
/// In uniform mode (the default) every child is arranged into a cell of exactly the same size, so all
/// cards end up with the same height; in non-uniform mode children keep their desired width and are
/// stretched only to the height of their own line, which is what a button row needs.
/// </summary>
public sealed class UniformWrapPanel : Panel
{
    /// <summary>Identifies the <see cref="MinItemWidth"/> dependency property.</summary>
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth),
        typeof(double),
        typeof(UniformWrapPanel),
        new PropertyMetadata(280d, OnLayoutPropertyChanged));

    /// <summary>Identifies the <see cref="HorizontalSpacing"/> dependency property.</summary>
    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
        nameof(HorizontalSpacing),
        typeof(double),
        typeof(UniformWrapPanel),
        new PropertyMetadata(0d, OnLayoutPropertyChanged));

    /// <summary>Identifies the <see cref="VerticalSpacing"/> dependency property.</summary>
    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
        nameof(VerticalSpacing),
        typeof(double),
        typeof(UniformWrapPanel),
        new PropertyMetadata(0d, OnLayoutPropertyChanged));

    /// <summary>Identifies the <see cref="UniformHeight"/> dependency property.</summary>
    public static readonly DependencyProperty UniformHeightProperty = DependencyProperty.Register(
        nameof(UniformHeight),
        typeof(bool),
        typeof(UniformWrapPanel),
        new PropertyMetadata(true, OnLayoutPropertyChanged));

    /// <summary>Minimum width a child may occupy; it decides how many columns fit into the available width.</summary>
    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    /// <summary>Horizontal gap between cells.</summary>
    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    /// <summary>Vertical gap between lines.</summary>
    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    /// <summary>
    /// <see langword="true"/> (default): all cells share one size, so every child has the same height.
    /// <see langword="false"/>: children keep their desired width and only stretch to the height of their
    /// own line (used for button rows).
    /// </summary>
    public bool UniformHeight
    {
        get => (bool)GetValue(UniformHeightProperty);
        set => SetValue(UniformHeightProperty, value);
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => (d as UniformWrapPanel)?.InvalidateMeasure();

    private readonly List<double> _lineHeights = [];
    private readonly List<int> _lineOfChild = [];
    private int _columns = 1;
    private double _cellWidth;
    private double _widestLine;

    private int ComputeColumns(double availableWidth)
    {
        var minimum = double.IsNaN(MinItemWidth) || MinItemWidth <= 0 ? 1d : MinItemWidth;
        var spacing = Math.Max(0d, HorizontalSpacing);
        var count = Math.Max(1, Children.Count);

        if (double.IsInfinity(availableWidth) || availableWidth <= 0)
        {
            return count;
        }

        var columns = (int)Math.Floor((availableWidth + spacing) / (minimum + spacing));
        return Math.Clamp(columns, 1, count);
    }

    /// <summary>
    /// Builds the line bookkeeping for a given width. Uniform mode uses a fixed number of columns, so
    /// the cells form a real grid; free mode breaks lines when the natural width no longer fits.
    /// </summary>
    private void BuildLayout(double availableWidth)
    {
        var spacingH = Math.Max(0d, HorizontalSpacing);
        _lineHeights.Clear();
        _lineOfChild.Clear();
        _widestLine = 0d;

        if (UniformHeight)
        {
            _columns = ComputeColumns(availableWidth);
            _cellWidth = double.IsInfinity(availableWidth) || availableWidth <= 0
                ? double.PositiveInfinity
                : Math.Max(0d, (availableWidth - spacingH * (_columns - 1)) / _columns);

            for (var index = 0; index < Children.Count; index++)
            {
                var line = index / _columns;
                _lineOfChild.Add(line);
                while (_lineHeights.Count <= line)
                {
                    _lineHeights.Add(0d);
                }

                _lineHeights[line] = Math.Max(_lineHeights[line], Children[index].DesiredSize.Height);
            }

            // One height for the whole panel: the tallest child decides and every cell is stretched to
            // it, which keeps all cards at exactly the same height.
            var tallest = 0d;
            foreach (var height in _lineHeights)
            {
                tallest = Math.Max(tallest, height);
            }

            for (var line = 0; line < _lineHeights.Count; line++)
            {
                _lineHeights[line] = tallest;
            }

            _widestLine = availableWidth;
        }
        else
        {
            _columns = Children.Count;
            _cellWidth = double.PositiveInfinity;

            var line = 0;
            var lineLength = 0d;
            for (var index = 0; index < Children.Count; index++)
            {
                var width = Children[index].DesiredSize.Width;
                if (lineLength > 0
                    && !double.IsInfinity(availableWidth)
                    && lineLength + spacingH + width > availableWidth)
                {
                    _widestLine = Math.Max(_widestLine, lineLength - spacingH);
                    line++;
                    lineLength = 0d;
                }

                _lineOfChild.Add(line);
                while (_lineHeights.Count <= line)
                {
                    _lineHeights.Add(0d);
                }

                _lineHeights[line] = Math.Max(_lineHeights[line], Children[index].DesiredSize.Height);
                lineLength += width + spacingH;
            }

            _widestLine = Math.Max(_widestLine, lineLength > 0 ? lineLength - spacingH : 0d);
        }
    }

    private double TotalHeight()
    {
        var spacingV = Math.Max(0d, VerticalSpacing);
        var total = 0d;
        for (var line = 0; line < _lineHeights.Count; line++)
        {
            total += _lineHeights[line] + (line > 0 ? spacingV : 0d);
        }

        return total;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0)
        {
            return new Size(0d, 0d);
        }

        var spacingH = Math.Max(0d, HorizontalSpacing);
        _columns = ComputeColumns(availableSize.Width);
        _cellWidth = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0
            ? double.PositiveInfinity
            : Math.Max(0d, (availableSize.Width - spacingH * (_columns - 1)) / _columns);

        foreach (var child in Children)
        {
            // Uniform cells: children are measured against the cell width, so text wraps exactly as it
            // will be presented. Free rows: children keep their natural width.
            child.Measure(UniformHeight && !double.IsInfinity(_cellWidth)
                ? new Size(_cellWidth, double.PositiveInfinity)
                : new Size(double.PositiveInfinity, double.PositiveInfinity));
        }

        BuildLayout(availableSize.Width);

        var width = UniformHeight && !double.IsInfinity(availableSize.Width)
            ? availableSize.Width
            : _widestLine;

        return new Size(width, TotalHeight());
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

        BuildLayout(finalSize.Width);

        var y = 0d;
        var x = 0d;
        var currentLine = 0;
        for (var index = 0; index < Children.Count; index++)
        {
            var child = Children[index];
            var line = _lineOfChild[index];
            if (line != currentLine)
            {
                currentLine = line;
                y += _lineHeights[line - 1] + spacingV;
                x = 0d;
            }

            if (UniformHeight)
            {
                var cellWidth = double.IsInfinity(_cellWidth) ? child.DesiredSize.Width : _cellWidth;
                child.Arrange(new Rect(x, y, cellWidth, _lineHeights[line]));
                x += cellWidth + spacingH;
            }
            else
            {
                var desired = child.DesiredSize;
                child.Arrange(new Rect(x, y, desired.Width, _lineHeights[line]));
                x += desired.Width + spacingH;
            }
        }

        return finalSize;
    }
}
