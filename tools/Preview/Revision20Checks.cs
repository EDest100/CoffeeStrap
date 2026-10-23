// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Text.Json;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Froststrap;
using Froststrap.Enums;
using Froststrap.UI.ViewModels.Settings;
using SettingsWindow = Froststrap.UI.Elements.Settings.MainWindow;

internal static partial class Program
{
    private static string PerformanceBackup => Path.Combine(Paths.Base, "PerformanceProfileRollback.json");
    private static void PerformanceFixture()
    {
        App.Settings.Prop.LockMaximumRenderDistance = false; App.Settings.Save();
        App.GlobalSettings.CreateTemplate(); App.GlobalSettings.Load();
        App.GlobalSettings.SetPreset("Rendering.SavedQualityLevel", "3");
        App.GlobalSettings.SetPreset("Rendering.MaxQualityEnabled", true); App.GlobalSettings.Save();
        App.FastFlags.Prop.Clear(); App.FastFlags.SetValue("DFIntDebugFRMQualityLevelOverride", "7");
        App.FastFlags.SetValue("DFIntTextureQualityOverride", "3"); App.FastFlags.SetValue("FFlagDebugGraphicsPreferVulkan", "True");
        App.FastFlags.Save();
        if (File.Exists(PerformanceBackup)) File.Delete(PerformanceBackup);
    }
    private static string PerformanceState() => JsonSerializer.Serialize(new
    {
        flags = App.FastFlags.Prop, distance = App.Settings.Prop.LockMaximumRenderDistance,
        slider = App.GlobalSettings.GetPreset("Rendering.SavedQualityLevel"), quality = App.GlobalSettings.GetPreset("Rendering.MaxQualityEnabled")
    });
    private static void ProbeRevision20()
    {
        PerformanceFixture(); var vm = new PerformanceProfilesViewModel(); vm.ApplyCommand.Execute(null);
        string applied = PerformanceState(), backup = File.ReadAllText(PerformanceBackup);
        using (var locked = new FileStream(App.Settings.FileLocation, FileMode.Open, FileAccess.Read, FileShare.Read))
            vm.RestoreCommand.Execute(null);
        var restore = new { modelsChanged = applied != PerformanceState(), undoAvailable = File.Exists(PerformanceBackup), status = vm.Status };
        PerformanceFixture(); vm = new PerformanceProfilesViewModel(); vm.ApplyCommand.Execute(null);
        App.Settings.Prop.LockMaximumRenderDistance = false; App.Settings.TrySaveSetting(nameof(App.Settings.Prop.LockMaximumRenderDistance));
        App.FastFlags.SetValue("DFIntDebugFRMQualityLevelOverride", "5");
        string before = PerformanceState(); backup = File.ReadAllText(PerformanceBackup);
        using (var locked = new FileStream(App.Settings.FileLocation, FileMode.Open, FileAccess.Read, FileShare.Read))
            vm.ApplyCommand.Execute(null);
        var apply = new { modelsChanged = before != PerformanceState(), previousUndoReplaced = backup != File.ReadAllText(PerformanceBackup), status = vm.Status };
        var report = new { fixtureOnly = true, apply, restore };
        File.WriteAllText(Path.Combine(_output, "performance-profile-save-reproduction.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(report));
    }
    private static bool SavedDistance()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(App.Settings.FileLocation));
        return document.RootElement.GetProperty(nameof(App.Settings.Prop.LockMaximumRenderDistance)).GetBoolean();
    }
    private static bool NoPerformanceStages() => !Directory.EnumerateFiles(Paths.Base, "PerformanceProfileRollback.json.*").Any(path => Path.GetFileName(path).StartsWith("PerformanceProfileRollback.json.", StringComparison.Ordinal) && !path.EndsWith(".restored", StringComparison.Ordinal)) &&
        !Directory.EnumerateFiles(Paths.Base, ".Settings.json.*.tmp").Any();
    private static void VerifyRevision20()
    {
        string settings = JsonSerializer.Serialize(App.Settings.Prop);
        var flags = App.FastFlags.Prop.ToDictionary(pair => pair.Key, pair => pair.Value);
        var document = App.GlobalSettings.Document == null ? null : new XDocument(App.GlobalSettings.Document);
        try
        {
            VerifyPerformanceWriteFailures();
            foreach (var theme in new[] { Theme.Dark, Theme.Light }) foreach (int width in new[] { 760, 1100 }) RenderPerformanceFailures(theme, width);
            File.WriteAllText(Path.Combine(_output, "performance-profile-recovery-probe.json"), JsonSerializer.Serialize(new
            {
                fixtureOnly = true, realWindowsSharingLocks = OperatingSystem.IsWindows(), checkedSaveFailures = true,
                previousUndoPreserved = true, backupDestinationRollback = true, restoredRetry = true,
                savedSlider = App.GlobalSettings.GetPreset("Rendering.SavedQualityLevel"), frm = App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride"), gameLaunched = false
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            App.Settings.Prop = JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(settings)!; App.Settings.Save();
            App.FastFlags.Prop = flags; App.FastFlags.Save();
            if (document != null) { App.GlobalSettings.Document = document; App.GlobalSettings.Save(); }
        }
    }
    private static void VerifyPerformanceWriteFailures()
    {
        PerformanceFixture(); var vm = new PerformanceProfilesViewModel(); int changes = 0; vm.Changed += () => changes++;
        string before = PerformanceState(), settings = File.ReadAllText(App.Settings.FileLocation), flags = File.ReadAllText(App.FastFlags.FileLocation), native = File.ReadAllText(GBSEditor.FileLocation);
        using (var locked = new FileStream(App.Settings.FileLocation, FileMode.Open, FileAccess.Read, FileShare.Read)) vm.ApplyCommand.Execute(null);
        Check(PerformanceState() == before && changes == 0 && vm.Status.StartsWith("Could not save", StringComparison.Ordinal), "failed profile preference save preserves all fourteen models and reports failure without a Changed event");
        Check(!File.Exists(PerformanceBackup) && !vm.RestoreCommand.CanExecute(null) && NoPerformanceStages(), "failed first Apply creates no Undo or staged-file residue");
        Check(File.ReadAllText(App.Settings.FileLocation) == settings && File.ReadAllText(App.FastFlags.FileLocation) == flags && File.ReadAllText(GBSEditor.FileLocation) == native, "failed profile Apply leaves all three saved settings files byte-identical");
        vm.ApplyCommand.Execute(null);
        Check(changes == 1 && SavedDistance() && App.GlobalSettings.GetPreset("Rendering.SavedQualityLevel") == "10" && App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride") == "1", "Apply retry succeeds after the settings lock clears with saved distance 10 and pending FRM 1");
        Check(File.ReadAllText(App.FastFlags.FileLocation) == flags && File.ReadAllText(GBSEditor.FileLocation) == native, "successful Apply still requires Save for native XML and flags");
        string backup = File.ReadAllText(PerformanceBackup);
        App.FastFlags.SetValue("DFIntDebugFRMQualityLevelOverride", "5"); App.Settings.Prop.LockMaximumRenderDistance = false;
        App.Settings.TrySaveSetting(nameof(App.Settings.Prop.LockMaximumRenderDistance)); before = PerformanceState();
        using (var locked = new FileStream(App.Settings.FileLocation, FileMode.Open, FileAccess.Read, FileShare.Read)) vm.ApplyCommand.Execute(null);
        Check(PerformanceState() == before && File.ReadAllText(PerformanceBackup) == backup && changes == 1, "failed subsequent Apply preserves the previous persistent Undo instead of replacing it");
        App.Settings.Prop.DisableAnimations = !App.Settings.Prop.DisableAnimations;
        bool pendingMotion = App.Settings.Prop.DisableAnimations;
        using (var locked = new FileStream(PerformanceBackup, FileMode.Open, FileAccess.Read, FileShare.Read)) vm.ApplyCommand.Execute(null);
        Check(PerformanceState() == before && !SavedDistance() && File.ReadAllText(PerformanceBackup) == backup && changes == 1, "a locked Apply backup rolls back the persisted distance preference before any render changes");
        Check(App.Settings.Prop.DisableAnimations == pendingMotion && App.Settings.HasUnsavedChanges && NoPerformanceStages(), "backup failure and partial-save rollback preserve unrelated pending settings and clean temporary files", new { pending = App.Settings.Prop.DisableAnimations == pendingMotion, unsaved = App.Settings.HasUnsavedChanges, stages = Directory.GetFiles(Paths.Base, "*ProfileRollback*").Select(Path.GetFileName).ToArray(), clean = NoPerformanceStages() });
        vm.ApplyCommand.Execute(null); before = PerformanceState(); backup = File.ReadAllText(PerformanceBackup); int appliedChanges = changes;
        Check(SavedDistance() && vm.RestoreCommand.CanExecute(null) && changes == 2, "Apply can retry after a locked existing backup is released");
        using (var locked = new FileStream(App.Settings.FileLocation, FileMode.Open, FileAccess.Read, FileShare.Read)) vm.RestoreCommand.Execute(null);
        Check(PerformanceState() == before && changes == appliedChanges && SavedDistance(), "failed Restore preference save leaves every rendering model and saved distance unchanged");
        Check(File.ReadAllText(PerformanceBackup) == backup && vm.RestoreCommand.CanExecute(null) && !File.Exists(PerformanceBackup + ".restored") && NoPerformanceStages(), "failed Restore reactivates the exact Undo backup and its enabled command for retry");
        var reopened = new PerformanceProfilesViewModel(); reopened.RestoreCommand.Execute(null);
        Check(!App.Settings.Prop.LockMaximumRenderDistance && !SavedDistance() && App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride") == "5" && !reopened.RestoreCommand.CanExecute(null), "a reopened view restores the preserved snapshot after the settings lock clears");
        Check(App.FastFlags.GetValue("FFlagDebugGraphicsPreferVulkan") == "True" && App.Settings.Prop.DisableAnimations == pendingMotion, "profile recovery preserves unrelated renderer and pending UI preferences");
        PerformanceFixture(); vm = new PerformanceProfilesViewModel(); before = PerformanceState();
        File.SetAttributes(App.Settings.FileLocation, File.GetAttributes(App.Settings.FileLocation) | FileAttributes.ReadOnly);
        try { vm.ApplyCommand.Execute(null); }
        finally { File.SetAttributes(App.Settings.FileLocation, File.GetAttributes(App.Settings.FileLocation) & ~FileAttributes.ReadOnly); }
        Check(PerformanceState() == before && !File.Exists(PerformanceBackup) && vm.Status.StartsWith("Could not save", StringComparison.Ordinal), "a read-only settings file blocks Apply without leaving partial state");
        settings = File.ReadAllText(App.Settings.FileLocation); File.WriteAllText(App.Settings.FileLocation, "[]"); vm.ApplyCommand.Execute(null);
        Check(PerformanceState() == before && File.ReadAllText(App.Settings.FileLocation) == "[]" && !File.Exists(PerformanceBackup), "malformed settings JSON fails closed before replacing a profile Undo backup");
        File.WriteAllText(App.Settings.FileLocation, settings); App.Settings.Load(false);
        vm.ApplyCommand.Execute(null); before = PerformanceState(); backup = File.ReadAllText(PerformanceBackup);
        File.WriteAllText(App.Settings.FileLocation, "[]"); vm.RestoreCommand.Execute(null);
        Check(PerformanceState() == before && File.ReadAllText(PerformanceBackup) == backup && vm.RestoreCommand.CanExecute(null) && File.ReadAllText(App.Settings.FileLocation) == "[]", "malformed settings JSON also keeps Restore retryable and preserves the invalid file for diagnosis");
        File.WriteAllText(App.Settings.FileLocation, settings); App.Settings.Load(false); vm.RestoreCommand.Execute(null);
        Check(!vm.RestoreCommand.CanExecute(null) && NoPerformanceStages(), "repairing settings JSON allows the original Restore to finish and retire its backup once");
    }
    private static void RenderPerformanceFailures(Theme theme, int width)
    {
        PerformanceFixture(); App.Settings.Prop.Theme = theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
        var window = new SettingsWindow(false) { Width = width, Height = 900 }; window.Show(); RunFor(60);
        var owner = (MainWindowViewModel)window.DataContext!; owner.NavigateRegistered("fastflags"); RunFor(60);
        var vm = (CuratedFastFlagsViewModel)owner.CurrentPage!;
        void Press(string content)
        {
            var button = window.GetVisualDescendants().OfType<Button>().Single(control => Equals(control.Content, content));
            button.BringIntoView(); RunFor(60); ClickSearchPopup(button); RunFor(60);
        }
        using (var locked = new FileStream(App.Settings.FileLocation, FileMode.Open, FileAccess.Read, FileShare.Read)) Press("Apply profile");
        Check(vm.Performance.Status.StartsWith("Could not save", StringComparison.Ordinal) && !vm.Performance.RestoreCommand.CanExecute(null), "actual Apply button shows retryable preference-write feedback: " + theme + " " + width);
        var status = window.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Text == vm.Performance.Status);
        status.BringIntoView(); RunFor(60);
        Check(status.Bounds.Height > 0 && (width != 760 || status.Bounds.Height > 20) && status.TextWrapping == Avalonia.Media.TextWrapping.Wrap && status.TranslatePoint(new Avalonia.Point(status.Bounds.Width, 0), window)!.Value.X < width, "profile write feedback fits the production page and wraps at narrow widths: " + theme + " " + width);
        Capture(window, theme, "revision20-apply-failure-" + width);
        Press("Apply profile"); string before = PerformanceState();
        using (var locked = new FileStream(App.Settings.FileLocation, FileMode.Open, FileAccess.Read, FileShare.Read)) Press("Restore previous profile");
        Check(PerformanceState() == before && vm.Performance.RestoreCommand.CanExecute(null) && vm.Performance.Status.StartsWith("Could not save", StringComparison.Ordinal), "actual Restore button keeps the previous values and retry enabled: " + theme + " " + width);
        status = window.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Text == vm.Performance.Status); status.BringIntoView(); RunFor(60);
        Capture(window, theme, "revision20-restore-failure-" + width);
        Press("Restore previous profile");
        Check(!vm.Performance.RestoreCommand.CanExecute(null) && App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride") == "7", "actual Restore retry refreshes shared graphics controls after recovery: " + theme + " " + width);
        Press("Apply profile"); owner.SaveSettings();
        Check(SavedSlider() == "10" && App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride") == "1" && SavedDistance(), "real main-window Save persists the confirmed slider 10 and FRM 1 pairing after recovery: " + theme + " " + width);
        window.Close();
    }
}
