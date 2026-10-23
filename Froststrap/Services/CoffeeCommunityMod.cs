// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
namespace Froststrap.Services;

internal static class CoffeeCommunityMod
{
    internal const string Id = "coffeestrap-espresso";
    internal static CommunityMod Create() => new()
    {
        Id = Id, Name = "CoffeeStrap Espresso", Author = "EDest100",
        Description = "Espresso, caramel and cream across Roblox's local icons, menus, cursors, default sky and particles. Custom game assets keep their own appearance.",
        DownloadUrl = "https://github.com/EDest100/CoffeeStrap/releases/download/v0.10.0/CoffeeStrap-Espresso.zip",
        ThumbnailUrl = "avares://CoffeeStrap/Resources/CommunityMods/EspressoPreview.png",
        ModType = ModType.ColorMod
    };

    internal static List<CommunityMod> Merge(IEnumerable<CommunityMod> remote, CommunityMod coffee) =>
        new[] { coffee }.Concat(remote.Where(mod => !string.Equals(mod.Id, Id, StringComparison.OrdinalIgnoreCase))).ToList();
}
