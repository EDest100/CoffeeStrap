// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Froststrap;
using Froststrap.Enums;
using Froststrap.Services;
using Froststrap.UI.Elements.Dialogs;
using Froststrap.UI.Elements.Controls;
using Froststrap.UI.Utility;
using Froststrap.UI.ViewModels.Settings;

internal static partial class Program
{
    private static void ProbeRevision19()
    {
        string previous = Paths.SavedFlagProfiles;
        string root = Path.Combine(Paths.Base, "revision19-probe"); Directory.CreateDirectory(root);
        typeof(Paths).GetProperty(nameof(Paths.SavedFlagProfiles))!.SetValue(null, root);
        App.Settings.Prop.ProfilePlaceIds = []; App.Settings.Save();
        App.FastFlags.Prop = new() { ["DFIntDebugFRMQualityLevelOverride"] = "1" };
        for (int i = 0; i < 256; i++) File.WriteAllText(Path.Combine(root, $"Snapshot-{i:D3}"), "{\"DFIntDebugFRMQualityLevelOverride\":1}");
        var dialog = new FlagProfilesDialog(true) { Width = 480, Height = 700 }; dialog.Show(); RunFor(60);
        dialog.Tabs.SelectedIndex = 2; dialog.SaveProfile.Text = "zzzz-overflow"; RunFor(50); ClickSearchPopup(dialog.OkButton); RunFor(80);
        bool created = File.Exists(Path.Combine(root, "zzzz-overflow")), available = dialog.PlaceProfile.Items.Contains("zzzz-overflow");
        App.Settings.Prop.ProfilePlaceIds["Snapshot-000"] = Enumerable.Range(1, 4096).Select(value => value.ToString()).ToList();
        dialog.PlaceProfile.SelectedItem = "Snapshot-001"; RunFor(30); dialog.PlaceProfile.SelectedItem = "Snapshot-000"; RunFor(70);
        var result = new { createdBeyondPickerLimit = created, newSnapshotSelectable = available, selected = dialog.PlaceProfile.SelectedItem, displayedLegacyIds = dialog.PlaceIdsListBox.ItemCount, storedLegacyIds = App.Settings.Prop.ProfilePlaceIds["Snapshot-000"].Count, feedback = dialog.ProfileFeedback.Text, fixtureOnly = true };
        File.WriteAllText(Path.Combine(_output, "profile-boundary-reproduction.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(result)); dialog.Close();
        typeof(Paths).GetProperty(nameof(Paths.SavedFlagProfiles))!.SetValue(null, previous);
    }
    private static int _maximumRecoveryRows;
    private static void VerifyRevision19()
    {
        _maximumRecoveryRows = 0;
        string previous = Paths.SavedFlagProfiles, settings = JsonSerializer.Serialize(App.Settings.Prop);
        var flags = App.FastFlags.Prop.ToDictionary(pair => pair.Key, pair => pair.Value);
        string root = Path.Combine(Paths.Base, "revision19"); Directory.CreateDirectory(root);
        try
        {
            App.Settings.Prop.LockMaximumRenderDistance = true;
            if (!App.GlobalSettings.Loaded) { App.GlobalSettings.CreateTemplate(); App.GlobalSettings.Load(); }
            App.GlobalSettings.ApplyDrawDistancePreference(); App.GlobalSettings.Save();
            App.FastFlags.Prop = new() { ["DFIntDebugFRMQualityLevelOverride"] = "1", ["DFIntCSGLevelOfDetailSwitchingDistance"] = "500" }; App.FastFlags.Save();
            VerifySnapshotQuota(root);
            foreach (var theme in new[] { Theme.Dark, Theme.Light }) VerifyAssignmentRecovery(root, theme);
            VerifyRecoveryOwnership(root);
            VerifyRecoveryMotion(root);
            Check(SavedSlider() == "10" && App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride") == "1", "profile quota and recovery retain saved native slider 10 plus FRM 1");
            File.WriteAllText(Path.Combine(_output, "profile-recovery-probe.json"), JsonSerializer.Serialize(new { fixtureOnly = true, savedSlider = SavedSlider(), frm = App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride"), maximumRenderedIds = _maximumRecoveryRows, gameLaunched = false }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            typeof(Paths).GetProperty(nameof(Paths.SavedFlagProfiles))!.SetValue(null, previous);
            App.Settings.Prop = JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(settings)!;
            App.FastFlags.Prop = flags; App.FastFlags.Save();
            AppearanceViewModel.DisableAnimations = true; CoffeeMotion.TimeSource = TimeProvider.System; _manualTime = null;
        }
    }
    private static void RecoveryRoot(string path)
    {
        Directory.CreateDirectory(path); typeof(Paths).GetProperty(nameof(Paths.SavedFlagProfiles))!.SetValue(null, path);
        App.Settings.Prop.ProfilePlaceIds = []; App.Settings.Save();
    }
    private static void ProfileClick(Control control)
    { control.BringIntoView(); RunFor(60); ClickSearchPopup(control); RunFor(60); if (TopLevel.GetTopLevel(control) is FlagProfilesDialog dialog) _maximumRecoveryRows = Math.Max(_maximumRecoveryRows, dialog.PlaceIdsListBox.ItemCount); }
    private static void VerifySnapshotQuota(string root)
    {
        RecoveryRoot(Path.Combine(root, "quota"));
        for (int i = 0; i < 256; i++) File.WriteAllText(Path.Combine(Paths.SavedFlagProfiles, $"Snapshot-{i:D3}"), "{\"DFIntDebugFRMQualityLevelOverride\":1}");
        File.WriteAllText(Path.Combine(Paths.SavedFlagProfiles, ".private-stage"), "ignored");
        Check(ProfileActionFails(() => FlagProfileFiles.SaveNew("zzzz-overflow", App.FastFlags.Prop)) && !File.Exists(FlagProfileFiles.Resolve("zzzz-overflow")), "the 257th snapshot is rejected before writing an unreachable file");
        FlagProfileFiles.Update("Snapshot-000", App.FastFlags.Prop);
        Check(AwaitResult(FlagProfileFiles.ReadAsync("Snapshot-000", CancellationToken.None)).Count == 2, "replacing an existing snapshot is allowed at the library limit");
        foreach (var theme in new[] { Theme.Dark, Theme.Light })
        {
            App.Settings.Prop.Theme = theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
            var dialog = new FlagProfilesDialog(true) { Width = 480, Height = 700 }; dialog.Show(); RunFor(60);
            dialog.Tabs.SelectedIndex = 2; dialog.SaveProfile.Text = "zzzz-overflow"; RunFor(40); ProfileClick(dialog.OkButton);
            Check(dialog.Tabs.SelectedIndex == 2 && dialog.ProfileFeedback.Text!.Contains("256", StringComparison.Ordinal) && !File.Exists(FlagProfileFiles.Resolve("zzzz-overflow")), "actual full-library save keeps the editor open with actionable feedback: " + theme);
            Capture(dialog, theme, "revision19-library-full"); dialog.Close();
        }
        FlagProfileFiles.Delete("Snapshot-255"); FlagProfileFiles.SaveNew("Recovered slot", App.FastFlags.Prop);
        Check(FlagProfileFiles.Names().Count() == 256 && File.Exists(FlagProfileFiles.Resolve("Recovered slot")), "deleting one snapshot permits one replacement slot and hidden staging files do not consume capacity");
        File.WriteAllText(Path.Combine(Paths.SavedFlagProfiles, "zzzz-legacy"), "{\"DFIntDebugFRMQualityLevelOverride\":1}");
        App.Settings.Prop.ProfilePlaceIds["zzzz-legacy"] = ["123"]; App.Settings.TrySaveSetting(nameof(App.Settings.Prop.ProfilePlaceIds));
        var manager = new FlagProfilesDialog(true) { Width = 760, Height = 700 }; manager.Show(); RunFor(70);
        manager.PlaceProfile.SelectedItem = "zzzz-legacy"; RunFor(50); manager.Tabs.SelectedIndex = 3; RunFor(50);
        Check(manager.LoadProfile.Items.Contains("zzzz-legacy") && manager.LoadProfile.ItemCount == 256 && (string?)manager.LoadProfile.SelectedItem == "zzzz-legacy", "selecting an assigned legacy file beyond the first batch reveals it in the bounded snapshot manager");
        manager.RenameTextBox.Text = "zzzzz-renamed"; ProfileClick(manager.RenameProfileButton);
        Check((string?)manager.LoadProfile.SelectedItem == "zzzzz-renamed" && manager.LoadProfile.ItemCount == 256 && App.Settings.Prop.ProfilePlaceIds["zzzzz-renamed"].Single() == "123", "legacy over-limit rename retains a selectable file and its assignment without growing the picker");
        Check(manager.ProfileLibraryStatus.Text!.Contains("legacy", StringComparison.Ordinal), "over-limit library status persists independently of operation feedback");
        Capture(manager, Theme.Light, "revision19-legacy-library-760"); manager.Close();
        Check(!FlagProfileFiles.TryPlaceId(new string('0', 100000) + "1", out _) && !FlagProfileFiles.TryPlaceId(null, out _), "legacy ID parsing rejects null and oversized values before numeric work");
    }
    private static void VerifyAssignmentRecovery(string root, Theme theme)
    {
        RecoveryRoot(Path.Combine(root, "ids-" + theme));
        FlagProfileFiles.SaveNew("Paged snapshot", App.FastFlags.Prop);
        var original = Enumerable.Range(1, 1025).Select(value => value.ToString()).ToList();
        App.Settings.Prop.ProfilePlaceIds = new() { ["Paged snapshot"] = original, ["Other profile"] = ["8000"] }; App.Settings.Save();
        App.Settings.Prop.Theme = theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
        var dialog = new FlagProfilesDialog(true) { Width = 480, Height = 760 }; dialog.Show(); dialog.PlaceProfile.SelectedItem = "Paged snapshot"; RunFor(80);
        Check(dialog.PlaceIdsListBox.ItemCount == 128 && dialog.AssignmentLimitsText.IsVisible && !dialog.AddPlaceButton.IsEnabled && dialog.IdPager.IsVisible, "legacy oversized IDs render one bounded page and disable new assignments: " + theme);
        Capture(dialog, theme, "revision19-legacy-ids");
        ProfileClick(dialog.IdNextButton);
        Check(dialog.PlaceIdsListBox.ItemsSource!.Cast<string>().First() == "129" && dialog.IdPageStatus.Text == "129–256 of 1025", "actual Next button renders the following page without changing stored IDs: " + theme);
        for (int i = 0; i < 7; i++) ProfileClick(dialog.IdNextButton);
        Check(dialog.PlaceIdsListBox.ItemCount == 1 && !dialog.IdNextButton.IsEnabled && dialog.PlaceIdsListBox.ItemsSource!.Cast<string>().Single() == "1025", "last page is bounded and disables Next: " + theme);
        dialog.PlaceIdsListBox.SelectedIndex = 0; RunFor(30); ProfileClick(dialog.RemovePlaceButton);
        var saved = JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(File.ReadAllText(App.Settings.FileLocation))!.ProfilePlaceIds;
        Check(saved["Paged snapshot"].SequenceEqual(original.Take(1024)) && saved["Other profile"].Single() == "8000" && dialog.PlaceIdsListBox.ItemCount <= 128 && !dialog.AssignmentLimitsText.IsVisible, "removing the last legacy ID preserves every hidden ID and other profiles, then repairs launch bounds: " + theme);
        Check(dialog.UndoAssignments.IsEnabled && dialog.ClearProfileAssignments.IsVisible, "a successful assignment change exposes session Undo and clear tools: " + theme);
        ProfileClick(dialog.UndoAssignments);
        Check(App.Settings.Prop.ProfilePlaceIds["Paged snapshot"].SequenceEqual(original) && !dialog.UndoAssignments.IsVisible && dialog.AssignmentLimitsText.IsVisible, "Undo restores the entire saved legacy list without truncating hidden pages: " + theme);
        ProfileClick(dialog.ClearProfileAssignments);
        Check(dialog.AssignmentConfirmation.IsExpanded && App.Settings.Prop.ProfilePlaceIds["Paged snapshot"].Count == 1025, "clear asks for confirmation before touching assignments: " + theme);
        ProfileClick(dialog.CancelAssignmentClear);
        Check(!dialog.AssignmentConfirmation.IsExpanded && App.Settings.Prop.ProfilePlaceIds["Paged snapshot"].Count == 1025, "Keep assignments cancels the pending clear: " + theme);
        ProfileClick(dialog.ClearProfileAssignments);
        dialog.KeyPress(Key.Escape, RawInputModifiers.None, default, null); RunFor(30);
        Check(dialog.IsVisible && !dialog.AssignmentConfirmation.IsExpanded && App.Settings.Prop.ProfilePlaceIds["Paged snapshot"].Count == 1025, "Escape dismisses only the pending confirmation and preserves assignments: " + theme);
        dialog.KeyRelease(Key.Escape, RawInputModifiers.None, default, null);
        ProfileClick(dialog.ClearProfileAssignments); Capture(dialog, theme, "revision19-clear-confirmation");
        using (var locked = new FileStream(App.Settings.FileLocation, FileMode.Open, FileAccess.Read, FileShare.Read)) ProfileClick(dialog.ConfirmAssignmentClear);
        Check(App.Settings.Prop.ProfilePlaceIds["Paged snapshot"].Count == 1025 && dialog.ProfileFeedback.Text!.Contains("Could not save", StringComparison.Ordinal), "failed confirmed clear preserves the model, file and retryable confirmation: " + theme);
        ProfileClick(dialog.ConfirmAssignmentClear);
        Check(!App.Settings.Prop.ProfilePlaceIds.ContainsKey("Paged snapshot") && App.Settings.Prop.ProfilePlaceIds["Other profile"].Single() == "8000" && File.Exists(FlagProfileFiles.Resolve("Paged snapshot")) && dialog.UndoAssignments.IsEnabled, "confirmed profile clear removes only that profile's IDs and preserves its snapshot: " + theme);
        Capture(dialog, theme, "revision19-clear-complete");
        using (var locked = new FileStream(App.Settings.FileLocation, FileMode.Open, FileAccess.Read, FileShare.Read)) ProfileClick(dialog.UndoAssignments);
        Check(!App.Settings.Prop.ProfilePlaceIds.ContainsKey("Paged snapshot") && dialog.UndoAssignments.IsEnabled, "failed Undo keeps the successful clear and its retryable backup: " + theme);
        ProfileClick(dialog.UndoAssignments);
        Check(App.Settings.Prop.ProfilePlaceIds["Paged snapshot"].Count == 1025 && JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(File.ReadAllText(App.Settings.FileLocation))!.ProfilePlaceIds["Paged snapshot"].Count == 1025, "Undo retry restores all IDs to both memory and disk: " + theme);
        dialog.SetRenderScaling(1.5); RunFor(70); Capture(dialog, theme, "revision19-recovery-150"); dialog.SetRenderScaling(1);
        dialog.Close(); Check(dialog.PlaceIdsListBox.ItemsSource == null, "closing the dialog releases its bounded ID view: " + theme);
    }
    private static void VerifyRecoveryOwnership(string root)
    {
        RecoveryRoot(Path.Combine(root, "ownership")); FlagProfileFiles.SaveNew("Valid file", App.FastFlags.Prop);
        App.Settings.Prop.ProfilePlaceIds = Enumerable.Range(1, 257).ToDictionary(value => "Missing " + value, value => new List<string> { value.ToString() });
        App.Settings.Prop.ProfilePlaceIds["Missing 1"] = null!; App.Settings.Save();
        var previous = App.Settings.Prop.ProfilePlaceIds;
        var dialog = new FlagProfilesDialog(true) { Width = 760, Height = 760 }; dialog.Show(); dialog.PlaceProfile.SelectedItem = "Missing 1"; RunFor(70);
        Check(dialog.PlaceProfile.ItemCount <= 512 && dialog.AssignmentLimitsText.IsVisible && dialog.ResetAllAssignments.IsVisible && dialog.ClearProfileAssignments.IsVisible, "oversized maps and null lists remain recoverable through bounded pickers");
        ProfileClick(dialog.ResetAllAssignments); Capture(dialog, Theme.Light, "revision19-global-recovery-760"); ProfileClick(dialog.ConfirmAssignmentClear);
        Check(App.Settings.Prop.ProfilePlaceIds.Count == 0 && dialog.UndoAssignments.IsEnabled && FlagProfileFiles.Names().Count() == 1, "global recovery clears even hidden legacy keys while preserving files");
        ProfileClick(dialog.UndoAssignments);
        Check(App.Settings.Prop.ProfilePlaceIds.Count == 257 && App.Settings.Prop.ProfilePlaceIds["Missing 1"] == null, "global recovery Undo restores all hidden keys and legacy null values");
        ProfileClick(dialog.ResetAllAssignments);
        App.Settings.Prop.ProfilePlaceIds = new() { ["External selection"] = ["999"] }; App.Settings.TrySaveSetting(nameof(App.Settings.Prop.ProfilePlaceIds));
        ProfileClick(dialog.ConfirmAssignmentClear);
        Check(App.Settings.Prop.ProfilePlaceIds.ContainsKey("External selection") && dialog.ProfileFeedback.Text!.Contains("Assignments changed", StringComparison.Ordinal), "confirmation cannot clear a model replaced after the user reviewed it");
        dialog.Close();
        App.Settings.Prop.ProfilePlaceIds = new() { ["Valid file"] = ["123"] }; App.Settings.Save();
        dialog = new FlagProfilesDialog(true) { Width = 760, Height = 760 }; dialog.Show(); dialog.PlaceProfile.SelectedItem = "Valid file"; RunFor(70);
        ProfileClick(dialog.ClearProfileAssignments); ProfileClick(dialog.ConfirmAssignmentClear);
        var external = JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(File.ReadAllText(App.Settings.FileLocation))!; external.ProfilePlaceIds["External file"] = ["444"];
        string changed = JsonSerializer.Serialize(external); File.WriteAllText(App.Settings.FileLocation, changed);
        ProfileClick(dialog.UndoAssignments);
        Check(File.ReadAllText(App.Settings.FileLocation) == changed && !App.Settings.Prop.ProfilePlaceIds.ContainsKey("Valid file") && dialog.ProfileFeedback.Text!.Contains("outside this dialog", StringComparison.Ordinal), "Undo refuses to overwrite a settings file changed by another tool");
        App.Settings.Load(false); dialog.Close();
        App.Settings.Prop.ProfilePlaceIds = new() { ["Valid file"] = [null!, new string('9', 1000), "123"] }; App.Settings.Save();
        dialog = new FlagProfilesDialog(true) { Width = 760, Height = 760 }; dialog.Show(); dialog.PlaceProfile.SelectedItem = "Valid file"; RunFor(70);
        Check(dialog.PlaceIdsListBox.ItemsSource!.Cast<string>().All(value => value.Length < 64), "invalid empty and long legacy IDs have bounded readable row labels");
        dialog.PlaceIdsListBox.SelectedIndex = 0; RunFor(20); ProfileClick(dialog.RemovePlaceButton);
        Check(App.Settings.Prop.ProfilePlaceIds["Valid file"].SequenceEqual(new[] { new string('9', 1000), "123" }), "removal uses the displayed row index so an empty legacy ID can be removed without altering other values");
        dialog.Close();
    }
    private static void VerifyRecoveryMotion(string root)
    {
        RecoveryRoot(Path.Combine(root, "motion")); FlagProfileFiles.SaveNew("Motion", App.FastFlags.Prop);
        App.Settings.Prop.ProfilePlaceIds = new() { ["Motion"] = ["123"] }; App.Settings.Save();
        _manualTime = new ManualMotionTime(); CoffeeMotion.TimeSource = _manualTime; AppearanceViewModel.DisableAnimations = false;
        var dialog = new FlagProfilesDialog(true) { Width = 760, Height = 760 }; dialog.Show(); RunFor(70);
        typeof(FlagProfilesDialog).GetMethod("ClearProfileAssignments_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [dialog, new Avalonia.Interactivity.RoutedEventArgs()]);
        var reveal = dialog.AssignmentConfirmation; var frames = new List<double>();
        for (int i = 0; i < 8; i++) { StepFrame(16); frames.Add(reveal.RevealedHeight); }
        Check(frames.Distinct().Count() > 2 && frames.Any(value => value > 0 && value < reveal.NaturalHeight), "assignment confirmation animates through intermediate production frames");
        reveal.IsExpanded = false; StepFrame(16); reveal.IsExpanded = true; StepFrame(16); AppearanceViewModel.DisableAnimations = true; RunFor(30);
        Check(!reveal.HasActiveMotion && Math.Abs(reveal.RevealedHeight - reveal.NaturalHeight) < .1, "runtime reduced motion settles an interrupted assignment confirmation");
        AppearanceViewModel.DisableAnimations = false; reveal.IsExpanded = false; StepFrame(16); dialog.Close(); RunFor(40);
        Check(!reveal.HasActiveMotion, "closing recovery during a transition stops its animation");
    }
}
