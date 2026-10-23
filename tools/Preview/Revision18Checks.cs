// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using Froststrap.Models;
using Froststrap.UI.Elements.Controls;
using Froststrap;
using Froststrap.Enums;
using Froststrap.Services;
using Froststrap.UI.Elements.Dialogs;

internal static partial class Program
{
    private static void VerifyRevision18()
    {
        string previous = Paths.SavedFlagProfiles, settings = JsonSerializer.Serialize(App.Settings.Prop);
        var originalFlags = App.FastFlags.Prop.ToDictionary(pair => pair.Key, pair => pair.Value);
        string root = Path.Combine(Paths.Base, "revision18"); Directory.CreateDirectory(root);
        typeof(Paths).GetProperty(nameof(Paths.SavedFlagProfiles))!.SetValue(null, root);
        try
        {
            App.Settings.Prop.ProfilePlaceIds = []; App.Settings.Prop.LockMaximumRenderDistance = true; App.Settings.Save();
            if (!App.GlobalSettings.Loaded) { App.GlobalSettings.CreateTemplate(); App.GlobalSettings.Load(); }
            App.GlobalSettings.ApplyDrawDistancePreference(); App.GlobalSettings.Save();
            App.FastFlags.Prop = new() { ["DFIntDebugFRMQualityLevelOverride"] = "1", ["DFIntCSGLevelOfDetailSwitchingDistance"] = "500", ["FFlagDebugGraphicsPreferD3D11"] = "True" }; App.FastFlags.Save();
            VerifyProfileMutations();
            VerifyPendingProfileLoad();
            VerifyProfileEditorIntegration();
            VerifyProfileCloseAndEmpty(root);
            VerifyGameTweaksSnapshotManagement();
            foreach (var theme in new[] { Theme.Dark, Theme.Light }) foreach (int width in new[] { 480, 760 }) RenderProfileEditing(theme, width);
            Check(SavedSlider() == "10" && App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride") == "1", "all profile editing/load/assignment paths retain saved native slider 10 and FRM 1");
            File.WriteAllText(Path.Combine(_output, "profile-editing-probe.json"), JsonSerializer.Serialize(new { fixtureOnly = true, savedSlider = SavedSlider(), frm = App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride"), remainingProfiles = Directory.EnumerateFiles(Paths.SavedFlagProfiles).Count(), gameLaunched = false }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            typeof(Paths).GetProperty(nameof(Paths.SavedFlagProfiles))!.SetValue(null, previous);
            App.Settings.Prop = JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(settings)!;
            App.FastFlags.Prop = originalFlags; App.FastFlags.Save();
        }
    }
    private static bool ProfileActionFails(Action action)
    {
        try { action(); return false; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { return true; }
    }
    private static void VerifyProfileMutations()
    {
        const string name = "Original.json", renamed = "Renamed.json";
        FlagProfileFiles.SaveNew(name, App.FastFlags.Prop);
        string path = FlagProfileFiles.Resolve(name), original = File.ReadAllText(path);
        App.Settings.Prop.ProfilePlaceIds[name] = ["123", "456"]; App.Settings.TrySaveSetting(nameof(App.Settings.Prop.ProfilePlaceIds));
        string originalSettings = File.ReadAllText(App.Settings.FileLocation);
        using (var locked = new FileStream(App.Settings.FileLocation, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Check(ProfileActionFails(() => FlagProfileFiles.Rename(name, renamed)) && File.ReadAllText(path) == original && !File.Exists(FlagProfileFiles.Resolve(renamed)), "rename rolls the file back when assignment persistence is locked");
            Check(ProfileActionFails(() => FlagProfileFiles.Delete(name)) && File.ReadAllText(path) == original, "delete restores its staged file when assignment persistence is locked");
            Check(App.Settings.Prop.ProfilePlaceIds.ContainsKey(name) && File.ReadAllText(App.Settings.FileLocation) == originalSettings, "failed file mutations preserve both the in-memory and persisted assignment map");
        }
        Check(!Directory.EnumerateFiles(Paths.SavedFlagProfiles, ".*").Any(), "failed rename/delete operations leave no staged or temporary files");
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Check(ProfileActionFails(() => FlagProfileFiles.Delete(name)) && App.Settings.Prop.ProfilePlaceIds.ContainsKey(name), "locked profile deletion does not remove assignments");
            Check(ProfileActionFails(() => FlagProfileFiles.Rename(name, renamed)) && File.ReadAllText(path) == original, "locked source rename preserves the original file");
            Check(ProfileActionFails(() => FlagProfileFiles.Update(name, new Dictionary<string, object> { ["DFIntCSGLevelOfDetailSwitchingDistance"] = "100" })) && File.ReadAllText(path) == original, "atomic snapshot replacement does not truncate a locked file");
        }
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Check(ProfileActionFails(() => FlagProfileFiles.Delete(name)) && File.ReadAllText(path) == original && JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(File.ReadAllText(App.Settings.FileLocation))!.ProfilePlaceIds.ContainsKey(name), "read-only delete failure after staging restores both the original file and already-saved assignments");
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
        Check(ProfileActionFails(() => FlagProfileFiles.Rename("Missing same name", "Missing same name")), "same-name rename cannot report success for a missing snapshot");
        App.Settings.Prop.ProfilePlaceIds[renamed] = ["999"];
        Check(ProfileActionFails(() => FlagProfileFiles.Rename(name, renamed)), "rename cannot steal assignments belonging to a missing profile name");
        App.Settings.Prop.ProfilePlaceIds.Remove(renamed);
        FlagProfileFiles.SaveNew("Collision.json", App.FastFlags.Prop);
        Check(ProfileActionFails(() => FlagProfileFiles.Rename(name, "Collision.json")) && File.ReadAllText(path) == original, "rename never overwrites another snapshot");
        Check(ProfileActionFails(() => FlagProfileFiles.Rename(name, "../escape")) && ProfileActionFails(() => FlagProfileFiles.Update("Missing.json", App.FastFlags.Prop)), "rename and replace reuse validated existing local profile paths");
        FlagProfileFiles.Rename(name, renamed);
        var disk = JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(File.ReadAllText(App.Settings.FileLocation))!;
        Check(!File.Exists(path) && File.ReadAllText(FlagProfileFiles.Resolve(renamed)) == original && disk.ProfilePlaceIds[renamed].SequenceEqual(new[] { "123", "456" }) && !disk.ProfilePlaceIds.ContainsKey(name), "rename retry moves the intact snapshot and persists all assignments under the new name");
        FlagProfileFiles.Update(renamed, new Dictionary<string, object> { ["DFIntCSGLevelOfDetailSwitchingDistance"] = "250", ["DFIntTaskSchedulerTargetFps"] = "999" });
        var updated = AwaitResult(FlagProfileFiles.ReadAsync(renamed, CancellationToken.None));
        Check(updated.Count == 1 && updated["DFIntCSGLevelOfDetailSwitchingDistance"].ToString() == "250" && App.Settings.Prop.ProfilePlaceIds[renamed].Count == 2, "replace writes only supported current flags and keeps assignments");
        FlagProfileFiles.Delete(renamed);
        disk = JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(File.ReadAllText(App.Settings.FileLocation))!;
        Check(!File.Exists(FlagProfileFiles.Resolve(renamed)) && !disk.ProfilePlaceIds.ContainsKey(renamed) && !Directory.EnumerateFiles(Paths.SavedFlagProfiles, ".*").Any(), "successful delete removes the file, persisted assignments and staging file");
    }
    private static void VerifyPendingProfileLoad()
    {
        var before = App.FastFlags.Prop.ToDictionary(pair => pair.Key, pair => pair.Value);
        string saved = File.ReadAllText(App.FastFlags.FileLocation);
        var result = App.FastFlags.ApplyProfile(new Dictionary<string, object> { ["DFIntCSGLevelOfDetailSwitchingDistance"] = "125", ["DFIntDebugFRMQualityLevelOverride"] = "21", ["DFIntTaskSchedulerTargetFps"] = "999" }, true);
        Check(result.Accepted == 2 && result.Excluded == 1 && App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride") == "1" && App.FastFlags.GetValue("FFlagDebugGraphicsPreferD3D11") == "True", "replace excludes unsupported flags and preserves selected FRM 1 and renderer");
        Check(App.FastFlags.HasUnsavedChanges && File.ReadAllText(App.FastFlags.FileLocation) == saved, "loading a snapshot stages pending edits without writing global settings or a live player file");
        App.FastFlags.Undo(); Check(before.Count == App.FastFlags.Prop.Count && before.All(pair => App.FastFlags.GetValue(pair.Key) == pair.Value.ToString()), "one real flag-editor Undo restores all values from before profile loading");
        App.FastFlags.Redo(); Check(App.FastFlags.GetValue("DFIntCSGLevelOfDetailSwitchingDistance") == "125", "real Redo restores the loaded supported snapshot");
        App.FastFlags.Prop = before;
        result = App.FastFlags.ApplyProfile(new Dictionary<string, object> { ["FFlagDebugSkyGray"] = "True" }, false);
        Check(App.FastFlags.GetValue("DFIntCSGLevelOfDetailSwitchingDistance") == "500" && App.FastFlags.GetValue("FFlagDebugSkyGray") == "True", "merge retains unrelated pending mesh settings");
        Check(ProfileActionFails(() => App.FastFlags.ApplyProfile(new Dictionary<string, object> { ["Unknown"] = "True" }, true)) && App.FastFlags.GetValue("FFlagDebugSkyGray") == "True", "wholly unsupported snapshots cannot erase pending settings");
        foreach (string preset in new[] { "Low Quality", "High Quality", "Balanced Quality" })
        {
            var source = AwaitResult(FlagProfileFiles.ReadPresetAsync(preset, CancellationToken.None));
            result = App.FastFlags.ApplyProfile(source, true);
            Check(result.Accepted > 0 && App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride") == "1" && App.FastFlags.Prop.Keys.All(CuratedFlagRegistry.Allowed.Contains), "built-in preset uses the same bounded supported pending-load path: " + preset);
        }
        App.FastFlags.Prop = before;
    }
    private static void VerifyProfileEditorIntegration()
    {
        var editor = new Froststrap.UI.Elements.Settings.Pages.FastFlags.FastFlagEditor();
        var owner = new Window { Width = 900, Height = 700, Content = editor }; owner.Show(); RunFor(80);
        var button = editor.GetVisualDescendants().OfType<IconButton>().Single(control => control.Icon == LucideAvalonia.Enum.LucideIconNames.FilePenLine);
        ClickSearchPopup(button); RunFor(80);
        var dialog = owner.OwnedWindows.OfType<FlagProfilesDialog>().Single();
        dialog.Tabs.SelectedIndex = 2; dialog.SaveProfile.Text = "Editor integration"; ClickSearchPopup(dialog.OkButton); RunFor(80);
        Check(File.Exists(FlagProfileFiles.Resolve("Editor integration")) && dialog.IsVisible, "real advanced-editor Profiles action saves once and remains available for review");
        var flags = AwaitResult(FlagProfileFiles.ReadAsync("Editor integration", CancellationToken.None)); flags["DFIntCSGLevelOfDetailSwitchingDistance"] = "333";
        FlagProfileFiles.Update("Editor integration", flags); ClickSearchPopup(dialog.OkButton); RunFor(100); ClickSearchPopup(dialog.CancelProfileButton); RunFor(60);
        var grid = editor.FindControl<DataGrid>("DataGrid")!;
        Check(owner.IsEnabled && grid.ItemsSource.Cast<FastFlag>().Any(flag => flag.Name == "DFIntCSGLevelOfDetailSwitchingDistance" && flag.Value == "333"), "closing the real dialog refreshes the advanced grid immediately without replaying an action or a delay");
        App.FastFlags.Undo(); owner.Close(); editor.Dispose();
    }
    private static void VerifyProfileCloseAndEmpty(string root)
    {
        string previous = Paths.SavedFlagProfiles, empty = Path.Combine(root, "empty"); Directory.CreateDirectory(empty);
        typeof(Paths).GetProperty(nameof(Paths.SavedFlagProfiles))!.SetValue(null, empty);
        foreach (var theme in new[] { Theme.Dark, Theme.Light })
        {
            App.Settings.Prop.Theme = theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
            var dialog = new FlagProfilesDialog { Width = 480, Height = 480 }; dialog.Show(); dialog.Tabs.SelectedIndex = 3; RunFor(80);
            Check(dialog.LoadProfileEmptyText.IsEffectivelyVisible && !dialog.RenamePanel.IsVisible && !dialog.OkButton.IsEnabled && !dialog.DeleteButton.IsEnabled, "advanced profile empty state disables unavailable actions at minimum size: " + theme);
            var viewport = dialog.LoadProfileEmptyText.GetVisualAncestors().OfType<Avalonia.Controls.Presenters.ScrollContentPresenter>().First();
            var labelOrigin = dialog.LoadProfileEmptyText.TranslatePoint(default, viewport)!.Value;
            Check(labelOrigin.Y >= 0 && labelOrigin.Y + dialog.LoadProfileEmptyText.Bounds.Height <= viewport.Bounds.Height, "minimum-size empty profile message stays inside the actual scroll viewport: " + theme);
            Capture(dialog, theme, "revision18-profile-empty-minimum"); dialog.Close();
        }
        string name = "Close during read.json";
        File.WriteAllText(FlagProfileFiles.Resolve(name), "{\"DFIntCSGLevelOfDetailSwitchingDistance\":125,\"Padding\":\"" + new string('x', 900000) + "\"}");
        var pending = new FlagProfilesDialog(); pending.Show(); pending.Tabs.SelectedIndex = 3; pending.LoadProfile.SelectedItem = name; RunFor(50);
        string before = JsonSerializer.Serialize(App.FastFlags.Prop);
        typeof(FlagProfilesDialog).GetMethod("OKButton_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pending, [pending, new Avalonia.Interactivity.RoutedEventArgs()]);
        pending.Close(); RunFor(150);
        Check(!pending.IsVisible && JsonSerializer.Serialize(App.FastFlags.Prop) == before, "closing during asynchronous profile loading cancels application and cannot publish stale flag changes");
        typeof(Paths).GetProperty(nameof(Paths.SavedFlagProfiles))!.SetValue(null, previous);
    }
    private static void VerifyGameTweaksSnapshotManagement()
    {
        foreach (var theme in new[] { Theme.Dark, Theme.Light })
        {
            App.Settings.Prop.Theme = theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
            var owner = new Froststrap.UI.Elements.Settings.MainWindow(false) { Width = 900, Height = 850 }; owner.Show(); RunFor(70);
            var vm = (Froststrap.UI.ViewModels.Settings.MainWindowViewModel)owner.DataContext!; vm.NavigateRegistered("fastflags"); RunFor(70);
            var entry = owner.GetVisualDescendants().OfType<CardAction>().Single(control => control.Header == "Manage experience profiles"); ClickSearchPopup(entry); RunFor(60);
            var dialog = owner.OwnedWindows.OfType<FlagProfilesDialog>().Single(); dialog.Width = 480; dialog.Height = 700;
            dialog.Tabs.SelectedIndex = 2; dialog.SaveProfile.Text = "Game Tweaks snapshot " + theme; RunFor(60); ClickSearchPopup(dialog.OkButton); RunFor(60);
            Check(dialog.Tabs.SelectedIndex == 1 && File.Exists(FlagProfileFiles.Resolve("Game Tweaks snapshot " + theme)), "Game Tweaks snapshot save returns to its real assignment controls: " + theme);
            string name = (string)dialog.PlaceProfile.SelectedItem!; dialog.NewPlaceIdTextBox.Text = "20261022"; ClickSearchPopup(dialog.AddPlaceButton); RunFor(50);
            dialog.Tabs.SelectedIndex = 3; dialog.LoadProfile.SelectedItem = name; RunFor(50);
            Check(dialog.LoadTab.IsEffectivelyVisible && !dialog.OkButton.IsVisible && !dialog.ClearFlags.IsVisible && dialog.DeleteButton.IsVisible, "real Game Tweaks entry exposes snapshot management without a global-load action: " + theme);
            Capture(dialog, theme, "revision18-game-tweaks-snapshots");
            string replacement = "Renamed Game Tweaks snapshot " + theme; dialog.RenameTextBox.Text = replacement; dialog.RenameTextBox.Focus();
            dialog.KeyPress(Key.Enter, RawInputModifiers.None, default, null); dialog.KeyRelease(Key.Enter, RawInputModifiers.None, default, null); RunFor(60);
            Check(App.Settings.Prop.ProfilePlaceIds[replacement].Single() == "20261022" && (string?)dialog.LoadProfile.SelectedItem == replacement, "actual keyboard rename in Game Tweaks follows persisted experience assignments: " + theme);
            ClickSearchPopup(dialog.UpdateProfileButton); RunFor(60);
            Check(dialog.ProfileFeedback.Text!.Contains("Snapshot replaced", StringComparison.Ordinal) && App.Settings.Prop.ProfilePlaceIds.ContainsKey(replacement), "Game Tweaks can replace an assigned snapshot while retaining the assignment: " + theme);
            ClickSearchPopup(dialog.DeleteButton); RunFor(70);
            Check(!App.Settings.Prop.ProfilePlaceIds.ContainsKey(replacement) && !File.Exists(FlagProfileFiles.Resolve(replacement)), "Game Tweaks actual delete removes the snapshot and assignment together: " + theme);
            ClickSearchPopup(dialog.CancelProfileButton); RunFor(50); vm.SaveSettings(); owner.Close();
        }
    }
    private static void RenderProfileEditing(Theme theme, int width)
    {
        App.Settings.Prop.Theme = theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
        string name = "Evening performance settings " + theme + width + ".json";
        FlagProfileFiles.SaveNew(name, App.FastFlags.Prop);
        var dialog = new FlagProfilesDialog { Width = width, Height = 740 }; dialog.Show(); RunFor(80);
        dialog.Tabs.SelectedIndex = 3; dialog.LoadProfile.SelectedItem = name; RunFor(60);
        Check(dialog.RenameTextBox.Bounds.Width > width / 2 && dialog.CopyProfileButton.IsEnabled && dialog.UpdateProfileButton.IsEnabled, "narrow profile name has usable width and enabled editing actions: " + theme + width);
        Capture(dialog, theme, "revision18-profile-editing-" + width);
        string replacement = "Renamed " + theme + width + ".json";
        dialog.RenameTextBox.Text = replacement; ClickSearchPopup(dialog.RenameProfileButton); RunFor(70);
        Check((string?)dialog.LoadProfile.SelectedItem == replacement && File.Exists(FlagProfileFiles.Resolve(replacement)), "actual Rename pointer action refreshes selection: " + theme + width);
        using (var locked = new FileStream(FlagProfileFiles.Resolve(replacement), FileMode.Open, FileAccess.Read, FileShare.Read))
        { ClickSearchPopup(dialog.DeleteButton); RunFor(60); }
        Check(dialog.IsVisible && dialog.ProfileFeedback.Text!.Contains("Could not complete", StringComparison.Ordinal) && dialog.DeleteButton.IsEnabled, "delete sharing failure stays visible and retryable in the real dialog: " + theme + width);
        Capture(dialog, theme, "revision18-profile-delete-error-" + width);
        string clipboardValue = "Quoted \"value\"\nwith slash \\ and Unicode café";
        File.WriteAllText(FlagProfileFiles.Resolve(replacement), JsonSerializer.Serialize(new Dictionary<string, object> { ["Unknown\"key"] = clipboardValue, ["DFIntCSGLevelOfDetailSwitchingDistance"] = 200 }));
        ClickSearchPopup(dialog.CopyProfileButton); RunFor(120);
        string copiedText = AwaitResult(dialog.Clipboard!.TryGetTextAsync()) ?? throw new InvalidDataException("Clipboard text was not published.");
        var copied = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(copiedText)!;
        Check(copied["Unknown\"key"].GetString() == clipboardValue && copied["DFIntCSGLevelOfDetailSwitchingDistance"].GetInt32() == 200 && dialog.ProfileFeedback.Text!.Contains("copied", StringComparison.Ordinal), "actual awaited clipboard copy preserves escaped JSON and numeric types: " + theme + width);
        File.WriteAllText(FlagProfileFiles.Resolve(replacement), "{broken");
        string flagsBefore = JsonSerializer.Serialize(App.FastFlags.Prop);
        ClickSearchPopup(dialog.OkButton); RunFor(120);
        Check(dialog.IsVisible && dialog.ProfileFeedback.Text!.Contains("Could not complete", StringComparison.Ordinal) && JsonSerializer.Serialize(App.FastFlags.Prop) == flagsBefore && dialog.Tabs.IsEnabled, "malformed load preserves edits and leaves actions usable: " + theme + width);
        Capture(dialog, theme, "revision18-profile-load-error-" + width);
        ClickSearchPopup(dialog.UpdateProfileButton); RunFor(60);
        Check(dialog.ProfileFeedback.Text!.Contains("Snapshot replaced", StringComparison.Ordinal), "actual replace action recovers the invalid snapshot using current supported tweaks: " + theme + width);
        var pending = AwaitResult(FlagProfileFiles.ReadAsync(replacement, CancellationToken.None)); pending["DFIntCSGLevelOfDetailSwitchingDistance"] = "125";
        FlagProfileFiles.Update(replacement, pending); ClickSearchPopup(dialog.OkButton); RunFor(120);
        Check(dialog.IsVisible && App.FastFlags.GetValue("DFIntCSGLevelOfDetailSwitchingDistance") == "125" && dialog.ProfileFeedback.Text!.Contains("pending", StringComparison.Ordinal), "successful load keeps a concrete reviewable pending-state confirmation: " + theme + width);
        Capture(dialog, theme, "revision18-profile-pending-load-" + width);
        App.FastFlags.Undo();
        if (width == 480) { dialog.SetRenderScaling(1.5); RunFor(60); Capture(dialog, theme, "revision18-profile-150"); dialog.SetRenderScaling(1); }
        ClickSearchPopup(dialog.DeleteButton); RunFor(80);
        Check(!File.Exists(FlagProfileFiles.Resolve(replacement)) && dialog.LoadProfile.SelectedItem == null && !dialog.CopyProfileButton.IsEnabled, "successful delete retry clears stale selection and editing actions: " + theme + width);
        dialog.Tabs.SelectedIndex = 2; dialog.SaveProfile.Text = "Keyboard save " + theme + width; dialog.SaveProfile.Focus();
        dialog.KeyPress(Key.Enter, RawInputModifiers.None, default, null); dialog.KeyRelease(Key.Enter, RawInputModifiers.None, default, null); RunFor(70);
        Check(dialog.Tabs.SelectedIndex == 3 && File.Exists(FlagProfileFiles.Resolve("Keyboard save " + theme + width)), "Enter saves the advanced snapshot once through the same validated path: " + theme + width);
        dialog.KeyPress(Key.Escape, RawInputModifiers.None, default, null); RunFor(40);
        Check(!dialog.IsVisible, "Escape closes the advanced profile dialog after completed operations: " + theme + width);
    }
}
