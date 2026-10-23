from pathlib import Path
from xml.dom import minidom as D
import re
root=Path.cwd()
def read(name):return (root/name).read_text(encoding='utf-8-sig')
def write(name,text):(root/name).write_text(text,encoding='utf-8')
def wrapper(source,body,model=None,extra=''):
 doc=D.parseString(source); top=doc.documentElement
 attrs=[(top.attributes.item(i).name,top.attributes.item(i).value) for i in range(top.attributes.length) if top.attributes.item(i).name not in ('d:DesignWidth','d:DesignHeight')]
 if model:attrs=[(k,model if k=='x:DataType' else v) for k,v in attrs]
 header='<!-- SPDX-FileCopyrightText: 2026 Froststrap; 2026 CoffeeStrap contributors\n     SPDX-License-Identifier: MPL-2.0 -->\n'
 return header+'<UserControl '+'\n    '.join(k+'="'+v.replace('&','&amp;').replace('"','&quot;')+'"' for k,v in attrs)+' '+extra+'>\n'+body+'\n</UserControl>\n'
s=read('Froststrap/Models/Persistable/Settings.cs').replace('} set; } = TrayDoubleClickAction.ServerInfo;','}')
write('Froststrap/Models/Persistable/Settings.cs',s)
# Advanced Settings contains the former Roblox deployment controls and minimal appearance preferences.
channel=read('Froststrap/UI/Elements/Settings/Pages/ChannelPage.axaml');dom=D.parseString(channel)
panel=dom.getElementsByTagName('controls:SettingsCardPanel')[0]
children=[n for n in panel.childNodes if n.nodeType==n.ELEMENT_NODE]
roblox=next(i for i,n in enumerate(children) if n.tagName=='TextBlock' and n.getAttribute('Text')=='Roblox')
studio=next((i for i,n in enumerate(children[roblox+1:],roblox+1) if n.tagName=='TextBlock' and n.getAttribute('Text')=='Studio'),len(children))
selected=[n.toxml() for n in children[roblox+1:studio] if n.tagName in ('controls:OptionControl','controls:CardExpander')]
appearance='''<controls:OptionControl Header="Theme"><ComboBox ItemsSource="{Binding Themes}" SelectedItem="{Binding Theme, Mode=TwoWay}" /></controls:OptionControl>
<controls:OptionControl Header="Reduce motion" Description="Use immediate transitions throughout CoffeeStrap."><ToggleSwitch IsChecked="{Binding DisableAnimations, Mode=TwoWay}" /></controls:OptionControl>'''
# Existing authoritative VM property names are checked at compile time.
body='<controls:SmoothScrollViewer><StackPanel Margin="{DynamicResource CoffeePageMargin}" Spacing="18">\n<controls:SettingsCardPanel DataContext="{Binding Appearance}" x:DataType="vm:AppearanceViewModel">'+appearance+'</controls:SettingsCardPanel>\n<controls:SettingsCardPanel DataContext="{Binding Deployment}" x:DataType="vm:ChannelViewModel">'+'\n'.join(selected)+'</controls:SettingsCardPanel>\n</StackPanel></controls:SmoothScrollViewer>'
base=read('Froststrap/UI/Elements/Settings/Pages/ShortcutsPage.axaml')
# Channel controls refer to the same common namespace aliases.
base=base.replace('xmlns:controls=', 'xmlns:ui="using:FluentAvalonia.UI.Controls" xmlns:gv="using:Froststrap.UI.ViewModels" xmlns:controls=',1) if 'xmlns:ui=' not in base else base
write('Froststrap/UI/Elements/Settings/Pages/ShortcutsPage.axaml',wrapper(base,body,'vm:AdvancedSettingsViewModel'))
# A compact curated flag editor; there is no unrestricted editor link or management/reset toggle.
p='Froststrap/UI/Elements/Settings/Pages/FastFlagsPage.axaml'
body='''<controls:SmoothScrollViewer><StackPanel Margin="{DynamicResource CoffeePageMargin}" Spacing="16">
<ItemsControl ItemsSource="{Binding Settings}"><ItemsControl.ItemsPanel><ItemsPanelTemplate><controls:ResponsiveCardGrid MinimumCardWidth="310" MaximumColumns="2" /></ItemsPanelTemplate></ItemsControl.ItemsPanel>
<ItemsControl.ItemTemplate><DataTemplate x:DataType="vm:FlagSetting"><controls:OptionControl Header="{Binding Title}" Description="{Binding Description}"><ComboBox ItemsSource="{Binding Choices}" SelectedItem="{Binding Selected, Mode=TwoWay}" /></controls:OptionControl></DataTemplate></ItemsControl.ItemTemplate></ItemsControl>
<TextBlock Text="Roblox may change supported overrides. Roblox default removes the override." FontSize="12" Foreground="{DynamicResource TextFillColorSecondaryBrush}" TextWrapping="Wrap" />
</StackPanel></controls:SmoothScrollViewer>'''
write(p,wrapper(read(p),body,'vm:CuratedFastFlagsViewModel'))
# Keep the GBS dependency and its real read-only behavior, remove top actions and excessive headings.
p='Froststrap/UI/Elements/Settings/Pages/GlobalSettingsPage.axaml';doc=D.parseString(read(p));outer=doc.getElementsByTagName('controls:SmoothScrollViewer')[0].getElementsByTagName('StackPanel')[0]
for n in list(outer.childNodes):
 if n.nodeType==n.ELEMENT_NODE and n.tagName=='Grid':outer.removeChild(n);break
for n in list(doc.getElementsByTagName('TextBlock')):
 if n.getAttribute('FontSize')=='20':n.parentNode.removeChild(n)
for n in list(doc.getElementsByTagName('Separator')):n.parentNode.removeChild(n)
for n in doc.getElementsByTagName('controls:OptionControl'):
 if 'ReadOnly_Title' in n.getAttribute('Header'):
  n.setAttribute('Header','Prevent Override');n.setAttribute('Description','Mark the local GlobalBasicSettings file read-only to prevent Roblox from overwriting these values. Turn this off before changing them in Roblox.')
# Preserve the global editor as a small final action, rather than a top button grid.
write(p,'<!-- SPDX-FileCopyrightText: 2026 Froststrap; 2026 CoffeeStrap contributors\n SPDX-License-Identifier: MPL-2.0 -->\n'+doc.documentElement.toxml()+'\n')
# Shared customization at the top, one cursor row with presets/custom/default.
p='Froststrap/UI/Elements/Settings/Pages/ModsPresetsPage.axaml';s=read(p);doc=D.parseString(s)
options=doc.getElementsByTagName('controls:OptionControl')
emoji=next(n.toxml() for n in options if 'EmojiType_Title' in n.getAttribute('Header'))
cursor='''<controls:OptionControl Header="Cursor" Description="Choose a preset or use your own PNG, JPEG, BMP or ICO image."><StackPanel Spacing="10"><ComboBox ItemsSource="{Binding CursorTypeTask.Selections}" SelectedItem="{Binding CursorTypeTask.NewState, Mode=TwoWay}"><ComboBox.ItemTemplate><DataTemplate><TextBlock Text="{Binding Converter={StaticResource EnumNameConverter}}" /></DataTemplate></ComboBox.ItemTemplate></ComboBox><StackPanel Orientation="Horizontal" Spacing="8"><Button Content="Choose image" Command="{Binding AddCustomCursorModCommand}"/><Button Content="Restore default" Command="{Binding RestoreCursorCommand}" /></StackPanel></StackPanel></controls:OptionControl>'''
def picker(title,description,choose,restore):return f'<controls:OptionControl Header="{title}" Description="{description}"><StackPanel Orientation="Horizontal" Spacing="8"><Button Content="Choose file" Command="{{Binding {choose}}}"/><Button Content="Restore default" Command="{{Binding {restore}}}" /></StackPanel></controls:OptionControl>'
body='<controls:SmoothScrollViewer><StackPanel Margin="{DynamicResource CoffeePageMargin}" Spacing="24"><controls:SettingsCardPanel>'+cursor+emoji+picker('Game font','TTF, OTF or TTC font.','ChooseCustomFontCommand','RemoveCustomFontCommand')+picker('Death sound','Use your own MP3, OGG or WAV sound.','AddCustomDeathSoundCommand','RemoveCustomDeathSoundCommand')+picker('Shift lock','Use your own cursor image.','AddCustomShiftlockModCommand','RemoveCustomShiftlockModCommand')+'</controls:SettingsCardPanel><Grid ColumnDefinitions="*,Auto"><TextBlock Text="Community mods" FontSize="20" FontWeight="Bold" VerticalAlignment="Center"/><Button Grid.Column="1" Content="Manage installed" Command="{Binding OpenModsCommand}" /></Grid><mods:CommunityModsPage DataContext="{Binding Community}" /></StackPanel></controls:SmoothScrollViewer>'
write(p,wrapper(s,body,extra='xmlns:mods="using:Froststrap.UI.Elements.Settings.Pages.Mods"'))
print('Advanced, curated flags, Global Settings and Texture Editor surfaces migrated.')
