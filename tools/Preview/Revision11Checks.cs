// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
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
    private sealed class SchedulingFixture : ISchedulingTarget
    {
        public uint Cpu { get; set; } = 0x20;
        private int _gpu = 2;
        public bool RejectGpu { get; set; }
        public int Gpu { get => RejectGpu ? throw new Win32Exception(5) : _gpu; set => _gpu = value; }
        public PowerPolicy Power { get; set; } = new(1, 5, 5);
        public bool Exited { get; set; }
        public int Disposals { get; private set; }
        public void Dispose() => Disposals++;
    }
    private static void VerifyRevision11()
    {
        VerifySessionScheduling();
        VerifyPerformanceProfiles();
        VerifyExplosionLayers();
        RenderRevision11();
        VerifyOptionMotion();
    }
    private static void VerifySessionScheduling()
    {
        Check(Marshal.SizeOf<PowerPolicy>() == 12, "Windows power policy has the documented 12-byte native layout");
        var target = new SchedulingFixture();
        var session = new SessionScheduling(target, true, true, true, true);
        Check(target.Cpu == 0x8000 && target.Gpu == 3 && target.Power == new PowerPolicy(1, 5, 0), "session applies CPU, WDDM GPU, execution QoS and timer policies");
        session.Dispose(); session.Dispose();
        Check(target.Cpu == 0x20 && target.Gpu == 2 && target.Power == new PowerPolicy(1, 5, 5) && target.Disposals == 1, "session restores every original policy and cleanup is idempotent");
        target = new SchedulingFixture { Cpu = 0x80, Gpu = 4, Power = new(1, 0, 0) };
        session = new(target, true, true, true, false);
        Check(target.Cpu == 0x80 && target.Gpu == 4 && target.Power == new PowerPolicy(1, 1, 0), "session preserves preexisting higher priority and scopes power policy to requested bits");
        target.Cpu = 0x4000; target.Gpu = 1; target.Power = new(1, 9, 8); session.Dispose();
        Check(target.Cpu == 0x4000 && target.Gpu == 1 && target.Power == new PowerPolicy(1, 8, 8), "cleanup preserves later external priority and unrelated power-policy changes");
        target = new SchedulingFixture { RejectGpu = true };
        session = new(target, true, true, false, false);
        Check(target.Cpu == 0x8000 && session.Result.Contains("GPU priority: unavailable", StringComparison.Ordinal), "driver access failure is reported without dropping independent CPU policy");
        session.Dispose(); Check(target.Cpu == 0x20, "partial scheduling success is still restored");
        target = new SchedulingFixture(); session = new(target, true, false, false, false); target.Exited = true; session.Dispose();
        Check(target.Cpu == 0x8000 && target.Disposals == 1, "exited session releases its handle without attempting writes to a replacement process");
        var vm = new PlatformTweaksViewModel { AboveNormalCpuPriority = true, AboveNormalGpuPriority = true, PreventEcoQoS = true, HonorTimerRequests = true };
        var persisted = JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(File.ReadAllText(App.Settings.FileLocation))!;
        Check(persisted.AboveNormalCpuPriority && persisted.AboveNormalGpuPriority && persisted.PreventEcoQoS && persisted.HonorTimerRequests, "all four opt-in session preferences persist");
        vm.AboveNormalCpuPriority = false; vm.AboveNormalGpuPriority = false; vm.PreventEcoQoS = false; vm.HonorTimerRequests = false;
        Check(SessionScheduling.ForPlayer(-1) == null, "default scheduling preferences do not open any process handle");
        App.State.Prop.LastSchedulingResult = "fixture result"; App.State.SaveSetting(nameof(App.State.Prop.LastSchedulingResult));
        vm.RefreshSchedulingStatusCommand.Execute(null);
        Check(vm.SchedulingStatus == "fixture result", "session status refresh reads the persisted watcher result");
        if (OperatingSystem.IsWindows())
        {
            using var native = new WindowsSchedulingTarget(Environment.ProcessId);
            uint cpu = native.Cpu;
            Check(!native.Exited && string.Equals(native.Image, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase), "native process query is confined to this isolated preview process");
            using (var policy = new SessionScheduling(native, true, true, true, true))
            {
                File.WriteAllText(Path.Combine(_output, "windows-scheduling-probe.json"), JsonSerializer.Serialize(new { target = "isolated preview process", result = policy.Result, memoryAccess = false }));
                Check(native.Cpu == 0x8000 || cpu is 0x80 or 0x100, "documented Windows API applies CPU scheduling to the isolated preview process");
            }
            using var after = new WindowsSchedulingTarget(Environment.ProcessId);
            Check(after.Cpu == cpu, "native CPU scheduling is restored after the isolated session");
        }
    }
    private static void VerifyPerformanceProfiles()
    {
        App.Settings.Prop.LockMaximumRenderDistance = false;
        App.GlobalSettings.CreateTemplate(); App.GlobalSettings.Load(); App.FastFlags.Prop.Clear();
        App.GlobalSettings.SetPreset("Rendering.SavedQualityLevel", "3"); App.GlobalSettings.SetPreset("Rendering.MaxQualityEnabled", "True");
        App.GlobalSettings.SetPreset("Audio.MasterVolume", "0.31");
        App.FastFlags.SetValue("FFlagDebugGraphicsPreferVulkan", "True"); App.FastFlags.SetValue("DFIntTextureQualityOverride", "3");
        var before = App.FastFlags.Prop.ToDictionary(pair => pair.Key, pair => pair.Value.ToString());
        var vm = new PerformanceProfilesViewModel();
        Check(App.FastFlags.Prop.Count == before.Count && App.GlobalSettings.GetPreset("Rendering.SavedQualityLevel") == "3", "opening profiles does not silently apply a preset");
        vm.Selected = PerformanceProfilesViewModel.Profiles[1]; vm.ApplyCommand.Execute(null);
        Check(App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride") == "1" && App.FastFlags.GetValue("DFIntTextureQualityOverride") == "0" &&
            App.FastFlags.GetValue("FIntDebugForceMSAASamples") == "1" && App.GlobalSettings.GetPreset("Rendering.SavedQualityLevel") == "10", "texture-saver profile pairs lowest detail with the maximum saved distance slider");
        Check(App.FastFlags.GetValue("FFlagDebugGraphicsPreferVulkan") == "True" && App.GlobalSettings.GetPreset("Audio.MasterVolume") == "0.31" &&
            App.FastFlags.Prop.All(pair => CuratedFlagRegistry.TryNormalize(pair.Key, pair.Value.ToString(), out _)), "profiles preserve unrelated renderer/audio and contain only allowlisted values");
        App.FastFlags.Save(); App.GlobalSettings.Save();
        App.FastFlags.SetValue("DFFlagDisableDPIScale", "True");
        var reopened = new PerformanceProfilesViewModel(); reopened.RestoreCommand.Execute(null);
        Check(!App.Settings.Prop.LockMaximumRenderDistance && App.GlobalSettings.GetPreset("Rendering.SavedQualityLevel") == "3" &&
            App.GlobalSettings.GetPreset("Rendering.MaxQualityEnabled") == "true" && App.FastFlags.GetValue("DFIntTextureQualityOverride") == "3" &&
            App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride") == null, "persistent undo restores the original mode, slider and exact owned flags after reopening");
        Check(App.FastFlags.GetValue("DFFlagDisableDPIScale") == "True" && !reopened.RestoreCommand.CanExecute(null), "undo preserves unrelated subsequent edits and consumes its backup once");
        string backup = Path.Combine(Paths.Base, "PerformanceProfileRollback.json");
        File.WriteAllText(backup, "{\"Flags\":{},\"Distance\":false,\"Slider\":\"3\",\"MaxQuality\":\"False\"}");
        new PerformanceProfilesViewModel().RestoreCommand.Execute(null);
        Check(App.FastFlags.GetValue("DFIntTextureQualityOverride") == "3" && !App.Settings.Prop.LockMaximumRenderDistance, "malformed undo snapshot is rejected before any flags or preferences change");
        File.Delete(backup);
        foreach (var profile in PerformanceProfilesViewModel.Profiles)
        {
            vm.Selected = profile; vm.ApplyCommand.Execute(null);
            Check(App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride") == profile.Effects.ToString() &&
                App.FastFlags.GetValue("DFIntCSGLevelOfDetailSwitchingDistanceL34") == profile.MeshDistance.ToString(), "profile writes its actual independent effect and CSG values: " + profile.Name);
        }
        vm.RestoreCommand.Execute(null); App.Settings.Prop.LockMaximumRenderDistance = false; App.FastFlags.Prop.Clear(); App.FastFlags.Save();
    }
    private static void VerifyExplosionLayers()
    {
        var visuals = new VisualTweaksViewModel(); visuals.Reset(); visuals.ExplosionStyle = "Hidden";
        var assets = BuiltInVisualMods.Files().ToArray();
        Check(assets.Length == 4 && assets.All(asset => ModFilePolicy.IsAllowed(asset.Relative)), "explosion control targets all four supported default sprite files");
        foreach (var asset in assets)
        {
            using var reader = new BinaryReader(File.OpenRead(asset.Source)); reader.BaseStream.Position = 12;
            int height = reader.ReadInt32(), width = reader.ReadInt32(); reader.BaseStream.Position = 28;
            Check(height == 256 && width == 256 && reader.ReadInt32() == 9 && File.ReadAllBytes(asset.Source).Skip(128).All(b => b == 0), "explosion replacement preserves dimensions, transparency and all mip levels: " + Path.GetFileName(asset.Relative));
        }
        Check(JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(File.ReadAllText(App.Settings.FileLocation))!.ExplosionStyle == "Hidden", "explosion layer choice persists immediately");
        string player = Path.Combine(Paths.Base, "revision11-player");
        foreach (var asset in assets) { string path = Path.Combine(player, asset.Relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "original explosion"); }
        App.State.Prop.Mods.Clear(); App.PlayerState.Prop.ModManifest.Clear(); App.PlayerState.Save();
        using (var bootstrapper = new Bootstrapper(LaunchMode.Player))
        {
            typeof(Bootstrapper).GetField("_latestVersionDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, player);
            typeof(Bootstrapper).GetField("PackageDirectoryMap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, new Dictionary<string, string>());
            var apply = typeof(Bootstrapper).GetMethod("ApplyModifications", BindingFlags.Instance | BindingFlags.NonPublic)!;
            AwaitResult((Task<bool>)apply.Invoke(bootstrapper, null)!);
            Check(assets.All(asset => File.ReadAllBytes(Path.Combine(player, asset.Relative)).SequenceEqual(File.ReadAllBytes(asset.Source))), "production launcher installs explosion replacements through its mod pipeline");
            visuals.ExplosionStyle = "Core and ring only"; AwaitResult((Task<bool>)apply.Invoke(bootstrapper, null)!);
            Check(BuiltInVisualMods.Files().Count() == 2 && assets.Where(asset => asset.Relative.Contains("core", StringComparison.Ordinal) || asset.Relative.Contains("shockwave", StringComparison.Ordinal)).All(asset => File.ReadAllText(Path.Combine(player, asset.Relative)) == "original explosion"), "reducing explosion layers restores the original core and shockwave files");
            visuals.Reset(); AwaitResult((Task<bool>)apply.Invoke(bootstrapper, null)!);
            Check(assets.All(asset => File.ReadAllText(Path.Combine(player, asset.Relative)) == "original explosion"), "disabling explosion styling restores all four originals");
        }
    }
    private static void RenderRevision11()
    {
        foreach (var theme in new[] { Theme.Dark, Theme.Light })
        {
            App.Settings.Prop.Theme = theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
            foreach (int width in new[] { 1100, 760 })
            {
                var window = new SettingsWindow(false) { Width = width, Height = 900 }; window.Show(); RunFor(80);
                ((MainWindowViewModel)window.DataContext!).NavigateRegistered("fastflags"); RunFor(80);
                var groups = window.GetVisualDescendants().OfType<CardExpander>().ToArray();
                foreach (string heading in new[] { "Performance profiles", "Windows session scheduling", "Built-in effect styles" })
                {
                    foreach (var group in groups) group.IsExpanded = Equals(group.Header, heading);
                    var selected = groups.Single(group => Equals(group.Header, heading)); RunFor(80);
                    var scroll = selected.GetVisualAncestors().OfType<ScrollViewer>().First();
                    var point = selected.TranslatePoint(default, scroll);
                    if (point.HasValue) scroll.Offset = new Vector(0, Math.Max(0, scroll.Offset.Y + point.Value.Y - 16));
                    RunFor(70); Capture(window, theme, "revision11-" + heading.Replace(' ', '-') + "-" + width);
                    if (heading == "Windows session scheduling") Check(selected.GetVisualDescendants().OfType<ToggleSwitch>().Count() == 4, "all four session controls are bound in the actual production page: " + theme + " " + width);
                    if (heading == "Performance profiles") Check(selected.GetVisualDescendants().OfType<ComboBox>().Single().ItemCount == 4, "actual production profile chooser exposes four tradeoffs: " + theme + " " + width);
                }
                window.Close();
            }
        }
    }
    private static void VerifyOptionMotion()
    {
        _manualTime = new ManualMotionTime(); CoffeeMotion.TimeSource = _manualTime;
        App.Settings.Prop.DisableAnimations = false; App.RemoveAnimationSettings();
        var option = new OptionControl { Header = "Responsive option", Description = "A finite hover lift with stable input bounds.", InnerContent = new ToggleSwitch() };
        var window = new Window { Width = 760, Height = 300, Content = new StackPanel { Margin = new Thickness(24), Children = { option } } }; window.Show(); RunFor(80);
        var root = option.GetVisualDescendants().OfType<Control>().Single(control => control.Name == "PART_RootCard");
        var transform = (TranslateTransform)root.RenderTransform!;
        var positions = new List<double>(); window.MouseMove(At(option, window), RawInputModifiers.None);
        for (int i = 0; i < 10; i++) { StepFrame(16); positions.Add(transform.Y); }
        Check(positions.Distinct().Count() > 3 && Math.Abs(transform.Y + 1.5) < .01, "option hover has intermediate rendered positions and settles at a bounded lift");
        for (int i = 0; i < 8; i++) { window.MouseMove(i % 2 == 0 ? new Point(2, 2) : At(option, window), RawInputModifiers.None); StepFrame(20); }
        window.MouseMove(new Point(2, 2), RawInputModifiers.None); RunFor(350);
        var motion = (CoffeeMotion)typeof(OptionControl).GetField("_hover", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(option)!;
        int ticks = motion.TickCount; RunFor(100);
        Check(Math.Abs(transform.Y) < .01 && !motion.IsRunning && motion.TickCount == ticks, "rapid option hover reversals return to rest and perform no idle animation work");
        App.Settings.Prop.DisableAnimations = true;
        window.MouseMove(At(option, window), RawInputModifiers.None); RunFor(50);
        Check(!motion.IsRunning && Math.Abs(transform.Y + 1.5) < .01, "reduced motion applies the option hover state immediately without animation", new { transform.Y, option.IsPointerOver, motion.Diagnostics });
        window.Content = null; RunFor(20);
        Check(!motion.IsRunning && transform.Y == 0, "detaching an option cancels and clears its hover animation");
        window.Close(); App.ApplyAnimationSettings(); CoffeeMotion.TimeSource = TimeProvider.System; _manualTime = null;
    }
}
