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
using Froststrap.UI.ViewModels.Settings;
using SettingsWindow = Froststrap.UI.Elements.Settings.MainWindow;

internal static partial class Program
{
    private static void VerifyRevision13()
    {
        string previousRoot = Paths.DataRoot, previousMods = Paths.Modifications;
        var previousSettings = JsonSerializer.Serialize(App.Settings.Prop);
        var previousState = App.State.Prop.Mods;
        var previousManifest = App.PlayerState.Prop.ModManifest.ToList();
        string root = Path.Combine(previousRoot, "revision13"); Directory.CreateDirectory(root);
        typeof(Paths).GetProperty(nameof(Paths.DataRoot))!.SetValue(null, root);
        string library = Path.Combine(root, "library"); Directory.CreateDirectory(library);
        typeof(Paths).GetProperty(nameof(Paths.Modifications))!.SetValue(null, library);
        try
        {
            new VisualTweaksViewModel().Reset(); App.Settings.Prop.LockMaximumRenderDistance = false;
            var empty = new ModReportViewModel();
            Check(!empty.HasReport && !empty.HasEntries && empty.Message.Contains("No player", StringComparison.Ordinal), "file-mod report has a truthful first-launch empty state");
            RenderReportState("empty", Theme.Dark, 760);
            string player = Path.Combine(root, "player"); Directory.CreateDirectory(player);
            string sky = Path.Combine("PlatformContent", "pc", "textures", "sky", "sky512_bk.tex");
            string indoor = ModAssetPreparation.IndoorSkyAlias(sky)!;
            foreach (string relative in new[] { sky, indoor })
            { string path = Path.Combine(player, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "original sky"); }
            string high = Path.Combine(library, "priority-sky"), low = Path.Combine(library, "secondary-sky");
            foreach (string mod in new[] { high, low }) Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(mod, sky))!);
            ModAssetPreparation.WriteDds(Path.Combine(high, sky), 4, 4, (_, _) => new SkiaSharp.SKColor(40, 80, 130));
            ModAssetPreparation.WriteDds(Path.Combine(low, sky), 4, 4, (_, _) => new SkiaSharp.SKColor(120, 40, 50));
            string broken = Path.Combine(high, "content", "textures", "broken.tex"); Directory.CreateDirectory(Path.GetDirectoryName(broken)!); File.WriteAllText(broken, "not an image");
            string blocked = Path.Combine(high, "content", "shaders", "unsupported.fx"); Directory.CreateDirectory(Path.GetDirectoryName(blocked)!); File.WriteAllText(blocked, "unsupported fixture");
            App.State.Prop.Mods = [new ModConfig { FolderName = "priority-sky", Priority = 5 }, new ModConfig { FolderName = "secondary-sky", Priority = 1 }, new ModConfig { FolderName = "missing-mod" }];
            App.PlayerState.Prop.ModManifest.Clear(); App.PlayerState.Save();
            App.Settings.Prop.DefaultSkyPalette = "Deep blue";
            using var bootstrapper = new Bootstrapper(LaunchMode.Player);
            typeof(Bootstrapper).GetField("_latestVersionDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, player);
            typeof(Bootstrapper).GetField("_latestVersionGuid", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, "fixture-player-13");
            typeof(Bootstrapper).GetField("PackageDirectoryMap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, new Dictionary<string, string>());
            var apply = typeof(Bootstrapper).GetMethod("ApplyModifications", BindingFlags.Instance | BindingFlags.NonPublic)!;
            bool Apply() => AwaitResult((Task<bool>)apply.Invoke(bootstrapper, null)!);
            Check(!Apply(), "corrupt texture is a reported partial failure rather than a pipeline exception");
            var report = ModApplicationReporter.Read()!;
            Check(report.Version == "fixture-player-13" && report.Outcome == "Completed with errors" && report.Entries.Any(entry => entry.Status == "Failed" && entry.Path.EndsWith("broken.tex", StringComparison.Ordinal)), "production report identifies the failed texture and actual player version");
            Check(File.ReadAllBytes(Path.Combine(player, sky)).SequenceEqual(File.ReadAllBytes(Path.Combine(high, sky))) &&
                report.Entries.Any(entry => entry.Status == "Applied" && entry.Path == sky) && report.Entries.Any(entry => entry.Status == "Applied" && entry.Path == indoor), "healthy sky and indoor aliases still copy and receive real application events");
            Check(!File.Exists(Path.Combine(player, "content", "shaders", "unsupported.fx")) && report.Entries.Any(entry => entry.Status == "Skipped" && entry.Path.EndsWith("unsupported.fx", StringComparison.Ordinal)), "blocked shader payload stays excluded and has a skipped-file diagnostic");
            Check(report.Entries.Any(entry => entry.Status == "Shadowed" && entry.Source == "secondary-sky") && report.Entries.Any(entry => entry.Status == "Shadowed" && entry.Source == "Built-in visual tweaks"), "report explains both installed-mod and built-in priority conflicts");
            Check(report.Entries.Any(entry => entry.Status == "Skipped" && entry.Source == "missing-mod") && report.Entries.Any(entry => entry.Status == "Skipped" && entry.Source == "Built-in visual tweaks"), "missing enabled folders and absent client textures receive actionable skip reasons");
            string diagnosticReport = File.ReadAllText(ModApplicationReporter.FileLocation());
            File.WriteAllText(Path.Combine(_output, "mod-application-probe.json"), diagnosticReport);
            var vm = new ModReportViewModel { Filter = "Needs attention" };
            Check(vm.HasReport && vm.Entries.Count > 0 && vm.Entries.All(entry => entry.Status is "Failed" or "Skipped" or "Shadowed"), "attention filter uses recorded failures, skips and conflicts");
            vm.Filter = "Applied"; Check(vm.Entries.All(entry => entry.Status == "Applied") && vm.Coverage.Contains("Showing " + vm.Entries.Count, StringComparison.Ordinal), "event filtering updates both rows and coverage");
            File.Delete(broken); Check(Apply(), "valid assets apply after a corrupt source is removed");
            report = ModApplicationReporter.Read()!;
            Check(report.Outcome == "Completed" && report.Counts.GetValueOrDefault("Current") >= 2 && report.Counts.GetValueOrDefault("Failed") == 0, "next report replaces stale failures with the current completed check");
            string completed = File.ReadAllText(ModApplicationReporter.FileLocation());
            using (var lockedReport = new FileStream(ModApplicationReporter.FileLocation(), FileMode.Open, FileAccess.Read, FileShare.Read))
                Check(Apply() && File.ReadAllText(ModApplicationReporter.FileLocation()) == completed, "an unwritable report preserves the previous complete file without failing mod application");
            Check(!Directory.EnumerateFiles(Path.GetDirectoryName(ModApplicationReporter.FileLocation())!, "*.tmp").Any(), "failed report publication cleans its temporary file");
            foreach (var mod in App.State.Prop.Mods) mod.Enabled = false;
            App.Settings.Prop.DefaultSkyPalette = "Roblox default";
            using (var locked = new FileStream(Path.Combine(player, sky), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Check(!Apply() && App.PlayerState.Prop.ModManifest.Contains(sky), "locked original restoration fails visibly and remains pending for the next launch");
                report = ModApplicationReporter.Read()!;
                Check(report.Entries.Any(entry => entry.Status == "Failed" && entry.Path == sky) && report.Entries.Any(entry => entry.Status == "Restored" && entry.Path == indoor), "one locked file does not prevent other originals from restoring");
            }
            Check(Apply() && File.ReadAllText(Path.Combine(player, sky)) == "original sky" && !App.PlayerState.Prop.ModManifest.Contains(sky), "unlocking restores the retained original on the next preparation");
            Check(ModApplicationReporter.Read()!.Entries.Any(entry => entry.Status == "Restored" && entry.Path == sky), "successful retry replaces the pending failure with restoration evidence");
            using (var studio = new ModApplicationReporter(true, "fixture-studio")) { studio.Record("content/example.dds", "Current", "Studio", "fixture"); studio.Complete(true); }
            Check(ModApplicationReporter.Read()!.Version == "fixture-player-13" && ModApplicationReporter.Read(true)!.Version == "fixture-studio", "Studio preparation cannot overwrite the player's report");
            VerifyReportBounds();
            File.WriteAllText(ModApplicationReporter.FileLocation(), "{\"Schema\":99}"); vm.RefreshCommand.Execute(null);
            Check(!vm.HasReport && !vm.HasEntries && vm.Message.StartsWith("Could not read", StringComparison.Ordinal), "malformed report clears old rows and shows a recoverable error");
            RenderReportState("unavailable", Theme.Light, 760);
            File.WriteAllText(ModApplicationReporter.FileLocation(), diagnosticReport); vm.RefreshCommand.Execute(null);
            Check(vm.HasReport, "refresh recovers after a report is repaired or replaced");
            foreach (var theme in new[] { Theme.Dark, Theme.Light }) foreach (int width in new[] { 760, 1100 }) RenderReportState("events", theme, width);
            ((CancellationTokenSource)typeof(Bootstrapper).GetField("_cancelTokenSource", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bootstrapper)!).Cancel();
            Check(Apply() && ModApplicationReporter.Read()!.Outcome == "Incomplete", "cancelled preparation records an incomplete check rather than a false completion");
        }
        finally
        {
            typeof(Paths).GetProperty(nameof(Paths.DataRoot))!.SetValue(null, previousRoot);
            typeof(Paths).GetProperty(nameof(Paths.Modifications))!.SetValue(null, previousMods);
            App.Settings.Prop = JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(previousSettings)!;
            App.State.Prop.Mods = previousState; App.PlayerState.Prop.ModManifest = previousManifest;
        }
    }
    private static void VerifyReportBounds()
    {
        using (var bounded = new ModApplicationReporter(false, "bounded-fixture"))
        {
            Parallel.For(0, 1000, i => bounded.Record(new string('漢', 1000), "Applied", new string('模', 200), new string('障', 200)));
            bounded.Record("content/late-failure.tex", "Failed", "Late failure", "Retain this error after ordinary entries fill the report."); bounded.Complete(false);
        }
        var report = ModApplicationReporter.Read()!;
        Check(report.Entries.Count == ModApplicationReporter.MaximumEntries && report.Omitted == 745 && report.Counts["Applied"] == 1000 && report.Counts["Failed"] == 1,
            "concurrent report recording remains bounded while total counts include omitted events");
        Check(report.Entries.Any(entry => entry.Status == "Failed") && new FileInfo(ModApplicationReporter.FileLocation()).Length <= ModApplicationReporter.MaximumBytes,
            "late failures remain visible and worst-case escaped values fit the report byte limit");
        using (var oversized = File.Create(ModApplicationReporter.FileLocation())) oversized.SetLength(ModApplicationReporter.MaximumBytes + 1L);
        bool rejected = false; try { ModApplicationReporter.Read(); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "oversized application reports are rejected before deserialization");
        using (var interrupted = new ModApplicationReporter(false, "interrupted-fixture")) interrupted.Record("content/partial.dds", "Applied", "Fixture", "One completed operation before interruption.");
        Check(ModApplicationReporter.Read()!.Outcome == "Incomplete", "an unfinished preparation is never reported as completed");
    }
    private static void RenderReportState(string state, Theme theme, int width)
    {
        App.Settings.Prop.Theme = theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
        var window = new SettingsWindow(false) { Width = width, Height = 900 }; window.Show(); RunFor(80);
        ((MainWindowViewModel)window.DataContext!).NavigateRegistered("mods"); RunFor(100);
        var group = window.GetVisualDescendants().OfType<CardExpander>().Single(control => Equals(control.Header, "Last file-mod application"));
        group.IsExpanded = true; RunFor(150);
        var scroller = group.GetVisualAncestors().OfType<ScrollViewer>().First();
        var point = group.TranslatePoint(default, scroller); if (point.HasValue) scroller.Offset = new Vector(0, scroller.Offset.Y + point.Value.Y - 16);
        RunFor(70); Capture(window, theme, "revision13-report-" + state + "-" + width);
        Check(group.GetVisualDescendants().OfType<Button>().Any(button => Equals(button.Content, "Refresh report") && button.Command != null), "report refresh is connected in the production " + state + " view: " + theme + " " + width);
        if (state == "events")
        {
            var list = group.GetVisualDescendants().OfType<ListBox>().Single(); var inside = list.GetVisualDescendants().OfType<ScrollViewer>().First();
            double before = scroller.Offset.Y;
            window.MouseWheel(At(list, window), new Vector(0, -3), RawInputModifiers.None); RunFor(80);
            Check(inside.Offset.Y > 0 && scroller.Offset.Y == before, "scrolling the report's bounded event list does not move the parent page: " + theme + " " + width);
            if (width == 760) { window.SetRenderScaling(1.5); RunFor(100); Capture(window, theme, "revision13-report-events-150"); window.SetRenderScaling(1); }
        }
        window.Close();
    }
}
