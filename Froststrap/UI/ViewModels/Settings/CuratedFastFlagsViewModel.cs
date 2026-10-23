// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.Collections.ObjectModel;

namespace Froststrap.UI.ViewModels.Settings;

internal sealed record FlagChoice(string Label, IReadOnlyDictionary<string, string> Values)
{ public override string ToString() => Label; }

internal sealed class FlagSetting : NotifyPropertyChangedViewModel
{
    public string Title { get; }
    public string Description { get; }
    public IReadOnlyList<FlagChoice> Choices { get; }
    private readonly string[] _keys;
    private FlagChoice? _selected;
    public event Action? Changed;
    public FlagChoice? Selected
    {
        get => _selected;
        set
        {
            if (value == null || !Choices.Contains(value) || !SetProperty(ref _selected, value)) return;
            foreach (string key in _keys) App.FastFlags.SetValue(key, value.Values.TryGetValue(key, out string? flag) ? flag : null);
            Changed?.Invoke();
        }
    }
    public FlagSetting(string title, string description, string[] keys, params FlagChoice[] choices)
    {
        Title = title; Description = description; _keys = keys; Choices = choices;
        Refresh();
    }
    public void Refresh()
    {
        _selected = Choices.FirstOrDefault(c => _keys.All(key => string.Equals(App.FastFlags.GetValue(key), c.Values.GetValueOrDefault(key), StringComparison.OrdinalIgnoreCase)));
        OnPropertyChanged(nameof(Selected));
    }
}

internal sealed class CuratedFastFlagsViewModel : IDisposable
{
    public void Dispose() => Platform.Dispose();
    public CuratedFastFlagsViewModel() : this(new PlatformTweaksViewModel()) { }
    internal CuratedFastFlagsViewModel(PlatformTweaksViewModel platform)
    {
        Platform = platform;
        Performance.Changed += () => { foreach (var setting in Settings) setting.Refresh(); Graphics.Refresh(); NativeSettings.RefreshGraphics(); };
        foreach (var setting in Settings) setting.Changed += () => { Graphics.Refresh(); NativeSettings.RefreshGraphics(); Performance.RefreshChanges(); };
        NativeSettings.GraphicsChanged += () => { foreach (var setting in Settings) setting.Refresh(); Visuals.Refresh(); Performance.RefreshChanges(); };
        Graphics.Changed += () => { foreach (var setting in Settings) setting.Refresh(); Performance.RefreshChanges(); };
    }
    public CommunityToolkit.Mvvm.Input.RelayCommand ResetVisualsCommand => new(() =>
    {
        NativeSettings.LockMaximumRenderDistance = false;
        Graphics.ResetVisualsCommand.Execute(null);
        foreach (var setting in Settings) setting.Selected = setting.Choices[0];
        NativeSettings.RefreshGraphics();
        Visuals.Reset();
        Performance.RefreshChanges();
    });
    public GlobalSettingsViewModel NativeSettings { get; } = new();
    public GraphicsCustomizationViewModel Graphics { get; } = new();
    public PlatformTweaksViewModel Platform { get; }
    public VisualTweaksViewModel Visuals { get; } = new();
    public PerformanceProfilesViewModel Performance { get; } = new();
    public static bool NativeSettingsLoaded => App.GlobalSettings.Loaded;
    private static FlagChoice Default => new("Roblox default", new Dictionary<string, string>());
    private static FlagChoice Value(string label, params (string Key, string Value)[] flags) => new(label, flags.ToDictionary(x => x.Key, x => x.Value));
    public ObservableCollection<FlagSetting> Settings { get; } =
    [
        new("Anti-aliasing", "Multisample preference. Roblox may adjust support for your renderer.", ["FIntDebugForceMSAASamples"], Default,
            Value("Off", ("FIntDebugForceMSAASamples", "1")), Value("2×", ("FIntDebugForceMSAASamples", "2")), Value("4×", ("FIntDebugForceMSAASamples", "4"))),
        new("Texture quality", "Texture quality override for the next launch. Roblox default removes the override.", ["DFFlagTextureQualityOverrideEnabled", "DFIntTextureQualityOverride"], Default,
            Value("Level 0", ("DFFlagTextureQualityOverrideEnabled", "True"), ("DFIntTextureQualityOverride", "0")),
            Value("Level 1", ("DFFlagTextureQualityOverrideEnabled", "True"), ("DFIntTextureQualityOverride", "1")),
            Value("Level 2", ("DFFlagTextureQualityOverrideEnabled", "True"), ("DFIntTextureQualityOverride", "2")),
            Value("Level 3 (highest)", ("DFFlagTextureQualityOverrideEnabled", "True"), ("DFIntTextureQualityOverride", "3"))),
        new("Renderer preference", "Request a renderer; Roblox may fall back when it is unavailable.", ["FFlagDebugGraphicsPreferD3D11", "FFlagDebugGraphicsPreferVulkan", "FFlagDebugGraphicsPreferOpenGL"], Default,
            Value("Direct3D 11", ("FFlagDebugGraphicsPreferD3D11", "True")), Value("Vulkan", ("FFlagDebugGraphicsPreferVulkan", "True")), Value("OpenGL", ("FFlagDebugGraphicsPreferOpenGL", "True"))),
        new("Preserve render resolution", "Disable Roblox's DPI scaling of the render surface.", ["DFFlagDisableDPIScale"], Default, Value("On", ("DFFlagDisableDPIScale", "True"))),
        new("Alt+Enter fullscreen", "Save and relaunch, then press Alt+Enter. Native fullscreen support depends on renderer/driver and may affect popouts or cursor behavior.", ["FFlagHandleAltEnterFullscreenManually"], Default,
            Value("Native fullscreen", ("FFlagHandleAltEnterFullscreenManually", "False")), Value("Roblox-handled", ("FFlagHandleAltEnterFullscreenManually", "True"))),
        new("Gray sky", "Use Roblox's gray debug sky preference.", ["FFlagDebugSkyGray"], Default, Value("On", ("FFlagDebugSkyGray", "True"))),
        new("Voxel lighting updates", "Pause the voxel lighting system. This can leave lighting stale until reset and relaunched.", ["DFFlagDebugPauseVoxelizer"], Default, Value("Paused", ("DFFlagDebugPauseVoxelizer", "True"))),
        new("Grass", "Use the existing zero-distance grass preset.", ["FIntFRMMinGrassDistance", "FIntFRMMaxGrassDistance", "FIntGrassMovementReducedMotionFactor"], Default,
            Value("Hidden", ("FIntFRMMinGrassDistance", "0"), ("FIntFRMMaxGrassDistance", "0"), ("FIntGrassMovementReducedMotionFactor", "0")))
    ];
}
