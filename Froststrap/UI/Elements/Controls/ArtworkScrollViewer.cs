// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Froststrap.UI.Utility;
namespace Froststrap.UI.Elements.Controls;

internal sealed class ArtworkScrollViewer : ScrollViewer
{
    protected override Type StyleKeyOverride => typeof(ScrollViewer);
    private readonly CoffeeMotion _motion;
    private bool _applying;
    public ArtworkScrollViewer()
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        _motion = new CoffeeMotion(this, x => { _applying = true; Offset = new Vector(Math.Clamp(x, 0, ScrollBarMaximum.X), 0); _applying = false; });
        AddHandler(PointerWheelChangedEvent, Wheel, RoutingStrategies.Tunnel);
    }
    private void Wheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Handled || ScrollBarMaximum.X <= 0) return;
        double delta = Math.Abs(e.Delta.X) > .01 ? e.Delta.X : e.Delta.Y;
        double target = Math.Clamp((_motion.IsRunning ? _motion.Target : Offset.X) - delta * 80, 0, ScrollBarMaximum.X);
        if (CoffeeMotion.Enabled) { if (!_motion.IsRunning) _motion.Jump(Offset.X); _motion.To(target, 170); }
        else Offset = new Vector(target, 0);
        e.Handled = true;
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    { base.OnPropertyChanged(change); if (change.Property == OffsetProperty && !_applying) _motion?.Stop(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { _motion.Stop(); base.OnDetachedFromVisualTree(e); }
}
