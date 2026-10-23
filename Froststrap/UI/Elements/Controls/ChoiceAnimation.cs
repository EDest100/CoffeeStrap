// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Froststrap.UI.Utility;

namespace Froststrap.UI.Elements.Controls;

internal sealed class ChoiceAnimation : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ChoiceAnimation, ComboBox, bool>("Enabled");
    public static bool GetEnabled(ComboBox control) => control.GetValue(EnabledProperty);
    public static void SetEnabled(ComboBox control, bool value) => control.SetValue(EnabledProperty, value);
    private static readonly ConditionalWeakTable<ComboBox, State> States = new();
    static ChoiceAnimation() => EnabledProperty.Changed.AddClassHandler<ComboBox>((box, args) =>
    {
        if (args.NewValue is true) States.GetValue(box, b => new State(b));
    });
    private sealed class State
    {
        private Control? _popup;
        private readonly TranslateTransform _translation = new();
        private readonly CoffeeMotion _motion;
        public State(ComboBox box)
        {
            _motion = new CoffeeMotion(box, t =>
            {
                if (_popup == null) return;
                _popup.Opacity = t;
                _translation.Y = -5 * (1 - t);
            });
            box.DropDownOpened += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (!box.IsDropDownOpen) return;
                _popup = box.GetVisualDescendants().OfType<Popup>().FirstOrDefault(p => p.IsOpen)?.Child;
                if (_popup == null) return;
                _popup.RenderTransform = _translation;
                _motion.Jump(CoffeeMotion.Enabled ? 0 : 1);
                _motion.To(1, 150);
            });
            box.DropDownClosed += (_, _) => _motion.Jump(1);
            box.DetachedFromVisualTree += (_, _) => _motion.Stop();
        }
    }
}
