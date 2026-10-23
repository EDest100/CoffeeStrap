// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Text.Json;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Froststrap;
using Froststrap.Enums;
using Froststrap.Enums.Messagebox;
using Froststrap.Models.SettingTasks;
using Froststrap.Models.SettingTasks.Base;
using Froststrap.UI.Elements.Controls;
using Froststrap.UI.Utility;
using Froststrap.UI.ViewModels.Settings;
using SettingsWindow = Froststrap.UI.Elements.Settings.MainWindow;

internal static partial class Program
{
    private sealed class SaveTaskFixture(string name) : BaseTask(name)
    {
        internal int Executions;
        public override bool Changed => true;
        public override void Execute() => Executions++;
    }
    private static void ProbeRevision21()
    {
        App.LaunchSettings.Silent = true; App.LaunchSettings.TestModeFlag.Active = true;
        App.Settings.Save(); App.State.Save(); App.FastFlags.Save();
        var vm = new MainWindowViewModel(); int notices = 0, closes = 0;
        vm.RequestSaveNoticeEvent += (_, _) => notices++; vm.RequestCloseWindowEvent += (_, _) => closes++;
        var task = new SaveTaskFixture("Isolated save gate"); App.PendingSettingTasks[task.Name] = task;
        App.Settings.Prop.DisableAnimations = !App.Settings.Prop.DisableAnimations;
        string before = File.ReadAllText(App.Settings.FileLocation);
        using (var locked = new FileStream(App.Settings.FileLocation, FileMode.Open, FileAccess.Read, FileShare.Read)) vm.SaveAndLaunchSettings();
        var report = new { fixtureOnly = true, saveNotices = notices, closeRequests = closes, nextAction = vm.CloseAction.ToString(), tasksExecuted = task.Executions,
            unsavedEdits = App.Settings.HasUnsavedChanges, settingsUnchanged = File.ReadAllText(App.Settings.FileLocation) == before, gameLaunched = false };
        File.WriteAllText(Path.Combine(_output, "main-save-reproduction.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(report));
    }
    private static void SaveFixture()
    {
        App.LaunchSettings.Silent = true; App.LaunchSettings.TestModeFlag.Active = true;
        App.PendingSettingTasks.Clear(); App.Settings.Prop.LockMaximumRenderDistance = true;
        App.Settings.Prop.DisableAnimations = true; App.Settings.Save(false); App.State.Save(false);
        App.FastFlags.Prop = new() { ["DFIntDebugFRMQualityLevelOverride"] = "1" }; App.FastFlags.Save(false);
        App.GlobalSettings.CreateTemplate(); App.GlobalSettings.Load(); App.GlobalSettings.Save();
    }
    private static void VerifyRevision21()
    {
        string settings = JsonSerializer.Serialize(App.Settings.Prop), state = JsonSerializer.Serialize(App.State.Prop);
        var flags = App.FastFlags.Prop.ToDictionary(pair => pair.Key, pair => pair.Value);
        var document = App.GlobalSettings.Document == null ? null : new XDocument(App.GlobalSettings.Document);
        var pending = App.PendingSettingTasks.ToArray(); bool silent = App.LaunchSettings.Silent, test = App.LaunchSettings.TestModeFlag.Active;
        try
        {
            VerifySaveGates(); VerifySaveFiles(); VerifyFlagArchiveFailure(); VerifyPendingFileRetry();
            foreach (var theme in new[] { Theme.Dark, Theme.Light }) foreach (int width in new[] { 760, 1100 }) RenderSaveRecovery(theme, width);
            VerifySaveRecoveryMotion();
            SaveFixture(); var vm = new MainWindowViewModel(); Check(vm.SaveSettings() && SavedSlider() == "10" && App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride") == "1", "main-save recovery keeps the saved native 10 and FRM 1 pairing");
            File.WriteAllText(Path.Combine(_output, "main-save-recovery-probe.json"), JsonSerializer.Serialize(new { fixtureOnly = true, actualWindowsLocks = OperatingSystem.IsWindows(), launchRestartCloseGated = true, jsonAndXmlStaged = true, actualModTaskRetried = true, savedSlider = SavedSlider(), frm = App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride"), gameLaunched = false }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            App.PendingSettingTasks.Clear(); foreach (var pair in pending) App.PendingSettingTasks[pair.Key] = pair.Value;
            App.Settings.Prop = JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(settings)!; App.Settings.Save(false);
            App.State.Prop = JsonSerializer.Deserialize<Froststrap.Models.Persistable.State>(state)!; App.State.Save(false);
            App.FastFlags.Prop = flags; App.FastFlags.Save(false);
            if (document != null) { App.GlobalSettings.Document = document; App.GlobalSettings.Save(); }
            App.LaunchSettings.Silent = silent; App.LaunchSettings.TestModeFlag.Active = test;
            AppearanceViewModel.DisableAnimations = true; CoffeeMotion.TimeSource = TimeProvider.System; _manualTime = null;
        }
    }
    private static void VerifySaveGates()
    {
        foreach (string kind in new[] { "preferences", "state", "flags", "XML" })
        {
            SaveFixture(); var vm = new MainWindowViewModel(); int notices = 0, closes = 0;
            vm.RequestSaveNoticeEvent += (_, _) => notices++; vm.RequestCloseWindowEvent += (_, _) => closes++;
            App.Settings.Prop.ShowGameInTitle = !App.Settings.Prop.ShowGameInTitle; App.State.Prop.LastSchedulingResult = "Pending " + kind;
            App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistance", "500"); App.GlobalSettings.SetPreset("Audio.MasterVolume", "0.25");
            var task = new SaveTaskFixture("Deferred " + kind); App.PendingSettingTasks[task.Name] = task;
            string path = kind switch { "preferences" => App.Settings.FileLocation, "state" => App.State.FileLocation, "flags" => App.FastFlags.FileLocation, _ => GBSEditor.FileLocation };
            string before = File.ReadAllText(path);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Check(!vm.SaveSettings() && vm.HasSaveError && notices == 0 && File.ReadAllText(path) == before, "main Save reports failure without success or target truncation: " + kind);
                vm.SaveAndLaunchSettings(); vm.RestartApp();
                Check(closes == 0 && vm.CloseAction == NextAction.Terminate && task.Executions == 0 && App.PendingSettingTasks.ContainsKey(task.Name), "failed persistence blocks launch, restart and pending file tasks: " + kind);
                Check(!vm.CanCloseAfterPrompt(MessageBoxResult.Yes) && !vm.CanCloseAfterPrompt(MessageBoxResult.Cancel) && !vm.CanCloseAfterPrompt(MessageBoxResult.None), "Save on close and cancelled/empty prompt results keep unsaved edits open: " + kind);
                Check(vm.CanCloseAfterPrompt(MessageBoxResult.No) && MainWindowViewModel.HasUnsavedChanges && task.Executions == 0, "explicit Discard permits close without applying remaining file tasks: " + kind);
            }
            Check(vm.SaveSettings() && !vm.HasSaveError && notices == 1 && task.Executions == 1 && !MainWindowViewModel.HasUnsavedChanges, "save retry finishes once and clears feedback after unlocking: " + kind);
            Check(vm.SaveSettings() && task.Executions == 1 && notices == 2, "unchanged saves succeed without replaying completed tasks: " + kind);
            if (kind == "XML") Check(vm.CanCloseAfterPrompt(MessageBoxResult.Yes) && task.Executions == 1, "Save on close is permitted after a successful retry without replaying file tasks");
        }
        SaveFixture(); var launch = new MainWindowViewModel(); int launchCloses = 0; launch.RequestCloseWindowEvent += (_, _) => launchCloses++;
        launch.SaveAndLaunchSettings();
        Check(launch.CloseAction == NextAction.LaunchRoblox && launchCloses == 1, "successful Save and launch retains the existing test-mode next-action route without starting Roblox");
        var restart = new MainWindowViewModel(); int restartCloses = 0; restart.RequestCloseWindowEvent += (_, _) => restartCloses++; restart.RestartApp();
        Check(restart.CloseAction == NextAction.LaunchSettings && restartCloses == 1, "successful Save and restart retains the existing close action");
    }
    private static void VerifySaveFiles()
    {
        string root = Path.Combine(Paths.Base, "revision21-files"); Directory.CreateDirectory(root);
        var manager = new JsonManager<Froststrap.Models.Persistable.Settings>("revision21-files/preferences"); manager.Save(false);
        string before = File.ReadAllText(manager.FileLocation), hash = manager.LastFileHash!; manager.Prop.ShowGameInTitle = !manager.Prop.ShowGameInTitle;
        using (var locked = new FileStream(manager.FileLocation, FileMode.Open, FileAccess.Read, FileShare.Read))
            Check(!manager.Save(false) && manager.HasUnsavedChanges && manager.LastFileHash == hash && File.ReadAllText(manager.FileLocation) == before, "full JSON save sharing failure preserves bytes, saved hash and dirty state");
        Check(!Directory.GetFiles(root, ".*.tmp").Any() && manager.Save(false) && !manager.HasUnsavedChanges, "full JSON staging cleans up after failure and retry succeeds");
        File.Delete(manager.FileLocation);
        Check(manager.Save(false) && File.Exists(manager.FileLocation) && !manager.HasUnsavedChanges, "explicit Save recreates an externally removed unchanged JSON file");
        File.WriteAllText(Path.Combine(root, "blocked"), "keep"); var blocked = new JsonManager<Froststrap.Models.Persistable.Settings>("revision21-files/blocked/preferences");
        Check(!blocked.Save(false) && blocked.HasUnsavedChanges && File.ReadAllText(Path.Combine(root, "blocked")) == "keep", "unwritable parent creation is a reported save failure rather than an uncaught exception");
        SaveFixture(); string xml = File.ReadAllText(GBSEditor.FileLocation); App.GlobalSettings.SetPreset("Audio.MasterVolume", "0.41");
        using (var locked = new FileStream(GBSEditor.FileLocation, FileMode.Open, FileAccess.Read, FileShare.Read))
            Check(!App.GlobalSettings.Save() && App.GlobalSettings.HasUnsavedChanges && File.ReadAllText(GBSEditor.FileLocation) == xml, "native XML replacement failure retains the complete previous document and pending edit");
        Check(!Directory.GetFiles(Path.GetDirectoryName(GBSEditor.FileLocation)!, ".*.tmp").Any() && App.GlobalSettings.Save() && !App.GlobalSettings.HasUnsavedChanges, "native XML uses unique staged files with cleanup and successful retry");
        var loaded = new GBSEditor(); loaded.Load();
        Check(loaded.GetPreset("Audio.MasterVolume") == "0.41" && loaded.GetPreset("Rendering.SavedQualityLevel") == "10", "staged XML remains readable by the real editor with unrelated quality intact");
        File.Delete(GBSEditor.FileLocation);
        Check(App.GlobalSettings.Save() && File.Exists(GBSEditor.FileLocation), "explicit Save recreates a removed loaded native XML file");
    }
    private static void VerifyFlagArchiveFailure()
    {
        SaveFixture(); var vm = new MainWindowViewModel();
        string folder = Path.Combine(Paths.Base, "FlagBackups"), held = Path.Combine(Paths.Base, "FlagBackups-held-" + Guid.NewGuid().ToString("N"));
        if (Path.GetDirectoryName(Path.GetFullPath(folder)) != Path.GetFullPath(Paths.Base) || Path.GetDirectoryName(Path.GetFullPath(held)) != Path.GetFullPath(Paths.Base)) throw new InvalidOperationException("Archive fixture escaped its isolated root.");
        bool existed = Directory.Exists(folder);
        if (existed) Directory.Move(folder, held);
        try
        {
            File.WriteAllText(folder, "fixture directory blocker"); App.FastFlags.Prop["FFlagUnsupportedSaveFixture"] = "True";
            string flags = File.ReadAllText(App.FastFlags.FileLocation);
            Check(!vm.SaveSettings() && vm.SaveError.Contains("Game Tweaks", StringComparison.Ordinal) && App.FastFlags.Prop.ContainsKey("FFlagUnsupportedSaveFixture") && File.ReadAllText(App.FastFlags.FileLocation) == flags, "unsupported-flag archive preparation failure is reported before values or saved flags are discarded");
        }
        finally
        {
            if (File.Exists(folder)) File.Delete(folder);
            if (existed) Directory.Move(held, folder);
        }
        Check(vm.SaveSettings() && !vm.HasSaveError && !App.FastFlags.Prop.ContainsKey("FFlagUnsupportedSaveFixture") && Directory.GetFiles(folder).Any(path => File.ReadAllText(path).Contains("FFlagUnsupportedSaveFixture", StringComparison.Ordinal)), "archive retry preserves the held flag in its actual backup before saving supported values");
    }
    private static void VerifyPendingFileRetry()
    {
        SaveFixture(); var vm = new MainWindowViewModel(); int notices = 0; vm.RequestSaveNoticeEvent += (_, _) => notices++;
        string relative = Path.Combine("revision21", "fixture-place.rbxl"), target = Path.Combine(Paths.Modifications, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllText(target, "fixture original");
        var first = new SaveTaskFixture("Completed before file failure"); App.PendingSettingTasks[first.Name] = first;
        var actual = new ModPresetTask("Save recovery fixture", relative, "OldAvatarBackground.rbxl") { NewState = true };
        var after = new SaveTaskFixture("After file failure"); App.PendingSettingTasks[after.Name] = after;
        using (var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
            Check(!vm.SaveSettings() && vm.HasSaveError && notices == 0 && first.Executions == 1 && after.Executions == 0 && App.PendingSettingTasks.ContainsKey(actual.Name), "a real locked mod-file task is retained and later tasks wait without a false success");
        Check(File.ReadAllText(target) == "fixture original" && !App.PendingSettingTasks.ContainsKey(first.Name), "failed mod task preserves locked bytes and removes only earlier completed tasks");
        Check(vm.SaveSettings() && notices == 1 && first.Executions == 1 && after.Executions == 1 && actual.OriginalState && File.ReadAllBytes(target).Length > 100, "retry applies the actual bundled mod file and does not replay completed tasks");
        actual.NewState = false;
        Check(vm.SaveSettings() && !File.Exists(target) && !MainWindowViewModel.HasUnsavedChanges, "the same real mod task can disable and remove its owned fixture asset after recovery");
    }
    private static void SaveClick(Control control)
    { control.BringIntoView(); RunFor(60); ClickSearchPopup(control); RunFor(80); }
    private static void RenderSaveRecovery(Theme theme, int width)
    {
        SaveFixture(); App.Settings.Prop.Theme = theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
        var window = new SettingsWindow(false) { Width = width, Height = 700 }; window.Show(); var owner = (MainWindowViewModel)window.DataContext!; owner.NavigateRegistered("fastflags"); RunFor(80);
        App.Settings.Prop.ShowGameInTitle = !App.Settings.Prop.ShowGameInTitle;
        using (var locked = new FileStream(App.Settings.FileLocation, FileMode.Open, FileAccess.Read, FileShare.Read))
            SaveClick(window.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, owner.SaveSettingsCommand) && button.Name != "RetrySaveButton"));
        var reveal = window.FindControl<AnimatedReveal>("SaveErrorReveal")!; var retry = window.FindControl<Button>("RetrySaveButton")!; var text = window.FindControl<TextBlock>("SaveErrorText")!;
        Check(owner.HasSaveError && reveal.RevealedHeight > 0 && retry.IsEffectivelyVisible && text.Text == owner.SaveError, "real Save exposes bound persistent retry feedback: " + theme + " " + width);
        Capture(window, theme, "revision21-save-error-" + width);
        window.Height = 415; RunFor(70);
        Check(retry.TranslatePoint(new Point(retry.Bounds.Width, retry.Bounds.Height), window)!.Value.Y < window.Bounds.Height && text.Bounds.Height > 0, "save recovery and footer remain reachable at minimum window height: " + theme + " " + width);
        Capture(window, theme, "revision21-save-error-minimum-" + width);
        if (width == 760) { window.Height = 700; window.SetRenderScaling(1.5); RunFor(80); Capture(window, theme, "revision21-save-error-150"); window.SetRenderScaling(1); }
        SaveClick(retry);
        Check(!owner.HasSaveError && reveal.RevealedHeight < .1 && !MainWindowViewModel.HasUnsavedChanges, "real Retry save persists pending edits and collapses feedback: " + theme + " " + width);
        if (width == 760)
        {
            owner.NavigateRegistered("home"); window.Height = 415; RunFor(70);
            App.Settings.Prop.ShowGameInTitle = !App.Settings.Prop.ShowGameInTitle; App.State.Prop.LastSchedulingResult = "Multi-file pending " + theme;
            App.FastFlags.SetValue("DFIntCSGLevelOfDetailSwitchingDistance", "777"); App.GlobalSettings.SetPreset("Audio.MasterVolume", "0.4");
            using (var settingsLock = new FileStream(App.Settings.FileLocation, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var stateLock = new FileStream(App.State.FileLocation, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var flagsLock = new FileStream(App.FastFlags.FileLocation, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var nativeLock = new FileStream(GBSEditor.FileLocation, FileMode.Open, FileAccess.Read, FileShare.Read))
                ((HomeViewModel)owner.CurrentPage!).LaunchCommand.Execute(null);
            RunFor(70);
            Check(owner.IsHome && owner.HasSaveError && owner.SaveError.Contains("Game Tweaks", StringComparison.Ordinal) && owner.SaveError.Contains("launcher state", StringComparison.Ordinal) && owner.SaveError.Contains("Roblox saved settings", StringComparison.Ordinal) && owner.CloseAction == NextAction.Terminate && retry.IsEffectivelyVisible && !window.FindControl<Panel>("NotificationPanel")!.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == Froststrap.Resources.Strings.Menu_SettingsSaved_Title), "Home Play reports all failed files, removes stale success notices and exposes retry with its Save footer hidden: " + theme);
            Capture(window, theme, "revision21-home-save-error-minimum");
            retry.Focus(NavigationMethod.Tab); window.KeyPress(Key.Enter, RawInputModifiers.None, default, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, default, null); RunFor(80);
            Check(!owner.HasSaveError && !MainWindowViewModel.HasUnsavedChanges && owner.CloseAction == NextAction.Terminate, "keyboard Retry save on Home persists all files without launching automatically: " + theme);
        }
        window.Close();
    }
    private static void VerifySaveRecoveryMotion()
    {
        SaveFixture(); _manualTime = new ManualMotionTime(); CoffeeMotion.TimeSource = _manualTime;
        var window = new SettingsWindow(false) { Width = 760, Height = 700 }; window.Show(); var owner = (MainWindowViewModel)window.DataContext!; owner.NavigateRegistered("fastflags"); RunFor(70);
        AppearanceViewModel.DisableAnimations = false; App.Settings.Prop.ShowGameInTitle = !App.Settings.Prop.ShowGameInTitle;
        using (var locked = new FileStream(App.Settings.FileLocation, FileMode.Open, FileAccess.Read, FileShare.Read)) owner.SaveSettings();
        var reveal = window.FindControl<AnimatedReveal>("SaveErrorReveal")!; var frames = new List<double>();
        for (int i = 0; i < 10; i++) { StepFrame(16); frames.Add(reveal.RevealedHeight); }
        Check(frames.Distinct().Count() > 2 && frames.Any(height => height > 0 && height < reveal.NaturalHeight), "save-error feedback renders finite intermediate animation frames");
        owner.SaveSettings(); StepFrame(16); AppearanceViewModel.DisableAnimations = true; RunFor(50);
        Check(!reveal.HasActiveMotion && reveal.RevealedHeight < .1, "runtime reduced motion settles interrupted save-error collapse");
        AppearanceViewModel.DisableAnimations = false; App.Settings.Prop.ShowGameInTitle = !App.Settings.Prop.ShowGameInTitle;
        using (var locked = new FileStream(App.Settings.FileLocation, FileMode.Open, FileAccess.Read, FileShare.Read)) owner.SaveSettings();
        StepFrame(16); App.Settings.Save(false); window.Close(); RunFor(40);
        Check(!reveal.HasActiveMotion && window.FindControl<Panel>("NotificationPanel")!.Children.Count == 0, "closing a failed-save view stops feedback and releases queued notifications");
    }
}
