// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Froststrap;
using Froststrap.Enums;
using Froststrap.Models;
using Froststrap.Services;
using Froststrap.UI.Elements.Controls;
using Froststrap.UI.ViewModels.Settings;
using SettingsWindow = Froststrap.UI.Elements.Settings.MainWindow;

internal static partial class Program
{
    private static void VerifyRevision25()
    {
        const string key = "FFlagHandleAltEnterFullscreenManually";
        string previousProfiles = Paths.SavedFlagProfiles, settings = JsonSerializer.Serialize(App.Settings.Prop);
        var flags = new Dictionary<string, object>(App.FastFlags.Prop);
        var mods = App.State.Prop.Mods; var manifest = App.PlayerState.Prop.ModManifest.ToList();
        string root = Path.Combine(Paths.DataRoot, "fullscreen-request"); Directory.CreateDirectory(root);
        typeof(Paths).GetProperty(nameof(Paths.SavedFlagProfiles))!.SetValue(null, Path.Combine(root, "profiles")); Directory.CreateDirectory(Paths.SavedFlagProfiles);
        try
        {
            App.GlobalSettings.CreateTemplate(); App.GlobalSettings.Load();
            var native = new GlobalSettingsViewModel { LockMaximumRenderDistance = true, GraphicsQuality = 10, RenderDistance = 1 };
            App.GlobalSettings.Save();
            Check(SavedSlider() == "10", "fullscreen regression starts from the saved native graphics-10 pairing");
            App.FastFlags.Prop.Clear(); App.FastFlags.SetValue("DFIntDebugFRMQualityLevelOverride", "1"); App.FastFlags.SetValue("FFlagDebugGraphicsPreferVulkan", "True");
            App.FastFlags.Save(); App.Settings.Prop.ProfilePlaceIds = []; App.Settings.Save();
            string xml = File.ReadAllText(GBSEditor.FileLocation);
            using var vm = new CuratedFastFlagsViewModel();
            var fullscreen = vm.Settings.Single(setting => setting.Title == "Alt+Enter fullscreen");
            Check(fullscreen.Selected == fullscreen.Choices[0] && CuratedFlagRegistry.Allowed.Contains(key), "fullscreen starts at Roblox default and uses an already allowlisted flag");
            fullscreen.Selected = fullscreen.Choices[1];
            Check(App.FastFlags.GetValue(key) == "False" && App.FastFlags.GetValue("FFlagDebugGraphicsPreferVulkan") == "True", "native Alt+Enter writes False without silently changing the selected renderer");
            fullscreen.Selected = new FlagChoice("Foreign", new Dictionary<string, string> { [key] = "True" });
            Check(fullscreen.Selected == fullscreen.Choices[1] && App.FastFlags.GetValue(key) == "False", "a foreign fullscreen choice cannot inject a different setting");
            Check(App.FastFlags.Save(false) && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(App.FastFlags.FileLocation))![key] == "False", "native fullscreen selection persists through the production filtered save");
            using (var reopened = new CuratedFastFlagsViewModel())
                Check(reopened.Settings.Single(setting => setting.Title == fullscreen.Title).Selected!.Label == "Native fullscreen", "the selected fullscreen request survives reopening Game Tweaks");
            string before = File.ReadAllText(App.FastFlags.FileLocation); fullscreen.Selected = fullscreen.Choices[2];
            using (var locked = new FileStream(App.FastFlags.FileLocation, FileMode.Open, FileAccess.Read, FileShare.Read))
                Check(!App.FastFlags.Save(false) && File.ReadAllText(App.FastFlags.FileLocation) == before && App.FastFlags.Changed, "a locked flag file preserves the prior fullscreen request and pending change");
            Check(App.FastFlags.Save(false) && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(App.FastFlags.FileLocation))![key] == "True", "fullscreen save retries after the lock and writes Roblox handling");
            var normal = CuratedFlagRegistry.Filter(new Dictionary<string, object> { [key] = false }, out var held);
            Check(normal[key].ToString() == "False" && held.Count == 0 && !CuratedFlagRegistry.TryNormalize(key, "invalid", out _), "fullscreen values use existing Boolean normalization and reject invalid data");
            string snapshot = "Per experience fullscreen.json";
            FlagProfileFiles.SaveNew(snapshot, new Dictionary<string, object> { [key] = "False", ["DFIntCSGLevelOfDetailSwitchingDistance"] = "777" });
            App.Settings.Prop.ProfilePlaceIds = new() { [snapshot] = ["818181"] }; App.Settings.Save();
            App.State.Prop.Mods = []; App.PlayerState.Prop.ModManifest.Clear(); App.PlayerState.Save();
            string player = Path.Combine(root, "player"); Directory.CreateDirectory(player);
            string output = Path.Combine(player, "ClientSettings", "ClientAppSettings.json");
            Dictionary<string, string> Output() => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(output))!;
            bool Prepare(long place)
            {
                using var launcher = new Bootstrapper(LaunchMode.Player);
                typeof(Bootstrapper).GetField("_latestVersionDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(launcher, player);
                typeof(Bootstrapper).GetField("PackageDirectoryMap", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(launcher, new Dictionary<string, string>());
                typeof(Bootstrapper).GetField("_joinData", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(launcher, new GameJoinData { PlaceId = place });
                return AwaitResult((Task<bool>)typeof(Bootstrapper).GetMethod("ApplyModifications", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(launcher, null)!);
            }
            Check(Prepare(818181) && Output()[key] == "True" && Output()["DFIntCSGLevelOfDetailSwitchingDistance"] == "777", "actual launch preparation keeps explicit global fullscreen handling over a conflicting assigned profile");
            fullscreen.Selected = fullscreen.Choices[0]; App.FastFlags.Save();
            Check(App.FastFlags.GetValue(key) == null && Prepare(818181) && Output()[key] == "False", "Roblox default removes the global override and permits an assigned per-experience fullscreen request");
            Check(Prepare(818182) && !Output().ContainsKey(key), "a different experience clears the previous profile's fullscreen request");
            fullscreen.Selected = fullscreen.Choices[1]; App.FastFlags.Save();
            Check(Prepare(818182) && Output()[key] == "False", "the actual global launch output contains the selected native fullscreen request");
            fullscreen.Selected = fullscreen.Choices[0]; App.FastFlags.Save();
            Check(Prepare(818182) && !Output().ContainsKey(key) && Output()["DFIntDebugFRMQualityLevelOverride"] == "1" && File.ReadAllText(GBSEditor.FileLocation) == xml, "returning to default removes the launch override without changing saved graphics 10 or FRM 1");
            fullscreen.Selected = fullscreen.Choices[1]; vm.ResetVisualsCommand.Execute(null);
            Check(App.FastFlags.GetValue(key) == null && fullscreen.Selected == fullscreen.Choices[0], "Restore Roblox visuals clears and refreshes the fullscreen choice");
            foreach (var theme in new[] { Theme.Dark, Theme.Light }) foreach (int width in new[] { 760, 1100 }) RenderFullscreenRequest(theme, width);
            File.WriteAllText(Path.Combine(_output, "fullscreen-request-probe.json"), JsonSerializer.Serialize(new { mechanism = key, savedOnly = true, nativeModeEntered = false, gameLaunched = false, gamePerformanceMeasured = false, memoryAccess = false }));
        }
        finally
        {
            typeof(Paths).GetProperty(nameof(Paths.SavedFlagProfiles))!.SetValue(null, previousProfiles);
            App.Settings.Prop = JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(settings)!; App.Settings.Save();
            App.FastFlags.Prop = flags; App.FastFlags.Save(); App.State.Prop.Mods = mods; App.PlayerState.Prop.ModManifest = manifest;
        }
    }
    private static void RenderFullscreenRequest(Theme theme, int width)
    {
        App.Settings.Prop.Theme = theme; App.Settings.Prop.DisableAnimations = false; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme(); App.ApplyAnimationSettings();
        var window = new SettingsWindow(false) { Width = width, Height = 900 }; window.Show(); RunFor(60);
        var owner = (MainWindowViewModel)window.DataContext!; owner.NavigateRegistered("fastflags"); RunFor(350);
        var page = (Control)window.FindControl<TransitioningContentControl>("PageContentControl")!.Content!;
        var card = page.GetVisualDescendants().OfType<OptionControl>().Single(item => Equals(item.Header, "Alt+Enter fullscreen"));
        var scroll = card.GetVisualAncestors().OfType<ScrollViewer>().First(); var point = card.TranslatePoint(default, scroll);
        if (point.HasValue) scroll.Offset = new Vector(0, scroll.Offset.Y + point.Value.Y - 16);
        RunFor(60);
        var choice = card.GetVisualDescendants().OfType<ComboBox>().Single(); choice.SelectedIndex = 0;
        Check(choice.Bounds.Width > 0 && choice.Bounds.Width <= card.Bounds.Width && card.Bounds.Width <= window.Width, "fullscreen choice fits the rendered settings grid: " + theme + width);
        choice.Focus(); window.KeyPress(Key.Down, RawInputModifiers.None, default, null); window.KeyRelease(Key.Down, RawInputModifiers.None, default, null); RunFor(40);
        Check(App.FastFlags.GetValue("FFlagHandleAltEnterFullscreenManually") == "False", "keyboard selection drives the production fullscreen binding: " + theme + width);
        Capture(window, theme, "fullscreen-request-" + width);
        if (width == 760) { window.SetRenderScaling(1.5); RunFor(60); Capture(window, theme, "fullscreen-request-scaled"); window.SetRenderScaling(1); }
        App.Settings.Prop.DisableAnimations = true; App.ApplyAnimationSettings(); owner.NavigateRegistered("behaviour"); RunFor(50);
        Check(TopLevel.GetTopLevel(card) == null, "fullscreen control detaches cleanly during reduced-motion navigation: " + theme + width);
        window.Close();
    }
}
