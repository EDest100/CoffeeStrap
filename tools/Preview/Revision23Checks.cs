// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)] private static extern nint CreateSizingWindow(uint extended, string type, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out PlayerWindowSizing.Rectangle rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    private static void VerifyRevision23()
    {
        string previous = JsonSerializer.Serialize(App.Settings.Prop);
        var mods = App.State.Prop.Mods; var manifest = App.PlayerState.Prop.ModManifest.ToList();
        string previousMods = Paths.Modifications;
        string root = Path.Combine(Paths.DataRoot, "render-tweaks"); Directory.CreateDirectory(root);
        try
        {
            var visuals = new VisualTweaksViewModel(); visuals.Reset();
            var platform = new PlatformTweaksViewModel(); platform.WindowSize = 0;
            Check(!BuiltInVisualMods.Files().Any() && !TextureResolutionBudget.Targets().Any(), "new rendering tweaks default to no asset writes");
            visuals.SmoothStuds = true; visuals.CloudTextureResolution = 2; platform.WindowSize = 3;
            var saved = JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(File.ReadAllText(App.Settings.FileLocation))!;
            Check(saved.SmoothStuds && saved.CloudTextureResolution == 2 && saved.PlayerWindowSize == 3, "all three new tweaks persist through actual settings writes");
            Check(platform.WindowPixelSummary.Contains("44%", StringComparison.Ordinal), "pixel summary describes pixel count rather than an FPS gain");
            visuals.CloudTextureResolution = 8; platform.WindowSize = -1;
            Check(visuals.CloudTextureResolution == 2 && platform.WindowSize == 3, "invalid new tweak selections cannot change persisted values");
            var studs = BuiltInVisualMods.Files().Single(); byte[] studBytes = File.ReadAllBytes(studs.Source);
            Check(studs.Relative.Replace('\\', '/') == "PlatformContent/pc/textures/studs.dds" && DdsValue(studBytes, 16) == 16 && DdsValue(studBytes, 12) == 256 && studBytes.AsSpan(128).ToArray().All(value => value == 255), "plain studs produce a compact opaque neutral atlas with full mipmaps and no black pixels");
            string player = Path.Combine(root, "player"), library = Path.Combine(root, "library"); Directory.CreateDirectory(library);
            typeof(Paths).GetProperty(nameof(Paths.Modifications))!.SetValue(null, library);
            var paths = TextureResolutionBudget.Targets().Select(item => item.Relative).Append(studs.Relative).ToArray();
            var originals = new Dictionary<string, byte[]>();
            foreach (string path in paths)
            {
                ModAssetPreparation.WriteDds(Path.Combine(player, path), 32, 32, (_, _) => new SkiaSharp.SKColor(50, 100, 150));
                originals[path] = File.ReadAllBytes(Path.Combine(player, path));
            }
            App.State.Prop.Mods = []; App.PlayerState.Prop.ModManifest.Clear();
            File.WriteAllText(App.PlayerState.FileLocation, JsonSerializer.Serialize(App.PlayerState.Prop)); App.PlayerState.Load(false);
            using var bootstrapper = new Bootstrapper(LaunchMode.Player);
            typeof(Bootstrapper).GetField("_latestVersionDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, player);
            typeof(Bootstrapper).GetField("_latestVersionGuid", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, "fixture-render-tweaks");
            typeof(Bootstrapper).GetField("PackageDirectoryMap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, new Dictionary<string, string>());
            var apply = typeof(Bootstrapper).GetMethod("ApplyModifications", BindingFlags.Instance | BindingFlags.NonPublic)!;
            bool Apply() => AwaitResult((Task<bool>)apply.Invoke(bootstrapper, null)!);
            Check(Apply() && paths.Take(2).All(path => DdsValue(File.ReadAllBytes(Path.Combine(player, path)), 16) == 8) && File.ReadAllBytes(Path.Combine(player, studs.Relative)).SequenceEqual(studBytes), "actual launcher applies cloud quarter mips and plain stud atlas");
            Check(Apply() && paths.Take(2).All(path => DdsValue(File.ReadAllBytes(Path.Combine(player, path)), 16) == 8), "new cloud budgets never shrink cumulatively across launches");
            ModAssetPreparation.WriteDds(Path.Combine(library, "custom-studs", studs.Relative), 16, 16, (_, _) => SkiaSharp.SKColors.Red);
            App.State.Prop.Mods = [new ModConfig { FolderName = "custom-studs", Priority = 9 }];
            Check(Apply() && DdsValue(File.ReadAllBytes(Path.Combine(player, studs.Relative)), 16) == 16, "installed surface mod priority overrides the built-in plain stud option");
            App.State.Prop.Mods.Clear(); visuals.Reset();
            Check(Apply() && paths.All(path => File.ReadAllBytes(Path.Combine(player, path)).SequenceEqual(originals[path])), "disabling new file tweaks restores exact original cloud and stud bytes");
            Check(paths.All(path => !App.PlayerState.Prop.ModManifest.Contains(path)), "new file tweaks retire their restored manifest entries");
            VerifyNativeSizing();
            foreach (var theme in new[] { Theme.Dark, Theme.Light }) foreach (int width in new[] { 760, 1100 }) RenderRevision23(theme, width);
            platform.WindowSize = 0;
            App.Settings.Prop.CloudTextureResolution = 999; App.Settings.Prop.PlayerWindowSize = 999;
            Check(visuals.CloudTextureResolution == 0 && platform.WindowSize == 0 && !TextureResolutionBudget.Targets().Any(), "invalid legacy values display defaults and produce no new asset targets");
        }
        finally
        {
            App.Settings.Prop = JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(previous)!;
            App.State.Prop.Mods = mods; App.PlayerState.Prop.ModManifest = manifest;
            typeof(Paths).GetProperty(nameof(Paths.Modifications))!.SetValue(null, previousMods);
        }
    }
    private static void VerifyNativeSizing()
    {
        var fit = PlayerWindowSizing.Fit(new(-1920, 0, 0, 1040), 16, 39, 1920, 1080);
        Check(fit.Left >= -1920 && fit.Right <= 0 && fit.Bottom <= 1040 && fit.Width < 1920, "window fitting handles negative-coordinate monitors and frame borders");
        bool tiny = false; try { PlayerWindowSizing.Fit(new(0, 0, 300, 200), 16, 39, 1280, 720); } catch (IOException) { tiny = true; }
        Check(tiny, "window fitting rejects unreadably small monitor areas");
        if (!OperatingSystem.IsWindows()) return;
        nint window = CreateSizingWindow(0, "STATIC", "CoffeeStrap isolated sizing test", 0x00CF0000, 100, 100, 1000, 700, 0, 0, 0, 0);
        Check(window != 0 && GetWindowRect(window, out _), "native Windows sizing probe creates only its own hidden window");
        GetWindowRect(window, out var original);
        try
        {
            using (var sizing = new PlayerWindowSizing(window, (uint)Environment.ProcessId, 960, 540))
            {
                RunFor(80);
                Check(sizing.Confirmed && sizing.ClientSize.Width <= 960 && sizing.ClientSize.Height <= 540, "real SetWindowPos and GetClientRect confirm the requested fitted client size");
            }
            RunFor(80); GetWindowRect(window, out var restored);
            Check(restored == original, "native sizing disposal restores its original window geometry");
            using (var sizing = new PlayerWindowSizing(window, (uint)Environment.ProcessId, 960, 540))
            {
                RunFor(60); SetWindowPos(window, 0, 120, 120, 800, 600, 0x14); RunFor(60);
            }
            RunFor(60); GetWindowRect(window, out var retained);
            Check(retained == new PlayerWindowSizing.Rectangle(120, 120, 920, 720), "native restore retains a later user size or position change");
            bool foreign = false; try { using var rejected = new PlayerWindowSizing(window, uint.MaxValue, 960, 540); } catch (IOException) { foreign = true; }
            Check(foreign, "native size rejects a window whose process identity does not match");
        }
        finally { DestroyWindow(window); }
    }
    private static void RenderRevision23(Theme theme, int width)
    {
        App.Settings.Prop.Theme = theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
        var window = new SettingsWindow(false) { Width = width, Height = 900 }; window.Show(); RunFor(60);
        ((MainWindowViewModel)window.DataContext!).NavigateRegistered("fastflags"); RunFor(70);
        var groups = window.GetVisualDescendants().OfType<CardExpander>().ToArray();
        foreach (string title in new[] { "Windowed render size", "Texture resolution budget", "Visual asset tweaks" })
        {
            foreach (var group in groups) group.IsExpanded = Equals(group.Header, title);
            var selected = groups.Single(group => Equals(group.Header, title)); RunFor(70);
            var scroller = selected.GetVisualAncestors().OfType<ScrollViewer>().First();
            var point = selected.TranslatePoint(default, scroller); if (point.HasValue) scroller.Offset = new Vector(0, scroller.Offset.Y + point.Value.Y - 16);
            RunFor(60); Capture(window, theme, "render-tweaks-" + title.Replace(' ', '-') + "-" + width);
            Check(selected.Bounds.Width > 0 && selected.Bounds.Width <= window.Bounds.Width, "new rendering group fits actual production viewport: " + title + " " + theme + " " + width);
            if (title == "Windowed render size")
            {
                var combo = selected.GetVisualDescendants().OfType<ComboBox>().Single(control => control.Name == "WindowSizePreset"); combo.SelectedIndex = 4; RunFor(40);
                Check(App.Settings.Prop.PlayerWindowSize == 4 && ((CuratedFastFlagsViewModel)selected.DataContext!).Platform.WindowPixelSummary.Contains("25%", StringComparison.Ordinal), "actual window-size picker updates selection and pixel summary: " + theme + " " + width);
                if (width == 760) { window.SetRenderScaling(1.5); RunFor(80); Capture(window, theme, "render-tweaks-scaled"); window.SetRenderScaling(1); }
            }
        }
        window.Close();
    }
}
