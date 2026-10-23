// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Reflection;
using System.Text.Json;
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
using Froststrap.UI.ViewModels.Settings;
using Froststrap.UI.Utility;
using SettingsWindow = Froststrap.UI.Elements.Settings.MainWindow;

internal static partial class Program
{
    private static void VerifyRevision17()
    {
        string previousProfiles = Paths.SavedFlagProfiles, settings = JsonSerializer.Serialize(App.Settings.Prop);
        var flags = App.FastFlags.Prop.ToDictionary(pair => pair.Key, pair => pair.Value);
        var mods = App.State.Prop.Mods;
        if (!File.Exists(App.PlayerState.FileLocation)) File.WriteAllText(App.PlayerState.FileLocation, "{}");
        var manifest = App.PlayerState.Prop.ModManifest.ToList();
        string root = Path.Combine(Paths.Base, "revision17"); Directory.CreateDirectory(root);
        typeof(Paths).GetProperty(nameof(Paths.SavedFlagProfiles))!.SetValue(null, Path.Combine(root, "profiles"));
        Directory.CreateDirectory(Paths.SavedFlagProfiles);
        try
        {
            App.Settings.Prop.ProfilePlaceIds = []; App.Settings.Save();
            App.FastFlags.Prop.Clear(); App.FastFlags.SetValue("DFIntDebugFRMQualityLevelOverride", "1");
            App.FastFlags.SetValue("DFIntTextureQualityOverride", "2"); App.FastFlags.SetValue("DFFlagTextureQualityOverrideEnabled", "True");
            App.FastFlags.SetValue("FFlagDebugGraphicsPreferD3D11", "True"); App.FastFlags.SetValue("DFFlagDisableDPIScale", "True");
            App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistance", "500"); App.FastFlags.Save();
            VerifyProfileFileBoundaries(root);
            VerifyPartialSettingsSave();
            VerifyExperienceLaunch(root);
            VerifyExperienceEmptyAndMotion(root);
            foreach (var theme in new[] { Theme.Dark, Theme.Light }) foreach (int width in new[] { 480, 760 }) RenderExperienceProfiles(theme, width);
        }
        finally
        {
            typeof(Paths).GetProperty(nameof(Paths.SavedFlagProfiles))!.SetValue(null, previousProfiles);
            App.Settings.Prop = JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(settings)!;
            App.FastFlags.Prop = flags; App.FastFlags.Save(); App.State.Prop.Mods = mods; App.PlayerState.Prop.ModManifest = manifest;
        }
    }
    private static void VerifyProfileFileBoundaries(string root)
    {
        foreach (string name in new[] { "../escape", @"C:\escape.json", "..", ".hidden", "CON.json", "bad:name", "trailing.", "trailing ", new string('x', 101) })
        {
            bool rejected = false; try { FlagProfileFiles.Resolve(name); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "profile resolver rejects paths, device names and ambiguous names: " + name);
        }
        Check(FlagProfileFiles.TryPlaceId("000123", out long id) && id == 123 && !FlagProfileFiles.TryPlaceId("0", out _) && !FlagProfileFiles.TryPlaceId("9223372036854775808", out _) && !FlagProfileFiles.TryPlaceId("-3", out _), "experience IDs normalize leading zeros and reject zero, negative and overflow input");
        FlagProfileFiles.SaveNew("Lean meshes.json", App.FastFlags.Prop);
        string path = FlagProfileFiles.Resolve("Lean meshes.json"), original = File.ReadAllText(path);
        bool collision = false; try { FlagProfileFiles.SaveNew("Lean meshes.json", App.FastFlags.Prop); } catch (IOException) { collision = true; }
        Check(collision && File.ReadAllText(path) == original, "saving a profile never overwrites an existing named snapshot");
        Check(AwaitResult(FlagProfileFiles.ReadAsync("Lean meshes.json", CancellationToken.None)).Count == App.FastFlags.Prop.Count, "saved supported snapshot round-trips through the bounded profile reader");
        string target = Path.Combine(root, "client", "ClientAppSettings.json"); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllText(target, "previous target");
        const long place = 12345;
        ProfileApplication Apply(string name)
        {
            App.Settings.Prop.ProfilePlaceIds = new() { [name] = [place.ToString()] };
            return AwaitResult(FlagProfileFiles.ApplyForPlaceAsync(place, target, CancellationToken.None));
        }
        File.WriteAllText(FlagProfileFiles.Resolve("Invalid.json"), "{broken");
        var invalid = Apply("Invalid.json");
        Check(invalid.Matched && !invalid.Applied && File.ReadAllText(target) == "previous target", "malformed profile leaves the previous output untouched and reports global fallback");
        using (var huge = File.Create(FlagProfileFiles.Resolve("Huge.json"))) huge.SetLength(FlagProfileFiles.MaximumBytes + 1);
        Check(!Apply("Huge.json").Applied && File.ReadAllText(target) == "previous target", "oversized profiles are refused before allocation or destination replacement");
        File.WriteAllText(FlagProfileFiles.Resolve("Unsupported.json"), "{\"DFIntTaskSchedulerTargetFps\":999}");
        Check(!Apply("Unsupported.json").Applied, "a wholly unsupported profile cannot erase selected global settings");
        Check(!Apply("Missing.json").Applied && !Apply("../escape").Applied, "missing or escaped mapped profiles fail with a recoverable result");
        File.WriteAllText(path, "{\"DFIntCSGLevelOfDetailSwitchingDistance\":3000,\"DFIntDebugFRMQualityLevelOverride\":21,\"FFlagDebugGraphicsPreferVulkan\":true,\"DFIntTaskSchedulerTargetFps\":999,\"FIntDebugForceMSAASamples\":8}");
        string before = File.ReadAllText(path);
        var applied = Apply("Lean meshes.json"); var accepted = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(target))!;
        Check(applied.Applied && accepted["DFIntCSGLevelOfDetailSwitchingDistance"] == "3000" && accepted["DFIntDebugFRMQualityLevelOverride"] == "1" && accepted["DFIntTextureQualityOverride"] == "2" && accepted["FFlagDebugGraphicsPreferD3D11"] == "True" && !accepted.ContainsKey("FFlagDebugGraphicsPreferVulkan"), "profile launch applies its mesh change while selected global effects, textures and renderer keep priority");
        Check(!accepted.ContainsKey("DFIntTaskSchedulerTargetFps") && !accepted.ContainsKey("FIntDebugForceMSAASamples") && File.ReadAllText(path) == before, "unsupported and invalid profile values never enter launch output and the source snapshot stays intact");
        File.WriteAllText(target, "locked original");
        using (var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
            Check(!Apply("Lean meshes.json").Applied && File.ReadAllText(target) == "locked original", "sharing-lock failure preserves the old destination and returns a retryable fallback");
        Check(Apply("Lean meshes.json").Applied, "profile write succeeds after a real destination lock is released");
        App.Settings.Prop.ProfilePlaceIds["Invalid.json"] = [place.ToString()];
        var conflict = AwaitResult(FlagProfileFiles.ApplyForPlaceAsync(place, target, CancellationToken.None));
        Check(!conflict.Applied && conflict.Message.Contains("Multiple", StringComparison.Ordinal), "ambiguous legacy assignments fall back instead of choosing dictionary order");
        App.Settings.Prop.ProfilePlaceIds.Remove("Invalid.json");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); bool stopped = false;
        try { Await(FlagProfileFiles.ApplyForPlaceAsync(place, target, cancelled.Token)); } catch (OperationCanceledException) { stopped = true; }
        Check(stopped && !Directory.EnumerateFiles(Path.GetDirectoryName(target)!, ".*.tmp").Any(), "cancelled profile work propagates cancellation and leaves no staged files");
        App.Settings.Prop.ProfilePlaceIds.Clear();
        App.Settings.Prop.ProfilePlaceIds = Enumerable.Range(0, 257).ToDictionary(index => "Profile" + index, _ => new List<string> { "12345" });
        Check(!AwaitResult(FlagProfileFiles.ApplyForPlaceAsync(place, target, CancellationToken.None)).Applied, "excessive assignment maps fail with a bounded global fallback");
        App.Settings.Prop.ProfilePlaceIds = new() { ["Lean meshes.json"] = Enumerable.Repeat("12345", 1025).ToList() };
        Check(!AwaitResult(FlagProfileFiles.ApplyForPlaceAsync(place, target, CancellationToken.None)).Applied, "oversized per-profile place lists are refused before matching");
        App.Settings.Prop.ProfilePlaceIds.Clear();
    }
    private static void VerifyPartialSettingsSave()
    {
        var manager = new JsonManager<Froststrap.Models.Persistable.Settings>("Revision17Partial"); manager.Save();
        bool distance = manager.Prop.LockMaximumRenderDistance; manager.Prop.LockMaximumRenderDistance = !distance;
        manager.Prop.ProfilePlaceIds["Snapshot"] = ["123"];
        Check(manager.TrySaveSetting(nameof(manager.Prop.ProfilePlaceIds)) && manager.HasUnsavedChanges, "saving assignments does not mark unrelated pending settings as saved");
        var loaded = new JsonManager<Froststrap.Models.Persistable.Settings>("Revision17Partial"); loaded.Load(false);
        Check(loaded.Prop.ProfilePlaceIds["Snapshot"].Single() == "123" && loaded.Prop.LockMaximumRenderDistance == distance, "partial settings save persists only its selected property");
        string original = File.ReadAllText(manager.FileLocation); manager.Prop.ProfilePlaceIds["Snapshot"] = ["456"];
        using (var locked = new FileStream(manager.FileLocation, FileMode.Open, FileAccess.Read, FileShare.Read))
            Check(!manager.TrySaveSetting(nameof(manager.Prop.ProfilePlaceIds)) && File.ReadAllText(manager.FileLocation) == original, "partial settings save reports sharing failure without truncating the existing document");
        Check(manager.TrySaveSetting(nameof(manager.Prop.ProfilePlaceIds)) && !Directory.EnumerateFiles(Paths.Base, ".Revision17Partial.json.*.tmp").Any(), "partial-save retry succeeds and removes its temporary file");
        File.WriteAllText(manager.FileLocation, "[]");
        Check(!manager.TrySaveSetting(nameof(manager.Prop.ProfilePlaceIds)) && File.ReadAllText(manager.FileLocation) == "[]", "a non-object settings document fails explicitly instead of silently replacing its content");
    }
    private static void VerifyExperienceLaunch(string root)
    {
        string player = Path.Combine(root, "player"); Directory.CreateDirectory(player);
        const long place = 777001;
        string name = "Experience launch.json";
        File.WriteAllText(FlagProfileFiles.Resolve(name), "{\"DFIntCSGLevelOfDetailSwitchingDistance\":3000,\"DFIntDebugFRMQualityLevelOverride\":21,\"DFIntTaskSchedulerTargetFps\":999}");
        App.Settings.Prop.ProfilePlaceIds = new() { [name] = [place.ToString()] }; App.Settings.Prop.LockMaximumRenderDistance = true;
        if (!App.GlobalSettings.Loaded) { App.GlobalSettings.CreateTemplate(); App.GlobalSettings.Load(); }
        App.GlobalSettings.ApplyDrawDistancePreference(); App.GlobalSettings.Save();
        App.State.Prop.Mods = []; App.PlayerState.Prop.ModManifest.Clear(); App.PlayerState.Save();
        bool Prepare(long placeId)
        {
            using var launcher = new Bootstrapper(LaunchMode.Player);
            typeof(Bootstrapper).GetField("_latestVersionDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(launcher, player);
            typeof(Bootstrapper).GetField("PackageDirectoryMap", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(launcher, new Dictionary<string, string>());
            typeof(Bootstrapper).GetField("_joinData", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(launcher, new GameJoinData { PlaceId = placeId });
            return AwaitResult((Task<bool>)typeof(Bootstrapper).GetMethod("ApplyModifications", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(launcher, null)!);
        }
        string output = Path.Combine(player, "ClientSettings", "ClientAppSettings.json");
        Dictionary<string, string> Flags() => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(output))!;
        Check(Prepare(place) && Flags()["DFIntCSGLevelOfDetailSwitchingDistance"] == "3000" && Flags()["DFIntDebugFRMQualityLevelOverride"] == "1" && SavedSlider() == "10", "real modification pipeline applies an assigned profile while preserving saved 10 and FRM 1");
        var report = ModApplicationReporter.Read()!;
        Check(report.Entries.Any(entry => entry.Source == name && entry.Status == "Applied") && !Flags().ContainsKey("DFIntTaskSchedulerTargetFps"), "actual profile preparation is reported and unsupported flags are absent from the final player file");
        Check(Prepare(place + 1) && Flags()["DFIntCSGLevelOfDetailSwitchingDistance"] == "500", "launching another experience restores the global mesh selection rather than leaving a stale profile");
        App.Settings.Prop.ProfilePlaceIds.Clear();
        Check(Prepare(place) && Flags()["DFIntCSGLevelOfDetailSwitchingDistance"] == "500", "removing an assignment restores global flags on that experience's next preparation");
        byte[] globalSource = File.ReadAllBytes(App.FastFlags.FileLocation); File.Delete(App.FastFlags.FileLocation);
        try { Check(Prepare(place) && !Flags().ContainsKey("DFIntCSGLevelOfDetailSwitchingDistance") && Flags()["DFIntDebugFRMQualityLevelOverride"] == "1", "a missing global flag file restores defaults instead of retaining a previous experience's mesh override"); }
        finally { File.WriteAllBytes(App.FastFlags.FileLocation, globalSource); }
        App.Settings.Prop.ProfilePlaceIds[name] = [place.ToString()]; File.WriteAllText(FlagProfileFiles.Resolve(name), "broken profile");
        Check(Prepare(place) && Flags()["DFIntCSGLevelOfDetailSwitchingDistance"] == "500" && ModApplicationReporter.Read()!.Entries.Any(entry => entry.Source == name && entry.Status == "Skipped"), "invalid profile falls back through the real pipeline and records a visible skipped-profile event");
        File.WriteAllText(Path.Combine(_output, "experience-profiles-probe.json"), JsonSerializer.Serialize(new { fixtureOnly = true, actualModificationPipeline = true, savedSlider = SavedSlider(), frm = Flags()["DFIntDebugFRMQualityLevelOverride"], restoredGlobalMesh = Flags()["DFIntCSGLevelOfDetailSwitchingDistance"], gameLaunched = false, settingsRoot = Paths.Base }, new JsonSerializerOptions { WriteIndented = true }));
        App.Settings.Prop.ProfilePlaceIds.Clear();
    }
    private static void RenderExperienceProfiles(Theme theme, int width)
    {
        App.Settings.Prop.Theme = theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
        var owner = new SettingsWindow(false) { Width = 900, Height = 850 }; owner.Show(); RunFor(60);
        var vm = (MainWindowViewModel)owner.DataContext!; vm.NavigateRegistered("fastflags"); RunFor(70);
        var button = owner.GetVisualDescendants().OfType<CardAction>().Single(control => control.Header == "Manage experience profiles");
        var index = new SearchIndexBuilder().ScanRenderedPageForElements(owner, "fastflags");
        Check(index.Any(item => item.DisplayName == "Manage experience profiles" && item.Description!.Contains("place IDs", StringComparison.Ordinal)), "experience action participates in the actual contextual settings index: " + theme + width);
        ClickSearchPopup(button); RunFor(80);
        var dialog = (FlagProfilesDialog)owner.OwnedWindows.Single(); dialog.Width = width; dialog.Height = 660; RunFor(80);
        Check(dialog.Title == "Experience rendering profiles" && dialog.Tabs.SelectedIndex == 1 && !dialog.PresetsTab.IsVisible && dialog.LoadTab.IsVisible, "actual Game Tweaks action opens the focused existing assignment dialog: " + theme + width);
        Capture(dialog, theme, "revision17-experience-assignments-" + width);
        dialog.Tabs.SelectedIndex = 2; dialog.SaveProfile.Text = "UI snapshot " + theme + width;
        RunFor(50); ClickSearchPopup(dialog.OkButton); RunFor(60);
        string name = (string)dialog.PlaceProfile.SelectedItem!;
        Check(dialog.Tabs.SelectedIndex == 1 && File.Exists(FlagProfileFiles.Resolve(name)), "actual Save snapshot button creates a profile and returns to its assignments: " + theme + width);
        dialog.NewPlaceIdTextBox.Text = "0"; ClickSearchPopup(dialog.AddPlaceButton); RunFor(40);
        Check(dialog.ProfileFeedback.Text!.Contains("positive", StringComparison.Ordinal), "invalid ID feedback is inline and keeps the dialog usable: " + theme + width);
        dialog.NewPlaceIdTextBox.Text = "000888"; ClickSearchPopup(dialog.AddPlaceButton); RunFor(50);
        Check(App.Settings.Prop.ProfilePlaceIds[name].Single() == "888" && JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(File.ReadAllText(App.Settings.FileLocation))!.ProfilePlaceIds[name].Single() == "888", "actual assignment normalizes its ID and persists immediately: " + theme + width);
        dialog.PlaceProfile.SelectedItem = "Lean meshes.json"; dialog.NewPlaceIdTextBox.Text = "888"; ClickSearchPopup(dialog.AddPlaceButton); RunFor(40);
        Check(dialog.ProfileFeedback.Text!.Contains("Remove that assignment first", StringComparison.Ordinal) && !App.Settings.Prop.ProfilePlaceIds.ContainsKey("Lean meshes.json"), "duplicate place assignments cannot silently replace another profile: " + theme + width);
        dialog.PlaceProfile.SelectedItem = name;
        using (var locked = new FileStream(App.Settings.FileLocation, FileMode.Open, FileAccess.Read, FileShare.Read))
        { dialog.NewPlaceIdTextBox.Text = "999"; ClickSearchPopup(dialog.AddPlaceButton); RunFor(40); }
        Check(App.Settings.Prop.ProfilePlaceIds[name].SequenceEqual(new[] { "888" }) && dialog.PlaceIdsListBox.ItemCount == 1 && dialog.ProfileFeedback.Text!.Contains("Could not save", StringComparison.Ordinal), "failed assignment save rolls back both the model and displayed list: " + theme + width);
        Capture(dialog, theme, "revision17-experience-save-error-" + width);
        dialog.PlaceIdsListBox.SelectedIndex = 0; ClickSearchPopup(dialog.RemovePlaceButton); RunFor(50);
        Check(!App.Settings.Prop.ProfilePlaceIds.ContainsKey(name) && dialog.PlaceIdsListBox.ItemCount == 0, "actual remove action disables the profile assignment without deleting its snapshot: " + theme + width);
        dialog.Tabs.SelectedIndex = 2; dialog.SaveProfile.Text = name; RunFor(30); ClickSearchPopup(dialog.OkButton); RunFor(40);
        Check(dialog.Tabs.SelectedIndex == 2 && dialog.ProfileFeedback.Text!.Contains("Could not save snapshot", StringComparison.Ordinal), "duplicate snapshot names keep the original and show an editable error: " + theme + width);
        Capture(dialog, theme, "revision17-experience-name-error-" + width);
        if (width == 480) { dialog.SetRenderScaling(1.5); RunFor(80); Capture(dialog, theme, "revision17-experience-150"); dialog.SetRenderScaling(1); }
        ClickSearchPopup(dialog.CancelProfileButton); RunFor(40);
        Check(!dialog.IsVisible && owner.IsEnabled, "closing the experience dialog releases the real owner window: " + theme + width);
        vm.SaveSettings(); owner.Close();
    }
    private static void VerifyExperienceEmptyAndMotion(string root)
    {
        string previous = Paths.SavedFlagProfiles, empty = Path.Combine(root, "empty-profiles"); Directory.CreateDirectory(empty);
        typeof(Paths).GetProperty(nameof(Paths.SavedFlagProfiles))!.SetValue(null, empty);
        foreach (var theme in new[] { Theme.Dark, Theme.Light })
        {
            App.Settings.Prop.ProfilePlaceIds.Clear();
            App.Settings.Prop.Theme = theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
            var dialog = new FlagProfilesDialog(true) { Width = 480, Height = 520 }; dialog.Show(); RunFor(70);
            Check(dialog.PlaceProfile.ItemCount == 0 && dialog.PlaceProfileEmptyMessage.IsEffectivelyVisible && !dialog.PlaceIdsContent.IsVisible, "first-launch experience profiles show an actionable empty state: " + theme);
            Capture(dialog, theme, "revision17-experience-empty");
            dialog.Tabs.SelectedIndex = 2; dialog.SaveProfile.Text = "Keyboard " + theme; dialog.SaveProfile.Focus();
            dialog.KeyPress(Key.Enter, RawInputModifiers.None, default, null); dialog.KeyRelease(Key.Enter, RawInputModifiers.None, default, null); RunFor(50);
            Check(dialog.Tabs.SelectedIndex == 1 && File.Exists(FlagProfileFiles.Resolve("Keyboard " + theme)), "Enter saves the actual focused snapshot editor: " + theme);
            dialog.NewPlaceIdTextBox.Text = theme == Theme.Dark ? "123456" : "123457"; dialog.NewPlaceIdTextBox.Focus();
            dialog.KeyPress(Key.Enter, RawInputModifiers.None, default, null); dialog.KeyRelease(Key.Enter, RawInputModifiers.None, default, null); RunFor(50);
            Check(dialog.PlaceIdsListBox.ItemCount == 1, "Enter assigns the actual focused place ID editor: " + theme);
            dialog.KeyPress(Key.Escape, RawInputModifiers.None, default, null); RunFor(30);
            Check(!dialog.IsVisible, "Escape closes the experience dialog while retaining immediate assignments: " + theme);
            foreach (string file in Directory.EnumerateFiles(empty)) File.Delete(file);
            var missing = new FlagProfilesDialog(true) { Width = 480, Height = 600 }; missing.Show(); RunFor(60);
            Check(missing.PlaceProfile.ItemCount == 1 && missing.PlaceIdsListBox.ItemCount == 1 && !missing.AddPlaceButton.IsEnabled && missing.ProfileFeedback.Text!.Contains("missing", StringComparison.Ordinal), "missing snapshot assignments remain visible and removable: " + theme);
            Capture(missing, theme, "revision17-experience-missing-snapshot");
            missing.PlaceIdsListBox.SelectedIndex = 0; ClickSearchPopup(missing.RemovePlaceButton); RunFor(50);
            Check(App.Settings.Prop.ProfilePlaceIds.Count == 0 && missing.PlaceIdsListBox.ItemCount == 0, "actual remove action retires an orphaned assignment without needing its file: " + theme);
            missing.Close();
        }
        _manualTime = new ManualMotionTime(); CoffeeMotion.TimeSource = _manualTime; AppearanceViewModel.DisableAnimations = false;
        var motion = new FlagProfilesDialog(true) { Width = 600, Height = 550 }; motion.Show(); RunFor(80);
        motion.ProfileFeedback.Text = "Profile save feedback with finite motion.";
        var reveal = motion.ProfileFeedbackReveal; var frames = new List<double>();
        for (int i = 0; i < 8; i++) { StepFrame(16); frames.Add(reveal.RevealedHeight); }
        Check(frames.Distinct().Count() > 2 && frames.Any(value => value > 0 && value < reveal.NaturalHeight), "actual profile feedback animates through intermediate frames");
        for (int i = 0; i < 8; i++) { motion.ProfileFeedback.Text = i % 2 == 0 ? "" : "Feedback"; StepFrame(16); }
        AppearanceViewModel.DisableAnimations = true; RunFor(50);
        Check(!reveal.HasActiveMotion && Math.Abs(reveal.RevealedHeight - reveal.NaturalHeight) < .1, "runtime reduced motion settles interrupted profile feedback immediately");
        AppearanceViewModel.DisableAnimations = false; motion.ProfileFeedback.Text = ""; StepFrame(16); motion.Close(); RunFor(40);
        Check(!reveal.HasActiveMotion, "closing the profile dialog stops its active feedback motion");
        AppearanceViewModel.DisableAnimations = true; CoffeeMotion.TimeSource = TimeProvider.System; _manualTime = null;
        typeof(Paths).GetProperty(nameof(Paths.SavedFlagProfiles))!.SetValue(null, previous);
        App.Settings.Prop.ProfilePlaceIds.Clear();
    }
}
