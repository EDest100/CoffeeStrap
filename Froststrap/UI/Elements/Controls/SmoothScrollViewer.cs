// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Froststrap.UI.Utility;

namespace Froststrap.UI.Elements.Controls;

internal sealed class SmoothScrollViewer : ScrollViewer
{
    protected override Type StyleKeyOverride => typeof(ScrollViewer);
    private readonly CoffeeMotion _motion;
    private bool _applying;
    internal bool HasActiveMotion => _motion.IsRunning;
    internal double TargetOffset => _motion.Target;
    internal int AnimationTicks => _motion.TickCount;

    public SmoothScrollViewer()
    {
        _motion = new CoffeeMotion(this, y =>
        {
            _applying = true;
            SetCurrentValue(OffsetProperty, new Vector(Offset.X, Math.Clamp(y, 0, ScrollBarMaximum.Y)));
            _applying = false;
        });
        AddHandler(PointerWheelChangedEvent, HandleWheel, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, (_, _) => _motion.Stop(), RoutingStrategies.Tunnel, true);
    }

    private void HandleWheel(object? sender, PointerWheelEventArgs e)
    {
        // A child editor or a popup owns its input. Horizontal input remains native.
        bool editorOwnsInput = e.Source is Visual source && source.GetSelfAndVisualAncestors()
            .TakeWhile(v => v != this).Any(v => v is ComboBox or NumericUpDown or Slider);
        if (e.Source is Visual origin && origin.GetSelfAndVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() != this) return;
        if (e.Handled || editorOwnsInput || !CoffeeMotion.Enabled ||
            Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) || ScrollBarMaximum.Y <= 0)
        {
            return;
        }
        double basis = _motion.IsRunning ? _motion.Target : Offset.Y;
        double target = Math.Clamp(basis - e.Delta.Y * 52, 0, ScrollBarMaximum.Y);
        if (Math.Abs(target - basis) < .01 && !_motion.IsRunning) return;
        if (!_motion.IsRunning) _motion.Jump(Offset.Y);
        _motion.To(target, 170);
        e.Handled = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Scrollbars, BringIntoView, search navigation and keyboard input interrupt immediately.
        if (change.Property == OffsetProperty && !_applying) _motion?.Stop();
    }
    protected override void OnKeyDown(KeyEventArgs e) { _motion.Stop(); base.OnKeyDown(e); }
    protected override void OnPointerPressed(PointerPressedEventArgs e) { _motion.Stop(); base.OnPointerPressed(e); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { _motion.Stop(); base.OnDetachedFromVisualTree(e); }
}
