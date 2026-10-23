// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using SkiaSharp;

namespace Froststrap.Services;

internal static class BuiltInVisualMods
{
    internal static IReadOnlyList<string> FireStyles { get; } = ["Roblox default", "Ember orange", "Icy blue", "Hidden"];
    internal static IReadOnlyList<string> SmokeStyles { get; } = ["Roblox default", "Light gray", "Charcoal", "Hidden"];
    internal static IReadOnlyList<string> SparklesStyles { get; } = ["Roblox default", "Warm gold", "Icy blue", "Hidden"];
    internal static IReadOnlyList<string> ForceFieldStyles { get; } = ["Roblox default", "Cyan", "Violet", "Hidden"];
    internal static IReadOnlyList<string> ExplosionStyles { get; } = ["Roblox default", "Core and ring only", "Hidden"];
    internal static IReadOnlyList<string> StylesFor(string effect) => effect switch
    {
        "fire" => FireStyles, "smoke" => SmokeStyles, "sparkles" => SparklesStyles, "forcefield" => ForceFieldStyles,
        _ => throw new ArgumentOutOfRangeException(nameof(effect))
    };
    internal static IEnumerable<(string Relative, string Source)> Files()
    {
        var settings = App.Settings.Prop;
        if (settings.SmoothStuds)
            yield return (Path.Combine("PlatformContent", "pc", "textures", "studs.dds"), ModAssetPreparation.Solid("plain-studs", 255, 255, 255, 255, 16, 256));
        if (settings.ExplosionStyle is "Core and ring only" or "Hidden")
            foreach (string part in settings.ExplosionStyle == "Hidden" ? new[] { "core", "implosion", "shockwave", "smoke" } : new[] { "implosion", "smoke" })
                yield return (Path.Combine("content", "textures", "particles", $"explosion01_{part}_main.dds"), ModAssetPreparation.Solid("hidden-explosion", 0, 0, 0, 0, 256, 256));
        if (settings.SmoothWater)
            for (int i = 1; i <= 25; i++) yield return ($"PlatformContent/pc/textures/water/normal_{i:00}.dds".Replace('/', Path.DirectorySeparatorChar), ModAssetPreparation.Solid("flat-normal", 128, 128, 255, 128, 256, 256));
        if (settings.SmoothPlastic)
            {
                yield return (Path.Combine("PlatformContent", "pc", "textures", "plastic", "normal.dds"), ModAssetPreparation.Solid("flat-normal", 128, 128, 255, 128, 128, 2048));
                yield return (Path.Combine("PlatformContent", "pc", "textures", "plastic", "normaldetail.dds"), ModAssetPreparation.Solid("flat-normal", 128, 128, 255, 128, 512, 512));
                yield return (Path.Combine("PlatformContent", "pc", "textures", "plastic", "diffuse.dds"), ModAssetPreparation.Solid("flat-plastic-color", 255, 255, 255, 255, 128, 2048));
            }
        foreach (var file in ParticleFiles(settings.FireStyle, "fire", ["fire_color.dds", "fire_sparks_color.dds"], [("fire_main.dds", 256), ("fire_sparks_main.dds", 64)], new(255, 119, 30), new(91, 189, 255))) yield return file;
        foreach (var file in ParticleFiles(settings.SmokeStyle, "smoke", ["smoke_color.dds"], [("smoke_main.dds", 128)], new(203, 210, 220), new(42, 46, 54))) yield return file;
        foreach (var file in ParticleFiles(settings.SparklesStyle, "sparkles", ["sparkles_color.dds"], [("sparkles_main.dds", 128)], new(255, 201, 62), new(132, 220, 255))) yield return file;
        foreach (var file in ParticleFiles(settings.ForceFieldStyle, "forcefield", ["forcefield_glow_color.dds", "forcefield_vortex_color.dds"], [("forcefield_glow_main.dds", 256), ("forcefield_vortex_main.dds", 128)], new(65, 218, 240), new(181, 111, 255))) yield return file;
        if (settings.HideSunMoon)
        {
            string source = Path.Combine(Paths.Cache, "PreparedTextures", "black-disc.jpg");
            if (!File.Exists(source))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(source)!);
                using var bitmap = new SKBitmap(4, 4); bitmap.Erase(SKColors.Black);
                using var image = SKImage.FromBitmap(bitmap);
                using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 100);
                string temporary = source + "." + Guid.NewGuid().ToString("N");
                try { using (var stream = File.Create(temporary)) encoded.SaveTo(stream); File.Move(temporary, source, true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            foreach (string file in new[] { "sun.jpg", "moon.jpg" }) yield return (Path.Combine("content", "sky", file), source);
        }
        if (settings.DefaultSkyPalette is "Warm dusk" or "Deep blue" or "Overcast")
        {
            string palette = settings.DefaultSkyPalette;
            var (top, horizon) = palette switch
            {
                "Warm dusk" => (new SKColor(32, 22, 65), new SKColor(235, 136, 87)),
                "Deep blue" => (new SKColor(5, 14, 55), new SKColor(63, 112, 163)),
                _ => (new SKColor(79, 89, 102), new SKColor(182, 190, 197))
            };
            foreach (string face in new[] { "bk", "dn", "ft", "lf", "rt", "up" })
            {
                string target = Path.Combine(Paths.Cache, "PreparedTextures", $"sky-v2-mips-{palette}-{face}.dds");
                if (!File.Exists(target)) ModAssetPreparation.WriteDds(target, 128, 128, (x, y) =>
                {
                    double u = (x + 0.5) / 64 - 1, v = (y + 0.5) / 64 - 1;
                    double elevation = (face == "up" ? 1 : face == "dn" ? -1 : -v) / Math.Sqrt(1 + u * u + v * v);
                    double blend = Math.Pow(Math.Abs(elevation), 0.55);
                    byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * blend);
                    return new SKColor(Mix(horizon.Red, top.Red), Mix(horizon.Green, top.Green), Mix(horizon.Blue, top.Blue));
                });
                yield return (Path.Combine("PlatformContent", "pc", "textures", "sky", $"sky512_{face}.tex"), target);
            }
        }
    }
    private static IEnumerable<(string Relative, string Source)> ParticleFiles(string style, string effect, string[] colors, (string File, int Size)[] shapes, SKColor first, SKColor second)
    {
        int selected = Array.IndexOf(StylesFor(effect).ToArray(), style);
        if (selected <= 0) yield break;
        if (style == "Hidden")
        {
            foreach (var shape in shapes)
                yield return (Path.Combine("content", "textures", "particles", shape.File), ModAssetPreparation.Solid("hidden-particle", 0, 0, 0, 0, shape.Size, shape.Size));
            if (effect == "fire") yield return (Path.Combine("content", "textures", "particles", "legacy_fire_alpha_color.dds"), ModAssetPreparation.Solid("hidden-particle", 0, 0, 0, 0, 128, 32));
        }
        else
        {
            SKColor color = selected == 1 ? first : second;
            foreach (string file in colors)
                yield return (Path.Combine("content", "textures", "particles", file), ModAssetPreparation.Solid("particle-color", color.Red, color.Green, color.Blue, 255, 128, 32));
        }
    }
}
