// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Froststrap.Services;

namespace Froststrap.UI.ViewModels.Settings;

internal sealed partial class PlatformTweaksViewModel
{
    private readonly Func<IReadOnlyList<RunningPlayerWindow>> _runningPlayers;
    private readonly Func<RunningPlayerWindow, bool> _currentWindow;
    private readonly CancellationTokenSource _upscaleLifetime = new();
    private RunningPlayerWindow[] _upscalePlayers = [];
    private RunningPlayerWindow? _upscalePlayer;
    private PlayerUpscaler? _upscaler;
    private bool _upscaleBusy, _upscaleDisposed;
    private string _upscaleStatus = "Start Roblox in windowed mode, refresh players, then choose Start upscaling.";
    private static readonly int[] UpscalePercents = [50, 67, 75];
    public static IReadOnlyList<string> UpscaleResolutions { get; } = ["50% · performance", "67% · balanced", "75% · quality"];
    public IReadOnlyList<RunningPlayerWindow> UpscalePlayers => _upscalePlayers;
    public string UpscaleStatus => _upscaleStatus;
    public bool UpscaleActive => _upscaler?.IsActive == true;
    public bool CanChooseUpscale => !_upscaleBusy && _upscaler == null && !_upscaleDisposed;
    public RunningPlayerWindow? UpscalePlayer
    {
        get => _upscalePlayer;
        set { if (CanChooseUpscale && (value == null || _upscalePlayers.Contains(value))) { _upscalePlayer = value; OnPropertyChanged(); NotifyUpscaleCommands(); } }
    }
    public int UpscaleResolutionIndex
    {
        get => Math.Max(0, Array.IndexOf(UpscalePercents, App.Settings.Prop.UpscaleResolutionPercent));
        set
        {
            if (!CanChooseUpscale || value < 0 || value >= UpscalePercents.Length) return;
            int previous = App.Settings.Prop.UpscaleResolutionPercent;
            App.Settings.Prop.UpscaleResolutionPercent = UpscalePercents[value];
            if (!App.Settings.TrySaveSetting(nameof(App.Settings.Prop.UpscaleResolutionPercent)))
            { App.Settings.Prop.UpscaleResolutionPercent = previous; UpscaleMessage("Could not save the render scale. Check settings access and retry."); }
            OnPropertyChanged();
        }
    }
    private void UpscaleMessage(string message) { if (_upscaleDisposed) return; _upscaleStatus = message; OnPropertyChanged(nameof(UpscaleStatus)); }
    private bool CanRefreshUpscale() => IsWindows && CanChooseUpscale;
    private bool CanStartUpscale() => CanRefreshUpscale() && _upscalePlayer != null;
    private bool CanStopUpscale() => !_upscaleDisposed && _upscaler != null;
    private void NotifyUpscaleCommands()
    {
        if (_upscaleDisposed) return;
        RefreshUpscalePlayersCommand.NotifyCanExecuteChanged(); StartUpscalerCommand.NotifyCanExecuteChanged(); StopUpscalerCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanChooseUpscale)); OnPropertyChanged(nameof(UpscaleActive));
    }
    [RelayCommand(CanExecute = nameof(CanRefreshUpscale))]
    private void RefreshUpscalePlayers()
    {
        try
        {
            var previous = _upscalePlayer;
            _upscalePlayers = _runningPlayers().Take(32).ToArray();
            _upscalePlayer = _upscalePlayers.FirstOrDefault(player => player == previous) ?? (_upscalePlayers.Length == 1 ? _upscalePlayers[0] : null);
            UpscaleMessage(_upscalePlayers.Length == 0 ? "No running Roblox player was found. Start a game, then refresh."
                : _upscalePlayers.Length == 1 ? "Player found. Start upscaling to use a lower render size across this monitor." : "Choose the player to upscale. Other windows stay unchanged.");
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        { App.Logger.Warn(error, "Upscaler player lookup failed."); _upscalePlayers = []; _upscalePlayer = null; UpscaleMessage("Could not list player windows. Check Windows permissions and refresh."); }
        OnPropertyChanged(nameof(UpscalePlayers)); OnPropertyChanged(nameof(UpscalePlayer)); NotifyUpscaleCommands();
    }
    [RelayCommand(CanExecute = nameof(CanStartUpscale))]
    private async Task StartUpscaler()
    {
        if (!CanStartUpscale()) return;
        var target = _upscalePlayer!; var token = _upscaleLifetime.Token;
        _upscaleBusy = true; NotifyUpscaleCommands();
        try
        {
            if (!_currentWindow(target)) throw new IOException("The selected player closed or changed. Refresh players and choose again.");
            UpscaleMessage("Preparing low-resolution fullscreen scaling…");
            var session = await PlayerUpscaler.StartAsync(target, UpscalePercents[UpscaleResolutionIndex], token);
            if (_upscaleDisposed || token.IsCancellationRequested) { session.Dispose(); return; }
            _upscaler = session; UpscaleMessage(session.Status); NotifyUpscaleCommands(); await ObserveUpscalerAsync(session);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or ArgumentException or NotSupportedException)
        { App.Logger.Warn(error, "Upscaler could not start."); UpscaleMessage("Upscaling unavailable: " + error.Message); }
        finally { _upscaleBusy = false; NotifyUpscaleCommands(); }
    }
    private async Task ObserveUpscalerAsync(PlayerUpscaler session)
    {
        await session.Completion.ConfigureAwait(false);
        Dispatcher.UIThread.Post(() =>
        {
            if (_upscaleDisposed || !ReferenceEquals(_upscaler, session)) return;
            _upscaler = null; UpscaleMessage(session.Status); session.Dispose(); NotifyUpscaleCommands();
        });
    }
    [RelayCommand(CanExecute = nameof(CanStopUpscale))]
    private void StopUpscaler() { _upscaler?.Stop(); UpscaleMessage("Stopping upscaling and restoring the player window…"); }
    public void Dispose()
    {
        if (_upscaleDisposed) return;
        _upscaleDisposed = true; _upscaleLifetime.Cancel(); _upscaler?.Dispose(); _upscaler = null; _upscaleLifetime.Dispose();
    }
}
