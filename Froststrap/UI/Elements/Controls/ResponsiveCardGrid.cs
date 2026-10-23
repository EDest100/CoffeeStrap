// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Avalonia;
using Avalonia.Controls;

namespace Froststrap.UI.Elements.Controls;

internal sealed class ResponsiveCardGrid : Panel
{
    public static readonly StyledProperty<double> MinimumCardWidthProperty = AvaloniaProperty.Register<ResponsiveCardGrid, double>(nameof(MinimumCardWidth), 260);
    public static readonly StyledProperty<int> MaximumColumnsProperty = AvaloniaProperty.Register<ResponsiveCardGrid, int>(nameof(MaximumColumns), 3);
    public double MinimumCardWidth { get => GetValue(MinimumCardWidthProperty); set => SetValue(MinimumCardWidthProperty, value); }
    public int MaximumColumns { get => GetValue(MaximumColumnsProperty); set => SetValue(MaximumColumnsProperty, value); }
    static ResponsiveCardGrid() => AffectsMeasure<ResponsiveCardGrid>(MinimumCardWidthProperty, MaximumColumnsProperty);
    private Size Layout(double width, bool arrange)
    {
        const double gap = 14;
        if (!double.IsFinite(width)) width = MinimumCardWidth;
        width = Math.Max(0, width);
        var children = Children.Where(c => c.IsVisible).ToArray();
        int columns = Math.Clamp((int)((width + gap) / (MinimumCardWidth + gap)), 1, Math.Max(1, MaximumColumns));
        double card = Math.Max(0, (width - gap * (columns - 1)) / columns), y = 0;
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        double Snap(double number) => Math.Round(number * scale) / scale;
        for (int start = 0; start < children.Length; start += columns)
        {
            var row = children.Skip(start).Take(columns).ToArray();
            foreach (var child in row) child.Measure(new Size(card, double.PositiveInfinity));
            double height = row.Max(c => c.DesiredSize.Height);
            if (arrange)
                for (int i = 0; i < row.Length; i++)
                { double x = Snap(i * (card + gap)), right = Snap(i * (card + gap) + card); row[i].Arrange(new Rect(x, Snap(y), Math.Max(0, right - x), Snap(height))); }
            y += height + gap;
        }
        return new Size(width, Math.Max(0, y - gap));
    }
    protected override Size MeasureOverride(Size availableSize) => Layout(availableSize.Width, false);
    protected override Size ArrangeOverride(Size finalSize) { Layout(finalSize.Width, true); return finalSize; }
}
