// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Reflection;
using System.Text;
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
using SkiaSharp;
using SettingsWindow = Froststrap.UI.Elements.Settings.MainWindow;

internal static partial class Program
{
    private static void VerifyRevision8()
    {
        App.GlobalSettings.CreateTemplate(); App.GlobalSettings.Load(); App.PlayerState.Save();
        Check(new Froststrap.Models.Persistable.Settings().LockMaximumRenderDistance, "maximum saved distance preference is enabled for configurations missing the option");
        App.Settings.Prop.LockMaximumRenderDistance = true;
        Directory.CreateDirectory(Path.GetDirectoryName(App.FastFlags.FileLocation)!);
        File.WriteAllText(App.FastFlags.FileLocation, "{\"DFIntDebugFRMQualityLevelOverride\":\"1\"}");
        App.FastFlags.Load(false);
        Check(App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride") == "1", "loading a low-effects profile preserves FRM 1 while maximum distance is selected");
        Check(!App.FastFlags.Changed, "loading the low-effects profile does not create a false unsaved-change prompt");
        App.FastFlags.SetValue("DFIntDebugFRMQualityLevelOverride", "6"); App.FastFlags.Save();
        Check(JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(App.FastFlags.FileLocation))!["DFIntDebugFRMQualityLevelOverride"] == "6", "saving maximum-distance mode preserves a separately selected FRM level");
        var native = new GlobalSettingsViewModel(); native.GraphicsQuality = 1; native.RenderDistance = 1;
        Check(native.RenderDistance == 1 && native.GraphicsQuality == 10 && App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride") == "1", "maximum-distance mode pins the saved slider to 10 without increasing FRM quality");
        string profile = Path.Combine(Paths.Base, "revision8-profile.json");
        File.WriteAllText(profile, "{\"DFIntDebugFRMQualityLevelOverride\":\"1\"}"); App.FastFlags.Prop.Clear();
        CuratedFlagRegistry.SanitizeFile(profile);
        Check(JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(profile))!["DFIntDebugFRMQualityLevelOverride"] == "1", "maximum-distance profiles use FRM 1 as their fallback when the global flag dictionary is empty");
        native.PlasticMaximumCommand.Execute(null);
        Check(native.LockMaximumRenderDistance && native.GraphicsQuality == 10 && native.RenderDistance == 1 && native.TextureQuality == 3 && App.Settings.Prop.SmoothPlastic, "plastic preset requests maximum saved distance with minimum FRM and material assets");
        Check(BuiltInVisualMods.Files().Any(asset => asset.Relative == Path.Combine("PlatformContent", "pc", "textures", "plastic", "diffuse.dds")), "plastic texture flattening now includes the diffuse color map");
        new VisualTweaksViewModel().Reset();
        VerifyRevision8Sky();
        RenderRevision8();
        native.LockMaximumRenderDistance = false; App.FastFlags.Prop.Clear(); App.FastFlags.Save(); App.State.Prop.Mods.Clear();
    }
    private static void VerifyRevision8Sky()
    {
        string source = Path.Combine(Paths.Modifications, "revision8-sky", "PlatformContent", "pc", "textures", "sky");
        Directory.CreateDirectory(source);
        void Png(string path, SKColor color)
        {
            using var bitmap = new SKBitmap(8, 8); bitmap.Erase(color);
            using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(path); data.SaveTo(stream);
        }
        Png(Path.Combine(source, "sky512_bk.tex"), new SKColor(220, 60, 140));
        string prepared = ModAssetPreparation.Files(Path.Combine(Paths.Modifications, "revision8-sky")).Single().Source;
        using (var reader = new BinaryReader(File.OpenRead(prepared)))
        {
            reader.BaseStream.Position = 28;
            Check(reader.ReadInt32() == 4 && reader.BaseStream.Length == 468, "normalized 8x8 sky textures contain every mip down to 1x1 with a correct DDS size");
            reader.BaseStream.Position = 108;
            Check((reader.ReadUInt32() & 0x400008) == 0x400008, "DDS header declares both mipmap and complex surface capabilities");
            reader.BaseStream.Position = reader.BaseStream.Length - 4;
            Check(reader.ReadBytes(4).SequenceEqual(new byte[] { 220, 60, 140, 255 }), "the smallest sky mip preserves opaque source color instead of disappearing at a reduced level");
        }
        string odd = Path.Combine(Paths.Cache, "PreparedTextures", "revision8-odd.dds");
        ModAssetPreparation.WriteDds(odd, 3, 5, (_, _) => new SKColor(70, 90, 110));
        Check(new FileInfo(odd).Length == 128 + (15 + 2 + 1) * 4, "non-power-of-two textures have complete correctly sized mip chains");
        string player = Path.Combine(Paths.Base, "revision8-player"), relative = Path.Combine("PlatformContent", "pc", "textures", "sky", "sky512_bk.tex");
        string indoor = ModAssetPreparation.IndoorSkyAlias(relative)!;
        foreach (string file in new[] { relative, indoor }) { string target = Path.Combine(player, file); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllText(target, "original " + Path.GetFileName(file)); }
        App.Settings.Prop.ReplaceIndoorSky = true; App.State.Prop.Mods = [new ModConfig { FolderName = "revision8-sky", Enabled = true, Priority = 9 }];
        App.PlayerState.Prop.ModManifest.Clear(); App.PlayerState.Save();
        using var bootstrapper = new Bootstrapper(LaunchMode.Player);
        typeof(Bootstrapper).GetField("_latestVersionDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, player);
        typeof(Bootstrapper).GetField("PackageDirectoryMap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, new Dictionary<string, string>());
        var method = typeof(Bootstrapper).GetMethod("ApplyModifications", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void Apply() => Await((Task<bool>)method.Invoke(bootstrapper, null)!);
        Apply();
        Check(File.ReadAllBytes(Path.Combine(player, indoor)).SequenceEqual(File.ReadAllBytes(prepared)), "production launcher maps default sky replacements to the existing indoor sky texture set");
        App.Settings.Prop.ReplaceIndoorSky = false; Apply();
        Check(File.ReadAllText(Path.Combine(player, indoor)) == "original indoor512_bk.tex", "turning off indoor sky replacement restores its original asset bytes");
        App.Settings.Prop.ReplaceIndoorSky = true; Png(Path.Combine(source, "indoor512_bk.tex"), SKColors.Blue); Apply();
        Check(!File.ReadAllBytes(Path.Combine(player, indoor)).SequenceEqual(File.ReadAllBytes(prepared)), "an explicit indoor face supplied by a mod takes priority over the generated alias");
        App.State.Prop.Mods[0].Enabled = false; Apply();
        Check(File.ReadAllText(Path.Combine(player, indoor)) == "original indoor512_bk.tex" && File.ReadAllText(Path.Combine(player, relative)) == "original sky512_bk.tex", "disabling a skybox restores both original sky texture sets");
    }
    private static void RenderRevision8()
    {
        foreach (var theme in new[] { Theme.Dark, Theme.Light })
        {
            App.Settings.Prop.Theme = theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
            var window = new SettingsWindow(false) { Width = 1100, Height = 900 }; window.Show(); RunFor(80);
            ((MainWindowViewModel)window.DataContext!).NavigateRegistered("fastflags"); RunFor(80);
            var groups = window.GetVisualDescendants().OfType<CardExpander>().ToArray();
            foreach (var group in groups) group.IsExpanded = Equals(group.Header, "Roblox graphics");
            RunFor(80);
            var panel = groups.Single(group => Equals(group.Header, "Roblox graphics"));
            var options = panel.GetVisualDescendants().OfType<OptionControl>().ToArray();
            var saved = options.Single(control => control.Header == "In-game graphics level");
            var advanced = options.Single(control => control.Header == "Override Graphics Quality level");
            Check(!saved.GetVisualDescendants().OfType<Slider>().Single().IsEffectivelyEnabled && advanced.GetVisualDescendants().OfType<ToggleSwitch>().Single().IsEffectivelyEnabled && advanced.GetVisualDescendants().OfType<Slider>().Single().IsEffectivelyEnabled,
                "maximum mode holds the saved slider at 10 and leaves the low FRM controls editable: " + theme);
            var scroll = panel.GetVisualAncestors().OfType<ScrollViewer>().First();
            var point = panel.TranslatePoint(default, scroll);
            if (point.HasValue) scroll.Offset = new Vector(0, Math.Max(0, scroll.Offset.Y + point.Value.Y - 16));
            RunFor(60); Capture(window, theme, "revision8-maximum-distance");
            advanced.BringIntoView(); RunFor(60); Capture(window, theme, "revision8-plastic-maximum");
            window.Close();
        }
    }
}
