// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Avalonia;
using Avalonia.Controls;
namespace Froststrap.UI.Elements.Controls;

/// <summary>A master setting and its dependent cards share one visible parent surface.</summary>
internal sealed class SettingsGroup : ContentControl
{
    public static readonly StyledProperty<Control?> MasterProperty = AvaloniaProperty.Register<SettingsGroup, Control?>(nameof(Master));
    public Control? Master { get => GetValue(MasterProperty); set => SetValue(MasterProperty, value); }
}
