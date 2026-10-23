// SPDX-FileCopyrightText: 2026 Froststrap; 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
namespace Froststrap.UI.ViewModels.Onboarding;

internal sealed record FaqItem(string Question, string Answer);

internal class Page6ViewModel
{
    public IReadOnlyList<FaqItem> FaqItems { get; } =
    [
        new("When do Game Tweaks apply?", "Overrides apply on the next Roblox launch. Choose **Roblox default** to remove an override. Roblox may change which settings it supports."),
        new("Does lower texture quality unload textures?", "No. It requests a texture-quality level; Roblox controls texture loading. Lower quality can reduce detail, but an FPS improvement is not guaranteed."),
        new("Can I change the frame rate while playing?", "Use Roblox's in-game **Settings** for live changes. CoffeeStrap edits the saved setting and does not provide a live FPS switcher."),
        new("How do Auto and Reconnect work?", "**Auto** uses Roblox's normal matchmaking; it does not guarantee the closest server. **Reconnect** returns to your last recorded place, with no promise of the same server instance."),
        new("How do I pin a game?", "Use the hollow star on a Quick Play card. Pinned games appear on Home; click the star again to remove one.")
    ];
}
