// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
#pragma warning disable CA1822
using System.ComponentModel;
using System.Security;
using CommunityToolkit.Mvvm.Input;
using Froststrap.Services;

namespace Froststrap.UI.ViewModels.Settings;

internal sealed partial class PlatformTweaksViewModel : NotifyPropertyChangedViewModel, IDisposable
{
    private static readonly Lazy<HardwareTuning.Snapshot> Hardware = new(HardwareTuning.Detect);
    private readonly Func<HardwareTuning.Snapshot> _detect;
    private readonly Func<string?> _player;
    private readonly Action<ProcessStartInfo> _launch;
    private HardwareTuning.Snapshot _hardware;
    private HardwareTuning.Panel? _selectedPanel;
    private string? _playerPath;
    private string _hardwareStatus = "";
    public PlatformTweaksViewModel() : this(Hardware.Value, HardwareTuning.Detect, HardwareTuning.PlayerExecutable,
        info => { using var process = Process.Start(info); }) { }
    internal PlatformTweaksViewModel(HardwareTuning.Snapshot initial, Func<HardwareTuning.Snapshot> detect, Func<string?> player, Action<ProcessStartInfo> launch,
        Func<IReadOnlyList<RunningPlayerWindow>>? runningPlayers = null, Func<RunningPlayerWindow, bool>? currentWindow = null)
    { _hardware = initial; _detect = detect; _player = player; _launch = launch; _runningPlayers = runningPlayers ?? RunningPlayerWindows.Find; _currentWindow = currentWindow ?? RunningPlayerWindows.IsCurrent; SetHardware(initial); }
    public string HardwareLabel => _hardware.Cpu + "\n" + _hardware.Gpu;
    public static bool IsWindows => OperatingSystem.IsWindows();
    public IReadOnlyList<HardwareTuning.Panel> DriverPanels => _hardware.Panels;
    public bool HasDriverPanel => DriverPanels.Count > 0;
    public HardwareTuning.Panel? SelectedPanel
    {
        get => _selectedPanel;
        set { if (value != null && !DriverPanels.Contains(value)) return; _selectedPanel = value; OnPropertyChanged(); }
    }
    public string PlayerPath => _playerPath ?? "Launch Roblox through CoffeeStrap once, then refresh to locate its executable.";
    public bool HasPlayerPath => _playerPath != null;
    public string HardwareStatus => _hardwareStatus;
    public string PanelAvailability => HasDriverPanel ? "Choose the app for the GPU Roblox uses. Settings availability depends on its driver."
        : "No supported desktop driver app was found in its standard location. Use Windows graphics preferences, or open your installed GPU app from Start. Store apps and custom install paths are not detected.";
    private void SetHardware(HardwareTuning.Snapshot hardware)
    {
        string? selected = _selectedPanel?.Path;
        _hardware = hardware;
        _selectedPanel = DriverPanels.FirstOrDefault(panel => panel.Path == selected) ?? (DriverPanels.Count > 0 ? DriverPanels[0] : null);
        _playerPath = _player();
        _hardwareStatus = hardware.Warning;
    }
    private void Status(string text) { _hardwareStatus = text; OnPropertyChanged(nameof(HardwareStatus)); }
    private void TryAction(Action action, string? success)
    {
        if (!IsWindows) { Status("Graphics shortcuts are available on Windows only."); return; }
        try { action(); if (success != null) Status(success); }
        catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or SecurityException)
        { App.Logger.Warn(error, "Graphics shortcut failed."); Status("Could not open this shortcut. Open Settings or your GPU app from Start, then refresh to retry. " + error.Message); }
    }
    [RelayCommand] private void RefreshHardware()
    {
        TryAction(() =>
        {
            SetHardware(_detect());
            foreach (string name in new[] { nameof(HardwareLabel), nameof(DriverPanels), nameof(HasDriverPanel), nameof(SelectedPanel), nameof(PlayerPath), nameof(HasPlayerPath), nameof(PanelAvailability) }) OnPropertyChanged(name);
            Status(_hardware.Warning.Length > 0 ? _hardware.Warning : "Hardware and Roblox path refreshed.");
        }, null);
    }
    private string _schedulingStatus = App.State.Prop.LastSchedulingResult;
    public string SchedulingStatus => _schedulingStatus;
    public bool AboveNormalCpuPriority
    {
        get => App.Settings.Prop.AboveNormalCpuPriority;
        set { App.Settings.Prop.AboveNormalCpuPriority = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.AboveNormalCpuPriority)); OnPropertyChanged(); }
    }
    public bool AboveNormalGpuPriority
    {
        get => App.Settings.Prop.AboveNormalGpuPriority;
        set { App.Settings.Prop.AboveNormalGpuPriority = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.AboveNormalGpuPriority)); OnPropertyChanged(); }
    }
    public bool PreventEcoQoS
    {
        get => App.Settings.Prop.PreventEcoQoS;
        set { App.Settings.Prop.PreventEcoQoS = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.PreventEcoQoS)); OnPropertyChanged(); }
    }
    public bool HonorTimerRequests
    {
        get => App.Settings.Prop.HonorTimerRequests;
        set { App.Settings.Prop.HonorTimerRequests = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.HonorTimerRequests)); OnPropertyChanged(); }
    }
    [RelayCommand] private void RefreshSchedulingStatus()
    {
        try
        {
            if (new FileInfo(App.State.FileLocation).Length > 2097152) throw new InvalidDataException("Session state is too large.");
            _schedulingStatus = JsonSerializer.Deserialize<Models.Persistable.State>(File.ReadAllText(App.State.FileLocation))?.LastSchedulingResult ?? "No session result is available.";
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
        { App.Logger.Warn(error, "Could not read the last scheduling result."); _schedulingStatus = "Could not read the last session result. Refresh again after the player starts."; }
        OnPropertyChanged(nameof(SchedulingStatus));
    }
    public bool ShowGameInTitle
    {
        get => App.Settings.Prop.ShowGameInTitle;
        set { App.Settings.Prop.ShowGameInTitle = value; App.Settings.SaveSetting(nameof(App.Settings.Prop.ShowGameInTitle)); OnPropertyChanged(); }
    }
    [RelayCommand] private void OpenWindowsGraphics() => TryAction(() => _launch(new("ms-settings:display-advancedgraphics") { UseShellExecute = true }), "Windows received the graphics preferences request. Choose a per-app preference for Roblox.");
    [RelayCommand] private void OpenDriverPanel()
    {
        if (_selectedPanel == null) { Status(PanelAvailability); return; }
        TryAction(() => _launch(new(_selectedPanel.Path) { UseShellExecute = true }), "Windows received the GPU app request. Select Roblox's program profile before changing settings.");
    }
    [RelayCommand] private void ShowPlayerFile()
    {
        TryAction(() =>
        {
            _playerPath = _player(); OnPropertyChanged(nameof(PlayerPath)); OnPropertyChanged(nameof(HasPlayerPath));
            if (_playerPath == null) throw new FileNotFoundException("The installed Roblox executable is unavailable. Launch Roblox through CoffeeStrap, then refresh.");
            _launch(new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "/select,\"" + _playerPath + "\"") { UseShellExecute = true });
        }, "Windows received the file selection request. Use this executable when adding a Roblox program profile; updates can change its folder.");
    }
}
