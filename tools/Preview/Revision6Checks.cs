// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Net;
using System.Text;
using System.Text.Json;
using System.Reflection;
using System.IO.Compression;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Froststrap;
using Froststrap.Enums;
using Froststrap.Integrations;
using Froststrap.Models;
using Froststrap.Models.APIs.Config;
using Froststrap.Models.APIs.Roblox;
using Froststrap.Services;
using Froststrap.UI.Elements.Controls;
using Froststrap.UI.ViewModels.Settings;
using Froststrap.UI.ViewModels.Settings.Mods;
using SettingsWindow = Froststrap.UI.Elements.Settings.MainWindow;

internal static partial class Program
{
    private sealed class RegionFixture : HttpMessageHandler
    {
        public int ProviderCalls, PublicCalls;
        public bool RateLimited;
        public readonly string First = "00000000-0000-0000-0000-000000000601", Second = "00000000-0000-0000-0000-000000000602";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Headers.Contains("Cookie") || request.Headers.Authorization != null) throw new InvalidOperationException("Region search must not forward credentials.");
            bool provider = request.RequestUri!.Host == "apis.rovalra.com";
            if (provider) ProviderCalls++; else PublicCalls++;
            if (RateLimited && provider) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
            string json = request.RequestUri.AbsolutePath switch
            {
                "/v1/datacenters/list" => "[{\"dataCenterIds\":[601],\"location\":{\"city\":\"Fixture City\",\"country\":\"US\"}}]",
                "/v1/servers/details" => JsonSerializer.Serialize(new { status = "success", servers = new[] { new { server_id = First, datacenter_id = 601, city = "Fixture City", country = "United States", ip_address = "203.0.113.10" }, new { server_id = Second, datacenter_id = 999, city = "Elsewhere", country = "CA", ip_address = "203.0.113.11" } } }),
                _ => JsonSerializer.Serialize(new { data = new[] { new { id = First, playing = 18, maxPlayers = 20 }, new { id = Second, playing = 19, maxPlayers = 20 } }, nextPageCursor = (string?)null })
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
    private static void VerifyRevision6()
    {
        App.GlobalSettings.CreateTemplate(); App.GlobalSettings.Load();
        App.FastFlags.Prop.Clear();
        var graphics = new GraphicsCustomizationViewModel();
        for (int level = 0; level <= 9; level++)
        {
            graphics.MeshDetail = level; App.FastFlags.Save(); App.FastFlags.Load(false);
            Check(graphics.MeshDetail == level && graphics.MeshDetailEnabled, "Mesh Detail level " + level + " persists without legacy rounding loss");
        }
        graphics.MeshDetailEnabled = false;
        Check(graphics.MeshThresholds.All(setting => App.FastFlags.GetValue(setting.Key) == null), "disabling Mesh Detail removes all four thresholds");
        graphics.MeshDetailEnabled = true;
        Check(graphics.MeshDetail == 5, "enabling mesh customization uses the established level five preset");
        graphics.MeshThresholds[2].Value = 2400;
        Check(App.FastFlags.GetValue(graphics.MeshThresholds[2].Key) == "2400", "advanced LOD transitions override the real stored threshold independently");
        graphics.GrassSettings[0].Value = 150; graphics.GrassSettings[1].Value = 600; graphics.GrassSettings[2].Value = 0;
        Check(App.FastFlags.GetValue("FIntFRMMaxGrassDistance") == "600" && App.FastFlags.GetValue("FIntGrassMovementReducedMotionFactor") == "0", "grass distance and motion are independent persisted flags");
        var native = new GlobalSettingsViewModel { RenderDistance = 17 };
        native.GraphicsQuality = 2;
        App.FastFlags.Save(); App.FastFlags.Load(false);
        Check(native.RenderDistance == 17 && native.RenderDistanceOverride && native.GraphicsQuality == 2, "fixed FRM override remains selected while saved Roblox graphics quality changes");
        native.RenderDistance = 500; Check(native.RenderDistance == 21, "render override clamps to level 21");
        string profile = Path.Combine(Paths.Base, "profile-override.json"); File.WriteAllText(profile, "{\"DFIntDebugFRMQualityLevelOverride\":\"3\"}");
        CuratedFlagRegistry.SanitizeFile(profile);
        Check(File.ReadAllText(profile).Contains("21", StringComparison.Ordinal), "selected render level also overrides conflicting per-place profile values at launch");
        native.RenderDistanceOverride = false;
        Check(App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride") == null, "disabling the render override restores normal Roblox slider control");
        var curated = new CuratedFastFlagsViewModel();
        curated.Settings.Single(setting => setting.Title == "Gray sky").Selected = curated.Settings.Single(setting => setting.Title == "Gray sky").Choices.Last();
        native.VignetteEnabled = true; native.MaxQualityEnabled = true;
        curated.ResetVisualsCommand.Execute(null);
        Check(App.FastFlags.Prop.Count == 0 && !native.VignetteEnabled && !native.MaxQualityEnabled && curated.Settings.All(setting => setting.Selected == setting.Choices[0]), "Restore Roblox visuals removes overrides and resets vignette and quality effects in the bound controls");
        VerifyRevision6Regions();
        VerifyRevision6Mods();
        VerifyRevision6Session();
        RenderRevision6();
    }
    private static void VerifyRevision6Regions()
    {
        bool saved = App.Settings.Prop.EnableRoValra;
        App.Settings.Prop.EnableRoValra = true; App.Settings.Prop.PreferredRegion = "Fixture City, US";
        var handler = new RegionFixture();
        using var fetcher = new RobloxServerFetcher(handler);
        var regions = AwaitResult(fetcher.GetPreferredRegionsAsync());
        Check(regions.Contains("Fixture City, US"), "region choices come from the provider datacenter response");
        string selected = AwaitResult(fetcher.FindFullestPreferredServerAsync(600600, "Fixture City, US"));
        Check(selected == handler.First, "fullest selection correlates occupancy with region instead of picking the fullest server elsewhere");
        int calls = handler.ProviderCalls;
        Await(fetcher.FindFullestPreferredServerAsync(600600, "Fixture City, US"));
        Check(handler.ProviderCalls == calls && handler.PublicCalls == 1, "both provider metadata and public population pages are reused for sixty seconds");
        Check(!Directory.GetFiles(Path.Combine(Paths.Cache, "RoValra"), "*.json").Any(file => File.ReadAllText(file).Contains("203.0.113", StringComparison.Ordinal)), "provider cache stores location metadata without server IP addresses");
        var locations = new[] { new RobloxServerFetcher.RegionDetail { ServerId = handler.First, DatacenterId = 601 }, new RobloxServerFetcher.RegionDetail { ServerId = handler.Second, DatacenterId = 601 } };
        var population = new[] { new ServerInstance { Id = handler.First, Playing = 20, MaxPlayers = 20 }, new ServerInstance { Id = handler.Second, Playing = 14, MaxPlayers = 20 } };
        Check(RobloxServerFetcher.SelectFullest(population, locations, "Fixture City, US", new Dictionary<int, string> { [601] = "Fixture City, US" })?.Id == handler.Second, "full servers are excluded even when they have the highest population");
        population[1].MaxPlayers = 0;
        Check(RobloxServerFetcher.SelectFullest(population, locations, "Fixture City, US", new Dictionary<int, string> { [601] = "Fixture City, US" }) == null, "unknown capacity cannot masquerade as a joinable server");
        string command = "roblox://experiences/start?placeId=600600";
        var join = GameJoin.GetJoinDataByLaunchCommand(command);
        Check(PreferredRegionMatchmaking.Applies(join, command), "normal CoffeeStrap game launches enter regional matchmaking");
        Check(PreferredRegionMatchmaking.WithServer(command, join, handler.First).Contains("gameInstanceId=" + handler.First, StringComparison.Ordinal), "regional selection reaches the real Roblox launch URI");
        foreach (string suffix in new[] { "&gameInstanceId=" + handler.First, "&accessCode=private", "&linkCode=private", "&userId=123", "&joinAttemptOrigin=friendServerListJoin" })
            Check(!PreferredRegionMatchmaking.Applies(GameJoin.GetJoinDataByLaunchCommand(command + suffix), command + suffix), "explicit join keeps precedence: " + suffix.Split('=')[0]);
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); bool cancelled = false;
        try { Await(fetcher.FindFullestPreferredServerAsync(600600, "Fixture City, US", cancel.Token)); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "cancelled region search stops before launching a server");
        handler.RateLimited = true;
        foreach (string file in Directory.GetFiles(RegionRequestCache.Folder, "*.json")) File.Delete(file);
        bool limited = false;
        try { Await(fetcher.FindFullestPreferredServerAsync(600601, "Fixture City, US")); } catch (HttpRequestException error) { limited = error.StatusCode == HttpStatusCode.TooManyRequests; }
        calls = handler.ProviderCalls;
        try { Await(fetcher.FindFullestPreferredServerAsync(600601, "Fixture City, US")); } catch (HttpRequestException) { }
        Check(limited && handler.ProviderCalls == calls, "429 responses stop provider calls until the retry window expires");
        foreach (string file in Directory.GetFiles(RegionRequestCache.Folder, "*.json")) File.Delete(file);
        App.Settings.Prop.EnableRoValra = false;
        Check(!PreferredRegionMatchmaking.Applies(join, command), "turning off RoValra restores ordinary matchmaking");
        App.Settings.Prop.PreferredRegion = "Auto"; App.Settings.Prop.EnableRoValra = saved;
    }
    private static T AwaitResult<T>(Task<T> task) { Await(task); return task.GetAwaiter().GetResult(); }
    private static void VerifyRevision6Mods()
    {
        App.RemoteData.LoadedState = GenericTriState.Successful;
        var mod = new CommunityMod { Id = "revision6-mod", Name = "revision6-mod", DownloadUrl = "https://example.invalid/mod.zip", ThumbnailUrl = "" };
        App.RemoteData.Prop.CommunityMods = [mod];
        string archive = Path.Combine(Paths.Base, "revision6-content.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) { using var writer = new StreamWriter(zip.CreateEntry("bundle/content/textures/revision6.txt").Open()); writer.Write("high priority"); }
        using var catalog = new CommunityModsViewModel((_, destination, _) => { File.Copy(archive, destination, true); return Task.CompletedTask; });
        Await(catalog.RefreshModsAsync()); Await(catalog.DownloadModAsync(mod));
        Check(mod.IsInstalled && mod.IsEnabled && mod.ToggleLabel == "Disable", "installed catalog immediately exposes Disable and uninstall commands");
        catalog.ToggleMod(mod);
        string lowPath = Path.Combine(Paths.Modifications, "revision6-low", "content", "textures"); Directory.CreateDirectory(lowPath); File.WriteAllText(Path.Combine(lowPath, "revision6.txt"), "lower priority");
        App.State.Prop.Mods.Add(new ModConfig { FolderName = "revision6-low", Priority = 0, Enabled = true });
        Await(catalog.RefreshModsAsync());
        var localCard = catalog.Mods.Single(item => item.Name == "revision6-low");
        Check(localCard.IsLocalOnly && localCard.IsInstalled && localCard.ToggleCommand != null && localCard.UninstallCommand != null, "locally installed mods absent from the catalog retain inline controls");
        Check(!mod.IsEnabled && mod.ToggleLabel == "Enable" && !App.State.Prop.Mods.Single(item => item.CommunityModId == mod.Id).Enabled, "Disable updates the actual launcher mod record and catalog state");
        catalog.ToggleMod(mod);
        string player = Path.Combine(Paths.Base, "revision6-player"); Directory.CreateDirectory(Path.Combine(player, "content", "textures"));
        string target = Path.Combine(player, "content", "textures", "revision6.txt"); File.WriteAllText(target, "original Roblox asset");
        App.PlayerState.Prop.ModManifest.Clear(); App.PlayerState.Save(); App.FastFlags.Save();
        using (var bootstrapper = new Bootstrapper(LaunchMode.Player))
        {
            typeof(Bootstrapper).GetField("_latestVersionDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, player);
            typeof(Bootstrapper).GetField("PackageDirectoryMap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, new Dictionary<string, string>());
            var apply = typeof(Bootstrapper).GetMethod("ApplyModifications", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Await((Task<bool>)apply.Invoke(bootstrapper, null)!);
            Check(File.ReadAllText(target) == "high priority", "production bootstrapper applies the downloaded content mod to the isolated player directory");
            catalog.ToggleMod(mod); Await((Task<bool>)apply.Invoke(bootstrapper, null)!);
            Check(File.ReadAllText(target) == "lower priority", "disabling the higher-priority mod exposes the next enabled replacement");
            catalog.UninstallMod(localCard); Await((Task<bool>)apply.Invoke(bootstrapper, null)!);
            Check(File.ReadAllText(target) == "original Roblox asset", "production bootstrapper restores original bytes after Disable");
            catalog.ToggleMod(mod); Await((Task<bool>)apply.Invoke(bootstrapper, null)!);
            catalog.UninstallMod(mod); Await((Task<bool>)apply.Invoke(bootstrapper, null)!);
            Check(!mod.IsInstalled && mod.CanDownload && File.ReadAllText(target) == "original Roblox asset", "trash action uninstalls the mod and the launcher restores the original asset");
        }
        string shader = Path.Combine(Paths.Base, "revision6-shader.zip");
        using (var zip = ZipFile.Open(shader, ZipArchiveMode.Create)) { using var writer = new StreamWriter(zip.CreateEntry("content/shaders/effect.fx").Open()); writer.Write("shader payload"); }
        bool rejected = false;
        try { Await(ModArchiveInstaller.InstallAsync(shader, Path.Combine(Paths.Modifications, "rejected-shader"))); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && !Directory.Exists(Path.Combine(Paths.Modifications, "rejected-shader")), "shader archives are rejected before installation");
        Check(!ModFilePolicy.IsAllowed("dxgi.dll") && !ModFilePolicy.IsAllowed("PlatformContent/pc/shaders/effect.bin") && ModFilePolicy.IsAllowed("content/textures/default.png"), "launcher shader policy blocks injection payloads and permits content assets");
        string repeated = Path.Combine(Paths.Modifications, "revision6-repeat");
        for (int i = 0; i < 12; i++) Await(ModArchiveInstaller.InstallAsync(archive, repeated));
        Check(File.ReadAllText(Path.Combine(repeated, "content", "textures", "revision6.txt")) == "high priority", "twelve sequential archive replacements commit validated content successfully");
    }
    private static void VerifyRevision6Session()
    {
        string joined = "[FLog::Output] ! Joining game '00000000-0000-0000-0000-000000000601' place 9391468976 at 203.0.113.10";
        string confirmed = "[FLog::Network] serverId: 203.0.113.10|123";
        string disconnected = "[FLog::Network] Time to disconnect replication data: 0";
        var state = new ReconnectState(); state.Read(joined); state.Read(confirmed); state.Read("[FLog::Network] Sending disconnect with reason: 277");
        Check(state.Read(disconnected) && !state.Read(disconnected), "network disconnect schedules one reconnect rather than repeating it");
        foreach (string reason in new[] { "0", "267", "279" }) { state.Read(joined); state.Read(confirmed); state.Read("[FLog::Network] Sending disconnect with reason: " + reason); Check(!state.Read(disconnected), "manual/kick/failed-join reason does not reconnect: " + reason); }
        state.Read(joined); state.Read(confirmed); state.Read("[FLog::SingleSurfaceApp] leaveUGCGameInternal"); state.Read("[FLog::Network] Sending disconnect with reason: 277");
        Check(!state.Read(disconnected), "intentional leave suppresses automatic reconnect");
        state.Read("[FLog::UgcExperienceController] UgcExperienceController: doTeleport: joinScriptUrl JoinTypeId\"%3a4%2c"); state.Read(joined); state.Read(confirmed); state.Read("[FLog::Network] Sending disconnect with reason: 277");
        Check(!state.Read(disconnected), "reserved/private teleport cannot reconnect into a public server");
        var now = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc); App.State.Prop.AutoReconnectWindowUtc = default; App.State.Prop.AutoReconnectAttempts = 0;
        Check(RobloxSession.TryReserveReconnect(now) && RobloxSession.TryReserveReconnect(now) && RobloxSession.TryReserveReconnect(now) && !RobloxSession.TryReserveReconnect(now), "automatic reconnect is bounded to three attempts in five minutes");
        Check(RobloxSession.FormatTitle("Jujutsu Shenanigans") == "Roblox - Jujutsu Shenanigans", "window title retains the game name without FPS telemetry");
    }
    private static void RenderRevision6()
    {
        App.FastFlags.Prop.Clear();
        App.Settings.Prop.EnableRoValra = false; App.Settings.Prop.DisableAnimations = true; App.ApplyAnimationSettings();
        App.RemoteData.LoadedState = GenericTriState.Successful; App.RemoteData.Prop.CommunityMods = FixtureMods();
        var installed = App.RemoteData.Prop.CommunityMods[0];
        Directory.CreateDirectory(Path.Combine(Paths.Modifications, installed.Name));
        App.State.Prop.Mods.Add(new ModConfig { FolderName = installed.Name, CommunityModId = installed.Id, Enabled = true });
        foreach (var theme in new[] { Theme.Dark, Theme.Light })
        {
            App.Settings.Prop.Theme = theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
            var window = new SettingsWindow(false); window.Show(); RunFor(150);
            var owner = (MainWindowViewModel)window.DataContext!; var home = (HomeViewModel)owner.CurrentPage!;
            WaitFor(() => !home.Games.IsLoading, 4000);
            home.Games.RecentGames.Clear(); home.Games.RecommendedGames.Clear();
            home.Games.RecentGames.Add(new QuickPlayGameItem { PlaceId = 600601, Name = "Continue fixture" }); home.Games.RecommendedGames.Add(new QuickPlayGameItem { PlaceId = 600602, Name = "Recommended fixture" });
            RunFor(100); Capture(window, theme, "revision6-home");
            var page = window.GetVisualDescendants().OfType<Froststrap.UI.Elements.Settings.Pages.HomePage>().Single();
            var search = page.GetVisualDescendants().OfType<TextBox>().Single(box => box.PlaceholderText == "Search your games");
            var pins = page.FindControl<ArtworkScrollViewer>("PinnedScroller")!;
            var recommended = page.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Recommended");
            Check(search.Bounds.Height == 32 && search.Bounds.Width > 600 && search.VerticalContentAlignment == Avalonia.Layout.VerticalAlignment.Center, "Home search is slim, long and vertically centered: " + theme);
            Check(pins.TranslatePoint(default, page)!.Value.Y > recommended.TranslatePoint(default, page)!.Value.Y, "pinned games render below Recommended: " + theme);
            pins.BringIntoView(); RunFor(50); Capture(window, theme, "revision6-home-pins-bottom");
            owner.NavigateRegistered("fastflags"); RunFor(100); Capture(window, theme, "revision6-mesh");
            foreach (var group in window.GetVisualDescendants().OfType<CardExpander>().ToArray())
            {
                group.IsExpanded = true; group.BringIntoView(); RunFor(70); Capture(window, theme, "revision6-" + group.Header?.ToString()?.Replace(' ', '-').Replace('&', '-'));
                var editor = group.GetVisualDescendants().OfType<OptionControl>().FirstOrDefault();
                if (editor != null) { editor.BringIntoView(); RunFor(50); Capture(window, theme, "revision6-" + group.Header?.ToString()?.Replace(' ', '-').Replace('&', '-') + "-controls"); }
            }
            var renderOverride = window.GetVisualDescendants().OfType<OptionControl>().Single(control => control.Header == "Override Graphics Quality level");
            renderOverride.BringIntoView(); RunFor(70); Capture(window, theme, "revision6-render-distance-override");
            window.Width = 800; window.SetRenderScaling(1.5); RunFor(100); Capture(window, theme, "revision6-tweaks-narrow-150"); VerifyCardPanels(window, "revision6-tweaks-narrow-150 " + theme);
            window.SetRenderScaling(1); window.Width = 1125;
            owner.NavigateRegistered("mods"); RunFor(150); Capture(window, theme, "revision6-texture");
            Check(!window.GetVisualDescendants().OfType<Button>().Any(button => Equals(button.Content, "Manage installed")), "Texture Editor omits Manage installed: " + theme);
            var trash = window.GetVisualDescendants().OfType<Button>().First(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Uninstall mod");
            trash.BringIntoView(); RunFor(80); Capture(window, theme, "revision6-installed-controls");
            Check(trash.IsEffectivelyVisible && trash.IsEffectivelyEnabled && window.GetVisualDescendants().OfType<Button>().Any(button => Equals(button.Content, "Disable") && button.IsEffectivelyVisible), "installed Texture Editor cards expose real Disable and trash controls: " + theme);
            Check(trash.GetVisualDescendants().OfType<LucideAvalonia.Lucide>().Single().IconSource != null, "uninstall button has a resolved trash-can glyph: " + theme);
            Check(App.FastFlags.Prop.Count == 0, "opening customization pages leaves all visual overrides opt-in: " + theme);
            window.Hide();
        }
    }
}
