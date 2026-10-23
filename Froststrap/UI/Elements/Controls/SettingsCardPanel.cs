// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Avalonia;
using Avalonia.Controls;

namespace Froststrap.UI.Elements.Controls;

/// <summary>Responsive equal-width card rows; complex editors retain the full width.</summary>
internal sealed class SettingsCardPanel : Panel
{
    public static readonly AttachedProperty<int> ColumnLimitProperty =
        AvaloniaProperty.RegisterAttached<SettingsCardPanel, Control, int>("ColumnLimit", 2, inherits: true);
    static SettingsCardPanel() => AffectsMeasure<SettingsCardPanel>(ColumnLimitProperty);
    public static int GetColumnLimit(Control control) => Math.Clamp(control.GetValue(ColumnLimitProperty), 1, 2);
    public static void SetColumnLimit(Control control, int value) => control.SetValue(ColumnLimitProperty, value);
    private const double Gap = 14;
    private const double MinimumCardWidth = 310;
    private readonly List<(Control Child, Rect Rect)> _layout = [];

    private Size Layout(double width, bool measure)
    {
        if (!double.IsFinite(width)) width = MinimumCardWidth;
        width = Math.Max(0, width);
        int maximumColumns = Math.Clamp((int)((width + Gap) / (MinimumCardWidth + Gap)), 1, GetColumnLimit(this));
        double y = 0;
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        double Snap(double value) => Math.Round(value * scale) / scale;
        var run = new List<Control>();
        _layout.Clear();
        void Flush()
        {
            if (run.Count == 0) return;
            int columns = Math.Min(maximumColumns, run.Count);
            double cardWidth = Math.Max(0, (width - Gap * (columns - 1)) / columns);
            for (int first = 0; first < run.Count; first += columns)
            {
                var row = run.Skip(first).Take(columns).ToArray();
                if (measure)
                    foreach (var child in row) child.Measure(new Size(cardWidth, double.PositiveInfinity));
                double rowHeight = row.Max(c => c.DesiredSize.Height);
                for (int i = 0; i < row.Length; i++)
                {
                    double left = row.Length == 1 ? 0 : Snap(i * (cardWidth + Gap));
                    double right = row.Length == 1 ? Snap(width) : Snap(i * (cardWidth + Gap) + cardWidth);
                    _layout.Add((row[i], new Rect(left, Snap(y), Math.Max(0, right - left), Snap(rowHeight))));
                }
                y += rowHeight + Gap;
            }
            run.Clear();
        }
        foreach (var child in Children.Where(c => c.IsVisible))
        {
            bool compact = child is OptionControl option && option.IsCompactCard;
            if (compact)
            {
                run.Add(child);
            }
            else
            {
                Flush();
                if (measure) child.Measure(new Size(width, double.PositiveInfinity));
                _layout.Add((child, new Rect(0, y, width, child.DesiredSize.Height)));
                y += child.DesiredSize.Height + Gap;
            }
        }
        Flush();
        return new Size(width, Math.Max(0, y - Gap));
    }

    protected override Size MeasureOverride(Size availableSize) => Layout(availableSize.Width, true);
    protected override Size ArrangeOverride(Size finalSize)
    {
        Layout(finalSize.Width, false);
        foreach (var (child, rect) in _layout) child.Arrange(rect);
        return finalSize;
    }
}
