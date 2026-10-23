// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Froststrap;
using Froststrap.Enums;
using Froststrap.UI.Elements.Controls;
using Froststrap.UI.ViewModels.Settings;
using SettingsWindow = Froststrap.UI.Elements.Settings.MainWindow;

internal static partial class Program
{
    private static readonly List<object> BehavioralResults = [];
    private static void Check(bool passed, string name, object? evidence = null)
    {
        BehavioralResults.Add(new { name, passed, evidence });
        File.WriteAllText(Path.Combine(_output, "behavioral-checks.json"), JsonSerializer.Serialize(BehavioralResults,
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"CHECK {(passed ? "PASS" : "FAIL")}: {name}");
        if (!passed) throw new InvalidOperationException("Behavioral check failed: " + name);
    }
    private static void RunFor(int milliseconds)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        int advanced = 0;
        // Queued transitions must start before their clock advances. Advancing
        // once before pumping left newly started animations frozen in captures.
        do
        {
            Dispatcher.UIThread.RunJobs();
            int now = Math.Min(milliseconds, (int)elapsed.ElapsedMilliseconds);
            _manualTime?.Advance(now - advanced); advanced = now;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(4);
        } while (elapsed.ElapsedMilliseconds < milliseconds);
        _manualTime?.Advance(milliseconds - advanced);
        Pump();
    }
    private static Point At(Control control, Window window, double x = .5, double y = .5) =>
        control.TranslatePoint(new Point(control.Bounds.Width * x, control.Bounds.Height * y), window)!.Value;
    private static void Click(Window window, Point point)
    {
        window.MouseMove(point, RawInputModifiers.None);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Pump();
    }
    private static void WaitFor(Func<bool> condition, int timeout = 1800)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeout);
        do { RunFor(20); } while (!condition() && DateTime.UtcNow < deadline);
    }
    private static void VerifyModernInteractions()
    {
        var originalSettings = JsonSerializer.Serialize(App.Settings.Prop);
        App.Settings.Prop.DisableAnimations = false;
        App.RemoveAnimationSettings();
        App.Settings.Prop.Theme = Theme.Dark;
        Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
        var window = new SettingsWindow(false);
        window.Show();
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.NavigateToBehaviourCommand.Execute(null);
        RunFor(420);
        var card = window.GetVisualDescendants().OfType<OptionControl>()
            .First(c => c.Toggle != null && c.IsEffectivelyVisible && c.IsEffectivelyEnabled);
        var toggle = card.Toggle!;
        int changes = 0;
        toggle.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) changes++; };
        bool? start = toggle.IsChecked;
        var cardPoint = At(card, window, .45, .8);
        Click(window, cardPoint);
        Check(toggle.IsChecked != start && changes == 1, "card background toggles exactly once", new { changes });
        var switchTrack = toggle.GetVisualDescendants().OfType<Border>().First(b => b.Name == "OuterBorder");
        Click(window, At(switchTrack, window));
        Check(toggle.IsChecked == start && changes == 2, "switch click does not double-toggle the card", new { changes });
        Check(switchTrack.Bounds.Width == 48 && switchTrack.Bounds.Height == 28,
            "rounded switch uses the intended 48 by 28 track", new { switchTrack.Bounds });
        RunFor(220);
        var thumb = toggle.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().First(e => e.Name == "SwitchKnobOn");
        var thumbPoint = thumb.TranslatePoint(default, switchTrack)!.Value;
        Check(thumbPoint.X >= 0 && thumbPoint.X + thumb.Bounds.Width <= switchTrack.Bounds.Width,
            "switch thumb stays within its rounded track");
        for (int i = 0; i < 8; i++) Click(window, cardPoint);
        Check(toggle.IsChecked == start && changes == 10, "eight rapid card clicks preserve every input", new { changes });
        card.Focus(NavigationMethod.Tab);
        window.KeyPress(Key.Space, RawInputModifiers.None, default, null);
        for (int i = 0; i < 5; i++) window.KeyPress(Key.Space, RawInputModifiers.None, default, null);
        window.KeyRelease(Key.Space, RawInputModifiers.None, default, null);
        Check(changes == 11 && toggle.IsChecked != start, "held Space activates a focused card once", new { changes });
        window.KeyPress(Key.Enter, RawInputModifiers.None, default, null);
        window.KeyRelease(Key.Enter, RawInputModifiers.None, default, null);
        Check(changes == 12 && toggle.IsChecked == start, "Enter activates the focused card once");
        card.IsEnabled = false;
        Click(window, cardPoint);
        Check(changes == 12, "disabled cards cannot change their setting");
        card.IsEnabled = true;
        window.MouseDown(cardPoint, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(cardPoint + new Vector(0, 25), RawInputModifiers.None);
        window.MouseUp(cardPoint + new Vector(0, 25), MouseButton.Left, RawInputModifiers.None);
        Check(changes == 12, "dragging a card does not accidentally toggle it");
        window.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && b.IsEffectivelyEnabled).Focus();
        card.Focus(NavigationMethod.Tab);
        Check(card.IsFocused && card.Classes.Contains(":focus-visible"), "card keyboard focus is visible and remains operable");
        Capture(window, Theme.Dark, "modern-card-keyboard-focus");

        var panel = window.GetVisualDescendants().OfType<SettingsCardPanel>()
            .First(p => p.Children.Contains(card));
        var compact = panel.Children.OfType<OptionControl>().Where(c => c.IsVisible && c.IsCompactCard).ToArray();
        Check(compact.Length >= 2 && compact[0].Bounds.Y == compact[1].Bounds.Y && compact[1].Bounds.X > compact[0].Bounds.X,
            "settings cards flow side by side at the normal window width");
        foreach (var width in new[] { 750d, 800, 1125, 1600 })
        {
            window.Width = width; RunFor(80);
            var visible = panel.Children.Where(c => c.IsVisible).ToArray();
            bool contained = visible.All(c => c.Bounds.X >= -.5 && c.Bounds.Right <= panel.Bounds.Width + .5);
            bool noOverlap = visible.SelectMany((a, i) => visible.Skip(i + 1).Select(b => (a, b)))
                .All(pair => !pair.a.Bounds.Deflate(1).Intersects(pair.b.Bounds.Deflate(1)));
            if (!contained || !noOverlap) Capture(window, Theme.Dark, "layout-diagnostic");
            Check(contained && noOverlap, $"responsive layout remains bounded without overlapping cards at {width}px",
                new { panel.Bounds, contained, noOverlap, children = visible.Select(c => new { type = c.GetType().Name, c.Name, c.Bounds, c.DesiredSize }) });
        }
        window.Width = 1125;
        window.Width = 800; RunFor(90);
        Check(SettingsCardPanel.GetColumnLimit(panel) == 1 && panel.Children.OfType<OptionControl>().Where(c => c.IsVisible && c.IsCompactCard).All(c => c.Bounds.X < 1),
            "narrow layouts inherit a stable one-column limit from navigation");
        window.Width = 1125; RunFor(90);
        foreach (double scale in new[] { 1.25, 1.5 })
        {
            window.SetRenderScaling(scale); RunFor(80);
            Check(panel.Children.Where(c => c.IsVisible).All(c => c.Bounds.Right <= panel.Bounds.Width + .5),
                $"card grid fits at {scale * 100}% simulated DPI");
        }
        window.SetRenderScaling(1);

        vm.NavigateToIntegrationsCommand.Execute(null); RunFor(420);
        var linkCard = window.GetVisualDescendants().OfType<OptionControl>().First(c => !string.IsNullOrEmpty(c.HelpLink) && c.IsEffectivelyVisible && c.IsEffectivelyEnabled);
        var help = linkCard.GetVisualDescendants().OfType<Hyperlink>().First();
        help.BringIntoView(); RunFor(100);
        string? url = help.Url; help.Url = null; // Exercise input without opening an external browser.
        int linkClicks = 0; help.Click += (_, _) => linkClicks++;
        bool? beforeLink = linkCard.Toggle!.IsChecked;
        Click(window, At(help, window)); help.Url = url;
        Check(linkClicks == 1 && linkCard.Toggle.IsChecked == beforeLink, "help links activate independently of card toggling",
            new { linkClicks, beforeLink, after = linkCard.Toggle.IsChecked, point = At(help, window), hit = window.InputHitTest(At(help, window))?.GetType().Name, linkCard.Header });
        var enumCard = window.GetVisualDescendants().OfType<OptionControl>().First(c => c.Choice != null && c.IsEffectivelyEnabled);
        var combo = enumCard.Choice!;
        var selected = combo.SelectedItem;
        Click(window, At(enumCard, window, .45, .85));
        Check(Equals(combo.SelectedItem, selected) && !combo.IsDropDownOpen, "enum card background does not change selection or open the dropdown");
        Click(window, At(combo, window)); RunFor(40);
        Check(combo.IsDropDownOpen, "outlined dropdown opens through real pointer input");
        RunFor(170);
        CapturePopup(combo, Theme.Dark, "modern-enum-popup");
        window.KeyPress(Key.Escape, RawInputModifiers.None, default, null);
        window.KeyRelease(Key.Escape, RawInputModifiers.None, default, null); Pump();
        Check(!combo.IsDropDownOpen && Equals(combo.SelectedItem, selected), "Escape dismisses the dropdown without changing selection");
        combo.Focus(NavigationMethod.Tab);
        window.KeyPress(Key.Down, RawInputModifiers.Alt, default, null);
        window.KeyRelease(Key.Down, RawInputModifiers.Alt, default, null); Pump();
        Check(combo.IsDropDownOpen, "Alt+Down opens the enum dropdown from keyboard focus");
        window.KeyPress(Key.Down, RawInputModifiers.None, default, null);
        window.KeyRelease(Key.Down, RawInputModifiers.None, default, null);
        window.KeyPress(Key.Enter, RawInputModifiers.None, default, null);
        window.KeyRelease(Key.Enter, RawInputModifiers.None, default, null); Pump();
        Check(!combo.IsDropDownOpen && !Equals(combo.SelectedItem, selected) && combo.SelectedItem is TrayDoubleClickAction action && action == App.Settings.Prop.DoubleClickAction,
            "keyboard enum selection updates the existing settings binding");

        var scroller = window.GetVisualDescendants().OfType<SmoothScrollViewer>().First(s => s.ScrollBarMaximum.Y > 0);
        var scrollPoint = At(scroller, window, .5, .4);
        scroller.Offset = default;
        var offsetSamples = new List<double>();
        scroller.PropertyChanged += (_, e) => { if (e.Property == ScrollViewer.OffsetProperty) offsetSamples.Add(scroller.Offset.Y); };
        window.MouseWheel(scrollPoint, new Vector(0, -2), RawInputModifiers.None);
        double target = scroller.TargetOffset;
        RunFor(40);
        Check(offsetSamples.Any(offset => offset > 0 && offset < target),
            "smooth wheel motion has intermediate frames", new { offsetSamples, target });
        for (int i = 0; i < 8; i++) window.MouseWheel(scrollPoint, new Vector(0, -.25), RawInputModifiers.None);
        double accumulated = scroller.TargetOffset;
        window.MouseWheel(scrollPoint, new Vector(0, 1), RawInputModifiers.None);
        Check(scroller.TargetOffset < accumulated && scroller.TargetOffset >= 0,
            "fractional trackpad deltas accumulate and reverse without losing input", new { accumulated, reversed = scroller.TargetOffset });
        WaitFor(() => !scroller.HasActiveMotion);
        Check(!scroller.HasActiveMotion && Math.Abs(scroller.Offset.Y - scroller.TargetOffset) < .1,
            "scroll motion settles exactly at the current target", new { scroller.HasActiveMotion, scroller.Offset, scroller.TargetOffset, scroller.AnimationTicks });
        int ticks = scroller.AnimationTicks; RunFor(80);
        Check(scroller.AnimationTicks == ticks, "smooth scrolling performs no idle animation work");
        window.MouseWheel(scrollPoint, new Vector(0, -2), RawInputModifiers.None);
        scroller.Offset = new Vector(0, 10);
        RunFor(90);
        Check(!scroller.HasActiveMotion && Math.Abs(scroller.Offset.Y - 10) < .1,
            "external scrollbar or search offset interrupts pending scroll motion");
        scroller.Offset = default; RunFor(50);
        double beforeComboWheel = scroller.Offset.Y;
        window.MouseWheel(At(combo, window), new Vector(0, -1), RawInputModifiers.None); RunFor(220);
        Check(!scroller.HasActiveMotion && scroller.Offset.Y == beforeComboWheel,
            "wheel input over a dropdown does not scroll the settings page");
        App.Settings.Prop.DisableAnimations = true;
        window.MouseWheel(scrollPoint, new Vector(0, -1), RawInputModifiers.None); Pump();
        Check(!scroller.HasActiveMotion, "Disable Animations uses native immediate scrolling");
        App.Settings.Prop.DisableAnimations = false; scroller.Offset = default;

        var nav = window.FindControl<CoffeeNavigationView>("NavView")!;
        window.MouseMove(new Point(500, 110), RawInputModifiers.None); RunFor(240);
        nav.IsPaneOpen = false; Pump();
        var content = window.FindControl<TransitioningContentControl>("PageContentControl")!;
        double contentWidth = content.Bounds.Width;
        var railItem = nav.MenuItems.First(i => i.IsVisible && i.IsEffectivelyEnabled);
        var railPoint = railItem.TranslatePoint(new Point(22, 22), window)!.Value;
        window.MouseMove(railPoint, RawInputModifiers.None); RunFor(45);
        Check(nav.IsPaneOpen, "hovering the compact rail opens the navigation drawer");
        Check(content.Bounds.Width < contentWidth - 10, "inline navigation reflows the feature area while opening");
        for (int i = 0; i < 8; i++)
        {
            window.MouseMove(new Point(500, 110), RawInputModifiers.None); RunFor(20);
            window.MouseMove(railPoint, RawInputModifiers.None); RunFor(20);
        }
        RunFor(360);
        Check(nav.IsPaneOpen && !nav.HasActiveMotion, "rapid hover reversals settle without boundary jitter or idle work");
        Capture(window, Theme.Dark, "modern-sidebar-expanded");
        var navItems = nav.MenuItems.OfType<CoffeeNavigationItem>().Where(i => i.IsVisible && i.IsEnabled).ToArray();
        var nextNav = navItems[3];
        double oldHighlight = nav.HighlightTarget;
        window.MouseMove(At(nextNav, window), RawInputModifiers.None); RunFor(45);
        Check(nav.HighlightTarget != oldHighlight && nav.HoveredItem == nextNav, "navigation highlight moves between hovered tabs",
            new { oldHighlight, nav.HighlightTarget, expected = nextNav.Content, actual = nav.HoveredItem?.Content, point = At(nextNav, window), hit = window.InputHitTest(At(nextNav, window))?.GetType().Name });
        window.MouseMove(new Point(500, 110), RawInputModifiers.None); RunFor(400);
        Check(!nav.IsPaneOpen, "navigation collapses after leaving its stable hover region");
        nextNav.Focus(NavigationMethod.Tab); RunFor(80);
        Check(nav.IsPaneOpen, "keyboard focus opens navigation without mouse hover");
        window.KeyPress(Key.Escape, RawInputModifiers.None, default, null); window.KeyRelease(Key.Escape, RawInputModifiers.None, default, null); Pump();
        Check(!nav.IsPaneOpen, "Escape collapses keyboard navigation");
        var touchPoint = nextNav.TranslatePoint(new Point(24, nextNav.Bounds.Height / 2), window)!.Value;
        var touch = window.TouchBegin(touchPoint, RawInputModifiers.None);
        window.TouchEnd(touch, touchPoint, RawInputModifiers.None); RunFor(420);
        Check(vm.SelectedPage == (string)nextNav.Tag!, "touch can navigate from the compact rail",
            new { expected = nextNav.Tag, actual = vm.SelectedPage, touchPoint });

        App.Settings.Prop.ConfirmLaunches = !App.Settings.Prop.ConfirmLaunches;
        bool persistedBoolean = App.Settings.Prop.ConfirmLaunches;
        var persistedEnum = App.Settings.Prop.DoubleClickAction;
        App.Settings.Save();
        App.Settings.Prop.ConfirmLaunches = !persistedBoolean;
        App.Settings.Prop.DoubleClickAction = TrayDoubleClickAction.None;
        bool loaded = App.Settings.Load(false);
        Check(loaded && App.Settings.Prop.ConfirmLaunches == persistedBoolean && App.Settings.Prop.DoubleClickAction == persistedEnum,
            "production settings save and reload preserve boolean and enum changes inside fixture data",
            new { file = App.Settings.FileLocation });
        window.Hide();
        App.Settings.Prop = JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(originalSettings)!;
        App.Settings.Prop.DisableAnimations = false;
        App.RemoveAnimationSettings();
        Console.WriteLine($"Completed {BehavioralResults.Count} isolated input, animation, layout and persistence checks.");
    }
}
