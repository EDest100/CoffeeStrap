// SPDX-FileCopyrightText: 2026 Froststrap; 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Avalonia;
using Avalonia.Controls;
using Froststrap.UI.Utility;
using Froststrap.UI.ViewModels.Dialogs;
using Froststrap.Services;
namespace Froststrap.UI.Elements.Dialogs;

internal partial class LaunchMenuDialog : Base.AvaloniaWindow
{
    protected override bool ApplyTopPadding => false;
    public NextAction CloseAction { get; private set; } = NextAction.Terminate;
    private readonly CoffeeMotion _glow;
    public LaunchMenuDialog() : this(GameLaunchService.Default) { }
    internal LaunchMenuDialog(GameLaunchService launches)
    {
        InitializeComponent();
        var model = new LaunchMenuViewModel(launches) { SelectedLaunchMode = LaunchMode.Player };
        DataContext = model;
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        model.CloseWindowRequest += (_, action) => { CloseAction = action; Close(); };
        _glow = new CoffeeMotion(this, value => this.FindControl<Border>("PlayGlow")!.Opacity = value);
        var play = this.FindControl<Button>("PlayButton")!;
        play.PointerEntered += (_, _) => _glow.To(1, 180);
        play.PointerExited += (_, _) => _glow.To(play.IsFocused ? 1 : 0, 180);
        play.GotFocus += (_, _) => _glow.To(1, 180);
        play.LostFocus += (_, _) => _glow.To(play.IsPointerOver ? 1 : 0, 180);
    }
    protected override void OnClosed(EventArgs e) { _glow.Stop(); (DataContext as LaunchMenuViewModel)?.Dispose(); base.OnClosed(e); }
}
