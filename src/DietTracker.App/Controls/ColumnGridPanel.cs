using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace DietTracker.App.Controls;

/// <summary>
/// Grid of equal-width columns, filled row-major. The month calendar uses it with
/// <see cref="Columns"/> set to seven (one week per row) so the weekday header and the day grid
/// line up exactly; the statistics trend charts leave <see cref="Columns"/> at zero, which means
/// one row with exactly one column per child - the chart then always spans the whole card, no
/// matter how many days or months it shows. All cells of a row share one height
/// (<see cref="RowHeight"/>, or the tallest cell when it is zero), and a requested row height never
/// exceeds the cell width, so the day cells stay as square as the panel allows while the whole
/// month remains inside it.
/// </summary>
public sealed class ColumnGridPanel : Panel
{
    /// <summary>Identifies the <see cref="Columns"/> dependency property.</summary>
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns),
        typeof(int),
        typeof(ColumnGridPanel),
        new PropertyMetadata(7, OnLayoutPropertyChanged));

    /// <summary>Identifies the <see cref="HorizontalSpacing"/> dependency property.</summary>
    public static readonly DependencyProperty HorizontalSpacingProperty = DependencyProperty.Register(
        nameof(HorizontalSpacing),
        typeof(double),
        typeof(ColumnGridPanel),
        new PropertyMetadata(8d, OnLayoutPropertyChanged));

    /// <summary>Identifies the <see cref="VerticalSpacing"/> dependency property.</summary>
    public static readonly DependencyProperty VerticalSpacingProperty = DependencyProperty.Register(
        nameof(VerticalSpacing),
        typeof(double),
        typeof(ColumnGridPanel),
        new PropertyMetadata(8d, OnLayoutPropertyChanged));

    /// <summary>Identifies the <see cref="RowHeight"/> dependency property.</summary>
    public static readonly DependencyProperty RowHeightProperty = DependencyProperty.Register(
        nameof(RowHeight),
        typeof(double),
        typeof(ColumnGridPanel),
        new PropertyMetadata(0d, OnLayoutPropertyChanged));

    /// <summary>Number of equal-width columns; zero puts every child into its own column of a single row.</summary>
    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    /// <summary>Horizontal gap between the cells.</summary>
    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    /// <summary>Vertical gap between the weeks.</summary>
    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    /// <summary>
    /// Height of one row in device-independent pixels; zero means "natural" (the tallest cell
    /// content decides). The calendar page sets it on the day grid so the squares of the month grow
    /// towards the square - never taller than the cell is wide - and shrink when the viewport cannot
    /// fit the whole month at its natural height.
    /// </summary>
    public double RowHeight
    {
        get => (double)GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => (d as ColumnGridPanel)?.InvalidateMeasure();

    /// <summary>The effective column count: the requested number, or one column per child when
    /// <see cref="Columns"/> is zero (the children always form a single row then).</summary>
    private int EffectiveColumns()
        => Columns <= 0 ? Children.Count : Math.Max(1, Columns);

    /// <inheritdoc />
    /// <remarks>Defensive against the first layout pass: the available width can be infinite
    /// (inside a ScrollViewer) or the window can be too narrow for the requested column count
    /// (never below one column and never below zero width), so the geometry stays valid in every
    /// pass.</remarks>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0)
        {
            return new Size(0d, 0d);
        }

        var columns = EffectiveColumns();
        var spacingH = Math.Max(0d, HorizontalSpacing);
        var spacingV = Math.Max(0d, VerticalSpacing);
        var infiniteWidth = double.IsInfinity(availableSize.Width);
        var cellWidth = infiniteWidth
            ? double.PositiveInfinity
            : Math.Max(0d, (Math.Max(0d, availableSize.Width) - (spacingH * (columns - 1))) / columns);

        var naturalRow = 0d;
        var widestChild = 0d;
        foreach (var child in Children)
        {
            child.Measure(new Size(cellWidth, double.PositiveInfinity));
            naturalRow = Math.Max(naturalRow, child.DesiredSize.Height);
            widestChild = Math.Max(widestChild, child.DesiredSize.Width);
        }

        var rows = (Children.Count + columns - 1) / columns;

        // Row height: the natural height of the tallest cell, unless the page requested one for the
        // day squares (RowHeight). A requested row never grows past the square - a cell taller than
        // it is wide would take space away from the days of the month - and never becomes negative.
        var rowHeight = naturalRow;
        if (RowHeight > 0d)
        {
            var requested = double.IsInfinity(cellWidth) ? RowHeight : Math.Min(RowHeight, cellWidth);
            rowHeight = Math.Max(0d, requested);
        }

        // Whatever the height comes from, the grid never asks for more than is actually available:
        // the whole month beats a comfortable cell, so a short window makes the squares a little
        // smaller instead of pushing days out of the panel.
        if (!double.IsInfinity(availableSize.Height))
        {
            var fittingRow = (Math.Max(0d, availableSize.Height) - (spacingV * (rows - 1))) / rows;
            rowHeight = Math.Max(0d, Math.Min(rowHeight, fittingRow));
        }

        var totalHeight = (rowHeight * rows) + (spacingV * (rows - 1));
        var resultWidth = infiniteWidth ? widestChild : Math.Max(0d, availableSize.Width);
        return new Size(resultWidth, Math.Max(0d, totalHeight));
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0)
        {
            return finalSize;
        }

        var columns = EffectiveColumns();
        var spacingH = Math.Max(0d, HorizontalSpacing);
        var spacingV = Math.Max(0d, VerticalSpacing);
        var cellWidth = Math.Max(0d, (Math.Max(0d, finalSize.Width) - (spacingH * (columns - 1))) / columns);
        var rows = (Children.Count + columns - 1) / columns;
        var rowHeight = Math.Max(0d, (Math.Max(0d, finalSize.Height) - (spacingV * (rows - 1))) / rows);

        // The day squares (a row height was requested) grow with the row they are given, but never
        // past the square: extra height would be empty space inside one cell instead of another day
        // of the month. Charts and the weekday header keep their natural row height.
        if (RowHeight > 0d)
        {
            rowHeight = Math.Min(rowHeight, cellWidth);
        }

        for (var index = 0; index < Children.Count; index++)
        {
            var row = index / columns;
            var column = index % columns;
            var x = (column * (cellWidth + spacingH));
            var y = (row * (rowHeight + spacingV));
            Children[index].Arrange(new Rect(Math.Max(0d, x), Math.Max(0d, y), cellWidth, rowHeight));
        }

        return finalSize;
    }
}