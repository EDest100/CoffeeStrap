from pathlib import Path
from xml.dom import minidom as D
root=Path.cwd()
def read(p):return (root/p).read_text(encoding='utf-8-sig')
def write(p,s):(root/p).write_text(s,encoding='utf-8')
p='Froststrap/UI/Elements/Settings/Pages/GlobalSettingsPage.axaml';doc=D.parseString(read(p));panel=doc.getElementsByTagName('controls:SettingsCardPanel')[0]
for card in panel.getElementsByTagName('controls:OptionControl'):
 if 'FramerateLimit_Title' in card.getAttribute('Header'):card.setAttribute('Header','Frame rate limit (saved)');card.setAttribute('Description','Stored in Roblox settings. Use the in-game menu for live frame-rate changes.')
 if 'StartQuality_Title' in card.getAttribute('Header'):card.setAttribute('Header','Graphics quality (saved)');card.setAttribute('Description','Roblox graphics level for the next settings load; 0 uses automatic quality.')
source='''<!-- SPDX-FileCopyrightText: 2026 Froststrap; 2026 CoffeeStrap contributors
 SPDX-License-Identifier: MPL-2.0 -->
<UserControl xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:controls="using:Froststrap.UI.Elements.Controls" xmlns:resources="using:Froststrap.Resources" xmlns:fa="using:FluentAvalonia.UI.Controls" xmlns:vm="using:Froststrap.UI.ViewModels.Settings" x:Class="Froststrap.UI.Elements.Controls.NativeGraphicsSettings" x:DataType="vm:GlobalSettingsViewModel">'''+panel.toxml()+'</UserControl>'
write('Froststrap/UI/Elements/Controls/NativeGraphicsSettings.axaml',source)
write('Froststrap/UI/Elements/Controls/NativeGraphicsSettings.axaml.cs','''// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Avalonia.Controls;
namespace Froststrap.UI.Elements.Controls;
internal partial class NativeGraphicsSettings : UserControl { public NativeGraphicsSettings() => InitializeComponent(); }
''')
replacement=doc.createElement('controls:NativeGraphicsSettings');panel.parentNode.replaceChild(replacement,panel);write(p,doc.documentElement.toxml())
p='Froststrap/UI/Elements/Settings/Pages/FastFlagsPage.axaml';s=read(p)
s=s.replace('<TextBlock Text="Roblox may change supported overrides.', '<controls:CardExpander Header="Roblox graphics" Description="Saved frame rate, quality and display settings. Live changes are not verified." IsExpanded="False"><controls:NativeGraphicsSettings DataContext="{Binding NativeSettings}" IsEnabled="{Binding NativeSettingsLoaded}" /></controls:CardExpander>\n<TextBlock Text="Roblox may change supported overrides.')
write(p,s)
p='Froststrap/UI/ViewModels/Settings/CuratedFastFlagsViewModel.cs';s=read(p).replace('internal sealed class CuratedFastFlagsViewModel\n{','internal sealed class CuratedFastFlagsViewModel\n{\n    public GlobalSettingsViewModel NativeSettings { get; } = new();\n    public bool NativeSettingsLoaded => App.GlobalSettings.Loaded;')
s=s.replace('new("Texture detail", "Override the texture detail preset; default leaves Roblox in control.",','new("Texture quality", "Texture quality override for the next launch. Roblox default removes the override.",')
write(p,s)
p='Froststrap/UI/ViewModels/Settings/GlobalSettingsViewModel.cs';s=read(p).replace('''                App.GlobalSettings.SetPreset("Audio.MasterVolumeStudio",
                    value.ToString(CultureInfo.InvariantCulture));
                OnPropertyChanged(nameof(GraphicsQuality));''','''                App.GlobalSettings.SetPreset("Rendering.SavedQualityLevel",
                    Math.Clamp(value, 0, 10).ToString(CultureInfo.InvariantCulture));
                OnPropertyChanged(nameof(GraphicsQuality));''',1)
write(p,s)
print('Shared native graphics controls integrated; graphics-quality mapping repaired.')
