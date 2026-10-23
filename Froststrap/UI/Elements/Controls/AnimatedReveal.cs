// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Avalonia;
using Avalonia.Controls;
using Froststrap.UI.Utility;

namespace Froststrap.UI.Elements.Controls;

/// <summary>Measures content at natural height and clips it during a reversible reveal.</summary>
internal sealed class AnimatedReveal : Decorator
{
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<AnimatedReveal, bool>(nameof(IsExpanded));
    public bool IsExpanded { get => GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }
    private readonly CoffeeMotion _motion;
    private double _naturalHeight;
    private bool _initialized, _measuring;
    internal double RevealedHeight => _motion.Value;
    internal double NaturalHeight => _naturalHeight;
    internal bool HasActiveMotion => _motion.IsRunning;
    public AnimatedReveal()
    {
        ClipToBounds = true;
        _motion = new CoffeeMotion(this, height =>
        {
            double fraction = _naturalHeight <= 0 ? 0 : Math.Clamp(height / _naturalHeight, 0, 1);
            Opacity = height < .1 ? 0 : .75 + .25 * fraction;
            IsHitTestVisible = IsExpanded;
            SetCurrentValue(IsEnabledProperty, IsExpanded || height > .1);
            if (!_measuring) InvalidateMeasure();
        });
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsExpandedProperty || _motion == null) return;
        if (IsExpanded) SetCurrentValue(IsEnabledProperty, true);
        if (_initialized) _motion.To(IsExpanded ? _naturalHeight : 0, 240);
        InvalidateMeasure();
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        _measuring = true;
        Child?.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        _naturalHeight = Child?.DesiredSize.Height ?? 0;
        double target = IsExpanded ? _naturalHeight : 0;
        if (!_initialized) { _initialized = true; _motion.Jump(target); }
        else if (Math.Abs(_motion.Target - target) > .5) _motion.To(target, 240);
        _measuring = false;
        return new Size(Child?.DesiredSize.Width ?? 0, Math.Max(0, _motion.Value));
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        // Content stays full-sized and disappears naturally behind the clipping edge.
        Child?.Arrange(new Rect(0, 0, finalSize.Width, _naturalHeight));
        return finalSize;
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { _motion.Stop(); base.OnDetachedFromVisualTree(e); }
}
