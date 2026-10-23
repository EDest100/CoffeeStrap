// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
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
using Froststrap.UI.Elements.Settings.Pages;
using Froststrap.UI.ViewModels.Settings;
using SettingsWindow = Froststrap.UI.Elements.Settings.MainWindow;

internal static partial class Program
{
    private static void VerifyRevision16()
    {
        string root = Path.Combine(_output, "fixture-data", "graphics shortcuts");
        var panels = HardwareTuning.FindDriverPanels("NVIDIA GeForce · AMD Radeon", root, _ => true);
        Check(panels.Count == 3 && panels[0].Name == "NVIDIA app" && panels[2].Name == "AMD Software", "mixed GPUs expose both vendors and prefer the modern NVIDIA app");
        Check(HardwareTuning.FindDriverPanels("Intel Arc", root, _ => true).Count == 0 && HardwareTuning.FindDriverPanels("NVIDIA", root, _ => false).Count == 0, "unsupported or absent desktop panels keep the Windows fallback instead of inventing a launch target");
        var amdOnly = HardwareTuning.FindDriverPanels("Radeon", root, _ => true);
        Check(amdOnly.Count == 1 && amdOnly[0].Name == "AMD Software", "Radeon-only detection does not depend on an AMD CPU");
        string? player = Path.Combine(root, "version one", "RobloxPlayerBeta.exe");
        HardwareTuning.Snapshot hardware = new("Fixture Ryzen CPU", "Fixture NVIDIA + Radeon", panels);
        var launches = new List<ProcessStartInfo>(); Exception? failure = null;
        var vm = new PlatformTweaksViewModel(hardware, () => hardware, () => player, info => { if (failure != null) throw failure; launches.Add(info); });
        Check(vm.HasDriverPanel && vm.SelectedPanel == panels[0] && vm.HasPlayerPath && vm.HardwareLabel.Contains("Ryzen", StringComparison.Ordinal), "graphics model initializes hardware, path and selected panel together");
        vm.SelectedPanel = new("Unrelated", "arbitrary.exe"); Check(vm.SelectedPanel == panels[0], "foreign driver selections cannot launch arbitrary targets");
        vm.SelectedPanel = panels[2]; vm.OpenDriverPanelCommand.Execute(null);
        Check(launches.Count == 1 && launches[0].FileName == panels[2].Path && launches[0].UseShellExecute && vm.HardwareStatus.Contains("request", StringComparison.Ordinal), "driver action opens only the chosen installed target and reports a request rather than applied settings");
        vm.OpenWindowsGraphicsCommand.Execute(null);
        Check(launches[^1].FileName == "ms-settings:display-advancedgraphics" && launches[^1].UseShellExecute, "Windows graphics action uses the documented settings URI");
        vm.ShowPlayerFileCommand.Execute(null);
        Check(launches[^1].FileName.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase) && launches[^1].Arguments == "/select,\"" + player + "\"", "file action selects the installed executable with a quoted path and never launches Roblox");
        player = Path.Combine(root, "version two", "RobloxPlayerBeta.exe"); vm.ShowPlayerFileCommand.Execute(null);
        Check(launches[^1].Arguments.Contains("version two", StringComparison.Ordinal) && vm.PlayerPath == player, "show-file refreshes the current version instead of revealing a stale executable");
        int launched = launches.Count; player = null; vm.ShowPlayerFileCommand.Execute(null);
        Check(launches.Count == launched && !vm.HasPlayerPath && vm.HardwareStatus.Contains("Could not open", StringComparison.Ordinal), "missing player produces a recoverable status and no Explorer or game launch");
        foreach (var error in new Exception[] { new Win32Exception(5), new Win32Exception(2), new InvalidOperationException("No shell handler."), new UnauthorizedAccessException("Denied fixture.") })
        {
            failure = error; vm.OpenWindowsGraphicsCommand.Execute(null);
            Check(launches.Count == launched && vm.HardwareStatus.Contains("retry", StringComparison.Ordinal), "graphics shell errors remain visible and do not escape the command: " + error.GetType().Name + error.Message);
        }
        failure = null; vm.OpenWindowsGraphicsCommand.Execute(null);
        Check(launches.Count == launched + 1 && !vm.HardwareStatus.Contains("Could not open", StringComparison.Ordinal), "successful retry replaces an old graphics shortcut error");
        var changed = new HashSet<string>(); vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);
        vm.RefreshHardwareCommand.Execute(null);
        Check(vm.SelectedPanel == panels[2] && changed.Contains(nameof(vm.DriverPanels)) && changed.Contains(nameof(vm.PlayerPath)), "refresh preserves a still-installed selection and updates hardware/path bindings");
        hardware = new("Fixture CPU", "Intel graphics", [], "CPU information is unavailable."); vm.RefreshHardwareCommand.Execute(null);
        Check(!vm.HasDriverPanel && vm.SelectedPanel == null && vm.HardwareStatus == hardware.Warning && vm.PanelAvailability.Contains("Store", StringComparison.Ordinal), "refresh removes unavailable panels and preserves detection warnings with honest Store/custom-path feedback");
        vm.OpenDriverPanelCommand.Execute(null); Check(vm.HardwareStatus == vm.PanelAvailability, "missing driver command provides the Windows and Start fallback");
        var failing = new PlatformTweaksViewModel(hardware, () => throw new IOException("Detection fixture."), () => null, _ => { });
        failing.RefreshHardwareCommand.Execute(null);
        Check(failing.HardwareStatus.Contains("Detection fixture", StringComparison.Ordinal), "refresh failures retain the actual failure rather than overwriting it with an old warning");
        VerifyPlayerExecutablePath();
        var detected = HardwareTuning.Detect();
        Check(detected.Cpu.Length > 0 && detected.Gpu.Length > 0 && detected.Panels.All(panel => File.Exists(panel.Path)), "native hardware probe returns usable labels and only existing panel paths");
        File.WriteAllText(Path.Combine(_output, "graphics-shortcuts-probe.json"), JsonSerializer.Serialize(new { windows = OperatingSystem.IsWindows(), detected.Cpu, detected.Gpu, panels = detected.Panels.Select(panel => panel.Name), detected.Warning, nativeShellLaunched = false, fixtureShellRequests = launches.Count, settingsChanged = false, gameTested = false }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var theme in new[] { Theme.Dark, Theme.Light }) foreach (int width in new[] { 760, 1100 }) RenderGraphicsShortcuts(theme, width, panels);
    }
    private static void VerifyPlayerExecutablePath()
    {
        if (!File.Exists(App.PlayerState.FileLocation)) File.WriteAllText(App.PlayerState.FileLocation, "{}");
        string version = App.PlayerState.Prop.VersionGuid; bool isStatic = App.Settings.Prop.StaticDirectory;
        try
        {
            App.Settings.Prop.StaticDirectory = false; App.PlayerState.Prop.VersionGuid = "graphics-shortcut-version";
            var data = new Froststrap.AppData.RobloxPlayerData(); Directory.CreateDirectory(data.Directory); File.WriteAllText(data.ExecutablePath, "fixture, not executable");
            Check(HardwareTuning.PlayerExecutable() == Path.GetFullPath(data.ExecutablePath), "player profile path shares the launcher's dynamic version directory");
            App.Settings.Prop.StaticDirectory = true; Directory.CreateDirectory(data.Directory); File.WriteAllText(data.ExecutablePath, "fixture, not executable");
            Check(HardwareTuning.PlayerExecutable() == Path.GetFullPath(data.ExecutablePath), "player profile path shares the launcher's static directory mode");
            App.Settings.Prop.StaticDirectory = false; App.PlayerState.Prop.VersionGuid = "";
            Check(HardwareTuning.PlayerExecutable() == null, "an empty installed version does not expose an arbitrary root executable");
            string outside = Path.Combine(Paths.Base, "outside-versions"); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, App.RobloxPlayerAppName), "fixture, not executable");
            App.PlayerState.Prop.VersionGuid = outside;
            Check(HardwareTuning.PlayerExecutable() == null, "a rooted state path outside managed versions cannot become a profile target");
            App.PlayerState.Prop.VersionGuid = "invalid\0version";
            Check(HardwareTuning.PlayerExecutable() == null, "malformed installed paths fail closed without breaking hardware bindings");
        }
        finally { App.PlayerState.Prop.VersionGuid = version; App.Settings.Prop.StaticDirectory = isStatic; }
    }
    private static void RenderGraphicsShortcuts(Theme theme, int width, IReadOnlyList<HardwareTuning.Panel> panels)
    {
        App.Settings.Prop.Theme = theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
        var window = new SettingsWindow(false) { Width = width, Height = 900 }; window.Show(); RunFor(60);
        var owner = (MainWindowViewModel)window.DataContext!; owner.NavigateRegistered("fastflags"); RunFor(90);
        var page = window.GetVisualDescendants().OfType<FastFlagsPage>().Single(); var model = (CuratedFastFlagsViewModel)page.DataContext!;
        HardwareTuning.Snapshot hardware = new("AMD Ryzen fixture CPU — long hardware label with multiple devices", "NVIDIA GeForce fixture + AMD Radeon integrated graphics", panels);
        string? player = @"C:\Users\Player with spaces\AppData\Local\CoffeeStrap\Versions\version-long-installed-version-id\RobloxPlayerBeta.exe";
        int launched = 0;
        var vm = new PlatformTweaksViewModel(hardware, () => hardware, () => player, _ => { launched++; throw new Win32Exception(5); });
        typeof(CuratedFastFlagsViewModel).GetField("<Platform>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(model, vm);
        page.DataContext = null; page.DataContext = model;
        var groups = page.GetVisualDescendants().OfType<CardExpander>().ToArray(); foreach (var group in groups) group.IsExpanded = Equals(group.Header, "Hardware & window title");
        var selected = groups.Single(group => Equals(group.Header, "Hardware & window title")); RunFor(70);
        var scroller = selected.GetVisualAncestors().OfType<ScrollViewer>().First();
        void Position() { var point = selected.TranslatePoint(default, scroller); if (point.HasValue) scroller.Offset = new Vector(0, scroller.Offset.Y + point.Value.Y - 16); RunFor(70); }
        Position(); Capture(window, theme, "revision16-graphics-shortcuts-" + width);
        var combo = selected.GetVisualDescendants().OfType<ComboBox>().Single(); combo.Focus(NavigationMethod.Tab);
        window.KeyPress(Key.Down, RawInputModifiers.Alt, default, null); window.KeyRelease(Key.Down, RawInputModifiers.Alt, default, null);
        for (int i = 0; i < 2; i++) { window.KeyPress(Key.Down, RawInputModifiers.None, default, null); window.KeyRelease(Key.Down, RawInputModifiers.None, default, null); }
        window.KeyPress(Key.Enter, RawInputModifiers.None, default, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, default, null); RunFor(40);
        Check(vm.SelectedPanel == panels[2] && combo.ItemCount == 3, "actual hardware selector binds both vendors: " + theme + width);
        var path = selected.GetVisualDescendants().OfType<TextBox>().Single(text => text.IsReadOnly && text.Text == player); path.Focus();
        window.KeyPress(Key.A, RawInputModifiers.Control, default, null); window.KeyRelease(Key.A, RawInputModifiers.Control, default, null);
        Check(path.IsReadOnly && path.SelectedText == player, "actual Roblox path supports keyboard selection without editing: " + theme + width);
        var windowsButton = selected.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Windows graphics preferences"));
        ClickSearchPopup(windowsButton); RunFor(60);
        Check(launched == 1 && selected.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == vm.HardwareStatus && text.IsEffectivelyVisible), "actual hardware button displays shell failure inline: " + theme + width);
        Capture(window, theme, "revision16-graphics-error-" + width);
        if (width == 760) { window.SetRenderScaling(1.5); Position(); Capture(window, theme, "revision16-graphics-150"); window.SetRenderScaling(1); }
        hardware = new("Fixture CPU", "Intel graphics", []); player = null;
        var refresh = selected.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Refresh hardware & path")); ClickSearchPopup(refresh); RunFor(70);
        var driver = selected.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Open GPU driver settings"));
        var reveal = selected.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Show Roblox executable"));
        Check(!driver.IsEnabled && !reveal.IsEnabled && !combo.IsEffectivelyVisible && vm.PanelAvailability.Contains("Windows", StringComparison.Ordinal), "refresh updates actual unavailable hardware/player states: " + theme + width);
        Capture(window, theme, "revision16-graphics-unavailable-" + width);
        owner.SaveSettings(); window.Close();
    }
}
