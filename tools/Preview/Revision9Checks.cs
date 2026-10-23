// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
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
    private static void VerifyRevision9()
    {
        App.GlobalSettings.CreateTemplate(); App.GlobalSettings.Load(); App.PlayerState.Save();
        var native = new GlobalSettingsViewModel();
        native.LockMaximumRenderDistance = false; native.RenderDistance = 1; native.GraphicsQuality = 1;
        App.FastFlags.SetValue("FFlagDebugGraphicsPreferVulkan", "True");
        App.FastFlags.SetValue("FFlagDebugGraphicsPreferOpenGL", "True");
        App.FastFlags.SetValue("DFFlagDebugPauseVoxelizer", "True");
        native.TextureQuality = 0; native.TextureCompatibilityCommand.Execute(null);
        Check(native.RenderDistance == 1 && native.GraphicsQuality == 1 && !native.LockMaximumRenderDistance,
            "texture compatibility preserves explicitly selected low graphics rather than hiding the issue with FRM 21");
        Check(App.FastFlags.GetValue("FFlagDebugGraphicsPreferD3D11") == "True" &&
            App.FastFlags.GetValue("FFlagDebugGraphicsPreferVulkan") == null && App.FastFlags.GetValue("FFlagDebugGraphicsPreferOpenGL") == null &&
            App.FastFlags.GetValue("DFFlagDebugPauseVoxelizer") == null && native.TextureQuality == 3,
            "texture workaround selects only Direct3D 11, full texture detail and normal lighting updates");
        App.FastFlags.Save(); App.FastFlags.Load(false);
        string profile = Path.Combine(Paths.Base, "revision9-renderer-profile.json");
        File.WriteAllText(profile, "{\"FFlagDebugGraphicsPreferVulkan\":\"True\",\"FFlagDebugGraphicsPreferOpenGL\":\"True\",\"DFIntTextureQualityOverride\":\"0\",\"DFIntDebugFRMQualityLevelOverride\":\"21\",\"DFFlagDebugPauseVoxelizer\":\"True\"}");
        CuratedFlagRegistry.SanitizeFile(profile);
        var flags = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(profile))!;
        Check(flags["FFlagDebugGraphicsPreferD3D11"] == "True" && !flags.ContainsKey("FFlagDebugGraphicsPreferVulkan") &&
            !flags.ContainsKey("FFlagDebugGraphicsPreferOpenGL") && !flags.ContainsKey("DFFlagDebugPauseVoxelizer") && flags["DFIntTextureQualityOverride"] == "3" && flags["DFIntDebugFRMQualityLevelOverride"] == "1",
            "conflicting per-place renderer and texture flags cannot undo the selected compatibility settings");
        native.LockMaximumRenderDistance = true; native.TextureCompatibilityCommand.Execute(null);
        Check(native.RenderDistance == 1 && native.GraphicsQuality == 10, "texture workaround preserves minimum FRM with the maximum saved distance setting");
        var visuals = new VisualTweaksViewModel(); visuals.Reset();
        visuals.SmoothPlastic = true; visuals.SmoothWater = true;
        var materials = BuiltInVisualMods.Files().ToArray();
        foreach (var asset in materials.Where(asset => asset.Relative.Contains("plastic", StringComparison.Ordinal)))
        {
            bool detail = asset.Relative.EndsWith("normaldetail.dds", StringComparison.Ordinal);
            using var reader = new BinaryReader(File.OpenRead(asset.Source));
            reader.BaseStream.Position = 12; int height = reader.ReadInt32(), width = reader.ReadInt32();
            reader.BaseStream.Position = 28; int mips = reader.ReadInt32();
            Check(height == (detail ? 512 : 2048) && width == (detail ? 512 : 128) && mips == (detail ? 10 : 12),
                "plastic replacement preserves the original atlas shape and complete mip count: " + Path.GetFileName(asset.Relative));
            reader.BaseStream.Position = reader.BaseStream.Length - 4;
            Check(reader.ReadBytes(4).Any(value => value != 0), "lowest plastic mip retains a valid material value: " + Path.GetFileName(asset.Relative));
        }
        using (var reader = new BinaryReader(File.OpenRead(materials.First(asset => asset.Relative.Contains("water", StringComparison.Ordinal)).Source)))
        {
            reader.BaseStream.Position = 12;
            Check(reader.ReadInt32() == 256 && reader.ReadInt32() == 256 && reader.BaseStream.Length == 349652,
                "water normal maps preserve original dimensions and all nine mip levels");
        }
        visuals.Reset();
        visuals.FireStyle = "Icy blue"; visuals.SmokeStyle = "Charcoal"; visuals.SparklesStyle = "Warm gold"; visuals.ForceFieldStyle = "Violet";
        var persisted = JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(File.ReadAllText(App.Settings.FileLocation))!;
        Check(persisted.FireStyle == "Icy blue" && persisted.SmokeStyle == "Charcoal" && persisted.SparklesStyle == "Warm gold" && persisted.ForceFieldStyle == "Violet",
            "all four built-in effect styles persist immediately for the next launch");
        var colored = BuiltInVisualMods.Files().ToArray();
        Check(colored.Length == 6 && colored.All(asset => asset.Relative.Contains(Path.Combine("textures", "particles"), StringComparison.Ordinal)) &&
            colored.All(asset => ModFilePolicy.IsAllowed(asset.Relative)), "effect color styles generate six real particle texture replacements");
        foreach (var asset in colored)
        {
            using var reader = new BinaryReader(File.OpenRead(asset.Source)); reader.BaseStream.Position = 12;
            Check(reader.ReadInt32() == 32 && reader.ReadInt32() == 128, "effect color lookup retains its original 128x32 dimensions: " + Path.GetFileName(asset.Relative));
        }
        string player = Path.Combine(Paths.Base, "revision9-player");
        foreach (var asset in colored)
        {
            string path = Path.Combine(player, asset.Relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "original particle");
        }
        App.State.Prop.Mods.Clear(); App.PlayerState.Prop.ModManifest.Clear(); App.PlayerState.Save();
        using (var bootstrapper = new Bootstrapper(LaunchMode.Player))
        {
            typeof(Bootstrapper).GetField("_latestVersionDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, player);
            typeof(Bootstrapper).GetField("PackageDirectoryMap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, new Dictionary<string, string>());
            var apply = typeof(Bootstrapper).GetMethod("ApplyModifications", BindingFlags.Instance | BindingFlags.NonPublic)!;
            void Apply() => Await((Task<bool>)apply.Invoke(bootstrapper, null)!);
            Apply();
            Check(colored.All(asset => File.ReadAllBytes(Path.Combine(player, asset.Relative)).SequenceEqual(File.ReadAllBytes(asset.Source))),
                "production launcher installs the selected effect colors through its real mod pipeline");
            string mod = Path.Combine(Paths.Modifications, "revision9-priority");
            var first = colored[0]; string replacement = Path.Combine(mod, first.Relative); Directory.CreateDirectory(Path.GetDirectoryName(replacement)!);
            File.Copy(colored[^1].Source, replacement);
            App.State.Prop.Mods = [new ModConfig { FolderName = "revision9-priority", Enabled = true, Priority = 1 }];
            Apply();
            Check(File.ReadAllBytes(Path.Combine(player, first.Relative)).SequenceEqual(File.ReadAllBytes(replacement)), "an installed effect mod retains priority over built-in styling");
            App.State.Prop.Mods[0].Enabled = false; visuals.Reset(); Apply();
            Check(colored.All(asset => File.ReadAllText(Path.Combine(player, asset.Relative)) == "original particle"),
                "disabling built-in styles and their overriding mod restores every original particle file");
        }
        visuals.FireStyle = "Hidden"; visuals.SmokeStyle = "Hidden"; visuals.SparklesStyle = "Hidden"; visuals.ForceFieldStyle = "Hidden";
        var hidden = BuiltInVisualMods.Files().ToArray();
        Check(hidden.Length == 7 && hidden.All(asset => File.ReadAllBytes(asset.Source).Skip(128).All(value => value == 0)),
            "hidden effect styles provide transparent textures at every mip without deleting required files");
        visuals.FireStyle = "invalid";
        Check(visuals.FireStyle == "Hidden", "invalid effect selections cannot write a disconnected setting");
        visuals.Reset();
        Check(!BuiltInVisualMods.Files().Any(), "restore visuals clears the new styles from the next launch plan");
        RenderRevision9();
        native.LockMaximumRenderDistance = false; App.FastFlags.Prop.Clear(); App.FastFlags.Save(); App.State.Prop.Mods.Clear();
    }
    private static void RenderRevision9()
    {
        foreach (var theme in new[] { Theme.Dark, Theme.Light })
        {
            App.Settings.Prop.Theme = theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
            foreach (int width in new[] { 1100, 760 })
            {
                var window = new SettingsWindow(false) { Width = width, Height = 900 }; window.Show(); RunFor(80);
                ((MainWindowViewModel)window.DataContext!).NavigateRegistered("fastflags"); RunFor(80);
                var groups = window.GetVisualDescendants().OfType<CardExpander>().ToArray();
                foreach (string heading in new[] { "Roblox graphics", "Built-in effect styles", "Hardware & window title" })
                {
                    foreach (var group in groups) group.IsExpanded = Equals(group.Header, heading);
                    var selected = groups.Single(group => Equals(group.Header, heading)); RunFor(80);
                    var scroll = selected.GetVisualAncestors().OfType<ScrollViewer>().First();
                    var point = selected.TranslatePoint(default, scroll);
                    if (point.HasValue) scroll.Offset = new Vector(0, Math.Max(0, scroll.Offset.Y + point.Value.Y - 16));
                    RunFor(70); Capture(window, theme, "revision9-" + heading.Replace(' ', '-') + "-" + width);
                    if (heading == "Roblox graphics")
                    {
                        selected.GetVisualDescendants().OfType<OptionControl>().Single(control => control.Header == "Black-texture compatibility").BringIntoView();
                        RunFor(70); Capture(window, theme, "revision9-texture-compatibility-" + width);
                    }
                    if (heading == "Built-in effect styles")
                        Check(selected.GetVisualDescendants().OfType<ComboBox>().Count() == 5, "rendered effect controls bind all five styles: " + theme + " " + width);
                    if (heading == "Hardware & window title")
                        Check(!selected.GetVisualDescendants().OfType<OptionControl>().Any(control => control.Header?.Contains("FPS", StringComparison.Ordinal) == true),
                            "FPS controls are absent from the rendered game tweaks: " + theme + " " + width);
                }
                window.Close();
            }
        }
    }
}
