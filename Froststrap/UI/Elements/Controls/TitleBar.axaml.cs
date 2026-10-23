// SPDX-FileCopyrightText: 2026 Froststrap
//
// SPDX-License-Identifier: MPL-2.0

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Metadata;
using LucideAvalonia.Enum;
using WindowState = Avalonia.Controls.WindowState;

namespace Froststrap.UI.Elements.Controls
{
    internal class TitleBar : TemplatedControl
    {
        public static readonly StyledProperty<string?> TitleProperty =
            AvaloniaProperty.Register<TitleBar, string?>(nameof(Title));
        public static readonly StyledProperty<object?> CenterContentProperty =
            AvaloniaProperty.Register<TitleBar, object?>(nameof(CenterContent));
        public object? CenterContent { get => GetValue(CenterContentProperty); set => SetValue(CenterContentProperty, value); }

        public static readonly StyledProperty<bool> ShowMinimizeProperty =
            AvaloniaProperty.Register<TitleBar, bool>(nameof(ShowMinimize), true);

        public static readonly StyledProperty<bool> ShowMaximizeProperty =
            AvaloniaProperty.Register<TitleBar, bool>(nameof(ShowMaximize), true);

        public static readonly StyledProperty<bool> ShowCloseProperty =
            AvaloniaProperty.Register<TitleBar, bool>(nameof(ShowClose), true);

        public static readonly StyledProperty<IImage?> IconProperty =
            AvaloniaProperty.Register<TitleBar, IImage?>(nameof(Icon), defaultValue: null);

        public static readonly StyledProperty<WindowState> WindowStateProperty =
            AvaloniaProperty.Register<TitleBar, WindowState>(nameof(WindowState), defaultValue: WindowState.Normal);

        public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
        public bool ShowMinimize { get => GetValue(ShowMinimizeProperty); set => SetValue(ShowMinimizeProperty, value); }
        public bool ShowMaximize { get => GetValue(ShowMaximizeProperty); set => SetValue(ShowMaximizeProperty, value); }
        public bool ShowClose { get => GetValue(ShowCloseProperty); set => SetValue(ShowCloseProperty, value); }
        public IImage? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
        public WindowState WindowState { get => GetValue(WindowStateProperty); set => SetValue(WindowStateProperty, value); }

        private Window? _window;
        private IconButton? _minBtn;
        private IconButton? _maxBtn;
        private IconButton? _closeBtn;
        private bool _connected;

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            Disconnect();
            base.OnApplyTemplate(e);

            foreach (var it in new[] { "PART_LeftPanel", "PART_RightPanel" })
            {
                var ctrl = e.NameScope.Find<StackPanel>(it);
                ctrl?.IsVisible = !OperatingSystem.IsMacOS();
            }

            _minBtn = e.NameScope.Find<IconButton>("PART_MinimizeButton");
            _maxBtn = e.NameScope.Find<IconButton>("PART_MaximizeButton");
            _closeBtn = e.NameScope.Find<IconButton>("PART_CloseButton");

            Connect();
            if (_minBtn != null) Avalonia.Automation.AutomationProperties.SetName(_minBtn, Strings.Coffee_Window_Minimize);
            if (_closeBtn != null) Avalonia.Automation.AutomationProperties.SetName(_closeBtn, Strings.Common_Close);

            UpdateMaximizeIcon();
        }

        private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Window.WindowStateProperty || e.Property == Window.CanResizeProperty || e.Property == Window.CanMaximizeProperty)
            {
                SetValue(WindowStateProperty, _window!.WindowState);
                UpdateMaximizeIcon();
            }
        }

        private void UpdateMaximizeIcon()
        {
            if (_maxBtn != null && _window != null)
            {
                _maxBtn.Icon = _window.WindowState == WindowState.Maximized
                    ? LucideIconNames.Minimize
                    : LucideIconNames.Maximize;
                _maxBtn.IsEnabled = _window.CanMaximize;
                var label = _window.WindowState == WindowState.Maximized ? Strings.Coffee_Window_Restore : Strings.Coffee_Window_Maximize;
                Avalonia.Automation.AutomationProperties.SetName(_maxBtn, label);
                ToolTip.SetTip(_maxBtn, label);
            }
        }

        private void OnMinimizeClick(object? sender, EventArgs e)
        {
            _window?.WindowState = WindowState.Minimized;
        }

        private void OnMaximizeClick(object? sender, EventArgs e)
        {
            if (_window == null) return;
            _window.WindowState = _window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void OnCloseClick(object? sender, EventArgs e)
        {
            _window?.Close();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);

            Disconnect();
        }
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        { base.OnAttachedToVisualTree(e); Connect(); }
        private void Connect()
        {
            if (_connected || TopLevel.GetTopLevel(this) is not Window window || _minBtn == null) return;
            _window = window; _connected = true;
            _window.PropertyChanged += OnWindowPropertyChanged;
            _minBtn.Click += OnMinimizeClick;
            _maxBtn?.Click += OnMaximizeClick;
            _closeBtn?.Click += OnCloseClick;
            ToolTip.SetTip(_minBtn, Strings.Coffee_Window_Minimize);
            if (_closeBtn != null) ToolTip.SetTip(_closeBtn, Strings.Common_Close);
            UpdateMaximizeIcon();
        }
        private void Disconnect()
        {
            _window?.PropertyChanged -= OnWindowPropertyChanged;
            _minBtn?.Click -= OnMinimizeClick;
            _maxBtn?.Click -= OnMaximizeClick;
            _closeBtn?.Click -= OnCloseClick;
            _connected = false;
        }
    }
}
