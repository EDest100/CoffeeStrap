// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Buffers.Binary;
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
    private static uint DdsValue(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    private static void DdsSet(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
    private static void VerifyRevision14()
    {
        string previousRoot = Paths.DataRoot, previousMods = Paths.Modifications;
        string previousSettings = JsonSerializer.Serialize(App.Settings.Prop);
        var previousState = App.State.Prop.Mods; var previousManifest = App.PlayerState.Prop.ModManifest.ToList();
        string root = Path.Combine(previousRoot, "revision14"); Directory.CreateDirectory(root);
        typeof(Paths).GetProperty(nameof(Paths.DataRoot))!.SetValue(null, root);
        string library = Path.Combine(root, "library"); Directory.CreateDirectory(library);
        typeof(Paths).GetProperty(nameof(Paths.Modifications))!.SetValue(null, library);
        try
        {
            var visuals = new VisualTweaksViewModel(); visuals.Reset();
            Check(!TextureResolutionBudget.Targets().Any(), "texture budgets default to no file operations");
            VerifyDdsMips(root);
            VerifyBudgetPipeline(root, library, visuals);
            visuals.SkyTextureResolution = 1; visuals.WaterTextureResolution = 2; visuals.SurfaceTextureResolution = 1; visuals.EffectTextureResolution = 2;
            var saved = JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(File.ReadAllText(App.Settings.FileLocation))!;
            Check(saved.SkyTextureResolution == 1 && saved.WaterTextureResolution == 2 && saved.SurfaceTextureResolution == 1 && saved.EffectTextureResolution == 2,
                "all four texture budgets persist through the real settings writer");
            Check(TextureResolutionBudget.Targets().Count() == 51 && TextureResolutionBudget.Targets().Select(item => item.Relative).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 51,
                "texture budgets use a bounded unique inventory of 51 shipping paths");
            using (var inventory = TextureResolutionBudget.Targets().GetEnumerator())
            {
                inventory.MoveNext(); var snapshot = new List<(string Relative, int Levels)> { inventory.Current };
                App.Settings.Prop.SkyTextureResolution = 0; App.Settings.Prop.WaterTextureResolution = -1;
                while (inventory.MoveNext()) snapshot.Add(inventory.Current);
                Check(snapshot.Count == 51 && snapshot.All(item => item.Levels is 1 or 2), "in-flight texture preparation retains valid selections when preferences change during enumeration");
            }
            visuals.SkyTextureResolution = 1; visuals.WaterTextureResolution = 2;
            foreach (var theme in new[] { Theme.Dark, Theme.Light }) foreach (int width in new[] { 760, 1100 }) RenderTextureBudget(theme, width);
            visuals.Reset();
            Check(!TextureResolutionBudget.Targets().Any() && visuals.SkyTextureResolution == 0 && visuals.EffectTextureResolution == 0, "restore visuals removes every texture budget");
            App.Settings.Prop.SkyTextureResolution = -1; App.Settings.Prop.WaterTextureResolution = 999;
            Check(!TextureResolutionBudget.Targets().Any() && visuals.SkyTextureResolution == 0 && visuals.WaterTextureResolution == 0, "invalid persisted texture selections are inactive and display Original");
            visuals.SkyTextureResolution = 8;
            Check(App.Settings.Prop.SkyTextureResolution == -1, "invalid UI selection cannot write a texture budget");
        }
        finally
        {
            typeof(Paths).GetProperty(nameof(Paths.DataRoot))!.SetValue(null, previousRoot);
            typeof(Paths).GetProperty(nameof(Paths.Modifications))!.SetValue(null, previousMods);
            App.Settings.Prop = JsonSerializer.Deserialize<Froststrap.Models.Persistable.Settings>(previousSettings)!;
            App.State.Prop.Mods = previousState; App.PlayerState.Prop.ModManifest = previousManifest;
        }
    }
    private static void VerifyDdsMips(string root)
    {
        string rgba = Path.Combine(root, "rgba.dds");
        ModAssetPreparation.WriteDds(rgba, 32, 16, (x, y) => new SkiaSharp.SKColor((byte)(x * 7), (byte)(y * 13), 60, (byte)(x * 5)));
        byte[] original = File.ReadAllBytes(rgba);
        string half = DdsMipReducer.Prepare(rgba, 1), quarter = DdsMipReducer.Prepare(rgba, 2);
        byte[] a = File.ReadAllBytes(half), b = File.ReadAllBytes(quarter);
        Check(DdsValue(a, 16) == 16 && DdsValue(a, 12) == 8 && DdsValue(a, 20) == 64 && DdsValue(a, 28) == 5,
            "RGBA half resolution updates width, height, row pitch and mip count");
        Check(DdsValue(b, 16) == 8 && DdsValue(b, 12) == 4 && DdsValue(b, 28) == 4 && b.AsSpan(128).SequenceEqual(original.AsSpan(128 + 32 * 16 * 4 + 16 * 8 * 4)),
            "quarter resolution reuses the exact existing mip bytes including alpha");
        Check(a.AsSpan(76, 32).SequenceEqual(original.AsSpan(76, 32)) && File.ReadAllBytes(rgba).SequenceEqual(original), "mip reduction preserves the pixel format and never edits its source");
        var stamp = File.GetLastWriteTimeUtc(half);
        Check(DdsMipReducer.Prepare(rgba, 1) == half && File.GetLastWriteTimeUtc(half) == stamp, "an unchanged source reuses its content-addressed mip cache without rewriting it");
        ModAssetPreparation.WriteDds(rgba, 32, 16, (_, _) => new SkiaSharp.SKColor(2, 4, 6));
        Check(DdsMipReducer.Prepare(rgba, 1) != half, "same-size changed texture data invalidates the cache");
        foreach (string format in new[] { "DXT1", "DXT3", "DXT5" })
        {
            string path = Path.Combine(root, format + ".dds"); byte[] compressed = CompressedDds(format, 64, 16); File.WriteAllBytes(path, compressed);
            byte[] reduced = File.ReadAllBytes(DdsMipReducer.Prepare(path, 2)); int block = format == "DXT1" ? 8 : 16;
            int skip = 16 * 4 * block + 8 * 2 * block;
            Check(DdsValue(reduced, 16) == 16 && DdsValue(reduced, 12) == 4 && DdsValue(reduced, 20) == 4 * block && DdsValue(reduced, 28) == 5 &&
                reduced.AsSpan(76, 32).SequenceEqual(compressed.AsSpan(76, 32)) && reduced.AsSpan(128).SequenceEqual(compressed.AsSpan(128 + skip)),
                format + " rectangular mip reduction preserves compressed blocks and minimum block sizes");
            File.WriteAllBytes(path, CompressedDds(format, 32, 8));
            bool rejected = false; try { DdsMipReducer.Prepare(path, 2); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, format + " does not promote an 8x2 mip to an invalid block-compressed base texture");
        }
        string edge = Path.Combine(root, "edge.dds"); ModAssetPreparation.WriteDds(edge, 2, 1, (_, _) => SkiaSharp.SKColors.White);
        byte[] last = File.ReadAllBytes(DdsMipReducer.Prepare(edge, 1));
        Check(DdsValue(last, 16) == 1 && DdsValue(last, 12) == 1 && DdsValue(last, 28) == 1 && DdsValue(last, 108) == 0x1000 && (DdsValue(last, 8) & 0x20000) == 0,
            "single remaining mip removes complex/mipmap header flags");
        var invalid = new Dictionary<string, Action<byte[]>>
        {
            ["magic"] = h => h[0] = 0, ["header size"] = h => DdsSet(h, 4, 123), ["pixel header size"] = h => DdsSet(h, 76, 31),
            ["oversized width"] = h => DdsSet(h, 16, uint.MaxValue), ["zero height"] = h => DdsSet(h, 12, 0),
            ["cube map"] = h => DdsSet(h, 112, 0x200), ["volume flag"] = h => DdsSet(h, 8, DdsValue(h, 8) | 0x800000),
            ["unsupported DX10"] = h => { DdsSet(h, 80, 4); DdsSet(h, 84, 0x30315844); },
            ["padded pitch"] = h => DdsSet(h, 20, 400), ["excessive mip count"] = h => DdsSet(h, 28, 999),
            ["no smaller mip"] = h => DdsSet(h, 28, 1), ["unsupported RGB bits"] = h => DdsSet(h, 88, 24)
        };
        foreach (var item in invalid)
        {
            byte[] bytes = original.ToArray(); item.Value(bytes); string path = Path.Combine(root, "bad.dds"); File.WriteAllBytes(path, bytes);
            bool rejected = false; try { DdsMipReducer.Prepare(path, 1); } catch (InvalidDataException) { rejected = true; }
            Check(rejected && File.ReadAllBytes(path).SequenceEqual(bytes), "unsupported " + item.Key + " is rejected without source mutation");
        }
        foreach (int trim in new[] { -1, 1 })
        {
            string path = Path.Combine(root, "length.dds"); File.WriteAllBytes(path, trim < 0 ? original[..^1] : original.Concat(new byte[1]).ToArray());
            bool rejected = false; try { DdsMipReducer.Prepare(path, 1); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "DDS payload length must exactly match all declared mip levels: " + trim);
        }
        string failure = DdsMipReducer.Prepare(rgba, 2); File.Delete(failure); Directory.CreateDirectory(failure);
        bool locked = false; try { DdsMipReducer.Prepare(rgba, 2); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { locked = true; }
        Check(locked && !Directory.EnumerateFiles(Path.GetDirectoryName(failure)!, "*.tmp").Any(), "failed cache publication cleans temporary output and leaves its source intact");
    }
    private static byte[] CompressedDds(string format, int width, int height)
    {
        var header = new byte[128]; "DDS "u8.CopyTo(header);
        DdsSet(header, 4, 124); DdsSet(header, 8, 0xa1007); DdsSet(header, 12, (uint)height); DdsSet(header, 16, (uint)width);
        DdsSet(header, 76, 32); DdsSet(header, 80, 4); System.Text.Encoding.ASCII.GetBytes(format).CopyTo(header, 84); DdsSet(header, 108, 0x401008);
        using var stream = new MemoryStream(); stream.Write(header); int levels = 0, block = format == "DXT1" ? 8 : 16;
        while (true)
        {
            int length = Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * block;
            if (levels == 0) DdsSet(header, 20, (uint)length);
            stream.Write(Enumerable.Repeat((byte)(20 + levels++), length).ToArray());
            if (width == 1 && height == 1) break; width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
        }
        DdsSet(header, 28, (uint)levels); stream.Position = 0; stream.Write(header); return stream.ToArray();
    }
    private static void VerifyBudgetPipeline(string root, string library, VisualTweaksViewModel visuals)
    {
        string player = Path.Combine(root, "player"); Directory.CreateDirectory(player);
        string sky = Path.Combine("PlatformContent", "pc", "textures", "sky", "sky512_bk.tex"), indoor = ModAssetPreparation.IndoorSkyAlias(sky)!;
        string water = Path.Combine("PlatformContent", "pc", "textures", "water", "normal_01.dds");
        foreach (string path in new[] { sky, indoor, water }) ModAssetPreparation.WriteDds(Path.Combine(player, path), 32, 32, (_, _) => new SkiaSharp.SKColor(50, 90, 140));
        byte[] original = File.ReadAllBytes(Path.Combine(player, sky));
        App.State.Prop.Mods = []; App.PlayerState.Prop.ModManifest.Clear();
        Directory.CreateDirectory(Path.GetDirectoryName(App.PlayerState.FileLocation)!);
        File.WriteAllText(App.PlayerState.FileLocation, JsonSerializer.Serialize(App.PlayerState.Prop)); App.PlayerState.Load(false);
        using var bootstrapper = new Bootstrapper(LaunchMode.Player);
        typeof(Bootstrapper).GetField("_latestVersionDirectory", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, player);
        typeof(Bootstrapper).GetField("_latestVersionGuid", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, "fixture-player-14");
        typeof(Bootstrapper).GetField("PackageDirectoryMap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bootstrapper, new Dictionary<string, string>());
        var method = typeof(Bootstrapper).GetMethod("ApplyModifications", BindingFlags.Instance | BindingFlags.NonPublic)!;
        bool Apply() => AwaitResult((Task<bool>)method.Invoke(bootstrapper, null)!);
        visuals.SkyTextureResolution = 1;
        Check(Apply() && DdsValue(File.ReadAllBytes(Path.Combine(player, sky)), 16) == 16 && DdsValue(File.ReadAllBytes(Path.Combine(player, water)), 16) == 32,
            "production sky budget changes only its selected group");
        Check(Apply() && DdsValue(File.ReadAllBytes(Path.Combine(player, sky)), 16) == 16, "repeated launches read the preserved source instead of shrinking cumulatively");
        visuals.SkyTextureResolution = 2; Check(Apply() && DdsValue(File.ReadAllBytes(Path.Combine(player, sky)), 16) == 8, "changing the budget selects the original's requested mip");
        visuals.SkyTextureResolution = 0;
        Check(Apply() && File.ReadAllBytes(Path.Combine(player, sky)).SequenceEqual(original) && !App.PlayerState.Prop.ModManifest.Contains(sky), "Original restores the exact pre-budget file and retires its manifest entry");
        foreach (string name in new[] { "high", "low" }) ModAssetPreparation.WriteDds(Path.Combine(library, name, sky), 64, 64, (_, _) => name == "high" ? SkiaSharp.SKColors.Red : SkiaSharp.SKColors.Blue);
        App.State.Prop.Mods = [new ModConfig { FolderName = "high", Priority = 9 }, new ModConfig { FolderName = "low", Priority = 1 }];
        visuals.SkyTextureResolution = 1;
        Check(Apply() && File.ReadAllBytes(Path.Combine(player, sky)).SequenceEqual(File.ReadAllBytes(DdsMipReducer.Prepare(Path.Combine(library, "high", sky), 1))),
            "budget processes the winning installed mod after priority resolution");
        Check(ModApplicationReporter.Read()!.Entries.Any(entry => entry.Path == sky && entry.Source.Contains("high · 50%", StringComparison.Ordinal)) &&
            ModApplicationReporter.Read()!.Entries.Any(entry => entry.Source == "low" && entry.Status == "Shadowed"), "application report retains the winning owner, budget and priority conflict");
        visuals.SkyTextureResolution = 0;
        Check(Apply() && File.ReadAllBytes(Path.Combine(player, sky)).SequenceEqual(File.ReadAllBytes(Path.Combine(library, "high", sky))), "Original removes only the budget while the installed mod stays enabled");
        foreach (var mod in App.State.Prop.Mods) mod.Enabled = false;
        visuals.SkyTextureResolution = 1;
        Check(Apply() && DdsValue(File.ReadAllBytes(Path.Combine(player, sky)), 16) == 16, "disabling a mod with its budget active switches to the preserved original instead of the old replacement");
        visuals.SkyTextureResolution = 0;
        Check(Apply() && File.ReadAllBytes(Path.Combine(player, sky)).SequenceEqual(original) && File.ReadAllBytes(Path.Combine(player, indoor)).SequenceEqual(original), "disabling the mod restores both untouched original sky sources");
        string unsupported = Path.Combine(player, "PlatformContent", "pc", "textures", "water", "normal_02.dds"); File.WriteAllText(unsupported, "unsupported DDS");
        visuals.WaterTextureResolution = 1;
        Check(Apply() && File.ReadAllText(unsupported) == "unsupported DDS" && DdsValue(File.ReadAllBytes(Path.Combine(player, water)), 16) == 16 &&
            ModApplicationReporter.Read()!.Entries.Any(entry => entry.Path.EndsWith("normal_02.dds", StringComparison.Ordinal) && entry.Status == "Skipped"), "unsupported texture is unchanged and reported while healthy files still receive their budget");
        string lockedSource = Path.Combine(player, "PlatformContent", "pc", "textures", "water", "normal_03.dds"); ModAssetPreparation.WriteDds(lockedSource, 32, 32, (_, _) => SkiaSharp.SKColors.Gray);
        using (var locked = new FileStream(lockedSource, FileMode.Open, FileAccess.Read, FileShare.None))
            Check(!Apply() && ModApplicationReporter.Read()!.Entries.Any(entry => entry.Path.EndsWith("normal_03.dds", StringComparison.Ordinal) && entry.Status == "Failed") &&
                ModApplicationReporter.Read()!.Entries.Any(entry => entry.Path == water && entry.Status == "Current"), "unreadable source produces a real budget error without stopping other textures");
        Check(Apply() && DdsValue(File.ReadAllBytes(lockedSource), 16) == 16, "budget retries an unreadable source successfully after its sharing lock is released");
        visuals.WaterTextureResolution = 0; Check(Apply(), "water budget can be removed through the normal restoration path");
        var backup = new ModBackupStore(player);
        Check(backup.OriginalSource(sky, true) == null && backup.OriginalSource("missing.dds", false) == null, "missing preserved originals never reuse a previously modified target");
        string absent = Path.Combine("content", "absent.dds"); backup.Capture(absent); Directory.CreateDirectory(Path.Combine(player, "content")); File.WriteAllText(Path.Combine(player, absent), "later replacement");
        Check(backup.OriginalSource(absent, false) == null, "a captured absent original cannot become a later mod's source");
        File.WriteAllText(Path.Combine(_output, "texture-budget-probe.json"), JsonSerializer.Serialize(new { fixtureOnly = true, originalBytes = original.Length, halfBytes = new FileInfo(DdsMipReducer.Prepare(Path.Combine(library, "high", sky), 1)).Length, restored = true, gameTested = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void RenderTextureBudget(Theme theme, int width)
    {
        App.Settings.Prop.Theme = theme; Froststrap.UI.Elements.Base.AvaloniaWindow.ApplyTheme();
        var window = new SettingsWindow(false) { Width = width, Height = 900 }; window.Show(); RunFor(70);
        ((MainWindowViewModel)window.DataContext!).NavigateRegistered("fastflags"); RunFor(90);
        var groups = window.GetVisualDescendants().OfType<CardExpander>().ToArray();
        foreach (var group in groups) group.IsExpanded = Equals(group.Header, "Texture resolution budget");
        var selected = groups.Single(group => Equals(group.Header, "Texture resolution budget")); RunFor(90);
        var scroller = selected.GetVisualAncestors().OfType<ScrollViewer>().First();
        var point = selected.TranslatePoint(default, scroller); if (point.HasValue) scroller.Offset = new Vector(0, scroller.Offset.Y + point.Value.Y - 16);
        RunFor(70); Capture(window, theme, "revision14-texture-budget-" + width);
        var choices = selected.GetVisualDescendants().OfType<ComboBox>().ToArray();
        Check(choices.Length == 6 && choices.All(choice => choice.ItemCount == 3), "actual budget menu exposes all six independent resolution controls: " + theme + " " + width);
        choices[0].SelectedIndex = 1; choices[0].Focus(NavigationMethod.Tab);
        window.KeyPress(Key.Down, RawInputModifiers.Alt, default, null); window.KeyRelease(Key.Down, RawInputModifiers.Alt, default, null); Pump();
        window.KeyPress(Key.Down, RawInputModifiers.None, default, null); window.KeyRelease(Key.Down, RawInputModifiers.None, default, null);
        window.KeyPress(Key.Enter, RawInputModifiers.None, default, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, default, null); RunFor(40);
        Check(choices[0].SelectedIndex == 2 && App.Settings.Prop.SkyTextureResolution == 2, "keyboard selection updates the actual sky budget: " + theme + " " + width);
        choices[0].SelectedIndex = 1;
        if (width == 760) { window.SetRenderScaling(1.5); RunFor(90); Capture(window, theme, "revision14-texture-budget-150"); window.SetRenderScaling(1); }
        window.Close();
    }
}
