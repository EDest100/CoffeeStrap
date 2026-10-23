// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
namespace Froststrap.Services;

internal static class TextureResolutionBudget
{
    internal static IReadOnlyList<string> Choices { get; } = ["Original", "50% dimensions", "25% dimensions"];
    internal static IEnumerable<(string Relative, int Levels)> Targets()
    {
        var settings = App.Settings.Prop;
        int sky = settings.SkyTextureResolution, water = settings.WaterTextureResolution, surface = settings.SurfaceTextureResolution, effects = settings.EffectTextureResolution;
        int clouds = settings.CloudTextureResolution, volumes = settings.CloudVolumeResolution;
        if (volumes is 1 or 2)
            foreach (string name in new[] { "clouds", "clouds-bc4", "cloudDetail3D", "cloudDetail3D-bc4", "cloudsfb" })
                yield return (Path.Combine("content", "sky", name + ".dds"), volumes);
        if (clouds is 1 or 2)
            foreach (string name in new[] { "cloudAdvection", "cloudDetail" })
                yield return (Path.Combine("content", "sky", name + ".dds"), clouds);
        if (sky is 1 or 2)
            foreach (string prefix in new[] { "sky512", "indoor512" })
                foreach (string face in new[] { "bk", "dn", "ft", "lf", "rt", "up" })
                    yield return (Path.Combine("PlatformContent", "pc", "textures", "sky", $"{prefix}_{face}.tex"), sky);
        if (water is 1 or 2)
            for (int i = 1; i <= 25; i++)
                yield return (Path.Combine("PlatformContent", "pc", "textures", "water", $"normal_{i:00}.dds"), water);
        if (surface is 1 or 2)
        {
            foreach (string name in new[] { "diffuse.dds", "normal.dds", "normaldetail.dds" })
                yield return (Path.Combine("PlatformContent", "pc", "textures", "plastic", name), surface);
            yield return (Path.Combine("PlatformContent", "pc", "textures", "studs.dds"), surface);
        }
        if (effects is 1 or 2)
            foreach (string name in new[] { "fire_main", "fire_sparks_main", "smoke_main", "sparkles_main", "forcefield_glow_main", "forcefield_vortex_main", "explosion01_core_main", "explosion01_implosion_main", "explosion01_shockwave_main", "explosion01_smoke_main" })
                yield return (Path.Combine("content", "textures", "particles", name + ".dds"), effects);
    }
}
