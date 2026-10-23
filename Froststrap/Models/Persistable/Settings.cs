#pragma warning disable CA1822 // Instance accessors are required for existing serialized/bound contracts.
// SPDX-FileCopyrightText: 2026 Froststrap
//
// SPDX-License-Identifier: MPL-2.0

using Froststrap.Enums.AppStoragePresets;
using System.Collections.ObjectModel;

namespace Froststrap.Models.Persistable
{
    internal class Settings
    {

        // Integration Page
        [JsonIgnore] public bool EnableActivityTracking { get => false; set { } }
        [JsonIgnore] public bool ShowServerDetails { get => false; set { } }
        [JsonIgnore] public bool ShowServerUptime { get => false; set { } }
        [JsonIgnore] public bool AutoRejoin { get => false; set { } }
        [JsonIgnore] public bool ShowGameHistoryMenu { get => false; set { } }
        [JsonIgnore] public bool PlaytimeCounter { get => false; set { } }
        [JsonIgnore] public TrayDoubleClickAction DoubleClickAction { get => TrayDoubleClickAction.None; set { } }
        public bool UseDisableAppPatch { get; set; }
        public bool AutoChangeTitle { get; set; } = true;
        public bool AutoChangeTitleWithPlayerCount { get; set; } = true;
        public bool AutoChangeIcon { get; set; }
        [JsonIgnore] public bool ShowUsingFroststrapRPC { get => false; set { } }
        [JsonIgnore] public bool UseDiscordRichPresence { get => false; set { } }
        public bool HideRPCButtons { get; set; } = true;
        public bool EnableCustomStatusDisplay { get; set; } = true;
        public bool ShowAccountOnRichPresence { get; set; }
        [JsonIgnore] public bool StudioRPC { get => false; set { } }
        public bool StudioThumbnailChanging { get; set; }
        public bool StudioEditingInfo { get; set; }
        public bool StudioWorkspaceInfo { get; set; }
        public bool StudioShowTesting { get; set; }
        public bool StudioGameButton { get; set; }
        [JsonIgnore] public ObservableCollection<CustomIntegration> CustomIntegrations { get => []; set { } }

        // Bootstrapper Page
        public bool ConfirmLaunches { get; set; } = true;
        public bool AllowCookieAccess { get; set; }
        public bool AutoCloseCrashHandler { get; set; }
        [JsonIgnore] public CleanerOptions CleanerOptions { get => CleanerOptions.Never; set { } }
        [JsonIgnore] public List<string> CleanerDirectories { get => []; set { } }
        [JsonIgnore] public bool BackgroundUpdatesEnabled { get => false; set { } }
        public bool LaunchWithVirtualDisplay { get; set; }
        public string VirtualDisplayName { get; set; } = "";
        public int VirtualDisplayWidth { get; set; }
        public int VirtualDisplayHeight { get; set; }
        public double[] VirtualDisplayHzModes { get; set; } = [ 24.0, 25.0, 30.0, 60.0, 120.0, 144.0, 240.0 ];
        public uint VirtualDisplaySerial { get; set; }
        public bool SoftKeyEnabled { get; set; }
        public SoftKeyProfile SoftKeyProfile { get; set; } = SoftKeyProfile.WASD;
        [JsonIgnore] public bool EnableBetterMatchmaking { get => false; set { } }
        public string SelectedRegion { get; set; } = Strings.Common_Auto;
        public string PreferredRegion { get; set; } = "Auto";
        public bool EnableRoValra { get; set; }
        public bool AutoReconnect { get; set; }
        public bool ShowGameInTitle { get; set; } = true;
        public bool AboveNormalCpuPriority { get; set; }
        public bool AboveNormalGpuPriority { get; set; }
        public bool PreventEcoQoS { get; set; }
        public bool HonorTimerRequests { get; set; }
        public bool BackgroundResourceSaver { get; set; }
        public bool UpscaleEnabled { get; set; }
        public int UpscaleResolutionPercent { get; set; } = 50;
        public string DefaultSkyPalette { get; set; } = "Roblox default";
        public bool SmoothWater { get; set; }
        public bool SmoothPlastic { get; set; }
        public bool SmoothStuds { get; set; }
        public int CloudTextureResolution { get; set; }
        public int CloudVolumeResolution { get; set; }
        [JsonIgnore] public int PlayerWindowSize { get => 0; set { } }
        public int SkyTextureResolution { get; set; }
        public int WaterTextureResolution { get; set; }
        public int SurfaceTextureResolution { get; set; }
        public int EffectTextureResolution { get; set; }
        public bool LockMaximumRenderDistance { get; set; } = true;
        public bool ReplaceIndoorSky { get; set; } = true;
        public bool HideSunMoon { get; set; }
        public string FireStyle { get; set; } = "Roblox default";
        public string SmokeStyle { get; set; } = "Roblox default";
        public string SparklesStyle { get; set; } = "Roblox default";
        public string ForceFieldStyle { get; set; } = "Roblox default";
        public string ExplosionStyle { get; set; } = "Roblox default";
        public bool EnableRobloxBackgroundApp { get; set; }
        public RobloxTheme RobloxTheme { get; set; } = RobloxTheme.Default;

        // FastFlag Editor/Settings
        [JsonIgnore] public bool UseFastFlagManager { get => true; set { } }
        public Dictionary<string, List<string>> ProfilePlaceIds { get; set; } = [];

        // Appearance Page
        public BootstrapperStyle BootstrapperStyle { get; set; } = BootstrapperStyle.FluentAeroDialog;
        public string? SelectedCustomTheme { get; set; }
        public Dictionary<string, string> CommunityThemeFolders { get; set; } = [];
        public bool CycleEnabled { get; set; }
        public CycleFrequency CycleFrequency { get; set; } = CycleFrequency.EveryLaunch;
        public int CycleIntervalValue { get; set; } = 1;
        public List<string> CycleEnabledCustomThemes { get; set; } = [];
        public int CycleCurrentIndex { get; set; }
        public DateTime CycleLastCycleTime { get; set; } = DateTime.MinValue;
        public BootstrapperIcon BootstrapperIcon { get; set; } = BootstrapperIcon.IconCoffeeStrap;
        public WindowsBackdrops SelectedBackdrop { get; set; } = WindowsBackdrops.None;
        public string Locale { get; set; } = "nil";
        public List<GradientStops> CustomGradientStops { get; set; } =
        [
            new GradientStops { Offset = 0.0, Color = "#FF362A22" },
            new GradientStops { Offset = 0.5, Color = "#FF2B221C" },
            new GradientStops { Offset = 1.0, Color = "#FF211C18" }
        ];
        public double? GradientAngle { get; set; } = 45;
        public BackgroundMode BackgroundType { get; set; } = BackgroundMode.Gradient;
        public string? BackgroundImagePath { get; set; } = "";
        public BackgroundStretch BackgroundStretch { get; set; } = BackgroundStretch.Fill;
        public double BackgroundOpacity { get; set; } = 1.0;
        public string BootstrapperTitle { get; set; } = App.ProjectName;
        public string BootstrapperIconCustomLocation { get; set; } = "";
        public Theme Theme { get; set; } = Theme.Dark;
        public bool EnableWindowManipulation { get; set; }
        public RobloxIcon RobloxIcon { get; set; } = RobloxIcon.IconDefault;
        public string RobloxTitle { get; set; } = "Roblox";
        public string RobloxIconCustomLocation { get; set; } = "";

        // Deployment Page
        [JsonIgnore] public UpdateCheck UpdateChecks { get => UpdateCheck.Disabled; set { } }
        public bool DisableAnimations { get; set; }
        public bool UpdateRoblox { get; set; } = true;
        public bool AutomaticallyUpdateSober { get; set; } = true;
        public int MaxThreadDownload { get; set; } = 3;
        public string RobloxDomain { get; set; } = RobloxInterfaces.Deployment.DefaultRobloxDomain;
        public bool StaticDirectory { get; set; }
        public string PlayerChannel { get; set; } = RobloxInterfaces.Deployment.DefaultChannel;
        public string StudioChannel { get; set; } = RobloxInterfaces.Deployment.DefaultChannel;
        public ChannelChangeMode ChannelChangeMode { get; set; } = ChannelChangeMode.Prompt;
        public bool StudioVersionOverrideEnabled { get; set; }
        public string StudioVersionOverrideHash { get; set; } = string.Empty;
        public bool PlayerVersionOverrideEnabled { get; set; }
        public string PlayerVersionOverrideHash { get; set; } = string.Empty;

        // Linux Settings page
        public bool EnableWebView2 { get; set; } = true;
        public string? StudioVirtualDesktop { get; set; } = string.Empty;
        public string? StudioLauncher { get; set; } = string.Empty;
        public StudioRenderer StudioRenderer { get; set; } = StudioRenderer.DXVK;
        public bool StudioGameMode { get; set; }
        public bool StudioDebug { get; set; }
        public Dictionary<string, string> StudioEnvironmentVariables { get; set; } = [];

        // Misc Stuff
        public bool ForceLocalData { get; set; }
        public bool DebugDisableVersionPackageCleanup { get; set; }
    }
}
