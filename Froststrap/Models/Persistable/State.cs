// SPDX-FileCopyrightText: 2026 Froststrap
//
// SPDX-License-Identifier: MPL-2.0

namespace Froststrap.Models.Persistable
{
    internal class State
    {
        public bool TestModeWarningShown { get; set; }

        public bool PromptWebView2Install { get; set; } = true;

        public string? LastPage { get; set; } = null!;

        public bool ForceReinstall { get; set; }

        //if we were still windows only i would of just done it in nsis installer
        public bool IsFirstLaunch { get; set; } = true;

        public WindowState SettingsWindow { get; set; } = new();

        public LaunchMode LastLaunchMode { get; set; } = LaunchMode.Player;
        public bool HasSeededJjsPin { get; set; }
        public long LastPlaceId { get; set; }
        public string? LastGameName { get; set; }
        public string LastSchedulingResult { get; set; } = "No scheduling request has been made. Options apply to the next CoffeeStrap-launched player.";
        public string LastBackgroundSaverResult { get; set; } = "Background resource saver is off. Enable it before your next CoffeeStrap launch.";
        public int AutoReconnectAttempts { get; set; }
        public DateTime AutoReconnectWindowUtc { get; set; }
        public List<QuickLaunchPin> QuickLaunchPins { get; set; } = [];

        public string? LastMigratedVersion { get; set; }

        public string? PendingUpdateVersion { get; set; }

        public string? PendingUpdateReleaseNotes { get; set; }

        public List<ModConfig> Mods { get; set; } = [];
    }
}
