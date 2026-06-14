using Avalonia;
using Avalonia.Controls;

namespace Agnosia.Controls;

public sealed class MasonryPanel : Panel
{
    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<MasonryPanel, double>(nameof(ColumnSpacing), 0);

    public static readonly StyledProperty<int> MaxColumnsProperty =
        AvaloniaProperty.Register<MasonryPanel, int>(nameof(MaxColumns), 2);

    public static readonly StyledProperty<double> MinColumnWidthProperty =
        AvaloniaProperty.Register<MasonryPanel, double>(nameof(MinColumnWidth), 160);

    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<MasonryPanel, double>(nameof(RowSpacing), 0);

    static MasonryPanel()
    {
        AffectsMeasure<MasonryPanel>(
            ColumnSpacingProperty,
            MaxColumnsProperty,
            MinColumnWidthProperty,
            RowSpacingProperty);
    }

    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public int MaxColumns
    {
        get => GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    public double MinColumnWidth
    {
        get => GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public double RowSpacing
    {
        get => GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var layout = CreateLayout(availableSize.Width);

        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;

            var columnIndex = FindShortestColumn(layout.ColumnHeights);
            child.Measure(new Size(layout.ColumnWidth, double.PositiveInfinity));

            if (layout.ColumnHeights[columnIndex] > 0)
                layout.ColumnHeights[columnIndex] += RowSpacing;

            layout.ColumnHeights[columnIndex] += child.DesiredSize.Height;
        }

        return new Size(layout.Width, FindTallestColumn(layout.ColumnHeights));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var layout = CreateLayout(finalSize.Width);

        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;

            var columnIndex = FindShortestColumn(layout.ColumnHeights);
            if (layout.ColumnHeights[columnIndex] > 0)
                layout.ColumnHeights[columnIndex] += RowSpacing;

            var x = columnIndex * (layout.ColumnWidth + ColumnSpacing);
            var y = layout.ColumnHeights[columnIndex];
            child.Arrange(new Rect(x, y, layout.ColumnWidth, child.DesiredSize.Height));

            layout.ColumnHeights[columnIndex] += child.DesiredSize.Height;
        }

        return finalSize;
    }

    private MasonryLayout CreateLayout(double availableWidth)
    {
        var maxColumns = Math.Max(1, MaxColumns);
        var minColumnWidth = Math.Max(1, MinColumnWidth);
        var columnSpacing = Math.Max(0, ColumnSpacing);

        if (double.IsInfinity(availableWidth))
        {
            var unconstrainedWidth = (maxColumns * minColumnWidth) + ((maxColumns - 1) * columnSpacing);
            return new MasonryLayout(unconstrainedWidth, minColumnWidth, new double[maxColumns]);
        }

        var columnsByWidth = (int)Math.Floor((availableWidth + columnSpacing) / (minColumnWidth + columnSpacing));
        var columnCount = Math.Clamp(columnsByWidth, 1, maxColumns);
        var columnWidth = (availableWidth - ((columnCount - 1) * columnSpacing)) / columnCount;

        return new MasonryLayout(availableWidth, columnWidth, new double[columnCount]);
    }

    private static int FindShortestColumn(double[] columnHeights)
    {
        var shortestIndex = 0;

        for (var i = 1; i < columnHeights.Length; i++)
        {
            if (columnHeights[i] < columnHeights[shortestIndex])
                shortestIndex = i;
        }

        return shortestIndex;
    }

    private static double FindTallestColumn(double[] columnHeights)
    {
        var tallestHeight = 0d;

        foreach (var columnHeight in columnHeights)
        {
            if (columnHeight > tallestHeight)
                tallestHeight = columnHeight;
        }

        return tallestHeight;
    }

    private sealed record MasonryLayout(double Width, double ColumnWidth, double[] ColumnHeights);
}
