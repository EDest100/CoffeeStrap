// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System.Windows.Input;
using Froststrap.Models;
using Froststrap.UI.Utility;
namespace Froststrap.UI.Elements.Controls;

internal partial class GameArtworkCard : UserControl
{
    public static readonly StyledProperty<QuickPlayGameItem?> GameProperty = AvaloniaProperty.Register<GameArtworkCard, QuickPlayGameItem?>(nameof(Game));
    public static readonly StyledProperty<ICommand?> PlayCommandProperty = AvaloniaProperty.Register<GameArtworkCard, ICommand?>(nameof(PlayCommand));
    public static readonly StyledProperty<bool> ShowPinProperty = AvaloniaProperty.Register<GameArtworkCard, bool>(nameof(ShowPin), true);
    public QuickPlayGameItem? Game { get => GetValue(GameProperty); set => SetValue(GameProperty, value); }
    public ICommand? PlayCommand { get => GetValue(PlayCommandProperty); set => SetValue(PlayCommandProperty, value); }
    public bool ShowPin { get => GetValue(ShowPinProperty); set => SetValue(ShowPinProperty, value); }
    private readonly CoffeeMotion _reveal;
    internal double RevealOpacity => this.FindControl<Border>("PlayReveal")!.Opacity;
    public GameArtworkCard()
    {
        InitializeComponent();
        _reveal = new CoffeeMotion(this, value => this.FindControl<Border>("PlayReveal")!.Opacity = value);
        PointerEntered += (_, _) => Reveal(true); PointerExited += (_, _) => Reveal(IsKeyboardFocusWithin);
        GotFocus += (_, _) => Reveal(true); LostFocus += (_, _) => Reveal(IsPointerOver || IsKeyboardFocusWithin);
    }
    private void Reveal(bool visible) => _reveal.To(visible ? 1 : 0, 160);
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { _reveal.Stop(); base.OnDetachedFromVisualTree(e); }
}
