// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Security.Cryptography;
using SkiaSharp;

namespace Froststrap.Services;

internal static class ModAssetPreparation
{
    private static readonly HashSet<string> Roots = new(StringComparer.OrdinalIgnoreCase) { "content", "PlatformContent", "ExtraContent" };
    private static readonly HashSet<string> SkyNames = new(StringComparer.OrdinalIgnoreCase) { "sky512_bk.tex", "sky512_dn.tex", "sky512_ft.tex", "sky512_lf.tex", "sky512_rt.tex", "sky512_up.tex" };
    internal static string ContentRoot(string directory)
    {
        string current = directory;
        for (int depth = 0; depth < 8; depth++)
        {
            var folders = Directory.GetDirectories(current);
            if (folders.Any(folder => Roots.Contains(Path.GetFileName(folder)))) return current;
            if (folders.Length != 1) break;
            current = folders[0];
        }
        return directory;
    }
    internal static IEnumerable<(string Relative, string Source)> Files(string directory, Action<string, string, string>? report = null)
    {
        string root = ContentRoot(directory);
        foreach (string source in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, source);
            string first = relative.Split(Path.DirectorySeparatorChar)[0];
            if (!Roots.Contains(first) || !ModFilePolicy.IsAllowed(relative))
            {
                report?.Invoke(relative, "Skipped", !ModFilePolicy.IsAllowed(relative) ? "Shader and executable payloads are not supported." : "Outside the supported content folders.");
                continue;
            }
            string extension = Path.GetExtension(relative);
            bool image = extension.Equals(".dds", StringComparison.OrdinalIgnoreCase) || extension.Equals(".tex", StringComparison.OrdinalIgnoreCase);
            if (image && Path.GetFileNameWithoutExtension(relative).EndsWith("_Delete", StringComparison.Ordinal))
            {
                string name = Path.GetFileNameWithoutExtension(relative)[..^7] + extension;
                yield return (Path.Combine(Path.GetDirectoryName(relative)!, name), Solid("transparent", 0, 0, 0, 0));
            }
            else
            {
                string prepared = source;
                if (image)
                {
                    try { prepared = PrepareTexture(source); }
                    catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
                    { App.Logger.Warn(error, "A mod texture could not be prepared."); report?.Invoke(relative, "Failed", "Texture preparation failed: " + error.Message); continue; }
                }
                yield return (relative, prepared);
            }
        }
    }
    private static string PrepareTexture(string source)
    {
        using var stream = File.OpenRead(source);
        Span<byte> magic = stackalloc byte[4];
        if (stream.Read(magic) == 4 && magic.SequenceEqual("DDS "u8)) return source;
        stream.Position = 0;
        string key = Convert.ToHexString(SHA256.HashData(stream));
        string target = Path.Combine(Paths.Cache, "PreparedTextures", "v2-mips-" + key + ".dds");
        if (File.Exists(target)) return target;
        using var codec = SKCodec.Create(source, out var result);
        if (result != SKCodecResult.Success) throw new InvalidDataException("The mod texture could not be opened: " + result);
        if (codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 16777216) throw new InvalidDataException("The mod texture is larger than 16 megapixels.");
        using var bitmap = SKBitmap.Decode(codec) ?? throw new InvalidDataException("The mod texture could not be decoded.");
        WriteDds(target, bitmap.Width, bitmap.Height, (x, y) => bitmap.GetPixel(x, y));
        return target;
    }
    internal static string Solid(string name, byte red, byte green, byte blue, byte alpha, int width = 4, int height = 4)
    {
        string target = Path.Combine(Paths.Cache, "PreparedTextures", $"v3-{width}x{height}-{name}-{red}-{green}-{blue}-{alpha}.dds");
        if (!File.Exists(target)) WriteDds(target, width, height, (_, _) => new SKColor(red, green, blue, alpha));
        return target;
    }
    internal static void PruneCache()
    {
        string root = Path.Combine(Paths.Cache, "PreparedTextures");
        if (!Directory.Exists(root)) return;
        long bytes = 0;
        int count = 0;
        foreach (var file in new DirectoryInfo(root).GetFiles().OrderByDescending(file => file.LastWriteTimeUtc))
        {
            bytes += file.Length; count++;
            if (count <= 512 && bytes <= 1073741824) continue;
            try { file.Delete(); }
            catch (IOException error) { App.Logger.Warn(error, "Prepared texture cache file is still in use."); }
        }
    }
    internal static string? IndoorSkyAlias(string relative)
    {
        string directory = Path.GetDirectoryName(relative) ?? "";
        string name = Path.GetFileName(relative);
        if (!directory.Equals(Path.Combine("PlatformContent", "pc", "textures", "sky"), StringComparison.OrdinalIgnoreCase) ||
            !SkyNames.Contains(name)) return null;
        return Path.Combine(directory, "indoor" + name[3..]);
    }
    internal static void WriteDds(string target, int width, int height, Func<int, int, SKColor> pixel)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if ((long)width * height > 16777216) throw new InvalidDataException("The texture is larger than 16 megapixels.");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        int levels = 1;
        for (int size = Math.Max(width, height); size > 1; size /= 2) levels++;
        try
        {
            using (var writer = new BinaryWriter(File.Create(temporary)))
            {
                writer.Write("DDS "u8); writer.Write(124); writer.Write(0x2100f); writer.Write(height); writer.Write(width); writer.Write(width * 4); writer.Write(0); writer.Write(levels);
                for (int i = 0; i < 11; i++) writer.Write(0);
                writer.Write(32); writer.Write(0x41); writer.Write(0); writer.Write(32);
                writer.Write(0x000000ff); writer.Write(0x0000ff00); writer.Write(0x00ff0000); writer.Write(0xff000000u);
                writer.Write(levels > 1 ? 0x401008 : 0x1000); for (int i = 0; i < 4; i++) writer.Write(0);
                var pixels = new uint[width * height];
                for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                {
                    var color = pixel(x, y);
                    pixels[y * width + x] = (uint)(color.Red | color.Green << 8 | color.Blue << 16 | color.Alpha << 24);
                }
                while (true)
                {
                    foreach (uint value in pixels) writer.Write(value);
                    if (width == 1 && height == 1) break;
                    int nextWidth = Math.Max(1, width / 2), nextHeight = Math.Max(1, height / 2);
                    var next = new uint[nextWidth * nextHeight];
                    for (int y = 0; y < nextHeight; y++) for (int x = 0; x < nextWidth; x++)
                    {
                        uint red = 0, green = 0, blue = 0, alpha = 0, count = 0;
                        for (int sy = y * height / nextHeight; sy < (y + 1) * height / nextHeight; sy++)
                            for (int sx = x * width / nextWidth; sx < (x + 1) * width / nextWidth; sx++)
                            {
                                uint value = pixels[sy * width + sx]; count++;
                                red += value & 255; green += value >> 8 & 255; blue += value >> 16 & 255; alpha += value >> 24;
                            }
                        next[y * nextWidth + x] = ((red + count / 2) / count) | ((green + count / 2) / count) << 8 | ((blue + count / 2) / count) << 16 | ((alpha + count / 2) / count) << 24;
                    }
                    pixels = next; width = nextWidth; height = nextHeight;
                }
            }
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
