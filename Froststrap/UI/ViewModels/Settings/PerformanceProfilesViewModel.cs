// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using CommunityToolkit.Mvvm.Input;
using Froststrap.Services;

namespace Froststrap.UI.ViewModels.Settings;

internal sealed record PerformanceProfile(string Name, string Description, int Effects, int Textures, int Samples, int MeshDistance)
{ public override string ToString() => Name; }
internal sealed record ProfileValueChange(string Label, string Current, string Selected)
{
    public bool IsChanged => !string.Equals(Current, Selected, StringComparison.OrdinalIgnoreCase);
    public string CurrentDisplay => Display(Current);
    public string SelectedDisplay => Display(Selected);
    private static string Display(string value) => bool.TryParse(value, out bool enabled) ? enabled ? "On" : "Off" : value;
}

internal sealed class PerformanceProfilesViewModel : NotifyPropertyChangedViewModel
{
    private sealed record Snapshot(Dictionary<string, string?> Flags, bool Distance, string Slider, string MaxQuality);
    private static string SnapshotPath => Path.Combine(Paths.Base, "PerformanceProfileRollback.json");
    private static readonly string[] MeshKeys = ["DFIntCSGLevelOfDetailSwitchingDistance", "DFIntCSGLevelOfDetailSwitchingDistanceL12", "DFIntCSGLevelOfDetailSwitchingDistanceL23", "DFIntCSGLevelOfDetailSwitchingDistanceL34"];
    private static readonly string[] Keys = ["DFIntDebugFRMQualityLevelOverride", "DFFlagTextureQualityOverrideEnabled", "DFIntTextureQualityOverride", "FIntDebugForceMSAASamples", "FIntFRMMinGrassDistance", "FIntFRMMaxGrassDistance", "FIntGrassMovementReducedMotionFactor", .. MeshKeys];
    private static readonly string[] Labels = ["Effects quality (1–21)", "Texture override enabled", "Texture quality (0–3)", "MSAA samples", "Grass minimum distance", "Grass maximum distance", "Grass motion factor", "Base mesh distance", "Mesh level 1 → 2", "Mesh level 2 → 3", "Mesh level 3 → 4"];
    public static IReadOnlyList<PerformanceProfile> Profiles { get; } = [
        new("Low detail, long distance", "Effects 1 · textures 1 · no MSAA · reduced mesh detail · no grass. Keeps the saved Roblox slider at 10.", 1, 1, 1, 500),
        new("Texture saver", "Effects 1 · textures 0 · no MSAA · lowest mesh detail · no grass. Requests lower texture detail; Roblox controls allocation and unloading.", 1, 0, 1, 0),
        new("Balanced long distance", "Effects 5 · textures 2 · 2× MSAA · moderate mesh detail · no grass. Trades some detail for a lighter rendering workload.", 5, 2, 2, 1000),
        new("Sharp long distance", "Effects 9 · textures 3 · 4× MSAA · higher mesh detail · no grass. Higher image quality can cost more GPU time.", 9, 3, 4, 2000)];
    private PerformanceProfile _selected = Profiles[0];
    private string _status = "Preview the tradeoff, apply, then Save. Profiles use supported flags on all GPU vendors; gains depend on the game and hardware.";
    public PerformanceProfile Selected
    {
        get => _selected;
        set { if (Profiles.Contains(value) && SetProperty(ref _selected, value)) { OnPropertyChanged(nameof(Description)); RefreshChanges(); } }
    }
    private bool _showComparison;
    public bool ShowComparison { get => _showComparison; set => SetProperty(ref _showComparison, value); }
    public IReadOnlyList<ProfileValueChange> Changes { get; private set; } = [];
    public string ComparisonSummary => $"{Changes.Count(change => change.IsChanged)} of {Changes.Count} settings would change. Current values include unsaved edits.";
    public string Description => Selected.Description;
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public event Action? Changed;
    public PerformanceProfilesViewModel()
    {
        ApplyCommand = new(Apply);
        RestoreCommand = new(Restore, () => File.Exists(SnapshotPath));
        RefreshChanges();
    }
    private Dictionary<string, string> ProposedFlags()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        void Set(string key, int value) => values[key] = value.ToString(CultureInfo.InvariantCulture);
        Set("DFIntDebugFRMQualityLevelOverride", Selected.Effects); Set("DFIntTextureQualityOverride", Selected.Textures);
        values["DFFlagTextureQualityOverrideEnabled"] = "True"; Set("FIntDebugForceMSAASamples", Selected.Samples);
        foreach (string key in MeshKeys) Set(key, Selected.MeshDistance);
        foreach (string key in new[] { "FIntFRMMinGrassDistance", "FIntFRMMaxGrassDistance", "FIntGrassMovementReducedMotionFactor" }) Set(key, 0);
        return values;
    }
    internal void RefreshChanges()
    {
        var proposed = ProposedFlags();
        Changes = [.. Keys.Select((key, index) => new ProfileValueChange(Labels[index], App.FastFlags.GetValue(key) ?? "Roblox default", proposed[key])),
            new("Maximum distance preference", App.Settings.Prop.LockMaximumRenderDistance.ToString(), "True"),
            new("Saved graphics slider", App.GlobalSettings.GetPreset("Rendering.SavedQualityLevel") ?? "Unavailable", "10"),
            new("Maximum quality effects", App.GlobalSettings.GetPreset("Rendering.MaxQualityEnabled") ?? "Unavailable", "False")];
        OnPropertyChanged(nameof(Changes)); OnPropertyChanged(nameof(ComparisonSummary));
    }
    private void Apply()
    {
        if (!App.GlobalSettings.Loaded) { Status = "Roblox saved settings are not loaded yet."; return; }
        var snapshot = new Snapshot(Keys.ToDictionary(key => key, App.FastFlags.GetValue), App.Settings.Prop.LockMaximumRenderDistance,
            App.GlobalSettings.GetPreset("Rendering.SavedQualityLevel") ?? "2", App.GlobalSettings.GetPreset("Rendering.MaxQualityEnabled") ?? "False");
        string temporary = SnapshotPath + "." + Guid.NewGuid().ToString("N");
        bool distanceSaved = false;
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot));
            App.Settings.Prop.LockMaximumRenderDistance = true;
            if (!App.Settings.TrySaveSetting(nameof(App.Settings.Prop.LockMaximumRenderDistance)))
            {
                App.Settings.Prop.LockMaximumRenderDistance = snapshot.Distance;
                Status = "Could not save the maximum distance preference. Profile was not applied; the previous Undo backup is unchanged. Try again after checking the settings file.";
                return;
            }
            distanceSaved = true;
            File.Move(temporary, SnapshotPath, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            App.Logger.Warn(error, "Could not preserve the performance-profile settings.");
            App.Settings.Prop.LockMaximumRenderDistance = snapshot.Distance;
            bool reverted = !distanceSaved || App.Settings.TrySaveSetting(nameof(App.Settings.Prop.LockMaximumRenderDistance));
            Status = "Could not save the previous settings; profile was not applied." +
                (reverted ? " The previous Undo backup is unchanged. Try again after checking the backup file." : " The distance preference could not be reverted on disk. Save settings to retry; the previous Undo backup is unchanged.");
            return;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { App.Logger.Warn(error, "Could not remove the temporary profile backup."); }
        }
        App.GlobalSettings.ApplyDrawDistancePreference(); App.GlobalSettings.SetPreset("Rendering.MaxQualityEnabled", false);
        foreach (var pair in ProposedFlags()) App.FastFlags.SetValue(pair.Key, pair.Value);
        Status = Selected.Name + " selected. Save, then relaunch Roblox. Restore previous profile can undo this change across app restarts.";
        RefreshChanges(); RestoreCommand.NotifyCanExecuteChanged(); Changed?.Invoke();
    }
    private void Restore()
    {
        if (!App.GlobalSettings.Loaded) { Status = "Roblox saved settings are not loaded yet."; return; }
        try
        {
            if (new FileInfo(SnapshotPath).Length > 65536) throw new InvalidDataException("Profile backup is too large.");
            var snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(SnapshotPath)) ?? throw new InvalidDataException("Profile backup is empty.");
            if (snapshot.Flags == null || snapshot.Flags.Count != Keys.Length || Keys.Any(key => !snapshot.Flags.ContainsKey(key)) ||
                snapshot.Flags.Any(pair => pair.Value != null && !CuratedFlagRegistry.TryNormalize(pair.Key, pair.Value, out _)) ||
                !int.TryParse(snapshot.Slider, out int slider) || slider is < 0 or > 10 || !bool.TryParse(snapshot.MaxQuality, out _))
                throw new InvalidDataException("Profile backup contains unsupported settings.");
            File.Move(SnapshotPath, SnapshotPath + ".restored", true);
            bool previousDistance = App.Settings.Prop.LockMaximumRenderDistance;
            App.Settings.Prop.LockMaximumRenderDistance = snapshot.Distance;
            if (!App.Settings.TrySaveSetting(nameof(App.Settings.Prop.LockMaximumRenderDistance)))
            {
                App.Settings.Prop.LockMaximumRenderDistance = previousDistance;
                Status = "Could not save the distance preference. Rendering settings were not restored.";
                try
                {
                    File.Move(SnapshotPath + ".restored", SnapshotPath, true);
                    Status += " The Undo backup is still available; try again after checking the settings file.";
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    App.Logger.Warn(error, "Could not reactivate the performance-profile backup.");
                    Status += " The backup is preserved at " + SnapshotPath + ".restored. Close CoffeeStrap and move it back to " + SnapshotPath + " to retry.";
                }
                return;
            }
            foreach (var pair in snapshot.Flags) App.FastFlags.SetValue(pair.Key, pair.Value);
            App.GlobalSettings.SetPreset("Rendering.SavedQualityLevel", snapshot.Slider); App.GlobalSettings.SetPreset("Rendering.MaxQualityEnabled", snapshot.MaxQuality);
            App.GlobalSettings.ApplyDrawDistancePreference();
            Status = "Previous rendering settings restored. Save to keep them."; RefreshChanges(); Changed?.Invoke();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { App.Logger.Warn(error, "Performance-profile backup could not be restored."); Status = "Could not restore the profile backup: " + error.Message; }
        finally { RestoreCommand.NotifyCanExecuteChanged(); }
    }
}
