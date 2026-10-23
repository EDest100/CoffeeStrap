// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
namespace Froststrap.Models;

internal sealed class QuickLaunchPin
{
    public long PlaceId { get; set; }
    public long UniverseId { get; set; }
    public string Name { get; set; } = "Roblox experience";
    public string ThumbnailUrl { get; set; } = "";
}
