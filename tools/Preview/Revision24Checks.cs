// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Froststrap;
using Froststrap.Enums;
using Froststrap.Services;
using Froststrap.UI.Elements.Controls;
using Froststrap.UI.ViewModels.Settings;
using SettingsWindow = Froststrap.UI.Elements.Settings.MainWindow;

internal static partial class Program
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern uint LiveWindowStyle(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern uint SetLiveWindowStyle(nint window, int index, uint style);
    private static void VerifyRevision24()
    {
        App.GlobalSettings.CreateTemplate(); App.GlobalSettings.Load();
        var graphics = new GlobalSettingsViewModel { LockMaximumRenderDistance = true, GraphicsQuality = 10, RenderDistance = 1 };
        App.FastFlags.Save();
        string flagsBefore = File.ReadAllText(App.FastFlags.FileLocation), xmlBefore = File.ReadAllText(GBSEditor.FileLocation);
        string previousSettings = JsonSerializer.Serialize(App.Settings.Prop);
        nint first = CreateSizingWindow(0, "STATIC", "Owned live-size fixture", 0xCF0000, 100, 100, 1100, 760, 0, 0, 0, 0);
        nint second = CreateSizingWindow(0, "STATIC", "Other owned live-size fixture", 0xCF0000, 160, 160, 850, 650, 0, 0, 0, 0);
        Check(first != 0 && second != 0, "live sizing creates only its own two hidden test windows");
        GetWindowRect(first, out var original); GetWindowRect(second, out var otherOriginal);
        using var process = Process.GetCurrentProcess();
        var target = new RunningPlayerWindow(process.Id, process.StartTime.ToUniversalTime().Ticks, first, "Owned native fixture");
        var other = target with { Handle = second, Title = "Other owned fixture" };
        HardwareTuning.Snapshot hardware = new("Fixture CPU", "Fixture GPU", []);
        IReadOnlyList<RunningPlayerWindow> found = []; bool valid = true; Exception? lookupError = null;
        PlatformTweaksViewModel Model(Func<RunningPlayerWindow, bool>? current = null) => new(hardware, () => hardware, () => null, _ => throw new InvalidOperationException("No shell or game launch is permitted."),
            () => lookupError == null ? found : throw lookupError, current ?? (candidate => valid && candidate.ProcessId == process.Id && candidate.StartedUtcTicks == target.StartedUtcTicks && candidate.Handle == first));
        try
        {
            using var vm = Model(); vm.WindowSize = 4;
            Check(!vm.ApplyLiveWindowSizeCommand.CanExecute(null) && !vm.RestoreLiveWindowSizeCommand.CanExecute(null), "live controls start disabled without a selected player or restore point");
            vm.RefreshLivePlayersCommand.Execute(null);
            Check(vm.LivePlayers.Count == 0 && vm.LiveWindowStatus.Contains("No accessible", StringComparison.Ordinal), "no-player state reports absence without changing any game");
            found = [target, other]; vm.RefreshLivePlayersCommand.Execute(null);
            Check(vm.SelectedLivePlayer == null && vm.LivePlayers.Count == 2, "multiple players require an explicit selection");
            vm.SelectedLivePlayer = target with { ProcessId = int.MaxValue };
            Check(vm.SelectedLivePlayer == null, "foreign player selections are rejected");
            vm.SelectedLivePlayer = target; Await(vm.ApplyLiveWindowSizeCommand.ExecuteAsync(null));
            GetWindowRect(first, out var applied); GetWindowRect(second, out var otherAfter);
            Check(applied != original && otherAfter == otherOriginal && vm.LiveWindowStatus.Contains("client pixels", StringComparison.Ordinal), "live Apply changes only the selected native window and confirms client dimensions");
            Check(vm.LiveHasRestore && !vm.CanChooseLivePlayer && !vm.RefreshLivePlayersCommand.CanExecute(null), "an active restore point holds its player selection");
            vm.SelectedLivePlayer = other; Check(vm.SelectedLivePlayer == target, "changing a different player cannot silently discard an active restore point");
            vm.WindowSize = 3; Await(vm.ApplyLiveWindowSizeCommand.ExecuteAsync(null));
            Check(vm.LiveHasRestore, "reapplying a preset retains the first original geometry");
            Await(vm.RestoreLiveWindowSizeCommand.ExecuteAsync(null)); GetWindowRect(first, out var restored);
            Check(restored == original && !vm.LiveHasRestore && vm.LiveWindowStatus.StartsWith("Restored", StringComparison.Ordinal), "live Restore confirms the original size and position across repeated preset changes");
            Check(JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(File.ReadAllText(App.Settings.FileLocation))!.PlayerWindowSize == 3, "the live preset selection remains saved for future launches");
            Check(File.ReadAllText(App.FastFlags.FileLocation) == flagsBefore && File.ReadAllText(GBSEditor.FileLocation) == xmlBefore, "live resize and restore leave saved graphics 10 and FRM 1 byte-for-byte unchanged");
            vm.WindowSize = 0; Check(!vm.ApplyLiveWindowSizeCommand.CanExecute(null), "Roblox default is not presented as an arbitrary live pixel size");
            vm.WindowSize = 4; Await(vm.ApplyLiveWindowSizeCommand.ExecuteAsync(null));
            SetWindowPos(first, 0, 150, 150, 900, 600, 0x14); RunFor(30);
            GetWindowRect(first, out var manual); Await(vm.RestoreLiveWindowSizeCommand.ExecuteAsync(null)); GetWindowRect(first, out var manualAfter);
            Check(manualAfter == manual && vm.LiveWindowStatus.Contains("later resize", StringComparison.Ordinal), "live Restore preserves a later manual move or resize");
            Await(vm.ApplyLiveWindowSizeCommand.ExecuteAsync(null)); GetWindowRect(first, out var kept);
            vm.KeepLiveWindowSizeCommand.Execute(null); vm.Dispose(); GetWindowRect(first, out var keptAfter);
            Check(keptAfter == kept && !vm.RestoreLiveWindowSizeCommand.CanExecute(null), "Keep and disposal retain the applied size and release the restore point");
            using (var closed = Model())
            {
                found = [target]; closed.RefreshLivePlayersCommand.Execute(null); closed.WindowSize = 4; valid = false;
                Await(closed.ApplyLiveWindowSizeCommand.ExecuteAsync(null)); GetWindowRect(first, out var staleAfter);
                Check(staleAfter == keptAfter && !closed.LiveHasRestore && closed.LiveWindowStatus.Contains("closed or changed", StringComparison.Ordinal), "stale player identity blocks all native resizing");
                valid = true; Await(closed.ApplyLiveWindowSizeCommand.ExecuteAsync(null)); valid = false;
                Await(closed.RestoreLiveWindowSizeCommand.ExecuteAsync(null));
                Check(!closed.LiveHasRestore && closed.LiveWindowStatus.Contains("No other window", StringComparison.Ordinal), "a closed or replaced player cannot redirect Restore to another window");
            }
            valid = true;
            using (var fullscreen = Model())
            {
                fullscreen.RefreshLivePlayersCommand.Execute(null); fullscreen.WindowSize = 4;
                uint style = LiveWindowStyle(first, -16); SetLiveWindowStyle(first, -16, style & ~0xC00000u);
                GetWindowRect(first, out var beforeMode);
                try
                {
                    Await(fullscreen.ApplyLiveWindowSizeCommand.ExecuteAsync(null)); GetWindowRect(first, out var afterMode);
                    Check(beforeMode == afterMode && !fullscreen.LiveHasRestore && fullscreen.LiveWindowStatus.Contains("windowed", StringComparison.OrdinalIgnoreCase), "captionless fullscreen-style windows are left unchanged with actionable feedback");
                }
                finally { SetLiveWindowStyle(first, -16, style); }
            }
            using (var denied = Model())
            {
                lookupError = new Win32Exception(5); denied.RefreshLivePlayersCommand.Execute(null);
                Check(denied.LivePlayers.Count == 0 && denied.LiveWindowStatus.Contains("Could not list", StringComparison.Ordinal), "player discovery access failures remain visible and retryable");
                lookupError = null; denied.RefreshLivePlayersCommand.Execute(null);
                Check(denied.SelectedLivePlayer == target, "discovery retry selects the sole available player");
            }
            PlatformTweaksViewModel? cancelled = null;
            bool closedDuringValidation = false;
            cancelled = Model(_ => { cancelled!.Dispose(); closedDuringValidation = true; return true; });
            cancelled.WindowSize = 4; cancelled.RefreshLivePlayersCommand.Execute(null); GetWindowRect(first, out var beforeCancel);
            int changesAfterClose = 0; cancelled.PropertyChanged += (_, _) => { if (closedDuringValidation) changesAfterClose++; };
            Await(cancelled.ApplyLiveWindowSizeCommand.ExecuteAsync(null)); GetWindowRect(first, out var afterCancel);
            Check(beforeCancel == afterCancel && !cancelled.LiveHasRestore && !cancelled.ApplyLiveWindowSizeCommand.CanExecute(null), "disposal during validation cancels before native application");
            Check(changesAfterClose == 0, "cancelled live work publishes no property notifications after disposal");
            Check(!RunningPlayerWindows.IsCurrent(target), "production discovery validation rejects a non-Roblox process even with a valid window");
            foreach (var theme in new[] { Theme.Dark, Theme.Light }) foreach (int width in new[] { 760, 1100 }) RenderLiveSizing(theme, width, target, hardware);
            File.WriteAllText(Path.Combine(_output, "live-window-sizing-probe.json"), JsonSerializer.Serialize(new { nativeWindowsOnly = true, runningRobloxChanged = false, memoryAccess = false, desktopSettingsChanged = false, liveFrameRateCap = false, gamePerformanceMeasured = false }));
        }
        finally { DestroyWindow(first); DestroyWindow(second); App.Settings.Prop = JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(previousSettings)!; App.Settings.Save(); }
    }
    private static void RenderLiveSizing(Theme theme, int width, RunningPlayerWindow target, HardwareTuning.Snapshot hardware)
    {
        App.Settings.Prop.Theme = theme; App.Settings.Prop.DisableAnimations = false; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme(); App.ApplyAnimationSettings();
        var window = new SettingsWindow(false) { Width = width, Height = 900 }; window.Show(); RunFor(60);
        var owner = (MainWindowViewModel)window.DataContext!; owner.NavigateRegistered("fastflags"); RunFor(40);
        var displayTarget = width == 760 ? target with { Title = "Owned native fixture · " + new string('W', 140) } : target;
        using var platform = new PlatformTweaksViewModel(hardware, () => hardware, () => null, _ => { }, () => [displayTarget], _ => true);
        (owner.CurrentPage as IDisposable)?.Dispose(); owner.CurrentPage = new CuratedFastFlagsViewModel(platform); RunFor(350);
        var page = (Control)window.FindControl<TransitioningContentControl>("PageContentControl")!.Content!;
        var group = page.GetVisualDescendants().OfType<CardExpander>().Single(item => Equals(item.Header, "Windowed render size")); group.IsExpanded = true; RunFor(60);
        var scroll = group.GetVisualAncestors().OfType<ScrollViewer>().First(); var point = group.TranslatePoint(default, scroll); if(point.HasValue) scroll.Offset = new Vector(0, scroll.Offset.Y + point.Value.Y - 12);
        RunFor(60); Capture(window, theme, "live-size-empty-" + width);
        var refresh = group.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Refresh players")); ClickSearchPopup(refresh); RunFor(30); platform.WindowSize = 4;
        var apply = group.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Apply to player now")); ClickSearchPopup(apply); RunFor(80);
        Check(platform.LiveHasRestore && apply.IsEffectivelyEnabled, "actual live sizing controls apply through their production bindings: " + theme + width);
        Check(group.GetVisualDescendants().OfType<Button>().Where(button => button.Content is string text && text is "Refresh players" or "Apply to player now" or "Restore previous size" or "Keep this size").All(button => button.Bounds.Width > 0 && button.Bounds.Width <= group.Bounds.Width), "live actions fit the actual narrow/wide layout: " + theme + width);
        Capture(window, theme, "live-size-applied-" + width);
        if (width == 760) { window.SetRenderScaling(1.5); RunFor(80); Capture(window, theme, "live-size-scaled"); window.SetRenderScaling(1); }
        var restore = group.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Restore previous size")); restore.Focus();
        window.KeyPress(Key.Enter, RawInputModifiers.None, default, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, default, null); RunFor(60);
        Check(!platform.LiveHasRestore && platform.LiveWindowStatus.Contains("original", StringComparison.Ordinal), "keyboard Enter restores through the actual focused live action: " + theme + width);
        App.Settings.Prop.DisableAnimations = true; App.ApplyAnimationSettings(); Await(platform.ApplyLiveWindowSizeCommand.ExecuteAsync(null)); RunFor(50);
        owner.NavigateRegistered("behaviour"); RunFor(50);
        Check(!platform.ApplyLiveWindowSizeCommand.CanExecute(null) && !platform.LiveHasRestore, "navigation cancels live actions and keeps the applied size under reduced motion: " + theme + width);
        window.Close();
    }
}
