// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Froststrap;
using Froststrap.Enums;
using Froststrap.Integrations;
using Froststrap.Models;
using Froststrap.Services;
using Froststrap.UI.Elements.Controls;
using Froststrap.UI.ViewModels.Settings;
using SkiaSharp;
using SettingsWindow = Froststrap.UI.Elements.Settings.MainWindow;

internal static partial class Program
{
    private sealed class RetryFixture : HttpMessageHandler
    {
        internal int Calls;
        internal bool ShortRetry;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls == 1)
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(ShortRetry ? 1 : 60));
                return Task.FromResult(response);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[\"recovered\"]") });
        }
    }
    private sealed class NameFixture : HttpMessageHandler
    {
        internal bool Fail;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(
            new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = new StringContent(request.RequestUri!.AbsolutePath.Contains("/places/", StringComparison.Ordinal) ? "{\"universeId\":700701}" : "{\"data\":[{\"name\":\"New experience\"}]}") });
    }
    private static void VerifyRevision7()
    {
        App.GlobalSettings.CreateTemplate(); App.GlobalSettings.Load();
        App.PlayerState.Save();
        var native = new GlobalSettingsViewModel();
        native.PlasticMaximumCommand.Execute(null); App.FastFlags.Save(); App.GlobalSettings.Save();
        App.FastFlags.Load(false); App.GlobalSettings.Load();
        Check(native.TextureQuality == 3 && native.TextureQualityOverride && native.GraphicsQuality == 10 && native.RenderDistance == 1 && native.LockMaximumRenderDistance && App.Settings.Prop.SmoothPlastic,
            "plastic preset pairs maximum saved distance with minimum FRM quality and independent texture replacements");
        string profile = Path.Combine(Paths.Base, "revision7-profile.json");
        File.WriteAllText(profile, "{\"DFIntTextureQualityOverride\":\"0\",\"DFFlagTextureQualityOverrideEnabled\":\"False\",\"DFIntDebugFRMQualityLevelOverride\":\"21\"}");
        CuratedFlagRegistry.SanitizeFile(profile);
        var applied = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(profile))!;
        Check(applied["DFIntTextureQualityOverride"] == "3" && applied["DFFlagTextureQualityOverrideEnabled"] == "True" && applied["DFIntDebugFRMQualityLevelOverride"] == "1", "per-place profiles cannot overwrite selected texture and effects quality at launch");
        native.TextureQualityOverride = false; CuratedFlagRegistry.SanitizeFile(profile);
        Check(!File.ReadAllText(profile).Contains("TextureQuality", StringComparison.Ordinal), "disabling texture override removes both flags from the launch profile");
        App.Settings.Prop.EnableRoValra = false; App.Settings.Prop.PreferredRegion = "Saved Region, US"; App.Settings.SaveSetting(nameof(App.Settings.Prop.PreferredRegion));
        using (var home = new HomeViewModel())
        {
            Check(home.Regions.Contains("Saved Region, US") && home.PreferredRegion == "Saved Region, US", "saved region exists before ComboBox binding even while RoValra is off");
            home.EnableRoValra = false;
            Check(home.PreferredRegion == "Saved Region, US" && home.Regions.Contains(home.PreferredRegion), "disabling or refreshing region choices cannot reset the saved preference to Auto");
            home.PreferredRegion = "Auto"; home.PreferredRegion = "Saved Region, US";
        }
        var diskSettings = JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(File.ReadAllText(App.Settings.FileLocation))!;
        Check(diskSettings.PreferredRegion == "Saved Region, US", "region selection is immediately saved to disk for the next process");
        using (var handler = new RetryFixture { ShortRetry = true })
        using (var client = new HttpClient(handler))
        {
            var uri = new Uri("https://retry-short.invalid/region");
            Check(AwaitResult(RegionRequestCache.GetAsync<List<string>>(client, uri, TimeSpan.FromSeconds(60), default)).Single() == "recovered" && handler.Calls == 2, "short Retry-After responses retry once after the requested delay");
        }
        using (var handler = new RetryFixture())
        {
            using (var client = new HttpClient(handler, false))
            {
                try { Await(RegionRequestCache.GetAsync<List<string>>(client, new Uri("https://retry-long.invalid/first"), TimeSpan.FromSeconds(60), default)); } catch (HttpRequestException) { }
            }
            using var second = new HttpClient(handler, false);
            bool savedCooldown = false;
            try { Await(RegionRequestCache.GetAsync<List<string>>(second, new Uri("https://retry-long.invalid/second"), TimeSpan.FromSeconds(60), default)); }
            catch (HttpRequestException error) { savedCooldown = error.StatusCode == HttpStatusCode.TooManyRequests && error.Message.Contains("selected region is saved", StringComparison.Ordinal); }
            Check(savedCooldown && handler.Calls == 1, "disk cooldown prevents repeated requests across fetcher instances and reports a useful retry time");
        }
        VerifyRevision7Assets();
        using (var handler = new NameFixture())
        using (var client = new HttpClient(handler))
        {
            var session = new RobloxSession(new WatcherData { PlaceId = 700700, GameName = "Launch name" }, client);
            Check(session.Title == "Roblox - Launch name", "title starts from the actual launch destination before logs appear");
            session.ReadLine("[FLog::Output] ! Joining game 'fixture' place 700701 at 203.0.113.1");
            Check(session.Title == "Roblox - Place 700701", "teleport drops the previous game's name immediately");
            Check(AwaitResult(session.ResolveNameAsync(700701, default)) && session.Title == "Roblox - New experience", "Roblox universe APIs resolve the new experience title without activity tracking");
            handler.Fail = true;
            Check(!AwaitResult(session.ResolveNameAsync(700701, default)), "failed name lookups are reported as failures so the session can retry");
            session.ReadLine("[FLog::SingleSurfaceApp] leaveUGCGameInternal");
            Check(session.Title == "Roblox - Home", "leaving an experience resets the title to Home");
        }
        Check(typeof(App).Assembly.GetType("Froststrap.Services.FpsTelemetry") == null, "FPS telemetry and its elevated helper are removed from the application");
        Check(new Froststrap.Models.Persistable.Settings().ShowGameInTitle, "automatic game-name title is enabled for new configurations");
        App.FastFlags.Prop.Clear(); App.FastFlags.Save(); App.Settings.Prop.PreferredRegion = "Auto";
        RenderRevision7();
    }
    private static void VerifyRevision7Assets()
    {
        string bundle = Path.Combine(Paths.Modifications, "revision7-sky");
        string source = Path.Combine(bundle, "wrapper", "nested", "PlatformContent", "pc", "textures", "sky", "sky512_bk.tex");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        using (var bitmap = new SKBitmap(8, 8))
        { bitmap.Erase(new SKColor(212, 80, 150)); using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100); using var stream = File.Create(source); data.SaveTo(stream); }
        string textureFolder = Path.GetDirectoryName(Path.GetDirectoryName(source))!;
        File.WriteAllText(Path.Combine(textureFolder, "studs_Delete.dds"), "legacy deletion marker");
        File.WriteAllText(Path.Combine(bundle, "README.txt"), "fixture wrapper metadata");
        var assets = ModAssetPreparation.Files(bundle).ToArray();
        Check(assets.Length == 2 && assets.All(asset => asset.Relative.StartsWith("PlatformContent", StringComparison.Ordinal)), "nested community archives resolve their real content root despite README files");
        string nestedArchive = Path.Combine(Paths.Base, "revision7-nested.zip"), installed = Path.Combine(Paths.Modifications, "revision7-nested");
        ZipFile.CreateFromDirectory(bundle, nestedArchive); Await(ModArchiveInstaller.InstallAsync(nestedArchive, installed));
        Check(File.Exists(Path.Combine(installed, "PlatformContent", "pc", "textures", "sky", "sky512_bk.tex")), "archive installer commits the normalized content root rather than retaining wrapper directories");
        string invalidArchive = Path.Combine(Paths.Base, "revision7-unsupported.zip");
        using (var zip = ZipFile.Open(invalidArchive, ZipArchiveMode.Create)) { using var writer = new StreamWriter(zip.CreateEntry("readme.txt").Open()); writer.Write("no actual assets"); }
        bool rejected = false;
        try { Await(ModArchiveInstaller.InstallAsync(invalidArchive, installed)); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && File.Exists(Path.Combine(installed, "PlatformContent", "pc", "textures", "sky", "sky512_bk.tex")), "unsupported archives fail before replacing a working installed mod");
        var sky = assets.Single(asset => asset.Relative.EndsWith("sky512_bk.tex", StringComparison.Ordinal));
        using (var reader = new BinaryReader(File.OpenRead(sky.Source)))
        {
            Check(Encoding.ASCII.GetString(reader.ReadBytes(4)) == "DDS " && reader.ReadInt32() == 124, "PNG skybox payloads with TEX extensions normalize to valid DDS headers");
            reader.BaseStream.Position = 12;
            Check(reader.ReadInt32() == 8 && reader.ReadInt32() == 8 && new FileInfo(sky.Source).Length == 128 + (64 + 16 + 4 + 1) * 4, "normalized skybox preserves dimensions and supplies all four RGBA mip levels");
        }
        var studs = assets.Single(asset => asset.Relative.EndsWith("studs.dds", StringComparison.Ordinal));
        Check(File.ReadAllBytes(studs.Source).Skip(128).All(value => value == 0), "legacy image deletions supply a transparent texture instead of a missing Roblox asset");
        string player = Path.Combine(Paths.Base, "revision7-player");
        foreach (var asset in assets) { string path = Path.Combine(player, asset.Relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "original fixture asset"); }
        App.State.Prop.Mods = [new ModConfig { FolderName = "revision7-sky", Enabled = true, Priority = 7 }];
        App.PlayerState.Prop.ModManifest.Clear(); App.PlayerState.Save();
        using (var bootstrapper = new Bootstrapper(LaunchMode.Player))
        {
            typeof(Bootstrapper).GetField("_latestVersionDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, player);
            typeof(Bootstrapper).GetField("PackageDirectoryMap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, new Dictionary<string, string>());
            var apply = typeof(Bootstrapper).GetMethod("ApplyModifications", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Await((Task<bool>)apply.Invoke(bootstrapper, null)!);
            Check(File.ReadAllBytes(Path.Combine(player, sky.Relative)).SequenceEqual(File.ReadAllBytes(sky.Source)) && File.Exists(Path.Combine(player, studs.Relative)), "production launcher applies a decoded skybox and transparent studs to their exact asset paths");
            App.State.Prop.Mods[0].Enabled = false; Await((Task<bool>)apply.Invoke(bootstrapper, null)!);
            Check(assets.All(asset => File.ReadAllText(Path.Combine(player, asset.Relative)) == "original fixture asset"), "disabling normalized texture mods restores original sky and stud assets");
        }
        var visuals = new VisualTweaksViewModel { SkyPalette = "Warm dusk", SmoothWater = true, SmoothPlastic = true, HideSunMoon = true };
        var builtIn = BuiltInVisualMods.Files().ToArray();
        Check(builtIn.Length == 36 && builtIn.Count(asset => asset.Relative.Contains("normal_", StringComparison.Ordinal)) == 25, "built-in visual tweaks generate six sky faces, 25 water maps, three plastic maps and two celestial discs");
        Check(builtIn.All(asset => File.Exists(asset.Source)) && builtIn.All(asset => ModFilePolicy.IsAllowed(asset.Relative)), "new visual controls produce actual supported content files without executable or shader payloads");
        string builtinPlayer = Path.Combine(Paths.Base, "revision7-builtin-player");
        foreach (var asset in builtIn) { string path = Path.Combine(builtinPlayer, asset.Relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "original builtin asset"); }
        App.PlayerState.Prop.ModManifest.Clear(); App.PlayerState.Save();
        using (var bootstrapper = new Bootstrapper(LaunchMode.Player))
        {
            typeof(Bootstrapper).GetField("_latestVersionDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, builtinPlayer);
            typeof(Bootstrapper).GetField("PackageDirectoryMap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, new Dictionary<string, string>());
            var apply = typeof(Bootstrapper).GetMethod("ApplyModifications", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Await((Task<bool>)apply.Invoke(bootstrapper, null)!);
            Check(builtIn.All(asset => File.ReadAllBytes(Path.Combine(builtinPlayer, asset.Relative)).SequenceEqual(File.ReadAllBytes(asset.Source))), "production launcher applies all 36 built-in visual replacements through the real mod pipeline");
            visuals.Reset(); Await((Task<bool>)apply.Invoke(bootstrapper, null)!);
            Check(builtIn.All(asset => File.ReadAllText(Path.Combine(builtinPlayer, asset.Relative)) == "original builtin asset"), "disabling built-in visuals restores every original water, plastic, sky and celestial asset");
        }
        Check(!BuiltInVisualMods.Files().Any(), "restoring visuals removes every built-in replacement from the next launch plan");
        App.State.Prop.Mods.Clear();
    }
    private static void RenderRevision7()
    {
        foreach (var theme in new[] { Theme.Dark, Theme.Light })
        {
            App.Settings.Prop.Theme = theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
            var window = new SettingsWindow(false) { Width = 1100, Height = 900 }; window.Show(); RunFor(80);
            ((MainWindowViewModel)window.DataContext!).NavigateRegistered("fastflags"); RunFor(80);
            var groups = window.GetVisualDescendants().OfType<CardExpander>().ToArray();
            foreach (string title in new[] { "Roblox graphics", "Visual asset tweaks", "Hardware & window title" })
            {
                foreach (var group in groups) group.IsExpanded = Equals(group.Header, title);
                var selected = groups.Single(group => Equals(group.Header, title)); RunFor(80);
                var scroll = selected.GetVisualAncestors().OfType<ScrollViewer>().First();
                var point = selected.TranslatePoint(default, scroll);
                if (point.HasValue) scroll.Offset = new Vector(0, Math.Max(0, scroll.Offset.Y + point.Value.Y - 16));
                RunFor(60);
                Capture(window, theme, "revision7-" + title.Replace(' ', '-').Replace('&', '-'));
                var last = selected.GetVisualDescendants().OfType<OptionControl>().LastOrDefault(); last?.BringIntoView(); RunFor(60);
                Capture(window, theme, "revision7-" + title.Replace(' ', '-').Replace('&', '-') + "-bottom");
            }
            window.Close();
        }
    }
}
