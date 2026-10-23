// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
namespace Froststrap.Services;

internal static class RetiredPresets
{
    public static void RemoveAvatarBackground()
    {
        // ModPresetTask removes a file only when its hash matches this original
        // bundled preset. User-provided place files remain untouched.
        var preset = new Models.SettingTasks.ModPresetTask("OldAvatarBackground", @"ExtraContent\places\Mobile.rbxl", "OldAvatarBackground.rbxl") { NewState = false };
        preset.Execute();
        App.PendingSettingTasks.Remove(preset.Name);
    }
}
