// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Froststrap.UI.Utility;

namespace Froststrap.UI.Elements.Controls;

internal sealed class CoffeeNavigationItem : Button
{
    public static readonly StyledProperty<object?> IconProperty = AvaloniaProperty.Register<CoffeeNavigationItem, object?>(nameof(Icon));
    public object? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
}
internal sealed class CoffeeNavigationInvokedEventArgs(CoffeeNavigationItem item) : EventArgs
{ public CoffeeNavigationItem InvokedItemContainer { get; } = item; }

/// <summary>Inline, frame-driven navigation with a single persistent highlight.</summary>
internal sealed class CoffeeNavigationView : ContentControl
{
    public AvaloniaList<CoffeeNavigationItem> MenuItems { get; } = [];
    public AvaloniaList<CoffeeNavigationItem> FooterMenuItems { get; } = [];
    public static readonly StyledProperty<bool> IsPaneOpenProperty = AvaloniaProperty.Register<CoffeeNavigationView, bool>(nameof(IsPaneOpen));
    public static readonly StyledProperty<double> OpenPaneLengthProperty = AvaloniaProperty.Register<CoffeeNavigationView, double>(nameof(OpenPaneLength), 232);
    public static readonly StyledProperty<double> CompactPaneLengthProperty = AvaloniaProperty.Register<CoffeeNavigationView, double>(nameof(CompactPaneLength), 68);
    public static readonly StyledProperty<double> CurrentPaneLengthProperty = AvaloniaProperty.Register<CoffeeNavigationView, double>(nameof(CurrentPaneLength), 68);
    public static readonly StyledProperty<object?> PaneToggleRowContentProperty = AvaloniaProperty.Register<CoffeeNavigationView, object?>(nameof(PaneToggleRowContent));
    public static readonly StyledProperty<CoffeeNavigationItem?> SelectedItemProperty = AvaloniaProperty.Register<CoffeeNavigationView, CoffeeNavigationItem?>(nameof(SelectedItem));
    public bool IsPaneOpen { get => GetValue(IsPaneOpenProperty); set => SetValue(IsPaneOpenProperty, value); }
    public double OpenPaneLength { get => GetValue(OpenPaneLengthProperty); set => SetValue(OpenPaneLengthProperty, value); }
    public double CompactPaneLength { get => GetValue(CompactPaneLengthProperty); set => SetValue(CompactPaneLengthProperty, value); }
    public double CurrentPaneLength { get => GetValue(CurrentPaneLengthProperty); private set => SetValue(CurrentPaneLengthProperty, value); }
    public object? PaneToggleRowContent { get => GetValue(PaneToggleRowContentProperty); set => SetValue(PaneToggleRowContentProperty, value); }
    public CoffeeNavigationItem? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public event EventHandler<CoffeeNavigationInvokedEventArgs>? ItemInvoked;
    private readonly CoffeeMotion _widthMotion, _highlightMotion;
    private readonly TranslateTransform _highlightTransform = new();
    private readonly DispatcherTimer _closeDelay = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private Control? _pane, _contentHost;
    private Border? _highlight;
    private CoffeeNavigationItem? _hovered, _focused;
    private bool _pointerInside, _keyboardInside;
    internal bool HasActiveMotion => _widthMotion.IsRunning || _highlightMotion.IsRunning;
    internal double HighlightTarget => _highlightMotion.Target;
    internal double HighlightPosition => _highlightTransform.Y;
    internal CoffeeNavigationItem? HoveredItem => _hovered;
    internal object WidthDiagnostics => _widthMotion.Diagnostics;

    public CoffeeNavigationView()
    {
        _widthMotion = new CoffeeMotion(this, width => CurrentPaneLength = width);
        _highlightMotion = new CoffeeMotion(this, y => _highlightTransform.Y = y);
        _widthMotion.Jump(CompactPaneLength);
        _closeDelay.Tick += (_, _) => { _closeDelay.Stop(); if (!_pointerInside && !_keyboardInside) IsPaneOpen = false; };
        AddHandler(PointerMovedEvent, TrackPointer, RoutingStrategies.Bubble, true);
        AddHandler(PointerPressedEvent, (_, e) =>
        { if (Inside(e.GetPosition(this))) { _keyboardInside = false; IsPaneOpen = true; } }, RoutingStrategies.Tunnel, true);
        AddHandler(GotFocusEvent, TrackFocus, RoutingStrategies.Bubble, true);
        AddHandler(LostFocusEvent, (_, _) => Dispatcher.UIThread.Post(CheckFocus), RoutingStrategies.Bubble, true);
        AddHandler(Button.ClickEvent, OnItemClick, RoutingStrategies.Bubble, true);
    }
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _pane = e.NameScope.Find<Control>("PART_Pane");
        _contentHost = e.NameScope.Find<Control>("PART_FeatureArea");
        _highlight = e.NameScope.Find<Border>("PART_Highlight");
        if (_highlight != null) _highlight.RenderTransform = _highlightTransform;
        _widthMotion.Jump(IsPaneOpen ? OpenPaneLength : CompactPaneLength);
        UpdateColumnLimit();
        Dispatcher.UIThread.Post(() => MoveHighlight(true), DispatcherPriority.Loaded);
    }
    private bool Inside(Point point)
    {
        var origin = _pane?.TranslatePoint(default, this) ?? new Point(12, 0);
        return point.X >= origin.X - 3 && point.X < origin.X + CurrentPaneLength + 3 && point.Y >= 0 && point.Y < Bounds.Height;
    }
    private static CoffeeNavigationItem? FindItem(object? source) => (source as Visual)?.GetSelfAndVisualAncestors().OfType<CoffeeNavigationItem>().FirstOrDefault();
    private bool InPane(object? source) => source is Visual visual && visual.GetSelfAndVisualAncestors().Any(v => v == _pane);
    private void TrackPointer(object? sender, PointerEventArgs e)
    {
        bool inside = Inside(e.GetPosition(this));
        _pointerInside = inside;
        if (inside)
        {
            _closeDelay.Stop(); IsPaneOpen = true;
            // Gaps and icon/text boundaries preserve the last hovered row.
            if (FindItem(e.Source) is { IsEffectivelyEnabled: true } item) _hovered = item;
        }
        else { _hovered = null; ScheduleClose(); }
        MoveHighlight();
    }
    protected override void OnPointerExited(PointerEventArgs e)
    { _pointerInside = false; _hovered = null; ScheduleClose(); MoveHighlight(); base.OnPointerExited(e); }
    private void TrackFocus(object? sender, FocusChangedEventArgs e)
    {
        if (!InPane(e.Source) || e.NavigationMethod == NavigationMethod.Pointer) return;
        _focused = FindItem(e.Source); _keyboardInside = true; _closeDelay.Stop(); IsPaneOpen = true; MoveHighlight();
    }
    private void CheckFocus()
    {
        if (!InPane(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement()))
        { _keyboardInside = false; _focused = null; ScheduleClose(); MoveHighlight(); }
    }
    private void ScheduleClose() { if (!_closeDelay.IsEnabled) _closeDelay.Start(); }
    private void OnItemClick(object? sender, RoutedEventArgs e)
    {
        if (FindItem(e.Source) is not { IsEffectivelyEnabled: true } item) return;
        if (!Equals(item.Tag, "about")) SelectedItem = item;
        ItemInvoked?.Invoke(this, new CoffeeNavigationInvokedEventArgs(item));
    }
    private void MoveHighlight(bool immediate = false)
    {
        var item = _hovered ?? (_keyboardInside ? _focused : null) ?? SelectedItem;
        if (_pane == null || _highlight == null || item == null || item.TranslatePoint(default, _highlight.GetVisualParent()!) is not { } point) return;
        _highlight.IsVisible = true;
        _highlight.Height = item.Bounds.Height > 0 ? item.Bounds.Height : 44;
        if (immediate) _highlightMotion.Jump(point.Y); else _highlightMotion.To(point.Y, 180);
    }
    private void UpdateColumnLimit()
    {
        // Pick columns for the fully expanded rail, avoiding repeated breakpoint flips
        // while its width animates. Actual card widths still follow available space.
        SettingsCardPanel.SetColumnLimit(this, Bounds.Width - OpenPaneLength - 64 >= 634 ? 2 : 1);
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_widthMotion == null) return;
        if (change.Property == IsPaneOpenProperty) _widthMotion.To(IsPaneOpen ? OpenPaneLength : CompactPaneLength, 220);
        if (change.Property == BoundsProperty) UpdateColumnLimit();
        if (change.Property == SelectedItemProperty)
        {
            foreach (var item in MenuItems.Concat(FooterMenuItems)) item.Classes.Set("selected", item == SelectedItem);
            MoveHighlight();
        }
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsPaneOpen)
        { _keyboardInside = _pointerInside = false; _hovered = null; IsPaneOpen = false; MoveHighlight(); e.Handled = true; return; }
        if (e.Key is Key.Up or Key.Down && FindItem(e.Source) is { } current)
        {
            var items = MenuItems.Concat(FooterMenuItems).Where(i => i.IsVisible && i.IsEffectivelyEnabled).ToList();
            int index = Math.Clamp(items.IndexOf(current) + (e.Key == Key.Down ? 1 : -1), 0, items.Count - 1);
            items[index].Focus(NavigationMethod.Directional); e.Handled = true; return;
        }
        base.OnKeyDown(e);
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { _closeDelay.Stop(); _widthMotion.Stop(); _highlightMotion.Stop(); base.OnDetachedFromVisualTree(e); }
}
