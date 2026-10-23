// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using CommunityToolkit.Mvvm.Input;

namespace Froststrap.UI.ViewModels.Settings;

internal sealed partial class PlatformTweaksViewModel
{
    private string _backgroundSaverStatus = App.State.Prop.LastBackgroundSaverResult;
    public bool BackgroundResourceSaver
    {
        get => App.Settings.Prop.BackgroundResourceSaver;
        set
        {
            if (value == App.Settings.Prop.BackgroundResourceSaver) return;
            bool previous = App.Settings.Prop.BackgroundResourceSaver;
            App.Settings.Prop.BackgroundResourceSaver = value;
            if (!App.Settings.TrySaveSetting(nameof(App.Settings.Prop.BackgroundResourceSaver)))
            {
                App.Settings.Prop.BackgroundResourceSaver = previous;
                _backgroundSaverStatus = "Could not save the background resource saver preference. Check settings access and retry.";
            }
            else _backgroundSaverStatus = value ? "Enabled for the next launched player. Minimize all player windows to use the background policy."
                : "Disabled for future launches. A running player's existing session preference ends when its watcher stops.";
            OnPropertyChanged();
            OnPropertyChanged(nameof(BackgroundSaverStatus));
        }
    }
    public string BackgroundSaverStatus => _backgroundSaverStatus;
    [RelayCommand] private void RefreshBackgroundSaverStatus()
    {
        try
        {
            if (new FileInfo(App.State.FileLocation).Length > 2097152) throw new InvalidDataException("Session state is too large.");
            _backgroundSaverStatus = JsonSerializer.Deserialize<Models.Persistable.State>(File.ReadAllText(App.State.FileLocation))?.LastBackgroundSaverResult
                ?? "No background saver result is available.";
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            App.Logger.Warn(error, "Could not read the background resource saver result.");
            _backgroundSaverStatus = "Could not read the last result. Refresh after the player starts.";
        }
        OnPropertyChanged(nameof(BackgroundSaverStatus));
    }
}
