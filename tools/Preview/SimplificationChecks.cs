// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.IO.Compression;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Froststrap;
using Froststrap.Enums;
using Froststrap.Models;
using Froststrap.Models.APIs.Config;
using Froststrap.Services;
using Froststrap.UI.Elements.Controls;
using Froststrap.UI.Utility;
using Froststrap.UI.ViewModels.Settings;
using Froststrap.UI.ViewModels.Settings.Mods;
using SettingsWindow = Froststrap.UI.Elements.Settings.MainWindow;

internal static partial class Program
{
    private static readonly List<object> RevisionFrames = [];
    private static void Await(Task task)
    { var end = DateTime.UtcNow.AddSeconds(12); while (!task.IsCompleted && DateTime.UtcNow < end) RunFor(10); if (!task.IsCompleted) throw new TimeoutException("An isolated check did not complete."); task.GetAwaiter().GetResult(); }
    private static void RecordFrame(Window window, string stream, int frame)
    {
        string relative = $"temporal-frames/{stream}-{frame:D2}.png";
        Directory.CreateDirectory(Path.Combine(_output, "temporal-frames"));
        using var bitmap = window.GetLastRenderedFrame() ?? throw new InvalidOperationException("No temporal frame.");
        bitmap.Save(Path.Combine(_output, relative), PngBitmapEncoderOptions.Default);
        Captures.Add(new { fileName = relative, theme = "Dark", width = bitmap.PixelSize.Width, height = bitmap.PixelSize.Height, actualViewType = window.GetType().FullName, kind = "temporal" });
        RevisionFrames.Add(new { stream, file = relative, elapsedMilliseconds = (_manualTime?.GetTimestamp() ?? 0) / TimeSpan.TicksPerMillisecond });
    }
    private static List<CommunityMod> FixtureMods() => Enumerable.Range(1, 9).Select(i => new CommunityMod
    {
        Id = "preview-" + i, Name = new[] { "Amber cursor", "Morning sky", "Cocoa palette", "Quiet textures", "Cream cursor", "Evening sky", "Warm accents", "Clean textures", "Espresso palette" }[i - 1],
        Author = "Preview fixture", Description = "Offline catalog fixture for the real community card, including image fallback and download states.",
        DownloadUrl = "https://example.invalid/fixture.zip", ThumbnailUrl = "", ModType = ModType.ColorMod
    }).ToList();
    private static void VerifySimplification()
    {
        var savedSettings = JsonSerializer.Serialize(App.Settings.Prop);
        var savedState = JsonSerializer.Serialize(App.State.Prop);
        App.RemoteData.LoadedState = GenericTriState.Successful;
        App.RemoteData.Prop.CommunityMods = FixtureMods();
        Check(Path.GetFullPath(GBSEditor.FileLocation).StartsWith(Path.Combine(_output, "fixture-data") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            "native settings mutation checks are confined to fixture paths");
        App.GlobalSettings.CreateTemplate(); App.GlobalSettings.Load();
        var native = new GlobalSettingsViewModel();
        string? volume = App.GlobalSettings.GetPreset("Audio.MasterVolumeStudio");
        native.GraphicsQuality = 5; native.FramerateCap = 120; App.GlobalSettings.Save(); App.GlobalSettings.Load();
        Check(native.GraphicsQuality == 5 && native.FramerateCap == 120 && App.GlobalSettings.GetPreset("Audio.MasterVolumeStudio") == volume,
            "saved graphics quality and frame rate persist without changing Studio audio");
        App.GlobalSettings.SetReadOnly(true); Check(GBSEditor.GetReadOnly(), "Prevent Override sets the real fixture file read-only attribute");
        App.GlobalSettings.SetReadOnly(false); Check(!GBSEditor.GetReadOnly(), "Prevent Override restores fixture file writability");
        VerifyCustomizationRestoration();
        var legacy = JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>("{\"EnableActivityTracking\":true,\"AutoRejoin\":true,\"ShowUsingFroststrapRPC\":true,\"UseDiscordRichPresence\":true,\"StudioRPC\":true,\"EnableBetterMatchmaking\":true}")!;
        Check(!legacy.EnableActivityTracking && !legacy.AutoRejoin && !legacy.ShowUsingFroststrapRPC && !legacy.UseDiscordRichPresence && !legacy.StudioRPC && !legacy.EnableBetterMatchmaking,
            "removed integrations and automatic region provider stay inert after loading legacy settings");
        Check(legacy.UpdateChecks == UpdateCheck.Disabled && legacy.DoubleClickAction == TrayDoubleClickAction.None && legacy.CustomIntegrations.Count == 0,
            "removed update, double-click and executable integrations have no active legacy behavior");
        var game = new QuickPlayGameItem { PlaceId = 1818, UniverseId = 1414, Name = "Pinned fixture" };
        game.IsPinned = true; game.IsPinned = true;
        Check(App.State.Prop.QuickLaunchPins.Count(p => p.PlaceId == 1818) == 1, "pinning is persistent and does not create duplicates");
        App.State.Save(); App.State.Prop.QuickLaunchPins.Clear(); App.State.Load(false);
        Check(game.IsPinned, "pin survives production state save and reload in fixture paths");
        game.IsPinned = false;
        Check(!App.State.Prop.QuickLaunchPins.Any(p => p.PlaceId == game.PlaceId), "unpin removes the main-menu shortcut");
        Uri? opened = null;
        var launches = new GameLaunchService(uri => opened = uri);
        launches.Launch(1818, "Last launched fixture");
        Check(opened?.Query == "?placeId=1818" && App.State.Prop.LastPlaceId == 1818, "explicit launch records minimal last-place state without activity tracking");
        var reconnect = new HomeViewModel(launches, null, null, null); reconnect.ReconnectCommand.Execute(null);
        Check(opened?.AbsoluteUri == "roblox://experiences/start?placeId=1818" && !opened.Query.Contains("gameInstanceId"), "Reconnect is user-initiated same-place matchmaking without a same-instance claim");
        var lastPlaces = new List<long>();
        Await(LastPlaceRecorder.ReadAvailableAsync(new StringReader("[FLog::Output] ! Joining game '00000000-0000-0000-0000-000000000000' place 2121 at 203.0.113.10\nignored heartbeat place 999\n[FLog::Output] ! Joining game '00000000-0000-0000-0000-000000000000' place 2323 at 203.0.113.11"), lastPlaces.Add));
        Check(lastPlaces.SequenceEqual(new long[] { 2121, 2323 }) && LastPlaceRecorder.ParsePlace("unrelated place 999") == null,
            "minimal local-log recorder recognizes joins and teleports without recording unrelated activity");
        GameLaunchService.RecordPlace(lastPlaces.Last());
        Check(App.State.Prop.LastPlaceId == 2323 && !File.ReadAllText(App.State.FileLocation).Contains("203.0.113"), "reconnect state persists one place without retaining server IPs or a session history");
        string job = Guid.NewGuid().ToString("D");
        Check(GameLaunchService.BuildUri(123, job).Query.Contains(job), "explicit server-instance join remains intact");
        Check(GameLaunchService.BuildUri(123, job, "private code").Query.Contains("accessCode=private%20code") && !GameLaunchService.BuildUri(123, job, "private code").Query.Contains("gameInstanceId"), "private access code takes precedence over public instance selection");
        Check(GameLaunchService.BuildUri(123, linkCode: "reserved-link").Query.Contains("linkCode=reserved-link"), "explicit reserved/private link remains intact");
        var ownLaunch = LaunchEntry.CreateStartInfo(LaunchMode.Player, GameLaunchService.BuildUri(123, job).AbsoluteUri);
        Check(ownLaunch.FileName == Paths.Process && !ownLaunch.UseShellExecute && ownLaunch.ArgumentList.SequenceEqual(new[] { "--player", GameLaunchService.BuildUri(123, job).AbsoluteUri }),
            "quick play and reconnect invoke this CoffeeStrap executable without changing or relying on a default handler");
        Check(reconnect.Regions.SequenceEqual(new[] { "Auto" }) && !reconnect.HasUpdate && CoffeeReleasePolicy.VerifiedDestination == null,
            "Auto and update UI do not advertise an unavailable region provider or invented CoffeeStrap release");
        Check(!CoffeeReleasePolicy.CanShow(new Version(1, 0), new CoffeeRelease(new Version(2, 0), "Froststrap", "win-x64", new Uri("https://example.invalid"), true), new Uri("https://example.invalid")), "upstream release cannot produce a CoffeeStrap update warning");
        Check(CuratedFlagRegistry.Allowed.Count == 18, "central registry contains the 18 published allowlist keys");
        var accepted = CuratedFlagRegistry.Filter(new Dictionary<string, object> { ["FFlagDebugSkyGray"] = true, ["FIntDebugForceMSAASamples"] = "4", ["FFlagUnsupportedFixture"] = true, ["DFIntTextureQualityOverride"] = "999" }, out var held);
        Check(accepted.Count == 2 && held.Count == 2, "unknown keys and out-of-preset values are held rather than applied");
        CuratedFlagRegistry.PreserveHeld(held);
        Check(Directory.GetFiles(Path.Combine(Paths.Base, "FlagBackups"), "*.json").Any(file => File.ReadAllText(file).Contains("FFlagUnsupportedFixture")), "held overrides remain recoverable in local backup files");
        var flags = new CuratedFastFlagsViewModel(); var msaa = flags.Settings.First();
        msaa.Selected = msaa.Choices.Last(); Check(App.FastFlags.GetValue("FIntDebugForceMSAASamples") == "4", "curated selection writes the supported preset");
        msaa.Selected = msaa.Choices.First(); Check(App.FastFlags.GetValue("FIntDebugForceMSAASamples") == null, "Roblox default removes the override instead of inserting a guessed value");
        VerifyModTransactions();
        App.Settings.Prop.DisableAnimations = false; App.RemoveAnimationSettings();
        App.Settings.Prop.Theme = Theme.Dark; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
        var window = new SettingsWindow(false); window.Show(); RunFor(350);
        var model = (MainWindowViewModel)window.DataContext!;
        Check(model.IsHome && window.GetVisualDescendants().OfType<Froststrap.UI.Elements.Settings.Pages.HomePage>().Any(), "normal shell starts on the real Home view");
        var home = (HomeViewModel)model.CurrentPage!; home.SettingsCommand.Execute(null); RunFor(300);
        Check(model.SelectedPage == "behaviour" && ReferenceEquals(TopLevel.GetTopLevel(window), window), "Home Settings action navigates in the same window");
        var nav = window.FindControl<CoffeeNavigationView>("NavView")!;
        Check(!nav.MenuItems.Any(item => new[] { "integrations", "appearance", "channels", "regionselector" }.Contains(item.Tag as string)), "removed tabs are absent from active navigation");
        var card = window.GetVisualDescendants().OfType<OptionControl>().First(option => option.Toggle != null && option.IsEffectivelyEnabled);
        int changes = 0; card.Toggle!.PropertyChanged += (_, e) => { if (e.Property == Avalonia.Controls.Primitives.ToggleButton.IsCheckedProperty) changes++; };
        Click(window, At(card, window, .4, .8)); Check(changes == 1, "card click toggles the retained setting once");
        var track = card.Toggle.GetVisualDescendants().OfType<Border>().First(border => border.Name == "OuterBorder");
        Click(window, At(track, window)); Check(changes == 2, "switch click does not double-toggle its card");
        card.Focus(NavigationMethod.Tab); window.KeyPress(Key.Space, RawInputModifiers.None, default, null); window.KeyRelease(Key.Space, RawInputModifiers.None, default, null);
        Check(changes == 3 && card.IsFocused, "retained settings card remains keyboard operable");
        window.StartIndexingAllPagesIfNeeded(); WaitFor(() => !model.SearchBar.IsIndexing && model.SearchBar.GetSearchIndex().Count > 10, 8000);
        Check(model.SearchBar.GetSearchIndex().All(item => !new[] { "integrations", "appearance", "channels", "regionselector" }.Contains(item.PageTag)), "search has no stale entries for removed tabs");
        var result = model.SearchBar.GetSearchIndex().FirstOrDefault(item => item.DisplayName == "Anti-aliasing");
        Check(result != null, "inline search indexes actual curated setting controls");
        model.SearchBar.SearchResultSelectedCommand.Execute(result); RunFor(650);
        Check(model.SelectedPage == "fastflags" && window.LastSearchFocusTarget is ComboBox { IsFocused: true }, "search navigates, reveals and focuses the actual enum selector");
        VerifyPinInput(window, model);
        VerifyRevisionMotion(window, model);
        VerifyRevision5();
        window.Hide();
        App.Settings.Prop = JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(savedSettings)!;
        App.State.Prop = JsonSerializer.Deserialize<Froststrap.Models.Persistable.State>(savedState)!;
        App.Settings.Prop.DisableAnimations = false; App.RemoveAnimationSettings();
        RenderSimplifiedViews();
        File.WriteAllText(Path.Combine(_output, "temporal-recordings.json"), JsonSerializer.Serialize(RevisionFrames, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void VerifyCustomizationRestoration()
    {
        string folder = Path.Combine(Paths.Modifications, "content", "textures", "Cursors", "KeyboardMouse"); Directory.CreateDirectory(folder);
        using var asset = Avalonia.Platform.AssetLoader.Open(new Uri("avares://CoffeeStrap/CoffeeStrap.png"));
        using var bytes = new MemoryStream(); asset.CopyTo(bytes); byte[] image = bytes.ToArray();
        foreach (string name in new[] { "ArrowCursor.png", "ArrowFarCursor.png", "IBeamCursor.png" }) File.WriteAllBytes(Path.Combine(folder, name), image);
        using var presets = new ModsPresetsViewModel();
        presets.CursorTypeTask.OriginalState = CursorType.From2013; presets.CursorTypeTask.NewState = CursorType.From2006;
        presets.CursorTypeTask.AcceptCustomFiles(); presets.CursorTypeTask.Execute();
        Check(presets.CursorTypeTask.NewState == CursorType.Custom && File.ReadAllBytes(Path.Combine(folder, "ArrowCursor.png")).SequenceEqual(image),
            "custom cursor selection supersedes a pending preset without being overwritten on Save");
        presets.CursorTypeTask.NewState = CursorType.PurpleCross; presets.CursorTypeTask.Execute();
        Check(!File.ReadAllBytes(Path.Combine(folder, "ArrowCursor.png")).SequenceEqual(image), "choosing a built-in cursor after custom installs the real preset assets");
        presets.CursorTypeTask.NewState = CursorType.Default; presets.CursorTypeTask.Execute();
        Check(!Directory.GetFiles(folder, "*.png").Any(), "cursor default restores all cursor slots, including I-beam");
        App.PendingSettingTasks.Clear();
        string mobile = Path.Combine(Paths.Modifications, "ExtraContent", "places", "Mobile.rbxl"); Directory.CreateDirectory(Path.GetDirectoryName(mobile)!);
        File.WriteAllText(mobile, "user customization"); RetiredPresets.RemoveAvatarBackground();
        Check(File.ReadAllText(mobile) == "user customization", "removing the avatar preset preserves unrelated user place files");
    }
    private static void VerifyPinInput(SettingsWindow window, MainWindowViewModel model)
    {
        model.NavigateRegistered("quickplay"); RunFor(350); var games = (QuickPlayViewModel)model.CurrentPage!;
        games.RecentGames.Clear(); var game = new QuickPlayGameItem { PlaceId = 4545, Name = "Pointer pin fixture" }; games.RecentGames.Add(game); RunFor(80);
        var star = window.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First(button => ReferenceEquals(button.DataContext, game));
        star.BringIntoView(); RunFor(60); var point = At(star, window);
        Check(window.InputHitTest(point) is Avalonia.Visual hit && hit.GetSelfAndVisualAncestors().Contains(star), "pin pointer coordinates hit the real star control");
        Click(window, point); Check(game.IsPinned, "hollow star pointer input pins the game without launching it");
        Click(window, point); Check(!game.IsPinned, "second star click unpins the game once");
    }
    private static void VerifyModTransactions()
    {
        string zip = Path.Combine(_output, "fixture-data", "sample-mod.zip");
        using (var archive = new ZipArchive(File.Open(zip, FileMode.Create), ZipArchiveMode.Create))
        { using var writer = new StreamWriter(archive.CreateEntry("content/textures/fixture.txt").Open()); writer.Write("fixture"); }
        var mod = App.RemoteData.Prop.CommunityMods![0];
        using var catalog = new CommunityModsViewModel((_, destination, progress) => { File.Copy(zip, destination, true); progress.Report(100); return Task.CompletedTask; });
        Await(catalog.RefreshModsAsync()); Await(catalog.DownloadModAsync(mod));
        Check(mod.IsInstalled && mod.DownloadLabel == "Installed" && File.Exists(Path.Combine(Paths.Modifications, mod.Name, "content", "textures", "fixture.txt")), "real archive installation updates files, persisted mod identity and installed state");
        Check(catalog.Mods.First().Id == mod.Id, "installed catalog cards sort before downloads");
        var failed = App.RemoteData.Prop.CommunityMods![1];
        using var failing = new CommunityModsViewModel((_, _, _) => Task.FromException(new IOException("fixture transport failure")));
        Await(failing.DownloadModAsync(failed));
        Check(!failed.IsInstalled && failed.CanDownload && failed.DownloadLabel == "Retry" && !string.IsNullOrEmpty(failed.DownloadError), "download failure preserves a usable retry state");
        string protectedFolder = Path.Combine(Paths.Modifications, "retained-fixture"); Directory.CreateDirectory(protectedFolder);
        File.WriteAllText(Path.Combine(protectedFolder, "original.txt"), "keep");
        string broken = Path.Combine(_output, "fixture-data", "broken.zip"); File.WriteAllText(broken, "not a ZIP");
        bool rejected = false; try { Await(ModArchiveInstaller.InstallAsync(broken, protectedFolder)); } catch (InvalidDataException) { rejected = true; }
        Check(rejected && File.ReadAllText(Path.Combine(protectedFolder, "original.txt")) == "keep", "invalid archive cannot delete an existing mod before validation");
    }
    private static void VerifyRevisionMotion(SettingsWindow window, MainWindowViewModel model)
    {
        _manualTime = new ManualMotionTime(); CoffeeMotion.TimeSource = _manualTime;
        var holder = new Grid(); var old = new Border(); var next = new Border(); holder.Children.Add(old); holder.Children.Add(next);
        var animationWindow = new Window { Width = 600, Height = 400, Content = holder }; animationWindow.Show(); RunFor(80);
        var transition = new CoffeePageTransition(); using var cancel = new CancellationTokenSource();
        Task moving = transition.Start(old, next, true, cancel.Token);
        var samples = new List<double>();
        for (int i = 0; i < 8; i++) { StepFrame(16); samples.Add(next.Opacity); }
        Check(samples.Distinct().Count() > 3 && samples.Any(value => value > .3 && value < 1) && old.IsVisible, "page transition renders concurrent intermediate frames without a blank interval");
        cancel.Cancel(); Await(moving); Task reversed = transition.Start(next, old, false, CancellationToken.None);
        for (int i = 0; i < 16; i++) StepFrame(16);
        Await(reversed); Check(old.IsVisible && old.Opacity == 1 && !next.IsVisible, "interrupted page transition reverses and cleans up its endpoint");
        App.Settings.Prop.DisableAnimations = true; Await(transition.Start(old, next, true, CancellationToken.None));
        Check(next.IsVisible && next.Opacity == 1 && !old.IsVisible, "reduced motion switches pages immediately with a visible target");
        App.Settings.Prop.DisableAnimations = false; animationWindow.Hide();
        var shell = new SettingsWindow(false); shell.Show(); var shellModel = (MainWindowViewModel)shell.DataContext!; RunFor(300);
        shellModel.NavigateRegistered("fastflags");
        for (int i = 0; i < 12; i++) { StepFrame(16); RecordFrame(shell, "page-transition", i); }
        RunFor(300);
        var group = shell.GetVisualDescendants().OfType<CardExpander>().First(expander => Equals(expander.Header, "Roblox graphics"));
        group.IsExpanded = true; RunFor(300);
        var reveal = group.GetVisualDescendants().OfType<AnimatedReveal>().Single(); double height = reveal.RevealedHeight;
        group.IsExpanded = false; var heights = new List<double>();
        for (int i = 0; i < 10; i++) { StepFrame(16); heights.Add(reveal.RevealedHeight); RecordFrame(shell, "group-collapse", i); Check(reveal.RevealedHeight < .1 || reveal.Opacity > 0, "saved graphics children remain visible throughout collapse " + i); }
        Check(heights.Any(value => value > 1 && value < height - 1), "actual Game Tweaks group has intermediate clipped heights");
        for (int i = 0; i < 8; i++) { double before = reveal.RevealedHeight; group.IsExpanded = !group.IsExpanded; Check(Math.Abs(reveal.RevealedHeight - before) < 1, "rapid graphics group reversal preserves displayed height " + i); StepFrame(20); }
        group.IsExpanded = false; RunFor(300);
        shell.StartIndexingAllPagesIfNeeded(); WaitFor(() => !shellModel.SearchBar.IsIndexing && shellModel.SearchBar.GetSearchIndex().Any(item => item.DisplayName == "Frame rate limit (saved)"), 8000);
        var search = shell.FindControl<InlineSettingsSearch>("SettingsSearch")!;
        search.FindControl<TextBox>("SearchField")!.Focus(NavigationMethod.Tab); shell.KeyTextInput("Frame rate limit"); RunFor(300);
        Check(shellModel.SearchBar.IsDropDownOpen && shellModel.SearchBar.FilteredSearchResults.Any(), "inline query opens the actual dropdown results");
        CapturePopup(search, Theme.Dark, "inline-settings-results");
        var nested = shellModel.SearchBar.GetSearchIndex().First(item => item.PageTag == "fastflags" && item.DisplayName == "Frame rate limit (saved)");
        var popup = search.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Popup>().First(item => item.IsOpen);
        var resultRow = popup.Child!.GetVisualDescendants().OfType<ListBoxItem>().First(row => ReferenceEquals(row.DataContext, nested));
        Check(resultRow.IsEffectivelyEnabled, "rendered dropdown result exposes a selectable real setting");
        ClickSearchPopup(resultRow); RunFor(600);
        Check(group.IsExpanded && shell.LastSearchFocusTarget is TextBox { IsFocused: true }, "search reveals a collapsed real graphics group and focuses its saved frame-rate field", new { group.IsExpanded, target = shell.LastSearchFocusTarget?.GetType().Name, focused = shell.LastSearchFocusTarget?.IsFocused, shellModel.SelectedPage });
        var scroller = shell.FindControl<TransitioningContentControl>("PageContentControl")!.GetVisualDescendants().OfType<SmoothScrollViewer>().First();
        scroller.Offset = default; RunFor(50);
        var firstSetting = shell.GetVisualDescendants().OfType<OptionControl>().First(card => card.Header == "Mesh Detail");
        shell.MouseWheel(At(firstSetting, shell, .3, .15), new Vector(0, -2), RawInputModifiers.None);
        double destination = scroller.TargetOffset; StepFrame(16);
        Check(destination > 0 && scroller.Offset.Y > 0 && scroller.Offset.Y < destination, "smooth scrolling retains actual intermediate offsets in the redesigned page");
        for (int i = 0; i < 8; i++) shell.MouseWheel(At(firstSetting, shell, .3, .15), new Vector(0, -.25), RawInputModifiers.None);
        double accumulated = scroller.TargetOffset; shell.MouseWheel(At(firstSetting, shell, .3, .15), new Vector(0, 1), RawInputModifiers.None);
        Check(scroller.TargetOffset < accumulated, "fractional scrolling accumulates and immediately reverses");
        WaitFor(() => !scroller.HasActiveMotion); int ticks = scroller.AnimationTicks; RunFor(80);
        Check(scroller.AnimationTicks == ticks, "redesigned page scroll motion stops all idle work");
        firstSetting = shell.GetVisualDescendants().OfType<OptionControl>().First(card => card.Header == "Anti-aliasing");
        firstSetting.Choice!.Focus(NavigationMethod.Tab); firstSetting.BringIntoView(); RunFor(100);
        double beforeComboWheel = scroller.Offset.Y;
        shell.MouseWheel(At(firstSetting.Choice!, shell), new Vector(0, -1), RawInputModifiers.None); RunFor(100);
        Check(scroller.Offset.Y == beforeComboWheel && !scroller.HasActiveMotion, "dropdown wheel input does not scroll the redesigned page",
            new { beforeComboWheel, scroller.Offset, scroller.HasActiveMotion, point = At(firstSetting.Choice!, shell), hit = shell.InputHitTest(At(firstSetting.Choice!, shell))?.GetType().Name });
        var footer = shell.GetVisualDescendants().OfType<Button>().First(button => ReferenceEquals(button.Command, shellModel.SaveSettingsCommand)); footer.Focus(); RunFor(100);
        var navigation = shell.FindControl<CoffeeNavigationView>("NavView")!; navigation.IsPaneOpen = false; RunFor(300);
        var pane = navigation.GetVisualDescendants().OfType<Border>().First(border => border.Name == "PART_Pane");
        var area = navigation.GetVisualDescendants().OfType<Border>().First(border => border.Name == "PART_FeatureArea");
        var items = navigation.MenuItems.ToArray(); shell.MouseMove(At(items[0], shell, .1, .5), RawInputModifiers.None);
        var widths = new List<double>();
        for (int i = 0; i < 12; i++)
        {
            StepFrame(16); widths.Add(navigation.CurrentPaneLength); RecordFrame(shell, "sidebar-reflow", i);
            Check(pane.TranslatePoint(new Point(pane.Bounds.Width, 0), navigation)!.Value.X <= area.TranslatePoint(default, navigation)!.Value.X + .5,
                "sidebar and settings remain separated during reflow " + i);
        }
        Check(widths.Distinct().Count() > 3, "real sidebar renders intermediate reflow widths");
        var positions = new List<double>();
        for (int i = 0; i < 8; i++) { double before = navigation.HighlightPosition; shell.MouseMove(At(items[i % 2 + 1], shell), RawInputModifiers.None); Check(Math.Abs(navigation.HighlightPosition - before) < 1, "rapid hover retarget does not snap " + i); StepFrame(20); positions.Add(navigation.HighlightPosition); RecordFrame(shell, "hover-reversal", i); }
        Check(positions.Distinct().Count() > 3, "hover highlight renders intermediate reversal positions");
        var edge = pane.TranslatePoint(new Point(pane.Bounds.Width + 6, pane.Bounds.Height * .4), shell)!.Value;
        shell.MouseMove(edge, RawInputModifiers.None); RunFor(120); WaitFor(() => !navigation.HasActiveMotion);
        Check(!navigation.IsPaneOpen, "navigation collapses six pixels beyond the actual boundary");
        shell.Hide();
        CoffeeMotion.TimeSource = TimeProvider.System; _manualTime = null;
        foreach (double width in new[] { 750d, 800d, 1125d, 1280d })
        { window.Width = width; RunFor(90); VerifyCardPanels(window, "simplified settings at " + width); }
        foreach (double scale in new[] { 1.25, 1.5 })
        { window.SetRenderScaling(scale); RunFor(60); VerifyCardPanels(window, "simplified settings DPI " + scale); }
        window.SetRenderScaling(1);
    }
    private static void RenderSimplifiedViews()
    {
        App.State.Prop.QuickLaunchPins = [new QuickLaunchPin { PlaceId = 1818, Name = "Pinned experience" }, new QuickLaunchPin { PlaceId = 1919, Name = "Another experience" }];
        App.State.Prop.LastPlaceId = 1818; App.State.Prop.LastGameName = "Last launched experience";
        App.State.Prop.Mods.Add(new ModConfig { CommunityModId = "preview-1", FolderName = "Amber cursor", Enabled = true });
        App.State.Save();
        foreach (var theme in new[] { Theme.Dark, Theme.Light })
        {
            App.Settings.Prop.Theme = theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
            var window = new SettingsWindow(false); window.Show(); var model = (MainWindowViewModel)window.DataContext!;
            foreach (var route in NavigationRegistry.Pages.Where(page => page.Sidebar))
            {
                model.NavigateRegistered(route.Tag); RunFor(350);
                if (model.CurrentPage is QuickPlayViewModel games)
                {
                    games.RecentGames.Clear();
                    for (int i = 1; i < 7; i++) games.RecentGames.Add(new QuickPlayGameItem { PlaceId = 1817 + i, Name = "Experience " + i, Creator = "Offline fixture" });
                }
                Capture(window, theme, route.Tag); VerifyCardPanels(window, theme + " " + route.Tag);
                var pageHost = window.FindControl<TransitioningContentControl>("PageContentControl")!;
                var pageViews = pageHost.GetVisualDescendants().OfType<UserControl>().Where(view => view.GetType().Namespace == "Froststrap.UI.Elements.Settings.Pages").ToArray();
                Check(pageViews.Count(view => view.GetSelfAndVisualAncestors().All(parent => parent.IsVisible && parent.Opacity > 0)) == 1,
                    "settled navigation leaves exactly one visible page: " + theme + " " + route.Tag);
                if (model.CurrentPage is HomeViewModel home)
                {
                    WaitFor(() => !home.Games.IsLoading, 4000);
                    for (int i = 1; i <= 4; i++) home.Games.RecentGames.Add(new QuickPlayGameItem { PlaceId = 50000 + i, Name = "Continue fixture " + i });
                    for (int i = 1; i <= 4; i++) home.Games.RecommendedGames.Add(new QuickPlayGameItem { PlaceId = 60000 + i, Name = "Recommended fixture " + i });
                    Capture(window, theme, "home-fixture-games");
                }
                var scroll = window.FindControl<TransitioningContentControl>("PageContentControl")?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(view => view.ScrollBarMaximum.Y > 0);
                if (scroll != null) { scroll.Offset = new Vector(0, scroll.ScrollBarMaximum.Y); Capture(window, theme, route.Tag + "-bottom"); }
            }
            model.NavigateRegistered("mods"); RunFor(350);
            var nav = window.FindControl<CoffeeNavigationView>("NavView")!; nav.IsPaneOpen = true; RunFor(300); Capture(window, theme, "texture-sidebar-expanded");
            window.Width = 800; Capture(window, theme, "texture-narrow");
            foreach (double dpi in new[] { 1.25, 1.5 })
            {
                window.SetRenderScaling(dpi); Capture(window, theme, "texture-narrow-dpi-" + dpi);
                foreach (var card in window.GetVisualDescendants().OfType<OptionControl>().Where(card => card.Classes.Contains("stacked")))
                {
                    var editor = card.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().FirstOrDefault(control => control.Name == "PART_Editor");
                    Check(editor != null && editor.Bounds.Right <= card.Bounds.Width + 1, "stacked editor fits scaled card: " + theme + " " + dpi + " " + card.Header);
                }
            }
            window.SetRenderScaling(1);
            window.Width = 1280; nav.IsPaneOpen = false; RunFor(300);
            var community = window.GetVisualDescendants().OfType<Froststrap.UI.Elements.Settings.Pages.Mods.CommunityModsPage>().First(); community.BringIntoView();
            Capture(window, theme, "community-three-columns");
            model.NavigateRegistered("shortcuts"); window.Width = 1125; RunFor(300);
            foreach (double scale in new[] { 1.25, 1.5 }) { window.SetRenderScaling(scale); Capture(window, theme, "advanced-dpi-" + scale); }
            window.SetRenderScaling(1); window.Hide();
            RenderAuxiliary(theme);
        }
    }
}
