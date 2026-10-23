// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
namespace Froststrap.UI.ViewModels.Settings;

internal sealed class AdvancedSettingsViewModel
{
    public ChannelViewModel Deployment { get; } = new();
    public AppearanceViewModel Appearance { get; } = new();
}
