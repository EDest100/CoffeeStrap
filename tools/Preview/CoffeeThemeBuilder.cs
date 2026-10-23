// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Froststrap.Services;
using SkiaSharp;

internal static class CoffeeThemeBuilder
{
    internal static readonly SKColor Espresso = new(43, 29, 23), Caramel = new(178, 123, 80), Cream = new(255, 240, 220);
    internal static SKColor Recolor(SKColor source)
    {
        if (source.Alpha == 0) return source;
        double light = (source.Red * .2126 + source.Green * .7152 + source.Blue * .0722) / 255;
        return Mix(light < .5 ? Espresso : Caramel, light < .5 ? Caramel : Cream, light < .5 ? light * 2 : light * 2 - 1).WithAlpha(source.Alpha);
    }
    internal static SKColor Mix(SKColor first, SKColor second, double amount) => new(
        (byte)Math.Round(first.Red + (second.Red - first.Red) * amount),
        (byte)Math.Round(first.Green + (second.Green - first.Green) * amount),
        (byte)Math.Round(first.Blue + (second.Blue - first.Blue) * amount));

    internal static bool Eligible(string relative)
    {
        relative = relative.Replace('\\', '/');
        if (!relative.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return false;
        return new[] { "content/textures/ui/", "content/textures/Cursors/", "content/textures/DevConsole/", "content/textures/loading/", "content/textures/SelfView/", "ExtraContent/textures/ui/", "PlatformContent/pc/textures/ui/" }.Any(root => relative.StartsWith(root, StringComparison.OrdinalIgnoreCase)) ||
            relative.StartsWith("ExtraContent/LuaPackages/", StringComparison.OrdinalIgnoreCase) && relative.Contains("/SpriteSheets/", StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith("content/textures/", StringComparison.OrdinalIgnoreCase) && relative[17..].IndexOf('/') < 0 &&
            (Path.GetFileName(relative).Contains("Cursor", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(relative).Contains("LoadingCircle", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(relative).StartsWith("icon_", StringComparison.OrdinalIgnoreCase));
    }

    internal static void Build(string source, string output, string sourceVersion)
    {
        source = Path.GetFullPath(source); output = Path.GetFullPath(output);
        if (output.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || source.StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || source == output)
            throw new InvalidDataException("Keep the read-only client source separate from theme output.");
        Directory.CreateDirectory(output);
        string archive = Path.Combine(output, "CoffeeStrap-Espresso.zip"), temporary = archive + ".tmp";
        var manifest = new List<object>();
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
        using var logoStream = typeof(Froststrap.App).Assembly.GetManifestResourceStream("Froststrap.CoffeeStrap.png") ?? throw new InvalidDataException("CoffeeStrap branding is missing.");
        using var logo = SKBitmap.Decode(logoStream);
        using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
        {
            foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(source, file).Replace('\\', '/');
                if (!Eligible(relative)) continue;
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked source assets are not supported.");
                using var codec = SKCodec.Create(file, out var result);
                if (result != SKCodecResult.Success || codec == null || codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 16777216)
                    throw new InvalidDataException("Unsupported image: " + relative);
                using var bitmap = SKBitmap.Decode(codec) ?? throw new InvalidDataException("Cannot decode " + relative);
                bool coffeeMark = relative.Contains("/TopBar/coloredlogo", StringComparison.OrdinalIgnoreCase) ||
                    relative.EndsWith("/loading/robloxlogo.png", StringComparison.OrdinalIgnoreCase) || relative.Contains("/loading/robloxTilt", StringComparison.OrdinalIgnoreCase) ||
                    relative.EndsWith("/ui/InGameMenu/roblox_logo.png", StringComparison.OrdinalIgnoreCase);
                if (coffeeMark)
                {
                    using var canvas = new SKCanvas(bitmap); canvas.Clear(SKColors.Transparent);
                    float size = Math.Min(bitmap.Width, bitmap.Height);
                    canvas.DrawBitmap(logo, new SKRect((bitmap.Width - size) / 2, (bitmap.Height - size) / 2, (bitmap.Width + size) / 2, (bitmap.Height + size) / 2));
                }
                else for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++) bitmap.SetPixel(x, y, Recolor(bitmap.GetPixel(x, y)));
                using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                Add(zip, relative, data.ToArray());
                string sourceHash = Hash(File.ReadAllBytes(file)); inputs.Add(file, sourceHash);
                manifest.Add(new { path = relative, kind = coffeeMark ? "CoffeeStrap brand mark" : "palette PNG", bitmap.Width, bitmap.Height, sourceHash });
            }
            string icons = "ExtraContent/LuaPackages/Packages/_Index/BuilderIcons/BuilderIcons/";
            foreach (string variant in new[] { "Regular", "Filled" })
            {
                string relative = icons + $"Font/BuilderIcons-{variant}.ttf";
                byte[] original = File.ReadAllBytes(Path.Combine(source, relative));
                inputs.Add(Path.Combine(source, relative), Hash(original));
                byte[] font = ColorFont(original);
                Add(zip, relative, font);
                manifest.Add(new { path = relative, kind = "COLR/CPAL icon font", sourceHash = Hash(original) });
            }
            string family = icons + "BuilderIcons.json";
            byte[] familyBytes = File.ReadAllBytes(Path.Combine(source, family)); inputs.Add(Path.Combine(source, family), Hash(familyBytes));
            Add(zip, family, familyBytes);
            manifest.Add(new { path = family, kind = "original icon family references" });
            foreach (string face in new[] { "bk", "dn", "ft", "lf", "rt", "up" })
            {
                string dds = Path.Combine(output, "sky-" + face + ".dds");
                ModAssetPreparation.WriteDds(dds, 256, 256, (x, y) => Sky(face, x, y, 256));
                foreach (string prefix in new[] { "sky", "indoor" })
                {
                    string relative = $"PlatformContent/pc/textures/sky/{prefix}512_{face}.tex";
                    Add(zip, relative, File.ReadAllBytes(dds)); manifest.Add(new { path = relative, kind = "generated full-mip coffee sky" });
                }
                File.Delete(dds);
            }
            foreach (string name in new[] { "fire_color", "fire_sparks_color", "smoke_color", "sparkles_color", "forcefield_glow_color", "forcefield_vortex_color" })
            {
                string dds = Path.Combine(output, "particle.dds");
                ModAssetPreparation.WriteDds(dds, 128, 32, (x, _) => Mix(name.StartsWith("smoke", StringComparison.Ordinal) ? Espresso : Caramel, Cream, x / 127.0));
                string relative = $"content/textures/particles/{name}.dds";
                Add(zip, relative, File.ReadAllBytes(dds)); manifest.Add(new { path = relative, kind = "generated full-mip particle color ramp" }); File.Delete(dds);
            }
            string diffuse = "PlatformContent/pc/textures/plastic/diffuse.dds", surface = Path.Combine(output, "surface.dds");
            ModAssetPreparation.WriteDds(surface, 128, 2048, (_, _) => new SKColor(250, 239, 221));
            Add(zip, diffuse, File.ReadAllBytes(surface)); manifest.Add(new { path = diffuse, kind = "generated warm default plastic diffuse" }); File.Delete(surface);
            if (manifest.Count < 100) throw new InvalidDataException("Client source is incomplete; refusing to publish a partial pack.");
            Add(zip, "CoffeeStrap-theme.json", JsonSerializer.SerializeToUtf8Bytes(new { theme = "CoffeeStrap Espresso", version = "1.0.0", sourceVersion, palette = new[] { "#2B1D17", "#B27B50", "#FFF0DC" }, assets = manifest }, new JsonSerializerOptions { WriteIndented = true }));
            Add(zip, "README.txt", Encoding.UTF8.GetBytes("CoffeeStrap Espresso\nInstall in CoffeeStrap > Texture Editor > Community Mods. Save and relaunch Roblox through CoffeeStrap.\nDisable or use the trash icon, then relaunch to restore originals. A higher-priority mod can replace these files.\nThemes local default assets only: uploaded game assets, scripted colors/lighting, remote UI and newly changed client paths can differ.\nNo shaders, executable patches, scripts, memory editing or text-font replacements.\nSource and regeneration: https://github.com/EDest100/CoffeeStrap/tree/main/tools/Preview\nOriginal Roblox artwork/icon outlines remain the property of their respective owners. CoffeeStrap's palette and generator: MPL-2.0.\n"));
        }
        File.Move(temporary, archive, true);
        Preview(archive, Path.Combine(output, "EspressoPreview.png"));
        bool sourceUnchanged = inputs.All(input => Hash(File.ReadAllBytes(input.Key)) == input.Value);
        if (!sourceUnchanged) throw new IOException("The theme's input changed during generation.");
        File.WriteAllText(Path.Combine(output, "theme-build.json"), JsonSerializer.Serialize(new { sourceVersion, files = manifest.Count, bytes = new FileInfo(archive).Length, sha256 = Hash(File.ReadAllBytes(archive)), sourceFilesChecked = inputs.Count, sourceUnchanged }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"CoffeeStrap Espresso: {manifest.Count} assets, {new FileInfo(archive).Length:N0} bytes.");
    }

    internal static SKColor Sky(string face, int x, int y, int size)
    {
        double u = (x + .5) * 2 / size - 1, v = (y + .5) * 2 / size - 1;
        double elevation = (face == "up" ? 1 : face == "dn" ? -1 : -v) / Math.Sqrt(1 + u * u + v * v);
        return Mix(new SKColor(222, 178, 131), elevation >= 0 ? new SKColor(71, 44, 36) : new SKColor(105, 72, 51), Math.Pow(Math.Abs(elevation), .65));
    }
    private static void Add(ZipArchive zip, string path, byte[] bytes)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.SmallestSize);
        entry.LastWriteTime = new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
        using var stream = entry.Open(); stream.Write(bytes);
    }
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    internal static byte[] ColorFont(byte[] original)
    {
        int U16(byte[] b, int at) => BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(at));
        uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(at));
        void W16(byte[] b, int at, int value) => BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(at), checked((ushort)value));
        void W32(byte[] b, int at, uint value) => BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at), value);
        if (original.Length < 12 || U32(original, 0) != 0x10000) throw new InvalidDataException("Expected a TrueType icon font.");
        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        for (int i = 0; i < U16(original, 4); i++)
        {
            int at = 12 + i * 16;
            string tag = Encoding.ASCII.GetString(original, at, 4);
            int start = checked((int)U32(original, at + 8)), length = checked((int)U32(original, at + 12));
            tables.Add(tag, original.AsSpan(start, length).ToArray());
        }
        if (tables.ContainsKey("COLR") || tables.ContainsKey("CPAL") || !tables.ContainsKey("glyf")) throw new InvalidDataException("Use the original monochrome BuilderIcons font.");
        int count = U16(tables["maxp"], 4) - 1;
        if (count < 1 || count > 8192) throw new InvalidDataException("Unexpected icon glyph count.");
        var colr = new byte[14 + count * 10];
        W16(colr, 2, count); W32(colr, 4, 14); W32(colr, 8, (uint)(14 + count * 6)); W16(colr, 12, count);
        for (int i = 0; i < count; i++)
        {
            W16(colr, 14 + i * 6, i + 1); W16(colr, 16 + i * 6, i); W16(colr, 18 + i * 6, 1);
            W16(colr, 14 + count * 6 + i * 4, i + 1);
        }
        var cpal = new byte[18]; W16(cpal, 2, 1); W16(cpal, 4, 1); W16(cpal, 6, 1); W32(cpal, 8, 14);
        cpal[14] = Caramel.Blue; cpal[15] = Caramel.Green; cpal[16] = Caramel.Red; cpal[17] = 255;
        tables.Add("COLR", colr); tables.Add("CPAL", cpal); tables.Remove("DSIG"); W32(tables["head"], 8, 0);
        int directory = 12 + tables.Count * 16, total = directory + tables.Values.Sum(data => (data.Length + 3) & ~3);
        var font = new byte[total]; W32(font, 0, 0x10000); W16(font, 4, tables.Count);
        int power = 1, exponent = 0; while (power * 2 <= tables.Count) { power *= 2; exponent++; }
        W16(font, 6, power * 16); W16(font, 8, exponent); W16(font, 10, tables.Count * 16 - power * 16);
        int offset = directory, index = 0, head = 0;
        foreach (var (tag, data) in tables)
        {
            int record = 12 + index++ * 16; Encoding.ASCII.GetBytes(tag).CopyTo(font, record);
            W32(font, record + 4, Checksum(data)); W32(font, record + 8, (uint)offset); W32(font, record + 12, (uint)data.Length);
            data.CopyTo(font, offset); if (tag == "head") head = offset;
            offset += (data.Length + 3) & ~3;
        }
        W32(font, head + 8, unchecked(0xB1B0AFBA - Checksum(font)));
        return font;
    }
    internal static uint Checksum(byte[] bytes)
    {
        uint sum = 0;
        for (int i = 0; i < bytes.Length; i += 4)
        {
            uint part = 0; for (int j = 0; j < 4; j++) part = part << 8 | (i + j < bytes.Length ? bytes[i + j] : 0u);
            sum = unchecked(sum + part);
        }
        return sum;
    }

    private static void Preview(string archive, string target)
    {
        using var bitmap = new SKBitmap(1200, 720); using var canvas = new SKCanvas(bitmap); canvas.Clear(new SKColor(25, 20, 17));
        using var face = SKTypeface.FromFamilyName("Segoe UI"); using var title = new SKFont(face, 48); using var text = new SKFont(face, 20);
        using var paint = new SKPaint { IsAntialias = true, Color = Cream };
        canvas.DrawText("CoffeeStrap Espresso", 44, 78, title, paint);
        paint.Color = new SKColor(218, 189, 159); canvas.DrawText("A little coffee for your client.", 46, 116, text, paint);
        using var zip = ZipFile.OpenRead(archive);
        using (var stream = zip.Entries.First(entry => entry.FullName.EndsWith("/TopBar/coloredlogo@3x.png", StringComparison.Ordinal)).Open())
        using (var mark = SKBitmap.Decode(stream)) canvas.DrawBitmap(mark, new SKRect(1060, 20, 1160, 120));
        int index = 0;
        foreach (string suffix in new[] { "Settings/Help/LeaveIcon.png", "Settings/Help/ResetIcon.png", "Cursors/KeyboardMouse/ArrowCursor.png", "MouseLockedCursor.png", "Backpack/Backpack.png" })
        {
            var entry = zip.Entries.FirstOrDefault(item => item.FullName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)); if (entry == null) continue;
            using var stream = entry.Open(); using var icon = SKBitmap.Decode(stream); if (icon == null) continue;
            float x = 56 + index++ * 115;
            paint.Color = new SKColor(54, 40, 31); canvas.DrawRoundRect(new SKRect(x - 12, 161, x + 78, 251), 16, 16, paint);
            canvas.DrawBitmap(icon, new SKRect(x + 3, 176, x + 63, 236));
        }
        using (var entryStream = zip.Entries.First(item => item.FullName.EndsWith("BuilderIcons-Regular.ttf", StringComparison.Ordinal)).Open())
        using (var data = new MemoryStream())
        {
            entryStream.CopyTo(data); data.Position = 0;
            using var fontFace = SKTypeface.FromStream(data); using var icons = new SKFont(fontFace, 38);
            paint.Color = SKColors.White;
            for (int i = 0; i < 24; i++) canvas.DrawText(char.ConvertFromUtf32(0xF101 + i), 57 + i % 12 * 46, 322 + i / 12 * 62, icons, paint);
        }
        using var sky = new SKBitmap(400, 400);
        for (int y = 0; y < 400; y++) for (int x = 0; x < 400; x++) sky.SetPixel(x, y, Sky("ft", x, y, 400));
        canvas.DrawBitmap(sky, new SKRect(710, 160, 1156, 535));
        paint.Color = Cream; canvas.DrawText("LOCAL ICONS + CURSORS", 46, 453, text, paint);
        canvas.DrawText("DEFAULT COFFEE SKY", 740, 575, text, paint);
        foreach (var (color, label, x) in new[] { (Espresso, "Espresso", 46), (Caramel, "Caramel", 246), (Cream, "Cream", 446) })
        {
            paint.Color = color; canvas.DrawRoundRect(new SKRect(x, 483, x + 176, 535), 10, 10, paint);
            paint.Color = Cream; canvas.DrawText(label, x, 575, text, paint);
        }
        paint.Color = new SKColor(192, 165, 141);
        canvas.DrawText("Generated asset preview · local default assets only · not a gameplay screenshot", 46, 665, text, paint);
        using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var output = File.Create(target); encoded.SaveTo(output);
    }
}
