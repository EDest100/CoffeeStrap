// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Froststrap.Services;

namespace Froststrap.UI.ViewModels.Settings;

internal sealed partial class PlatformTweaksViewModel
{
    private readonly Func<IReadOnlyList<RunningPlayerWindow>> _runningPlayers;
    private readonly Func<RunningPlayerWindow, bool> _currentWindow;
    private readonly CancellationTokenSource _liveLifetime = new();
    private RunningPlayerWindow[] _livePlayers = [];
    private RunningPlayerWindow? _selectedLivePlayer, _sizedPlayer;
    private PlayerWindowSizing? _liveSizing;
    private bool _liveBusy, _liveDisposed;
    private string _liveWindowStatus = "Refresh players to choose a running Roblox window. No game restart is needed.";
    public IReadOnlyList<RunningPlayerWindow> LivePlayers => _livePlayers;
    public bool LiveHasRestore => _liveSizing != null;
    public bool CanChooseLivePlayer => !_liveBusy && !LiveHasRestore && !_liveDisposed;
    public string LiveWindowStatus => _liveWindowStatus;
    public RunningPlayerWindow? SelectedLivePlayer
    {
        get => _selectedLivePlayer;
        set
        {
            if (!CanChooseLivePlayer || value != null && !_livePlayers.Contains(value)) return;
            _selectedLivePlayer = value; OnPropertyChanged(); NotifyLiveCommands();
        }
    }
    private bool CanRefreshLive() => IsWindows && !_liveBusy && !LiveHasRestore && !_liveDisposed;
    private bool CanApplyLive() => IsWindows && !_liveBusy && !_liveDisposed && _selectedLivePlayer != null && WindowSize != 0;
    private bool CanRestoreLive() => !_liveBusy && !_liveDisposed && LiveHasRestore;
    private void NotifyLiveCommands()
    {
        RefreshLivePlayersCommand.NotifyCanExecuteChanged(); ApplyLiveWindowSizeCommand.NotifyCanExecuteChanged();
        RestoreLiveWindowSizeCommand.NotifyCanExecuteChanged(); KeepLiveWindowSizeCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanChooseLivePlayer)); OnPropertyChanged(nameof(LiveHasRestore));
    }
    private void LiveStatus(string message)
    {
        if (_liveDisposed) return;
        _liveWindowStatus = message; OnPropertyChanged(nameof(LiveWindowStatus));
    }
    [RelayCommand(CanExecute = nameof(CanRefreshLive))]
    private void RefreshLivePlayers()
    {
        if (!CanRefreshLive()) return;
        try
        {
            var selected = _selectedLivePlayer;
            _livePlayers = _runningPlayers().Take(32).ToArray();
            _selectedLivePlayer = _livePlayers.FirstOrDefault(window => selected != null && window.ProcessId == selected.ProcessId && window.StartedUtcTicks == selected.StartedUtcTicks && window.Handle == selected.Handle)
                ?? (_livePlayers.Length == 1 ? _livePlayers[0] : null);
            LiveStatus(_livePlayers.Length == 0 ? "No accessible running player window was found. Start Roblox, then refresh."
                : _livePlayers.Length == 1 ? "Player found. Choose a size above, then apply it now." : "Choose the player to resize. Other game windows are left alone.");
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        { App.Logger.Warn(error, "Running player lookup failed."); _livePlayers = []; _selectedLivePlayer = null; LiveStatus("Could not list players. Check that Roblox is running as your Windows user, then refresh."); }
        OnPropertyChanged(nameof(LivePlayers)); OnPropertyChanged(nameof(SelectedLivePlayer)); NotifyLiveCommands();
    }
    private static async Task ConfirmAsync(Func<bool> confirmed, CancellationToken token)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (confirmed()) return;
            await Task.Delay(50, token);
        }
        throw new IOException("Windows did not confirm the requested geometry. The game may have changed it.");
    }
    [RelayCommand(CanExecute = nameof(CanApplyLive))]
    private async Task ApplyLiveWindowSize()
    {
        if (!CanApplyLive()) return;
        var token = _liveLifetime.Token;
        _liveBusy = true; NotifyLiveCommands();
        try
        {
            var target = _selectedLivePlayer!;
            if (!_currentWindow(target)) throw new IOException("That player window has closed or changed. Keep any current size and refresh players.");
            token.ThrowIfCancellationRequested();
            var (width, height) = PlayerWindowSizing.Size(WindowSize);
            if (_liveSizing != null && !_liveSizing.Confirmed) { _liveSizing.KeepSize(); _liveSizing.Dispose(); _liveSizing = null; }
            if (_liveSizing == null) { _sizedPlayer = target; _liveSizing = new(target.Handle, (uint)target.ProcessId, width, height); }
            else _liveSizing.ApplySize(width, height);
            var sizing = _liveSizing;
            await ConfirmAsync(() => sizing.Confirmed, token);
            var actual = sizing.ClientSize;
            LiveStatus($"Applied to PID {target.ProcessId}: {actual.Width} × {actual.Height} client pixels. Restore keeps your original size across further preset changes.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or ArgumentException)
        { App.Logger.Warn(error, "Live player sizing failed."); LiveStatus("Size was not confirmed: " + error.Message); }
        finally { _liveBusy = false; if (!_liveDisposed) NotifyLiveCommands(); }
    }
    [RelayCommand(CanExecute = nameof(CanRestoreLive))]
    private async Task RestoreLiveWindowSize()
    {
        if (!CanRestoreLive()) return;
        var token = _liveLifetime.Token;
        _liveBusy = true; NotifyLiveCommands();
        try
        {
            var sizing = _liveSizing!;
            if (_sizedPlayer == null || !_currentWindow(_sizedPlayer))
            { ClearLiveRestore(); LiveStatus("The original player window is no longer available. No other window was changed."); return; }
            if (sizing.OriginalRestored) { ClearLiveRestore(); LiveStatus("The original window size is already in place."); return; }
            if (!sizing.Restore()) { ClearLiveRestore(); LiveStatus("Kept your later resize, move or fullscreen change. No window was forced back."); return; }
            await ConfirmAsync(() => sizing.OriginalRestored, token);
            ClearLiveRestore(); LiveStatus("Restored the original player size and position.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or ArgumentException)
        { App.Logger.Warn(error, "Live player size restore failed."); LiveStatus("Restore was not confirmed. You can retry or keep the current size. " + error.Message); }
        finally { _liveBusy = false; if (!_liveDisposed) NotifyLiveCommands(); }
    }
    private void ClearLiveRestore() { _liveSizing?.KeepSize(); _liveSizing?.Dispose(); _liveSizing = null; _sizedPlayer = null; }
    [RelayCommand(CanExecute = nameof(CanRestoreLive))]
    private void KeepLiveWindowSize()
    {
        if (!CanRestoreLive()) return;
        ClearLiveRestore(); LiveStatus("Current size kept. The previous-size restore has been cleared."); NotifyLiveCommands();
    }
    public void Dispose()
    {
        if (_liveDisposed) return;
        _liveDisposed = true; _liveLifetime.Cancel(); _liveSizing?.KeepSize(); _liveSizing?.Dispose(); _liveSizing = null; _sizedPlayer = null; _liveLifetime.Dispose();
    }
}
