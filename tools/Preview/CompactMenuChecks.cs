// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Froststrap;
using Froststrap.Enums;
using Froststrap.Models;
using Froststrap.Services;
using Froststrap.UI.Elements.Controls;
using Froststrap.UI.Elements.Dialogs;
using Froststrap.UI.ViewModels.Dialogs;

internal static partial class Program
{
    private static void VerifyCompactMenu()
    {
        var originalPins = App.State.Prop.QuickLaunchPins;
        bool originalSeed = App.State.Prop.HasSeededJjsPin;
        App.State.Prop.HasSeededJjsPin = true;
        App.Settings.Prop.DisableAnimations = true; App.ApplyAnimationSettings();
        foreach (var theme in new[] { Theme.Dark, Theme.Light })
        {
            App.State.Prop.QuickLaunchPins = [
                new QuickLaunchPin { PlaceId = 910001, Name = "Jujutsu Shenanigans" },
                new QuickLaunchPin { PlaceId = 910002, Name = "Garden adventure" },
                new QuickLaunchPin { PlaceId = 910003, Name = "A long pinned game title to check wrapping" },
                new QuickLaunchPin { PlaceId = 910004, Name = "Fourth pinned game" },
                new QuickLaunchPin { PlaceId = 910005, Name = "Fifth pinned game" }];
            App.Settings.Prop.Theme = theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
            var opened = new List<Uri>();
            var window = new LaunchMenuDialog(new GameLaunchService(opened.Add)); window.Show(); RunFor(100);
            var model = (LaunchMenuViewModel)window.DataContext!;
            var search = window.FindControl<TextBox>("PinnedSearch")!;
            var scroller = window.FindControl<ArtworkScrollViewer>("CompactPinnedScroller")!;
            var play = window.FindControl<Button>("PlayButton")!;
            Check(window.Bounds.Width == 390 && window.Bounds.Height == 520 && search.Bounds.Width >= 330 && search.Bounds.Height == 32 && search.VerticalContentAlignment == Avalonia.Layout.VerticalAlignment.Center,
                "compact menu has a long 32-high centered search field: " + theme);
            Check(scroller.TranslatePoint(default, window)!.Value.Y > search.TranslatePoint(default, window)!.Value.Y + search.Bounds.Height &&
                scroller.TranslatePoint(default, window)!.Value.Y > play.TranslatePoint(default, window)!.Value.Y + play.Bounds.Height &&
                scroller.TranslatePoint(default, window)!.Value.Y + scroller.Bounds.Height <= window.Bounds.Height - 16,
                "compact pinned strip is below Play, Settings and search at the bottom: " + theme);
            Check(scroller.ScrollBarMaximum.X > 0 && model.Pins.Games.Count == 5, "compact menu retains all pins in a horizontally scrollable strip: " + theme);
            Capture(window, theme, "compact-pinned-games");
            window.MouseWheel(At(scroller, window), new Vector(0, -2), RawInputModifiers.None); RunFor(50);
            Check(scroller.Offset.X > 0 && opened.Count == 0, "compact wheel scroll moves pins without launching a game: " + theme);
            Capture(window, theme, "compact-pins-scrolled");
            search.Focus(NavigationMethod.Tab); window.KeyTextInput("garden"); RunFor(80);
            Check(model.Pins.Games.Count == 1 && model.Pins.Games[0].PlaceId == 910002 && App.State.Prop.QuickLaunchPins.Count == 5,
                "typing in compact search filters shortcuts without changing saved pins: " + theme);
            Capture(window, theme, "compact-pins-filtered");
            search.SetCurrentValue(TextBox.TextProperty, "no matching shortcut"); RunFor(50);
            Check(model.Pins.IsEmpty && window.FindControl<TextBlock>("PinsEmptyMessage")!.IsEffectivelyVisible && !scroller.IsVisible,
                "compact search has an honest no-results state: " + theme);
            Capture(window, theme, "compact-pins-no-results");
            search.SetCurrentValue(TextBox.TextProperty, ""); RunFor(70);
            var pinToRemove = window.GetVisualDescendants().OfType<GameArtworkCard>().First(card => card.Game?.PlaceId == 910001);
            pinToRemove.BringIntoView(); RunFor(50);
            var star = pinToRemove.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().Single();
            Click(window, At(star, window));
            Check(model.Pins.Games.Count == 4 && !App.State.Prop.QuickLaunchPins.Any(pin => pin.PlaceId == 910001) && opened.Count == 0,
                "compact star unpins once without launching the tile: " + theme);
            GameLaunchService.SetPinned(new QuickPlayGameItem { PlaceId = 910006, Name = "Added from another surface" }, true);
            Check(model.Pins.Games.Any(game => game.PlaceId == 910006), "compact pins synchronize with the shared Home pin state: " + theme);
            foreach (double scale in new[] { 1.25, 1.5 })
            {
                window.SetRenderScaling(scale); RunFor(70); Capture(window, theme, "compact-pins-dpi-" + scale);
                Check(search.Bounds.Width >= 330 && search.Bounds.Height == 32 && scroller.Bounds.Width <= window.Bounds.Width - 40,
                    "compact search and pinned strip fit at scale " + scale + ": " + theme);
            }
            window.SetRenderScaling(1);
            var tile = window.GetVisualDescendants().OfType<GameArtworkCard>().First(card => card.Game?.PlaceId == 910002);
            tile.BringIntoView(); RunFor(50); Click(window, At(tile, window, .25, .65));
            Check(opened.Count == 1 && opened[0].AbsoluteUri == "roblox://experiences/start?placeId=910002" && window.CloseAction == NextAction.Terminate,
                "compact artwork invokes the shared CoffeeStrap launch service exactly once and closes the menu: " + theme);
            int retained = model.Pins.Games.Count;
            GameLaunchService.SetPinned(new QuickPlayGameItem { PlaceId = 910007, Name = "After close" }, true);
            Check(model.Pins.Games.Count == retained, "closed compact menu unsubscribes from pin changes: " + theme);
            model.PlayPinnedGameCommand.Execute(model.Pins.Games[0]);
            Check(opened.Count == 1, "closed compact menu cannot launch a stale shortcut: " + theme);

            App.State.Prop.QuickLaunchPins = [];
            var empty = new LaunchMenuDialog(new GameLaunchService(opened.Add)); empty.Show(); RunFor(80);
            var emptyModel = (LaunchMenuViewModel)empty.DataContext!;
            Check(emptyModel.Pins.IsEmpty && emptyModel.Pins.EmptyMessage.Contains("Pin games", StringComparison.Ordinal) && empty.FindControl<TextBox>("PinnedSearch")!.IsEffectivelyVisible,
                "compact menu keeps search and guidance visible with no saved pins: " + theme);
            Capture(empty, theme, "compact-pins-empty"); empty.Close();
        }
        App.State.Prop.QuickLaunchPins = originalPins; App.State.Prop.HasSeededJjsPin = originalSeed;
    }
}
