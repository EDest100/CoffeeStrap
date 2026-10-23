// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Froststrap;
using Froststrap.Enums;
using Froststrap.Services;
using Froststrap.UI.Elements.Controls;
using Froststrap.UI.ViewModels.Settings;
using SettingsWindow = Froststrap.UI.Elements.Settings.MainWindow;

internal static partial class Program
{
    private static void VerifyRevision10()
    {
        App.Settings.Prop.LockMaximumRenderDistance = false;
        App.GlobalSettings.CreateTemplate(); App.GlobalSettings.Load(); App.PlayerState.Save();
        App.GlobalSettings.SetPreset("Audio.MasterVolume", "0.37");
        App.FastFlags.Prop.Clear();
        App.FastFlags.SetValue("FFlagDebugGraphicsPreferVulkan", "True");
        App.FastFlags.SetValue("DFIntTextureQualityOverride", "2");
        App.FastFlags.SetValue("DFFlagTextureQualityOverrideEnabled", "True");
        App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistance", "500");
        App.FastFlags.SetValue("DFIntDebugFRMQualityLevelOverride", "21");
        bool plastic = App.Settings.Prop.SmoothPlastic;
        var native = new GlobalSettingsViewModel(); native.GraphicsQuality = 1;
        native.MaximumDistanceLowEffectsCommand.Execute(null);
        Check(native.LockMaximumRenderDistance && native.GraphicsQuality == 10 && native.RenderDistance == 1 && native.RenderDistanceOverride,
            "maximum-distance preset pairs saved Roblox slider 10 with allowlisted FRM 1");
        Check(App.FastFlags.GetValue("FFlagDebugGraphicsPreferVulkan") == "True" && native.TextureQuality == 2 &&
            App.FastFlags.GetValue("DFIntCSGLevelOfDetailSwitchingDistance") == "500" && App.Settings.Prop.SmoothPlastic == plastic,
            "distance/quality preset preserves renderer, texture, mesh and installed visual preferences");
        App.FastFlags.Save(); App.GlobalSettings.Save(); App.FastFlags.Load(false); App.GlobalSettings.Load();
        Check(native.GraphicsQuality == 10 && native.RenderDistance == 1 && !App.FastFlags.Changed,
            "FRM 1 and maximum saved slider survive save/reload without the obsolete FRM 21 policy");
        Check(App.GlobalSettings.GetPreset("Audio.MasterVolume") == "0.37", "graphics pairing preserves unrelated saved audio settings");
        native.RenderDistance = 4; App.FastFlags.Save();
        Check(App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride") == "4" && native.GraphicsQuality == 10,
            "FRM remains independently adjustable while maximum saved distance stays selected");
        native.RenderDistance = 1; native.GraphicsQuality = 2;
        Check(native.GraphicsQuality == 10 && native.RenderDistance == 1, "saved slider edits cannot lower the enabled distance preference or increase FRM");
        App.GlobalSettings.SetPreset("Rendering.SavedQualityLevel", "3"); App.GlobalSettings.Save();
        Check(SavedSlider() == "10", "GBS save re-applies the maximum slider after an editor or import changes it");
        string profile = Path.Combine(Paths.Base, "revision10-profile.json");
        File.WriteAllText(profile, "{\"DFIntDebugFRMQualityLevelOverride\":\"21\"}");
        CuratedFlagRegistry.SanitizeFile(profile);
        Check(JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(profile))!["DFIntDebugFRMQualityLevelOverride"] == "1",
            "a per-place FRM 21 profile cannot replace the selected lowest effects level");
        App.FastFlags.Save();
        var changedOnDisk = XDocument.Load(GBSEditor.FileLocation);
        changedOnDisk.Descendants("token").Single(element => (string?)element.Attribute("name") == "SavedQualityLevel").Value = "2";
        changedOnDisk.Descendants("float").Single(element => (string?)element.Attribute("name") == "MasterVolume").Value = "0.25";
        changedOnDisk.Save(GBSEditor.FileLocation);
        App.GlobalSettings.SetPreset("Audio.MasterVolume", "0.42");
        string player = Path.Combine(Paths.Base, "revision10-player"); Directory.CreateDirectory(player);
        App.State.Prop.Mods.Clear(); App.PlayerState.Prop.ModManifest.Clear(); App.PlayerState.Save();
        using (var bootstrapper = new Bootstrapper(LaunchMode.Player))
        {
            typeof(Bootstrapper).GetField("_latestVersionDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, player);
            typeof(Bootstrapper).GetField("PackageDirectoryMap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, new Dictionary<string, string>());
            var apply = typeof(Bootstrapper).GetMethod("ApplyModifications", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Check(AwaitResult((Task<bool>)apply.Invoke(bootstrapper, null)!), "production launcher successfully prepares the paired graphics settings");
        }
        string clientFlags = Path.Combine(player, "ClientSettings", "ClientAppSettings.json");
        Check(SavedSlider() == "10" && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(clientFlags))!["DFIntDebugFRMQualityLevelOverride"] == "1",
            "production launch output restores a lowered on-disk slider while keeping the actual client FRM flag at 1");
        Check((string?)XDocument.Load(GBSEditor.FileLocation).Descendants("float").Single(element => (string?)element.Attribute("name") == "MasterVolume") == "0.25" &&
            App.GlobalSettings.GetPreset("Audio.MasterVolume") == "0.42", "launch-time distance update preserves both fresh on-disk preferences and pending menu edits");
        native.LockMaximumRenderDistance = false; native.RenderDistance = 6; native.GraphicsQuality = 3;
        App.FastFlags.Save(); App.GlobalSettings.Save();
        Check(native.RenderDistance == 6 && SavedSlider() == "3", "disabling the distance preference restores manual slider control and preserves selected FRM");
        native.LockMaximumRenderDistance = true; native.RenderDistanceOverride = false; App.FastFlags.Save();
        Check(!native.LockMaximumRenderDistance && App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride") == null,
            "disabling the FRM override also leaves the paired mode and does not silently re-enable a flag");
        App.Settings.Prop.LockMaximumRenderDistance = true; App.FastFlags.Prop.Clear(); App.FastFlags.Save();
        Check(App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride") == "1", "an enabled distance preference defaults a missing FRM override to 1");
        App.GlobalSettings.SetReadOnly(true);
        try
        {
            App.GlobalSettings.SetPreset("Rendering.SavedQualityLevel", "2"); App.GlobalSettings.Save();
            Check(SavedSlider() == "10" && GBSEditor.GetReadOnly(), "paired saved settings respect the existing read-only-file lifecycle");
        }
        finally { App.GlobalSettings.SetReadOnly(false); }
        RenderRevision10();
        native.LockMaximumRenderDistance = false; App.FastFlags.Prop.Clear(); App.FastFlags.Save();
    }
    private static string? SavedSlider() => (string?)XDocument.Load(GBSEditor.FileLocation).Descendants("token")
        .Single(element => (string?)element.Attribute("name") == "SavedQualityLevel");
    private static void RenderRevision10()
    {
        foreach (var theme in new[] { Theme.Dark, Theme.Light })
        {
            App.Settings.Prop.Theme = theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
            foreach (int width in new[] { 1100, 800 })
            {
                var window = new SettingsWindow(false) { Width = width, Height = 900 }; window.Show(); RunFor(80);
                ((MainWindowViewModel)window.DataContext!).NavigateRegistered("fastflags"); RunFor(80);
                var groups = window.GetVisualDescendants().OfType<CardExpander>().ToArray();
                foreach (var group in groups) group.IsExpanded = Equals(group.Header, "Roblox graphics");
                var graphics = groups.Single(group => Equals(group.Header, "Roblox graphics"));
                var button = graphics.GetVisualDescendants().OfType<Button>().Single(control => Equals(control.Content, "Maximum distance + lowest effects"));
                button.Command!.Execute(button.CommandParameter); RunFor(80);
                var options = graphics.GetVisualDescendants().OfType<OptionControl>().ToArray();
                var saved = options.Single(option => option.Header == "In-game graphics level").GetVisualDescendants().OfType<Slider>().Single();
                var frm = options.Single(option => option.Header == "Override Graphics Quality level").GetVisualDescendants().OfType<Slider>().Single();
                Check(saved.Value == 10 && !saved.IsEffectivelyEnabled && frm.Value == 1 && frm.IsEffectivelyEnabled,
                    "actual bound preset shows saved 10 and editable FRM 1: " + theme + " " + width);
                frm.Value = 3; RunFor(40);
                Check(App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride") == "3" && saved.Value == 10,
                    "bound FRM slider edits rendering quality without moving the maximum saved slider: " + theme + " " + width);
                button.Command.Execute(button.CommandParameter); RunFor(40);
                options.Single(option => option.Header == "Maximum draw-distance setting").BringIntoView();
                RunFor(70); Capture(window, theme, "revision10-max-distance-low-effects-" + width);
                button.BringIntoView(); RunFor(70); Capture(window, theme, "revision10-paired-preset-" + width);
                window.Close();
            }
        }
    }
}
