// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.ComponentModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Froststrap;
using Froststrap.Enums;
using Froststrap.Services;
using Froststrap.UI.Elements.Controls;
using Froststrap.UI.Utility;
using Froststrap.UI.ViewModels.Settings;
using SettingsWindow = Froststrap.UI.Elements.Settings.MainWindow;

internal static partial class Program
{
    private sealed class PowerSchedulingFixture : ISchedulingTarget
    {
        public uint Cpu { get; set; } = 0x20;
        public int Gpu { get; set; } = 2;
        public bool Exited => false;
        public bool RejectTimers { get; init; }
        private PowerPolicy _power = new(1, 0, 0);
        public PowerPolicy Power
        {
            get => _power;
            set { if (RejectTimers && (value.ControlMask & 4) != 0) throw new Win32Exception(87); _power = value; }
        }
        public void Dispose() { }
    }
    private static void VerifyRevision12()
    {
        VerifyPowerOwnership();
        VerifyProfileComparison();
        RenderProfileComparison();
        VerifyMotionPreferenceSwitch();
        VerifyComparisonMotion();
    }
    private static void VerifyComparisonMotion()
    {
        _manualTime = new ManualMotionTime(); CoffeeMotion.TimeSource = _manualTime; AppearanceViewModel.DisableAnimations = false;
        var window = new SettingsWindow(false) { Width = 1000, Height = 900 }; window.Show(); RunFor(250);
        var owner = (MainWindowViewModel)window.DataContext!; owner.NavigateRegistered("fastflags"); RunFor(250);
        var profiles = ((CuratedFastFlagsViewModel)owner.CurrentPage!).Performance;
        var group = window.GetVisualDescendants().OfType<CardExpander>().Single(control => Equals(control.Header, "Performance profiles"));
        var reveals = group.GetVisualDescendants().OfType<AnimatedReveal>().ToArray();
        var inner = reveals.Single(reveal => reveal.GetVisualAncestors().OfType<AnimatedReveal>().Any());
        profiles.ShowComparison = true; var heights = new List<double>();
        for (int i = 0; i < 8; i++) { StepFrame(16); heights.Add(inner.RevealedHeight); }
        Check(heights.Distinct().Count() > 3 && heights.Any(height => height > 1 && height < inner.NaturalHeight - 1), "profile comparison uses intermediate reveal frames within the actual profile group");
        double previous = inner.RevealedHeight; profiles.ShowComparison = false;
        Check(Math.Abs(previous - inner.RevealedHeight) < .1, "reversing the comparison reveal preserves the displayed height");
        for (int i = 0; i < 6; i++) { profiles.ShowComparison = !profiles.ShowComparison; StepFrame(20); }
        profiles.ShowComparison = false; RunFor(750);
        Check(inner.RevealedHeight < .1 && reveals.All(reveal => !reveal.HasActiveMotion), "nested profile animations settle after rapid reversals without keeping the parent active");
        AppearanceViewModel.DisableAnimations = true; profiles.ShowComparison = true; RunFor(50);
        Check(Math.Abs(inner.RevealedHeight - inner.NaturalHeight) < .1 && reveals.All(reveal => !reveal.HasActiveMotion), "runtime reduced motion immediately reveals the entire comparison and settles both groups");
        window.Close(); CoffeeMotion.TimeSource = TimeProvider.System; _manualTime = null;
    }
    private static void VerifyProfileComparison()
    {
        App.Settings.Prop.LockMaximumRenderDistance = false; App.GlobalSettings.CreateTemplate(); App.GlobalSettings.Load(); App.FastFlags.Prop.Clear();
        App.GlobalSettings.SetPreset("Rendering.SavedQualityLevel", "3"); App.GlobalSettings.SetPreset("Rendering.MaxQualityEnabled", true);
        App.FastFlags.SetValue("DFIntDebugFRMQualityLevelOverride", "7"); App.FastFlags.SetValue("FIntDebugForceMSAASamples", "4");
        var vm = new CuratedFastFlagsViewModel();
        Check(vm.Performance.Changes.Count == 14 && vm.Performance.Changes.Single(change => change.Label == "Effects quality (1–21)").Current == "7" &&
            vm.Performance.Changes.Single(change => change.Label == "Texture quality (0–3)").Current == "Roblox default", "profile comparison reads actual unsaved values and distinguishes absent overrides");
        vm.Performance.Selected = PerformanceProfilesViewModel.Profiles[3];
        Check(vm.Performance.Changes.Single(change => change.Label == "Effects quality (1–21)").Selected == "9" &&
            App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride") == "7", "previewing a different profile changes the proposed values without applying it");
        vm.NativeSettings.RenderDistance = 5; vm.NativeSettings.MaxQualityEnabled = false;
        Check(vm.Performance.Changes.Single(change => change.Label == "Effects quality (1–21)").Current == "5" &&
            !vm.Performance.Changes.Single(change => change.Label == "Maximum quality effects").IsChanged, "profile comparison refreshes native effects and quality changes without stale or case-sensitive differences");
        vm.Graphics.MeshThresholds[1].Value = 711;
        Check(vm.Performance.Changes.Single(change => change.Label == "Mesh level 1 → 2").Current == "711", "profile comparison follows independently edited mesh thresholds");
        vm.Performance.ApplyCommand.Execute(null);
        Check(vm.Performance.Changes.All(change => !change.IsChanged), "the comparison and actual profile application agree on all fourteen settings");
        vm.Settings.Single(setting => setting.Title == "Anti-aliasing").Selected = vm.Settings.Single(setting => setting.Title == "Anti-aliasing").Choices[0];
        Check(vm.Performance.Changes.Single(change => change.Label == "MSAA samples").Current == "Roblox default", "comparison follows the existing shared anti-aliasing control");
        string before = JsonSerializer.Serialize(App.FastFlags.Prop);
        string backup = Path.Combine(Paths.Base, "PerformanceProfileRollback.json");
        using (var locked = new FileStream(backup + ".restored", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
        {
            vm.Performance.RestoreCommand.Execute(null);
            Check(JsonSerializer.Serialize(App.FastFlags.Prop) == before && App.Settings.Prop.LockMaximumRenderDistance && File.Exists(backup), "a locked undo destination leaves rendering values and the recoverable backup untouched");
        }
        vm.Performance.RestoreCommand.Execute(null);
        Check(!App.Settings.Prop.LockMaximumRenderDistance && App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride") == "5" &&
            vm.Performance.Changes.Single(change => change.Label == "Effects quality (1–21)").Current == "5", "undo succeeds after the file lock clears and refreshes the comparison");
        vm.ResetVisualsCommand.Execute(null);
        Check(vm.Performance.Changes.Single(change => change.Label == "MSAA samples").Current == "Roblox default", "restoring Roblox visuals refreshes the profile comparison");
        App.FastFlags.Prop.Clear(); App.FastFlags.Save();
    }
    private static void RenderProfileComparison()
    {
        foreach (var theme in new[] { Theme.Dark, Theme.Light })
        {
            App.Settings.Prop.Theme = theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
            foreach (int width in new[] { 760, 1100 })
            {
                var window = new SettingsWindow(false) { Width = width, Height = 900 }; window.Show(); RunFor(80);
                var owner = (MainWindowViewModel)window.DataContext!; owner.NavigateRegistered("fastflags"); RunFor(80);
                var profiles = (CuratedFastFlagsViewModel)owner.CurrentPage!;
                var group = window.GetVisualDescendants().OfType<CardExpander>().Single(control => Equals(control.Header, "Performance profiles"));
                var compare = group.GetVisualDescendants().OfType<ToggleButton>().Single(control => Equals(control.Content, "Compare settings"));
                compare.Focus(NavigationMethod.Tab); window.KeyPress(Key.Space, RawInputModifiers.None, default, null); window.KeyRelease(Key.Space, RawInputModifiers.None, default, null); RunFor(400);
                Check(profiles.Performance.ShowComparison && group.GetVisualDescendants().OfType<ItemsControl>().Any(control => control.ItemCount == 14), "keyboard comparison control reveals all fourteen production-bound rows: " + theme + " " + width);
                Capture(window, theme, "revision12-profile-comparison-" + width);
                if (width == 760) { window.SetRenderScaling(1.5); RunFor(120); Capture(window, theme, "revision12-profile-comparison-150"); window.SetRenderScaling(1); }
                window.Close();
            }
        }
    }
    private static void VerifyPowerOwnership()
    {
        var target = new PowerSchedulingFixture();
        var session = new SessionScheduling(target, false, false, true, true);
        target.Power = new(1, 5, 4); session.Dispose();
        Check(target.Power == new PowerPolicy(1, 4, 4), "power cleanup releases only unchanged owned QoS when another program changes timer policy");
        target = new PowerSchedulingFixture(); session = new(target, false, false, true, true);
        target.Power = new(1, 5, 1); session.Dispose();
        Check(target.Power == new PowerPolicy(1, 1, 1), "power cleanup releases unchanged owned timer policy while preserving external QoS changes");
        target = new PowerSchedulingFixture { RejectTimers = true }; session = new(target, false, false, true, true);
        Check(target.Power == new PowerPolicy(1, 1, 0) && session.Result.Contains("Timer policy: unavailable", StringComparison.Ordinal), "an unsupported timer mechanism is reported independently and does not prevent performance QoS");
        session.Dispose(); Check(target.Power == new PowerPolicy(1, 0, 0), "partially supported power policies restore their successful request");
        if (!OperatingSystem.IsWindows()) return;
        using var native = new WindowsSchedulingTarget(Environment.ProcessId);
        uint cpu = native.Cpu; int gpu = native.Gpu; PowerPolicy power = native.Power;
        var policy = new SessionScheduling(native, true, true, true, true);
        string result = policy.Result; policy.Dispose();
        using var restored = new WindowsSchedulingTarget(Environment.ProcessId);
        Check(restored.Cpu == cpu && restored.Gpu == gpu && restored.Power == power, "native Windows CPU, GPU and both power mechanisms restore exactly in the isolated preview process");
        File.WriteAllText(Path.Combine(_output, "windows-scheduling-cleanup.json"), JsonSerializer.Serialize(new { target = "isolated preview process", result, original = new { cpu, gpu, power }, restored = new { cpu = restored.Cpu, gpu = restored.Gpu, power = restored.Power }, memoryAccess = false }));
    }
    private static void VerifyMotionPreferenceSwitch()
    {
        App.Settings.Prop.Theme = Theme.Dark; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
        _manualTime = new ManualMotionTime(); CoffeeMotion.TimeSource = _manualTime;
        AppearanceViewModel.DisableAnimations = false;
        var window = new SettingsWindow(false) { Width = 1000, Height = 800 }; window.Show(); RunFor(250);
        var vm = (MainWindowViewModel)window.DataContext!; vm.NavigateRegistered("shortcuts"); RunFor(250);
        var option = window.GetVisualDescendants().OfType<OptionControl>().Single(control => control.Header == "Reduce motion");
        Click(window, At(option.Toggle!, window)); RunFor(100);
        Check(App.Settings.Prop.DisableAnimations, "the actual Reduce motion toggle enables the persisted preference during an open settings session");
        Click(window, At(option.Toggle!, window)); RunFor(100);
        Check(!App.Settings.Prop.DisableAnimations, "pointer input remains functional after changing reduced-motion styles at runtime");
        for (int i = 0; i < 6; i++) { Click(window, At(option.Toggle!, window)); RunFor(30); }
        Check(!App.Settings.Prop.DisableAnimations, "repeated runtime motion changes preserve toggle input and final state");
        Capture(window, Theme.Dark, "revision12-runtime-motion-settings");
        window.Close(); AppearanceViewModel.DisableAnimations = true; CoffeeMotion.TimeSource = TimeProvider.System; _manualTime = null;
    }
}
