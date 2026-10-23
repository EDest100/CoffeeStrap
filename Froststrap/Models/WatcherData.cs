// SPDX-FileCopyrightText: 2026 Froststrap
//
// SPDX-License-Identifier: MPL-2.0

namespace Froststrap.Models
{
    internal class WatcherData
    {
        public int ProcessId { get; set; }
        public long PlaceId { get; set; }
        public string? GameName { get; set; }

        public string? LogFile { get; set; }

        public List<int>? AutoclosePids { get; set; }

        public LaunchMode LaunchMode { get; set; } = LaunchMode.Player;

        public string? AccessCode { get; set; }
        public bool PublicJoin { get; set; }
    }
}
