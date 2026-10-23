// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using CommunityToolkit.Mvvm.Input;

namespace Froststrap.UI.ViewModels.Settings;

internal sealed class FlagNumberSetting(string title, string key, int maximum, int initial) : NotifyPropertyChangedViewModel
{
    public string Title { get; } = title;
    public string Key { get; } = key;
    public int Maximum { get; } = maximum;
    public event Action? Changed;
    public bool Enabled
    {
        get => App.FastFlags.GetValue(Key) != null;
        set { App.FastFlags.SetValue(Key, value ? Value.ToString(CultureInfo.InvariantCulture) : null); OnPropertyChanged(); Changed?.Invoke(); }
    }
    public int Value
    {
        get => int.TryParse(App.FastFlags.GetValue(Key), out int value) ? value : initial;
        set { App.FastFlags.SetValue(Key, Math.Clamp(value, 0, Maximum).ToString(CultureInfo.InvariantCulture)); OnPropertyChanged(); OnPropertyChanged(nameof(Enabled)); Changed?.Invoke(); }
    }
    public void Refresh() { OnPropertyChanged(nameof(Value)); OnPropertyChanged(nameof(Enabled)); }
}

internal sealed class GraphicsCustomizationViewModel : NotifyPropertyChangedViewModel
{
    public event Action? Changed;
    private static readonly string[] MeshKeys = ["DFIntCSGLevelOfDetailSwitchingDistance", "DFIntCSGLevelOfDetailSwitchingDistanceL12", "DFIntCSGLevelOfDetailSwitchingDistanceL23", "DFIntCSGLevelOfDetailSwitchingDistanceL34"];
    private static readonly int[] MeshDistances = [2000, 1500, 1000, 500];
    public IReadOnlyList<FlagNumberSetting> MeshThresholds { get; } = MeshKeys.Select((key, i) => new FlagNumberSetting(i == 0 ? "Base LOD distance" : $"LOD transition {i}", key, 10000, MeshDistances[i])).ToArray();
    public IReadOnlyList<FlagNumberSetting> GrassSettings { get; } = [
        new("Grass minimum distance", "FIntFRMMinGrassDistance", 10000, 100),
        new("Grass maximum distance", "FIntFRMMaxGrassDistance", 10000, 300),
        new("Grass motion factor", "FIntGrassMovementReducedMotionFactor", 100, 1)];
    public bool MeshDetailEnabled
    {
        get => MeshKeys.Any(key => App.FastFlags.GetValue(key) != null);
        set
        {
            if (value) MeshDetail = 5;
            else foreach (string key in MeshKeys) App.FastFlags.SetValue(key, null);
            RefreshMesh();
            Changed?.Invoke();
        }
    }
    public int MeshDetail
    {
        get => int.TryParse(App.FastFlags.GetValue(MeshKeys[0]), out int value) ? Math.Clamp((int)Math.Round(value * 9.0 / 2000), 0, 9) : 5;
        set
        {
            int level = Math.Clamp(value, 0, 9);
            for (int i = 0; i < MeshKeys.Length; i++) App.FastFlags.SetValue(MeshKeys[i], (MeshDistances[i] * level / 9).ToString(CultureInfo.InvariantCulture));
            RefreshMesh();
            Changed?.Invoke();
        }
    }
    private void RefreshMesh()
    {
        OnPropertyChanged(nameof(MeshDetail)); OnPropertyChanged(nameof(MeshDetailEnabled));
        foreach (var setting in MeshThresholds) setting.Refresh();
    }
    public RelayCommand ResetVisualsCommand { get; }
    public void Refresh()
    {
        RefreshMesh(); foreach (var setting in GrassSettings) setting.Refresh();
    }
    public GraphicsCustomizationViewModel()
    {
        foreach (var setting in MeshThresholds.Concat(GrassSettings)) setting.Changed += () => { Refresh(); Changed?.Invoke(); };
        ResetVisualsCommand = new(() =>
        {
            foreach (string key in Services.CuratedFlagRegistry.Allowed) App.FastFlags.SetValue(key, null);
            App.GlobalSettings.SetPreset("Rendering.VignetteEnabled", false);
            App.GlobalSettings.SetPreset("Rendering.VignetteEnableOption", false);
            App.GlobalSettings.SetPreset("Rendering.MaxQualityEnabled", false);
            RefreshMesh();
            foreach (var setting in GrassSettings) setting.Refresh();
        });
    }
}
