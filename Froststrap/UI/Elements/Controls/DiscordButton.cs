// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Avalonia.Controls;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;
namespace Froststrap.UI.Elements.Controls;
/// <summary>Shared icon-only invitation control. Discord glyph: Simple Icons, CC0-1.0.</summary>
internal sealed class DiscordButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);
    public DiscordButton()
    {
        Command = ViewModels.GlobalViewModel.OpenWebpageCommand;
        CommandParameter = "https://discord.gg/CZ3P4Rw9D4";
        Avalonia.Automation.AutomationProperties.SetName(this, "Discord");
        ToolTip.SetTip(this, "Discord");
        Content = new Path { Width = 22, Height = 22, Stretch = Stretch.Uniform, Data = Geometry.Parse("M20.317 4.37a19.791 19.791 0 0 0-4.885-1.515.074.074 0 0 0-.079.037c-.211.375-.445.865-.609 1.249a18.27 18.27 0 0 0-5.487 0 12.64 12.64 0 0 0-.618-1.249.077.077 0 0 0-.079-.037A19.736 19.736 0 0 0 3.675 4.37a.07.07 0 0 0-.032.027C.533 9.045-.32 13.579.099 18.057a.082.082 0 0 0 .031.055 19.9 19.9 0 0 0 5.993 3.03.078.078 0 0 0 .084-.028c.462-.63.874-1.295 1.226-1.994a.076.076 0 0 0-.042-.106 13.107 13.107 0 0 1-1.872-.892.077.077 0 0 1-.008-.128c.126-.094.252-.192.372-.291a.074.074 0 0 1 .077-.01c3.928 1.793 8.18 1.793 12.062 0a.074.074 0 0 1 .078.01c.12.099.246.197.373.291a.077.077 0 0 1-.006.128c-.6.35-1.224.65-1.873.892a.076.076 0 0 0-.041.106c.36.698.772 1.362 1.225 1.993a.076.076 0 0 0 .084.028 19.839 19.839 0 0 0 6.002-3.03.077.077 0 0 0 .032-.054c.5-5.177-.838-9.674-3.549-13.66a.061.061 0 0 0-.031-.03zM8.02 15.33c-1.183 0-2.157-1.085-2.157-2.419 0-1.333.955-2.419 2.157-2.419 1.21 0 2.176 1.096 2.157 2.419 0 1.334-.955 2.419-2.157 2.419zm7.975 0c-1.183 0-2.157-1.085-2.157-2.419 0-1.333.955-2.419 2.157-2.419 1.21 0 2.176 1.096 2.157 2.419 0 1.334-.946 2.419-2.157 2.419z"), Fill = Foreground };
        PropertyChanged += (_, change) => { if (change.Property == ForegroundProperty && Content is Path path) path.Fill = Foreground; };
    }
}
