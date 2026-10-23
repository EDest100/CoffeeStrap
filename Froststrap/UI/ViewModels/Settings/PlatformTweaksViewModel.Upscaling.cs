// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace Froststrap.UI.ViewModels.Settings;

internal sealed partial class PlatformTweaksViewModel
{
    private FileSystemWatcher? _upscaleStatusWatcher;
    private volatile bool _upscaleDisposed;
    private int _upscaleStatusPending;
    private string _upscaleStatus = App.State.Prop.LastUpscalerResult;
    private static readonly int[] UpscalePercents = [50, 67, 75];
    public static IReadOnlyList<string> UpscaleResolutions { get; } = ["50% · performance", "67% · balanced", "75% · quality"];
    public string UpscaleStatus => _upscaleStatus;
    public bool CanChooseUpscale => IsWindows && !_upscaleDisposed;
    public bool UpscaleEnabled
    {
        get => App.Settings.Prop.UpscaleEnabled;
        set
        {
            if (!CanChooseUpscale || value == UpscaleEnabled) return;
            bool previous = App.Settings.Prop.UpscaleEnabled;
            App.Settings.Prop.UpscaleEnabled = value;
            if (!App.Settings.TrySaveSetting(nameof(App.Settings.Prop.UpscaleEnabled)))
            { App.Settings.Prop.UpscaleEnabled = previous; UpscaleMessage("Could not save the upscaler preference. Check settings access and retry."); }
            else UpscaleMessage(value ? "Enabled. Automatically applies when your CoffeeStrap-launched Roblox player becomes active."
                : "Disabled. A running automatic session will stop and restore its player window.");
            OnPropertyChanged();
        }
    }
    public int UpscaleResolutionIndex
    {
        get => Math.Max(0, Array.IndexOf(UpscalePercents, App.Settings.Prop.UpscaleResolutionPercent));
        set
        {
            if (!CanChooseUpscale || value < 0 || value >= UpscalePercents.Length) return;
            int previous = App.Settings.Prop.UpscaleResolutionPercent;
            if (previous == UpscalePercents[value]) return;
            App.Settings.Prop.UpscaleResolutionPercent = UpscalePercents[value];
            if (!App.Settings.TrySaveSetting(nameof(App.Settings.Prop.UpscaleResolutionPercent)))
            { App.Settings.Prop.UpscaleResolutionPercent = previous; UpscaleMessage("Could not save the render scale. Check settings access and retry."); }
            else UpscaleMessage(UpscaleEnabled ? "Render scale saved. The active automatic session will restart with this size." : "Render scale saved. Enable the upscaler to apply it automatically.");
            OnPropertyChanged();
        }
    }
    private void UpscaleMessage(string message)
    { if (_upscaleDisposed) return; _upscaleStatus = message; OnPropertyChanged(nameof(UpscaleStatus)); }
    private void WatchUpscalerStatus()
    {
        if (!IsWindows || !Paths.Initialized) return;
        string? directory = Path.GetDirectoryName(App.State.FileLocation);
        if (directory == null || !Directory.Exists(directory)) return;
        try
        {
            _upscaleStatusWatcher = new(directory, Path.GetFileName(App.State.FileLocation)) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
            _upscaleStatusWatcher.Changed += UpscaleStatusChanged;
            _upscaleStatusWatcher.Created += UpscaleStatusChanged;
            _upscaleStatusWatcher.Renamed += UpscaleStatusChanged;
            _upscaleStatusWatcher.EnableRaisingEvents = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { App.Logger.Warn(error, "Automatic upscaler status notifications are unavailable."); }
    }
    private void UpscaleStatusChanged(object sender, FileSystemEventArgs change)
    {
        if (_upscaleDisposed || Interlocked.Exchange(ref _upscaleStatusPending, 1) != 0) return;
        Dispatcher.UIThread.Post(() => { Interlocked.Exchange(ref _upscaleStatusPending, 0); if (!_upscaleDisposed) RefreshUpscaleStatus(); });
    }
    [RelayCommand(CanExecute = nameof(CanChooseUpscale))]
    private void RefreshUpscaleStatus()
    {
        if (_upscaleDisposed) return;
        try
        {
            using var stream = new FileStream(App.State.FileLocation, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            long length = stream.Length;
            if (length > 2097152) throw new InvalidDataException("Session state is too large.");
            var bytes = new byte[(int)length]; stream.ReadExactly(bytes);
            using var state = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            string? result = state.RootElement.GetProperty(nameof(App.State.Prop.LastUpscalerResult)).GetString();
            UpscaleMessage(string.IsNullOrWhiteSpace(result) || result.Length > 2048 ? "No automatic upscaler result is available." : result);
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException)
        { App.Logger.Warn(error, "Could not read the automatic upscaler result."); UpscaleMessage("No current session result. Launch Roblox through CoffeeStrap to apply automatic upscaling."); }
    }
    public void Dispose()
    {
        if (_upscaleDisposed) return;
        _upscaleDisposed = true;
        _upscaleStatusWatcher?.Dispose(); _upscaleStatusWatcher = null;
        RefreshUpscaleStatusCommand.NotifyCanExecuteChanged(); OnPropertyChanged(nameof(CanChooseUpscale));
    }
}
