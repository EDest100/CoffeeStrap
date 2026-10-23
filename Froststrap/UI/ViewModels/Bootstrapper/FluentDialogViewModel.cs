// SPDX-FileCopyrightText: 2026 Froststrap
//
// SPDX-License-Identifier: MPL-2.0

using Avalonia.Controls;
using Avalonia.Media;
using Froststrap.RobloxInterfaces;
using Froststrap.UI.Utility;

namespace Froststrap.UI.ViewModels.Bootstrapper
{
    internal class FluentDialogViewModel : BootstrapperDialogViewModel
    {
        public List<WindowTransparencyLevel> WindowBackdropType { get; set; }
        public IBrush BackgroundColourBrush { get; set; } = Brushes.Transparent;
        public string VersionText { get; set; }
        public string ChannelText { get; set; }

        public FluentDialogViewModel(IBootstrapperDialog dialog, bool aero, string version) : base(dialog)
        {
            WindowBackdropType = aero
                ? [WindowTransparencyLevel.AcrylicBlur,
                   WindowTransparencyLevel.Blur,
                   WindowTransparencyLevel.None]
                : [WindowTransparencyLevel.None];

            var isLight = App.Settings.Prop.Theme.GetFinal() == Theme.Light;

            var background = ThemePalette.GetColor("SolidBackgroundFillColorBase",
                isLight ? Color.Parse("#FAF6F0") : Color.Parse("#211C18"));
            BackgroundColourBrush = new SolidColorBrush(aero
                ? Color.FromArgb(220, background.R, background.G, background.B)
                : background);

            VersionText = $"Version: V{ExtractMajorVersion(version)}";
            ChannelText = $"Channel: {Deployment.Channel}";

            Deployment.ChannelChanged += (_, newChannel) =>
            {
                ChannelText = $"Channel: {newChannel}";
                OnPropertyChanged(nameof(ChannelText));
            };
        }

        private static string ExtractMajorVersion(string versionStr)
        {
            string[] parts = versionStr.Split('.');
            return (parts.Length >= 2) ? parts[1] : "???";
        }
    }
}
