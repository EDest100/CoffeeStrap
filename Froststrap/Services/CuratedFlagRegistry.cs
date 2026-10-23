// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
namespace Froststrap.Services;

/// <summary>Published Roblox local allowlist. The UI exposes a smaller set with known preset semantics.</summary>
internal static class CuratedFlagRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public const string Source = "https://devforum.roblox.com/t/allowlist-for-local-client-configuration-via-fast-flags/3966569";
    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        "FFlagHandleAltEnterFullscreenManually", "DFFlagDebugPauseVoxelizer", "DFFlagDisableDPIScale",
        "DFFlagTextureQualityOverrideEnabled", "DFIntTextureQualityOverride", "DFIntDebugFRMQualityLevelOverride",
        "DFIntCSGLevelOfDetailSwitchingDistance", "DFIntCSGLevelOfDetailSwitchingDistanceL12",
        "DFIntCSGLevelOfDetailSwitchingDistanceL23", "DFIntCSGLevelOfDetailSwitchingDistanceL34",
        "FFlagDebugGraphicsPreferD3D11", "FFlagDebugGraphicsPreferVulkan", "FFlagDebugGraphicsPreferOpenGL",
        "FFlagDebugSkyGray", "FIntDebugForceMSAASamples", "FIntFRMMinGrassDistance", "FIntFRMMaxGrassDistance",
        "FIntGrassMovementReducedMotionFactor"
    };

    public static bool TryNormalize(string key, object? value, out string normalized)
    {
        normalized = "";
        if (!Allowed.Contains(key) || value == null) return false;
        string raw = value.ToString() ?? "";
        if (key.Contains("Flag", StringComparison.Ordinal) && bool.TryParse(raw, out bool flag))
        { normalized = flag ? "True" : "False"; return true; }
        if (!key.Contains("Int", StringComparison.Ordinal) || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)) return false;
        if (number < 0) return false;
        if (key == "FIntDebugForceMSAASamples" && number is not (1 or 2 or 4)) return false;
        if (key == "DFIntTextureQualityOverride" && number > 3) return false;
        if (key == "DFIntDebugFRMQualityLevelOverride" && number is < 1 or > 21) return false;
        normalized = number.ToString(CultureInfo.InvariantCulture); return true;
    }

    public static Dictionary<string, object> Filter(IReadOnlyDictionary<string, object> values, out Dictionary<string, object> held)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal); held = new(StringComparer.Ordinal);
        foreach (var pair in values)
            if (TryNormalize(pair.Key, pair.Value, out var normalized)) result[pair.Key] = normalized;
            else held[pair.Key] = pair.Value;
        return result;
    }

    public static void PreserveHeld(IReadOnlyDictionary<string, object> held)
    {
        if (held.Count == 0) return;
        string folder = Path.Combine(Paths.Base, "FlagBackups"); Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "unsupported-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(file, JsonSerializer.Serialize(held, JsonOptions));
    }

    public static void SanitizeFile(string path)
    {
        if (!File.Exists(path)) return;
        var source = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(path)) ?? [];
        var accepted = Filter(source, out var held);
        ApplySelectedOverrides(accepted);
        if (held.Count == 0 && accepted.Count == source.Count && accepted.All(pair => source.GetValueOrDefault(pair.Key)?.ToString() == pair.Value.ToString())) return;
        PreserveHeld(held);
        AtomicFile.WriteText(path, JsonSerializer.Serialize(accepted, JsonOptions));
    }

    internal static void ApplySelectedOverrides(Dictionary<string, object> accepted)
    {
        foreach (string key in new[] { "DFIntDebugFRMQualityLevelOverride", "DFFlagTextureQualityOverrideEnabled", "DFIntTextureQualityOverride", "DFFlagDebugPauseVoxelizer" })
        {
            if (TryNormalize(key, App.FastFlags.GetValue(key), out string selected)) accepted[key] = selected;
            else accepted.Remove(key);
        }
        string[] renderers = ["FFlagDebugGraphicsPreferD3D11", "FFlagDebugGraphicsPreferVulkan", "FFlagDebugGraphicsPreferOpenGL"];
        if (renderers.Any(key => string.Equals(App.FastFlags.GetValue(key), "True", StringComparison.OrdinalIgnoreCase)))
            foreach (string key in renderers)
            {
                if (TryNormalize(key, App.FastFlags.GetValue(key), out string selected)) accepted[key] = selected;
                else accepted.Remove(key);
            }
        const string fullscreen = "FFlagHandleAltEnterFullscreenManually";
        if (TryNormalize(fullscreen, App.FastFlags.GetValue(fullscreen), out string handling)) accepted[fullscreen] = handling;
        if (App.Settings.Prop.LockMaximumRenderDistance) accepted.TryAdd("DFIntDebugFRMQualityLevelOverride", "1");
    }
}
