// SPDX-FileCopyrightText: 2026 Froststrap
//
// SPDX-License-Identifier: MPL-2.0

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Froststrap.UI.Elements.Bootstrapper.Base;
using Froststrap.UI.ViewModels.Bootstrapper;
using Froststrap.UI.Utility;

namespace Froststrap.UI.Elements.Bootstrapper
{
    internal partial class ModernDialog : AvaloniaDialogBase
    {
        private readonly ModernDialogViewModel _viewModel;

        public ModernDialog()
        {
            InitializeComponent();

            string version = Froststrap.Utility.Versioning.GetRobloxVersionStr(Bootstrapper?.IsStudioLaunch ?? false);
            _viewModel = new ModernDialogViewModel(this, version);
            ApplyModernStyles();
            DataContext = _viewModel;

            SetupDialog();

            var iconImage = App.Settings.Prop.BootstrapperIcon.GetIcon().GetImageSource();
            if (iconImage is Bitmap bitmap)
                Icon = new WindowIcon(bitmap);

        }

        private void ApplyModernStyles()
        {
            _viewModel.DialogBorder = new Thickness(1);
            _viewModel.Background = new SolidColorBrush(ThemePalette.GetColor("SolidBackgroundFillColorBase", Colors.Black));
            var foreground = new SolidColorBrush(ThemePalette.GetColor("TextFillColorPrimary", Colors.White));
            _viewModel.Foreground = foreground;
            _viewModel.IconColor = foreground;
            _viewModel.ProgressBarBackground = new SolidColorBrush(ThemePalette.GetColor("ControlFillColorTertiary", Colors.Gray));
        }

        #region UI Elements Overrides
        public override string Message
        {
            get => _viewModel.Message;
            set => RunOnUI(() =>
            {
                _viewModel.Message = value;
                _viewModel.OnPropertyChanged(nameof(_viewModel.Message));
            });
        }

        public override int ProgressMaximum
        {
            get => _viewModel.ProgressMaximum;
            set => RunOnUI(() =>
            {
                _viewModel.ProgressMaximum = value;
                _viewModel.OnPropertyChanged(nameof(_viewModel.ProgressMaximum));
            });
        }

        public override int ProgressValue
        {
            get => _viewModel.ProgressValue;
            set => RunOnUI(() =>
            {
                _viewModel.ProgressValue = value;
                _viewModel.OnPropertyChanged(nameof(_viewModel.ProgressValue));
            });
        }

        public override bool CancelEnabled
        {
            get => _viewModel.CancelEnabled;
            set => RunOnUI(() =>
            {
                _viewModel.CancelEnabled = value;
                _viewModel.OnPropertyChanged(nameof(_viewModel.CancelEnabled));
                _viewModel.OnPropertyChanged(nameof(_viewModel.CancelButtonVisible));
            });
        }

        public override bool ProgressIndeterminate
        {
            get => _viewModel.ProgressIndeterminate;
            set => RunOnUI(() =>
            {
                _viewModel.ProgressIndeterminate = value;
                _viewModel.OnPropertyChanged(nameof(_viewModel.ProgressIndeterminate));
            });
        }
        #endregion
    }
}
