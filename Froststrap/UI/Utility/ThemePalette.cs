// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0

using Avalonia;
using Avalonia.Media;

namespace Froststrap.UI.Utility;

/// <summary>Shares the active semantic palette with launcher surfaces drawn in code.</summary>
internal static class ThemePalette
{
    public static Color GetColor(string resourceKey, Color fallback)
    {
        if (Application.Current?.Resources.TryGetResource(resourceKey, null, out var value) == true)
        {
            return value switch
            {
                Color color => color,
                ISolidColorBrush brush => brush.Color,
                _ => fallback
            };
        }

        return fallback;
    }
}
