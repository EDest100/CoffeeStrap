// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
namespace Froststrap.UI.ViewModels.Settings;

internal sealed class VisualTweaksViewModel : NotifyPropertyChangedViewModel
{
    internal void Reset()
    {
        SkyPalette = SkyPalettes[0]; SmoothWater = false; SmoothPlastic = false; SmoothStuds = false; HideSunMoon = false;
        FireStyle = "Roblox default"; SmokeStyle = "Roblox default"; SparklesStyle = "Roblox default"; ForceFieldStyle = "Roblox default";
        ExplosionStyle = "Roblox default";
        SkyTextureResolution = 0; WaterTextureResolution = 0; SurfaceTextureResolution = 0; EffectTextureResolution = 0;
        CloudTextureResolution = 0; CloudVolumeResolution = 0;
    }
    public static IReadOnlyList<string> SkyPalettes { get; } = ["Roblox default", "Warm dusk", "Deep blue", "Overcast"];
    public static IReadOnlyList<string> TextureResolutions => Services.TextureResolutionBudget.Choices;
    public int CloudVolumeResolution
    {
        get => App.Settings.Prop.CloudVolumeResolution is 1 or 2 ? App.Settings.Prop.CloudVolumeResolution : 0;
        set { if (value is >= 0 and <= 2) { App.Settings.Prop.CloudVolumeResolution = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.CloudVolumeResolution)); OnPropertyChanged(); } }
    }
    public int CloudTextureResolution
    {
        get => App.Settings.Prop.CloudTextureResolution is 1 or 2 ? App.Settings.Prop.CloudTextureResolution : 0;
        set { if (value is >= 0 and <= 2) { App.Settings.Prop.CloudTextureResolution = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.CloudTextureResolution)); OnPropertyChanged(); } }
    }
    public bool SmoothStuds
    {
        get => App.Settings.Prop.SmoothStuds;
        set { App.Settings.Prop.SmoothStuds = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.SmoothStuds)); OnPropertyChanged(); }
    }
    public int SkyTextureResolution
    {
        get => App.Settings.Prop.SkyTextureResolution is 1 or 2 ? App.Settings.Prop.SkyTextureResolution : 0;
        set { if (value is >= 0 and <= 2) { App.Settings.Prop.SkyTextureResolution = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.SkyTextureResolution)); OnPropertyChanged(); } }
    }
    public int WaterTextureResolution
    {
        get => App.Settings.Prop.WaterTextureResolution is 1 or 2 ? App.Settings.Prop.WaterTextureResolution : 0;
        set { if (value is >= 0 and <= 2) { App.Settings.Prop.WaterTextureResolution = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.WaterTextureResolution)); OnPropertyChanged(); } }
    }
    public int SurfaceTextureResolution
    {
        get => App.Settings.Prop.SurfaceTextureResolution is 1 or 2 ? App.Settings.Prop.SurfaceTextureResolution : 0;
        set { if (value is >= 0 and <= 2) { App.Settings.Prop.SurfaceTextureResolution = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.SurfaceTextureResolution)); OnPropertyChanged(); } }
    }
    public int EffectTextureResolution
    {
        get => App.Settings.Prop.EffectTextureResolution is 1 or 2 ? App.Settings.Prop.EffectTextureResolution : 0;
        set { if (value is >= 0 and <= 2) { App.Settings.Prop.EffectTextureResolution = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.EffectTextureResolution)); OnPropertyChanged(); } }
    }
    public string SkyPalette
    {
        get => App.Settings.Prop.DefaultSkyPalette;
        set { if (SkyPalettes.Contains(value)) { App.Settings.Prop.DefaultSkyPalette = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.DefaultSkyPalette)); OnPropertyChanged(); } }
    }
    public bool SmoothWater
    {
        get => App.Settings.Prop.SmoothWater;
        set { App.Settings.Prop.SmoothWater = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.SmoothWater)); OnPropertyChanged(); }
    }
    public bool SmoothPlastic
    {
        get => App.Settings.Prop.SmoothPlastic;
        set { App.Settings.Prop.SmoothPlastic = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.SmoothPlastic)); OnPropertyChanged(); }
    }
    public bool ReplaceIndoorSky
    {
        get => App.Settings.Prop.ReplaceIndoorSky;
        set { App.Settings.Prop.ReplaceIndoorSky = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.ReplaceIndoorSky)); OnPropertyChanged(); }
    }
    internal void Refresh() => OnPropertyChanged(nameof(SmoothPlastic));
    public static IReadOnlyList<string> FireStyles => Services.BuiltInVisualMods.FireStyles;
    public static IReadOnlyList<string> SmokeStyles => Services.BuiltInVisualMods.SmokeStyles;
    public static IReadOnlyList<string> SparklesStyles => Services.BuiltInVisualMods.SparklesStyles;
    public static IReadOnlyList<string> ForceFieldStyles => Services.BuiltInVisualMods.ForceFieldStyles;
    public static IReadOnlyList<string> ExplosionStyles => Services.BuiltInVisualMods.ExplosionStyles;
    public string ExplosionStyle
    {
        get => ExplosionStyles.Contains(App.Settings.Prop.ExplosionStyle) ? App.Settings.Prop.ExplosionStyle : ExplosionStyles[0];
        set { if (ExplosionStyles.Contains(value)) { App.Settings.Prop.ExplosionStyle = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.ExplosionStyle)); OnPropertyChanged(); } }
    }
    public string FireStyle
    {
        get => FireStyles.Contains(App.Settings.Prop.FireStyle) ? App.Settings.Prop.FireStyle : FireStyles[0];
        set { if (FireStyles.Contains(value)) { App.Settings.Prop.FireStyle = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.FireStyle)); OnPropertyChanged(); } }
    }
    public string SmokeStyle
    {
        get => SmokeStyles.Contains(App.Settings.Prop.SmokeStyle) ? App.Settings.Prop.SmokeStyle : SmokeStyles[0];
        set { if (SmokeStyles.Contains(value)) { App.Settings.Prop.SmokeStyle = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.SmokeStyle)); OnPropertyChanged(); } }
    }
    public string SparklesStyle
    {
        get => SparklesStyles.Contains(App.Settings.Prop.SparklesStyle) ? App.Settings.Prop.SparklesStyle : SparklesStyles[0];
        set { if (SparklesStyles.Contains(value)) { App.Settings.Prop.SparklesStyle = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.SparklesStyle)); OnPropertyChanged(); } }
    }
    public string ForceFieldStyle
    {
        get => ForceFieldStyles.Contains(App.Settings.Prop.ForceFieldStyle) ? App.Settings.Prop.ForceFieldStyle : ForceFieldStyles[0];
        set { if (ForceFieldStyles.Contains(value)) { App.Settings.Prop.ForceFieldStyle = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.ForceFieldStyle)); OnPropertyChanged(); } }
    }
    public bool HideSunMoon
    {
        get => App.Settings.Prop.HideSunMoon;
        set { App.Settings.Prop.HideSunMoon = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.HideSunMoon)); OnPropertyChanged(); }
    }
}
