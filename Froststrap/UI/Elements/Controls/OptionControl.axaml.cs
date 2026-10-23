// SPDX-FileCopyrightText: 2026 Froststrap
//
// SPDX-License-Identifier: MPL-2.0

using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Metadata;
using Avalonia.Data;

namespace Froststrap.UI.Elements.Controls
{
    internal partial class OptionControl : TemplatedControl
    {
        private Utility.CoffeeMotion? _hover;
        private Point? _pressedAt;
        private bool _keyHeld;
        internal ToggleSwitch? Toggle => EditorControls.OfType<ToggleSwitch>().SingleOrDefault();
        internal ComboBox? Choice => EditorControls.OfType<ComboBox>().SingleOrDefault();
        private IEnumerable<Control> EditorControls => GetEditorControls(InnerContent);
        internal static IEnumerable<Control> GetEditorControls(object? content) => content is Control control
            ? new[] { control }.Concat(control.GetLogicalDescendants().OfType<Control>()) : [];
        internal bool IsCompactCard => Toggle != null || Choice != null;

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            _hover?.Stop();
            if (e.NameScope.Find<Control>("PART_RootCard") is { } card)
            {
                var transform = new Avalonia.Media.TranslateTransform(); card.RenderTransform = transform;
                _hover = new Utility.CoffeeMotion(this, value => transform.Y = -1.5 * value);
                _hover.Jump(IsPointerOver && IsEffectivelyEnabled ? 1 : 0);
            }
            ConfigureEditor();
        }
        protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); if (IsEffectivelyEnabled) _hover?.To(1, 150); }
        protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _hover?.To(0, 170); }
        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { _hover?.Jump(0); base.OnDetachedFromVisualTree(e); }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == InnerContentProperty || change.Property == HeaderProperty) ConfigureEditor();
        }

        private void ConfigureEditor()
        {
            var toggle = Toggle;
            PseudoClasses.Set(":compact", IsCompactCard);
            PseudoClasses.Set(":toggle", toggle != null);
            PseudoClasses.Set(":choice", Choice != null);
            Focusable = toggle != null;
            AutomationProperties.SetName(this, Header ?? "");
            if (toggle != null)
            {
                AutomationProperties.SetName(toggle, Header ?? "");
                // The card is the keyboard/automation toggle; its switch remains pointer-operable.
                toggle.Focusable = false;
            }
            if (Choice is { } choice)
            {
                AutomationProperties.SetName(choice, Header ?? "");
                bool stacked = Classes.Contains("stacked");
                choice.SetCurrentValue(MinWidthProperty, stacked ? 0d : 128d);
                choice.SetCurrentValue(MaxWidthProperty, stacked ? double.PositiveInfinity : 200d);
                if (stacked) choice.SetCurrentValue(HorizontalAlignmentProperty, Avalonia.Layout.HorizontalAlignment.Stretch);
                choice.SetCurrentValue(MarginProperty, new Thickness(0));
                choice.Bind(ToolTip.TipProperty, new Binding(nameof(ComboBox.SelectedItem))
                { Source = choice, Converter = new Converters.EnumNameConverter() });
            }
            InvalidateMeasure();
        }

        internal bool ToggleValue()
        {
            if (!IsEffectivelyEnabled || Toggle is not { IsEffectivelyEnabled: true } toggle) return false;
            toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, toggle.IsChecked != true);
            return true;
        }

        private bool IsEditorSource(object? source) => source is Visual visual && visual.GetSelfAndVisualAncestors()
            .TakeWhile(v => v != this).Any(v => v is Button or ToggleSwitch or ComboBox or TextBox or Slider or NumericUpDown);

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (Toggle == null || IsEditorSource(e.Source) || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _pressedAt = e.GetPosition(this);
            Focus(NavigationMethod.Pointer);
            e.Pointer.Capture(this);
            PseudoClasses.Set(":pressed", true);
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_pressedAt is not { } start) return;
            _pressedAt = null;
            var end = e.GetPosition(this);
            e.Pointer.Capture(null);
            PseudoClasses.Set(":pressed", false);
            if (new Rect(Bounds.Size).Contains(end) && Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2) < 64) ToggleValue();
            e.Handled = true;
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        { _pressedAt = null; PseudoClasses.Set(":pressed", false); base.OnPointerCaptureLost(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Source != this || e.Key is not (Key.Space or Key.Enter) || Toggle == null) return;
            if (!_keyHeld) ToggleValue();
            _keyHeld = true; e.Handled = true;
        }
        protected override void OnKeyUp(KeyEventArgs e)
        { if (e.Key is Key.Space or Key.Enter) _keyHeld = false; base.OnKeyUp(e); }
        protected override void OnLostFocus(FocusChangedEventArgs e)
        { _keyHeld = false; base.OnLostFocus(e); }
        protected override AutomationPeer OnCreateAutomationPeer() => Toggle != null
            ? new CardTogglePeer(this) : new ControlAutomationPeer(this);

        private sealed class CardTogglePeer(OptionControl owner) : ControlAutomationPeer(owner), IToggleProvider
        {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.CheckBox;
            public ToggleState ToggleState => owner.Toggle?.IsChecked == true ? ToggleState.On : ToggleState.Off;
            public void Toggle() => owner.ToggleValue();
        }
        public static readonly StyledProperty<string?> HeaderProperty =
            AvaloniaProperty.Register<OptionControl, string?>(nameof(Header));

        public static readonly StyledProperty<string?> DescriptionProperty =
            AvaloniaProperty.Register<OptionControl, string?>(nameof(Description));

        public static readonly StyledProperty<string?> HelpLinkProperty =
            AvaloniaProperty.Register<OptionControl, string?>(nameof(HelpLink));

        public static readonly StyledProperty<object?> InnerContentProperty =
            AvaloniaProperty.Register<OptionControl, object?>(nameof(InnerContent));

        public string? Header
        {
            get => GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }

        public string? Description
        {
            get => GetValue(DescriptionProperty);
            set => SetValue(DescriptionProperty, value);
        }

        public string? HelpLink
        {
            get => GetValue(HelpLinkProperty);
            set => SetValue(HelpLinkProperty, value);
        }

        [Content]
        public object? InnerContent
        {
            get => GetValue(InnerContentProperty);
            set => SetValue(InnerContentProperty, value);
        }
    }
}
