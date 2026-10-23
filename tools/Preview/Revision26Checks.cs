// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Buffers.Binary;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
    private static byte[] CloudDds(bool compressed, bool volume, int size = 8)
    {
        byte[] header = new byte[148]; "DDS "u8.CopyTo(header);
        void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(offset, 4), value);
        Write(4, 124); Write(8, 0x21007u | (volume ? 0x800000u : 0) | (compressed ? 0x80000u : 8));
        int levels = 1; for (int dimension = size; dimension > 1; dimension /= 2) levels++;
        Write(12, (uint)size); Write(16, (uint)size); Write(20, compressed ? (uint)(Math.Max(1, size / 4) * Math.Max(1, size / 4) * 8 * (volume ? 4 : 1)) : (uint)size); Write(24, volume ? 4u : 1u); Write(28, (uint)levels);
        Write(76, 32); Write(80, 4); Write(84, 0x30315844); Write(108, 0x401008); Write(112, volume ? 0x200000u : 0);
        Write(128, compressed ? 80u : 61u); Write(132, volume ? 4u : 3u); Write(140, compressed && volume ? 0u : 1u);
        using var bytes = new MemoryStream(); bytes.Write(header);
        for (int mip = 0, w = size, h = size, d = volume ? 4 : 1; mip < levels; mip++, w = Math.Max(1, w / 2), h = Math.Max(1, h / 2), d = Math.Max(1, d / 2))
        {
            int length = (compressed ? Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 8 : w * h) * d;
            for (int index = 0; index < length; index++) bytes.WriteByte((byte)((mip * 31 + index * 7) % 251));
        }
        return bytes.ToArray();
    }
    private static void VerifyRevision26()
    {
        string settings = JsonSerializer.Serialize(App.Settings.Prop), previousMods = Paths.Modifications;
        var mods = App.State.Prop.Mods; var manifest = App.PlayerState.Prop.ModManifest.ToList();
        var flags = new Dictionary<string, object>(App.FastFlags.Prop);
        string root = Path.Combine(Paths.DataRoot, "cloud-volume"); Directory.CreateDirectory(root);
        try
        {
            foreach (bool compressed in new[] { false, true }) foreach (bool volume in new[] { false, true })
            {
                byte[] original = CloudDds(compressed, volume); string source = Path.Combine(root, $"source-{compressed}-{volume}.dds"); File.WriteAllBytes(source, original);
                foreach (int drop in new[] { 1, 2 })
                {
                    if (compressed && drop == 2)
                    {
                        bool rejected = false; try { DdsMipReducer.Prepare(source, drop); } catch (InvalidDataException) { rejected = true; }
                        Check(rejected, "BC4 reduction never promotes a 2x2 mip that Direct3D cannot create: volume=" + volume); continue;
                    }
                    string prepared = DdsMipReducer.Prepare(source, drop); byte[] result = File.ReadAllBytes(prepared);
                    int skipped = 0;
                    for (int level = 0, w = 8, h = 8, d = volume ? 4 : 1; level < drop; level++, w /= 2, h /= 2, d = Math.Max(1, d / 2))
                        skipped += (compressed ? Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 8 : w * h) * d;
                    Check(DdsValue(result, 16) == 8 >> drop && DdsValue(result, 12) == 8 >> drop && DdsValue(result, 24) == (volume ? 4 >> drop : 1) && DdsValue(result, 28) == 4 - drop && DdsValue(result, 140) == 1,
                        $"DX10 cloud dimensions, depth, mip count and canonical single texture metadata: BC4={compressed}, volume={volume}, drop={drop}");
                    Check(result.AsSpan(148).SequenceEqual(original.AsSpan(148 + skipped)) && File.ReadAllBytes(source).SequenceEqual(original) && DdsMipReducer.Prepare(source, drop) == prepared,
                        $"all retained slices/mips are byte-identical and source/cache remain stable: BC4={compressed}, volume={volume}, drop={drop}");
                }
            }
            var mutations = new (string Name, int Offset, uint Value)[] { ("format", 128, 98), ("array", 140, 2), ("cube", 136, 4), ("depth", 24, 0), ("missing volume flag", 112, 0), ("missing depth flag", 8, 0x2100f), ("excess mip count", 28, 99), ("padded R8 pitch", 20, 12), ("excess volume", 24, 2049), ("alpha metadata", 144, 4) };
            foreach (var mutation in mutations)
            {
                byte[] data = CloudDds(false, true); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(mutation.Offset, 4), mutation.Value);
                string source = Path.Combine(root, "invalid.dds"); File.WriteAllBytes(source, data);
                bool rejected = false; try { DdsMipReducer.Prepare(source, 1); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "unsupported cloud DDS input is rejected before output: " + mutation.Name);
            }
            foreach (int trim in new[] { 1, 200 })
            {
                string source = Path.Combine(root, "truncated.dds"); File.WriteAllBytes(source, CloudDds(true, true)[..^trim]);
                bool rejected = false; try { DdsMipReducer.Prepare(source, 1); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "truncated DX10 header/payload fails explicitly: " + trim);
            }
            var visuals = new VisualTweaksViewModel(); visuals.Reset();
            Check(!TextureResolutionBudget.Targets().Any(), "cloud volume maps remain opt-in and default to no file targets");
            visuals.CloudVolumeResolution = 2;
            Check(JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(File.ReadAllText(App.Settings.FileLocation))!.CloudVolumeResolution == 2, "cloud volume choice persists through the existing settings path");
            visuals.CloudVolumeResolution = 17; Check(visuals.CloudVolumeResolution == 2, "invalid cloud volume selections cannot overwrite the saved choice");
            var targets = TextureResolutionBudget.Targets().ToArray();
            Check(targets.Length == 5 && targets.All(target => target.Relative.StartsWith(Path.Combine("content", "sky"), StringComparison.Ordinal)) && targets.All(target => !target.Relative.Contains("noise", StringComparison.Ordinal)), "cloud volume budget has five exact shape/detail/fallback targets and leaves unmipped noise alone");
            using (var pending = TextureResolutionBudget.Targets().GetEnumerator())
            {
                pending.MoveNext(); var values = new List<int> { pending.Current.Levels }; App.Settings.Prop.CloudVolumeResolution = 0;
                while (pending.MoveNext()) values.Add(pending.Current.Levels);
                Check(values.Count == 5 && values.All(value => value == 2), "an in-flight cloud preparation retains one valid selection when preferences change");
                App.Settings.Prop.CloudVolumeResolution = 2;
            }
            string player = Path.Combine(root, "player"), library = Path.Combine(root, "library"); Directory.CreateDirectory(library);
            typeof(Paths).GetProperty(nameof(Paths.Modifications))!.SetValue(null, library);
            var originals = new Dictionary<string, byte[]>();
            foreach (var target in targets)
            {
                byte[] data = CloudDds(target.Relative.Contains("bc4", StringComparison.Ordinal), !target.Relative.Contains("cloudsfb", StringComparison.Ordinal), 16);
                string path = Path.Combine(player, target.Relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, data); originals[target.Relative] = data;
            }
            App.State.Prop.Mods = []; App.PlayerState.Prop.ModManifest.Clear(); App.PlayerState.Save();
            App.GlobalSettings.CreateTemplate(); App.GlobalSettings.Load();
            var native = new GlobalSettingsViewModel { LockMaximumRenderDistance = true, GraphicsQuality = 10, RenderDistance = 1 }; App.GlobalSettings.Save(); App.FastFlags.Save();
            string xml = File.ReadAllText(GBSEditor.FileLocation);
            using var launcher = new Bootstrapper(LaunchMode.Player);
            typeof(Bootstrapper).GetField("_latestVersionDirectory", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(launcher, player);
            typeof(Bootstrapper).GetField("PackageDirectoryMap", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(launcher, new Dictionary<string, string>());
            var apply = typeof(Bootstrapper).GetMethod("ApplyModifications", BindingFlags.NonPublic | BindingFlags.Instance)!;
            bool Apply() => AwaitResult((Task<bool>)apply.Invoke(launcher, null)!);
            Check(Apply() && targets.All(target => DdsValue(File.ReadAllBytes(Path.Combine(player, target.Relative)), 16) == 4), "real launcher applies quarter-size BC4/R8 cloud volumes and the 2D fallback");
            Check(Apply() && targets.All(target => DdsValue(File.ReadAllBytes(Path.Combine(player, target.Relative)), 16) == 4), "repeated cloud volume launches do not shrink cumulatively");
            string overridePath = Path.Combine(library, "custom-cloud", targets[0].Relative); Directory.CreateDirectory(Path.GetDirectoryName(overridePath)!);
            byte[] replacement = CloudDds(false, true, 16); replacement[148 + 16 * 16 * 4 + 8 * 8 * 2] = 33; File.WriteAllBytes(overridePath, replacement);
            App.State.Prop.Mods = [new ModConfig { FolderName = "custom-cloud", Priority = 9 }];
            Check(Apply() && File.ReadAllBytes(Path.Combine(player, targets[0].Relative)).AsSpan(148).SequenceEqual(replacement.AsSpan(148 + 16 * 16 * 4 + 8 * 8 * 2)), "the selected installed cloud mod wins before its requested mip budget is applied");
            byte[] tooSmall = CloudDds(true, true); File.WriteAllBytes(overridePath, tooSmall);
            Check(Apply() && File.ReadAllBytes(Path.Combine(player, targets[0].Relative)).SequenceEqual(tooSmall) && ModApplicationReporter.Read()!.Entries.Any(entry => entry.Path == targets[0].Relative && entry.Status == "Skipped" && entry.Detail.Contains("block-compressed", StringComparison.Ordinal)),
                "an undersized compressed mod keeps its source and reports the skipped budget instead of installing an invalid texture");
            File.WriteAllBytes(overridePath, replacement);
            visuals.CloudVolumeResolution = 0;
            Check(Apply() && File.ReadAllBytes(Path.Combine(player, targets[0].Relative)).SequenceEqual(replacement), "Original removes the cloud volume budget while retaining an enabled installed mod");
            App.State.Prop.Mods.Clear();
            Check(Apply() && originals.All(pair => File.ReadAllBytes(Path.Combine(player, pair.Key)).SequenceEqual(pair.Value)) && targets.All(target => !App.PlayerState.Prop.ModManifest.Contains(target.Relative)), "disabling cloud budget/mods restores exact original headers/payloads and retires manifest entries");
            Check(SavedSlider() == "10" && App.FastFlags.GetValue("DFIntDebugFRMQualityLevelOverride") == "1" && File.ReadAllText(GBSEditor.FileLocation) == xml, "cloud volume application and restore preserve saved graphics 10 / FRM 1");
            visuals.CloudVolumeResolution = 1; visuals.Reset(); Check(visuals.CloudVolumeResolution == 0 && !TextureResolutionBudget.Targets().Any(), "visual reset clears the cloud volume budget and all its targets");
            foreach (var theme in new[] { Theme.Dark, Theme.Light }) foreach (int width in new[] { 760, 1100 }) RenderCloudVolume(theme, width);
            File.WriteAllText(Path.Combine(_output, "cloud-volume-probe.json"), JsonSerializer.Serialize(new { actualModificationPipeline = true, gameLaunched = false, gamePerformanceMeasured = false, memoryAccess = false }));
        }
        finally
        {
            App.Settings.Prop = JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(settings)!; App.Settings.Save();
            App.State.Prop.Mods = mods; App.PlayerState.Prop.ModManifest = manifest; App.FastFlags.Prop = flags;
            typeof(Paths).GetProperty(nameof(Paths.Modifications))!.SetValue(null, previousMods);
        }
    }
    private static void RenderCloudVolume(Theme theme, int width)
    {
        App.Settings.Prop.Theme = theme; App.Settings.Prop.DisableAnimations = false; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme(); App.ApplyAnimationSettings();
        var window = new SettingsWindow(false) { Width = width, Height = 900 }; window.Show(); RunFor(60);
        var owner = (MainWindowViewModel)window.DataContext!; owner.NavigateRegistered("fastflags"); RunFor(350);
        var page = (Control)window.FindControl<TransitioningContentControl>("PageContentControl")!.Content!;
        var group = page.GetVisualDescendants().OfType<CardExpander>().Single(item => Equals(item.Header, "Texture resolution budget")); group.IsExpanded = true; RunFor(60);
        var scroll = group.GetVisualAncestors().OfType<ScrollViewer>().First(); var point = group.TranslatePoint(default, scroll); if (point.HasValue) scroll.Offset = new Vector(0, scroll.Offset.Y + point.Value.Y - 12); RunFor(60);
        var card = group.GetVisualDescendants().OfType<OptionControl>().Single(item => Equals(item.Header, "Cloud volume maps")); var choice = card.GetVisualDescendants().OfType<ComboBox>().Single();
        choice.SelectedIndex = 0; choice.Focus(); window.KeyPress(Key.Down, RawInputModifiers.None, default, null); window.KeyRelease(Key.Down, RawInputModifiers.None, default, null); RunFor(60);
        Check(App.Settings.Prop.CloudVolumeResolution == 1 && card.Bounds.Width <= group.Bounds.Width && choice.Bounds.Width <= card.Bounds.Width, "real cloud volume binding and responsive layout work: " + theme + width);
        Capture(window, theme, "cloud-volume-" + width);
        if (width == 760) { window.SetRenderScaling(1.5); RunFor(60); Capture(window, theme, "cloud-volume-scaled"); window.SetRenderScaling(1); }
        App.Settings.Prop.DisableAnimations = true; App.ApplyAnimationSettings(); group.IsExpanded = false; RunFor(30); owner.NavigateRegistered("behaviour"); RunFor(40);
        Check(TopLevel.GetTopLevel(card) == null, "cloud controls settle and detach under reduced motion: " + theme + width); window.Close();
    }
}
