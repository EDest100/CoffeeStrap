// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Froststrap;
using Froststrap.Enums;
using Froststrap.Models.APIs.Config;
using Froststrap.Services;
using Froststrap.UI.Elements.Dialogs;
using Froststrap.UI.ViewModels.Settings;
using Froststrap.UI.ViewModels.Settings.Mods;
using SkiaSharp;
using SettingsWindow = Froststrap.UI.Elements.Settings.MainWindow;

internal static partial class Program
{
    private static void VerifyCoffeeTheme(string archive)
    {
        using var zip = ZipFile.OpenRead(archive);
        var entries = zip.Entries.Where(entry => entry.FullName.StartsWith("content/", StringComparison.Ordinal) || entry.FullName.StartsWith("ExtraContent/", StringComparison.Ordinal) || entry.FullName.StartsWith("PlatformContent/", StringComparison.Ordinal)).ToArray();
        Check(entries.Length > 1500 && entries.Select(entry => entry.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == entries.Length, "Espresso covers the broad local client asset set without duplicate destinations");
        Check(entries.All(entry => ModFilePolicy.IsAllowed(entry.FullName) && new[] { ".png", ".dds", ".tex", ".ttf", ".json" }.Contains(Path.GetExtension(entry.FullName), StringComparer.OrdinalIgnoreCase)), "pack contains content assets only, with no scripts, shaders or executable payloads");
        Check(!entries.Any(entry => entry.FullName.Contains("normal", StringComparison.OrdinalIgnoreCase) || entry.FullName.Contains("brdf", StringComparison.OrdinalIgnoreCase)), "theme never recolors normal maps or lighting lookup data");
        Check(CoffeeThemeBuilder.Recolor(new SKColor(30, 80, 200, 79)).Alpha == 79 && CoffeeThemeBuilder.Recolor(new SKColor(23, 61, 81, 0)) == new SKColor(23, 61, 81, 0), "palette transform retains opacity and fully transparent source pixels");
        Check(!CoffeeThemeBuilder.Eligible("content/textures/plastic/normal.png") && CoffeeThemeBuilder.Eligible("ExtraContent/LuaPackages/Packages/_Index/Test/SpriteSheets/icons.png"), "coverage includes UI sheets and excludes material data PNGs");
        foreach (var entry in entries.Where(entry => entry.FullName.EndsWith(".ttf", StringComparison.Ordinal)))
        {
            using var stream = entry.Open(); using var data = new MemoryStream(); stream.CopyTo(data);
            Check(CoffeeThemeBuilder.Checksum(data.ToArray()) == 0xB1B0AFBA, "OpenType whole-font checksum is valid: " + entry.Name);
            data.Position = 0; using var typeface = SKTypeface.FromStream(data);
            Check(typeface != null && typeface.GlyphCount > 500, "actual Skia parser accepts themed icon font: " + entry.Name);
            using var font = new SKFont(typeface, 80); using var bitmap = new SKBitmap(180, 130); using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.Transparent);
            using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            canvas.DrawText("\uF101\uF102", 2, 100, font, paint);
            Check(bitmap.Pixels.Any(color => color.Alpha > 240 && Math.Abs(color.Red - CoffeeThemeBuilder.Caramel.Red) < 3 && Math.Abs(color.Green - CoffeeThemeBuilder.Caramel.Green) < 3), "real font rasterizer uses the coffee palette rather than the caller's white paint: " + entry.Name);
        }
        int ddsCount = 0;
        foreach (var entry in entries.Where(entry => entry.FullName.EndsWith(".dds", StringComparison.Ordinal) || entry.FullName.EndsWith(".tex", StringComparison.Ordinal)))
        {
            using var reader = new BinaryReader(entry.Open()); byte[] bytes = reader.ReadBytes((int)entry.Length);
            int width = BitConverter.ToInt32(bytes, 16), height = BitConverter.ToInt32(bytes, 12), levels = BitConverter.ToInt32(bytes, 28), total = 128, expectedLevels = 0;
            while (true) { total += width * height * 4; expectedLevels++; if (width == 1 && height == 1) break; width = Math.Max(1, width / 2); height = Math.Max(1, height / 2); }
            if (bytes.AsSpan(0, 4).SequenceEqual("DDS "u8) && levels == expectedLevels && bytes.Length == total) ddsCount++;
        }
        Check(ddsCount == 19, "all twelve sky faces, six particle ramps and plastic diffuse have complete valid-sized DDS mip chains");
        App.RemoteData.LoadedState = GenericTriState.Failed;
        bool failDownload = true;
        using var catalog = new CommunityModsViewModel((url, destination, progress) =>
        {
            Check(url == CoffeeCommunityMod.Create().DownloadUrl, "first-party card uses the pinned own-repository release asset");
            if (failDownload) throw new IOException("fixture download failure");
            File.Copy(archive, destination, true); progress.Report(100); return Task.CompletedTask;
        });
        Await(catalog.RefreshModsAsync()); var mod = catalog.Mods.Single(item => item.Id == CoffeeCommunityMod.Id);
        Check(catalog.HasError && mod.CanDownload && mod.Thumbnail is { PixelSize.Width: > 0 }, "CoffeeStrap card and bundled preview remain available when the remote catalog fails");
        var repeated = mod; Await(catalog.RefreshModsAsync()); Check(ReferenceEquals(repeated, catalog.Mods.Single(item => item.Id == CoffeeCommunityMod.Id)), "refresh retains one first-party model and thumbnail rather than leaking a bitmap per refresh");
        App.RemoteData.Prop.CommunityMods = [new CommunityMod { Id = CoffeeCommunityMod.Id, Name = "Untrusted duplicate" }]; App.RemoteData.LoadedState = GenericTriState.Successful;
        Await(catalog.RefreshModsAsync()); Check(catalog.Mods.Count(item => item.Id == CoffeeCommunityMod.Id) == 1 && mod.Name == "CoffeeStrap Espresso", "remote duplicate cannot replace the built-in first-party descriptor");
        catalog.SearchQuery = "espresso"; RunFor(300); Check(catalog.Mods.Contains(mod), "CoffeeStrap pack participates in the normal community search");
        catalog.SetFilterCommand.Execute(ModType.SkyBox); Check(!catalog.Mods.Contains(mod), "category filter doesn't mislabel the full theme as a standalone skybox");
        catalog.SetFilterCommand.Execute(null); catalog.SearchQuery = "no such theme"; RunFor(300); Check(catalog.IsEmpty, "theme search retains the real no-results state");
        catalog.SearchQuery = ""; RunFor(300);
        Await(catalog.DownloadModAsync(mod)); Check(!string.IsNullOrEmpty(mod.DownloadError) && mod.DownloadLabel == "Retry" && !mod.IsInstalled && mod.CanDownload, "download failure leaves a retryable card without a false install record");
        failDownload = false; Await(catalog.DownloadModAsync(mod));
        string installed = Path.Combine(Paths.Modifications, mod.Name);
        Check(mod.IsInstalled && mod.IsEnabled && App.State.Prop.Mods.Single(item => item.CommunityModId == CoffeeCommunityMod.Id).Enabled && Directory.EnumerateFiles(installed, "*", SearchOption.AllDirectories).Count() == zip.Entries.Count, "normal community download pipeline extracts the complete pack and persists its enabled registration");
        App.State.Load(); Check(App.State.Prop.Mods.Any(item => item.CommunityModId == CoffeeCommunityMod.Id), "theme registration survives state reload");
        App.GlobalSettings.CreateTemplate(); App.GlobalSettings.Load(); App.Settings.Prop.LockMaximumRenderDistance = true;
        var native = new GlobalSettingsViewModel { RenderDistance = 1 }; App.GlobalSettings.Save(); App.FastFlags.Save(); App.PlayerState.Save();
        string originalXml = File.ReadAllText(GBSEditor.FileLocation), originalFlags = JsonSerializer.Serialize(App.FastFlags.Prop);
        string player = Path.Combine(Paths.Base, "coffee-theme-player");
        string[] targets = ["content/textures/Cursors/KeyboardMouse/ArrowCursor.png", "content/textures/ui/Settings/Help/LeaveIcon.png", "ExtraContent/LuaPackages/Packages/_Index/BuilderIcons/BuilderIcons/Font/BuilderIcons-Regular.ttf", "ExtraContent/LuaPackages/Packages/_Index/BuilderIcons/BuilderIcons/BuilderIcons.json", "PlatformContent/pc/textures/sky/sky512_ft.tex", "PlatformContent/pc/textures/sky/indoor512_ft.tex", "content/textures/particles/fire_color.dds", "PlatformContent/pc/textures/plastic/diffuse.dds"];
        foreach (string path in targets) { string file = Path.Combine(player, path); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, "original " + path); }
        using (var bootstrapper = new Bootstrapper(LaunchMode.Player))
        {
            typeof(Bootstrapper).GetField("_latestVersionDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, player);
            typeof(Bootstrapper).GetField("PackageDirectoryMap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, new Dictionary<string, string>());
            var apply = typeof(Bootstrapper).GetMethod("ApplyModifications", BindingFlags.Instance | BindingFlags.NonPublic)!;
            void Apply() => Await((Task<bool>)apply.Invoke(bootstrapper, null)!);
            Apply();
            Check(targets.All(path => File.ReadAllBytes(Path.Combine(player, path)).SequenceEqual(File.ReadAllBytes(Path.Combine(installed, path)))), "actual launcher applies theme icons, font, family, both skies, particle ramp and surface bytes to an isolated player");
            catalog.ToggleMod(mod); Apply(); Check(targets.All(path => File.ReadAllText(Path.Combine(player, path)) == "original " + path), "Disable restores every representative original through the production manifest");
            catalog.ToggleMod(mod); Apply(); Check(targets.All(path => File.ReadAllBytes(Path.Combine(player, path)).SequenceEqual(File.ReadAllBytes(Path.Combine(installed, path)))), "re-enable applies the same complete theme again");
            catalog.UninstallMod(mod); Apply(); Check(!Directory.Exists(installed) && mod.CanDownload && targets.All(path => File.ReadAllText(Path.Combine(player, path)) == "original " + path), "trash unregisters the pack, preserves its local source and restores all original fixture bytes");
        }
        Check(File.ReadAllText(GBSEditor.FileLocation) == originalXml && JsonSerializer.Serialize(App.FastFlags.Prop) == originalFlags && native.GraphicsQuality == 10 && native.RenderDistance == 1, "install, toggle, relaunch and uninstall preserve native graphics 10 and FRM 1");
        foreach (var theme in new[] { Theme.Dark, Theme.Light }) foreach (int width in new[] { 760, 1100 }) RenderCoffeeTheme(catalog, mod, theme, width);
        File.WriteAllText(Path.Combine(_output, "coffee-theme-probe.json"), JsonSerializer.Serialize(new { assets = entries.Length, actualModPipeline = true, actualSkiaColorFontRaster = true, gameLaunched = false, performanceMeasured = false, memoryAccess = false }));
    }
    private static void RenderCoffeeTheme(CommunityModsViewModel catalog, CommunityMod mod, Theme theme, int width)
    {
        App.Settings.Prop.Theme = theme; App.Settings.Prop.DisableAnimations = false; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme(); App.ApplyAnimationSettings();
        var window = new SettingsWindow(false) { Width = width, Height = 900 }; window.Show(); RunFor(60);
        var owner = (MainWindowViewModel)window.DataContext!; owner.NavigateRegistered("mods"); RunFor(80);
        var page = new Froststrap.UI.Elements.Settings.Pages.Mods.CommunityModsPage { DataContext = catalog };
        window.FindControl<TransitioningContentControl>("PageContentControl")!.Content = page; RunFor(120);
        Check(page.GetVisualDescendants().OfType<Button>().Any(button => Equals(button.Command, mod.PreviewCommand)), "production community card exposes a keyboard focusable preview: " + theme + width);
        Capture(window, theme, "espresso-community-" + width);
        var previewButton = page.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Command, mod.PreviewCommand));
        if (width == 760)
        {
            previewButton.Focus(); window.KeyPress(Key.Enter, RawInputModifiers.None, default, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, default, null);
        }
        else Click(window, At(previewButton, window));
        RunFor(80);
        Check(window.OwnedWindows.OfType<CommunityModInfoDialog>().Count() == 1, "real keyboard/pointer preview action opens one owned dialog: " + theme + width);
        var preview = window.OwnedWindows.OfType<CommunityModInfoDialog>().Single(); preview.Width = width == 760 ? 460 : 720; RunFor(120);
        Check(preview.GetVisualDescendants().OfType<Image>().Any(image => ReferenceEquals(image.Source, mod.Thumbnail)), "preview dialog uses the real bundled asset image without a network request: " + theme + width);
        Capture(preview, theme, "espresso-preview-" + width);
        if (width == 760) { preview.SetRenderScaling(1.5); RunFor(40); Capture(preview, theme, "espresso-preview-scaled"); preview.SetRenderScaling(1); }
        App.Settings.Prop.DisableAnimations = true; App.ApplyAnimationSettings(); preview.Close(); owner.NavigateRegistered("behaviour"); RunFor(50); window.Close();
        Check(TopLevel.GetTopLevel(page) == null, "community and preview detach cleanly with reduced motion: " + theme + width);
    }
}
