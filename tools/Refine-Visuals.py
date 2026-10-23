from pathlib import Path
from xml.dom import minidom as D
import re
root=Path.cwd()
def read(p):return (root/p).read_text(encoding='utf-8-sig')
def write(p,s):(root/p).write_text(s,encoding='utf-8')
p='Froststrap/UI/ViewModels/Settings/ModsPresetsViewModel.cs';s=read(p).replace('public EnumModPresetTask<Enums.CursorType> CursorTypeTask','public CursorPresetTask CursorTypeTask')
s=s.replace('[CursorImageFileType], "cursors", RefreshStates, convertToPng: true);','[CursorImageFileType], "cursors", () => { CursorTypeTask.AcceptCustomFiles(); OnPropertyChanged(nameof(CursorTypeTask)); RefreshStates(); }, convertToPng: true);')
write(p,s)
# Remove the accidentally carried Studio controls from the consolidated deployment section.
p='Froststrap/UI/Elements/Settings/Pages/ShortcutsPage.axaml';doc=D.parseString(read(p))
for n in list(doc.getElementsByTagName('controls:OptionControl'))+list(doc.getElementsByTagName('controls:CardExpander')):
 if n.parentNode and ('Studio' in n.getAttribute('Header') or re.search(r'\{Binding !?Studio',n.toxml())):n.parentNode.removeChild(n)
write(p,'<!-- SPDX-FileCopyrightText: 2026 Froststrap; 2026 CoffeeStrap contributors\n SPDX-License-Identifier: MPL-2.0 -->\n'+doc.documentElement.toxml())
# Keep pinned games visible at the default size instead of spending a full card on Auto.
p='Froststrap/UI/Elements/Settings/Pages/HomePage.axaml';doc=D.parseString(read(p));stack=doc.getElementsByTagName('StackPanel')[0];stack.setAttribute('Spacing','18')
for image in doc.getElementsByTagName('Image'):
 if image.getAttribute('Width')=='70':image.setAttribute('Width','56');image.setAttribute('Height','56')
region=next(n for n in doc.getElementsByTagName('controls:OptionControl') if n.getAttribute('Header')=='Preferred Region')
replacement=D.parseString('<Border xmlns="https://github.com/avaloniaui" Padding="18,12" CornerRadius="18" Background="{DynamicResource CoffeeGroupBrush}" BorderBrush="{DynamicResource ControlStrokeColorDefaultBrush}" BorderThickness="1"><Grid ColumnDefinitions="*,Auto"><TextBlock Text="Preferred Region" FontWeight="SemiBold" VerticalAlignment="Center"/><ComboBox Grid.Column="1" ItemsSource="{Binding Regions}" SelectedItem="{Binding PreferredRegion, Mode=TwoWay}" ToolTip.Tip="{Binding RegionHint}" MinWidth="150" /></Grid></Border>').documentElement
replacement.removeAttribute('xmlns');region.parentNode.replaceChild(replacement,region);write(p,doc.documentElement.toxml())
# Opaque media children must respect the rounded card boundary.
for p in ('Froststrap/UI/Elements/Settings/Pages/Mods/CommunityModsPage.axaml','Froststrap/UI/Elements/Settings/Pages/QuickPlayPage.axaml'):
 s=read(p).replace('<Border Background="{DynamicResource CoffeeGroupBrush}">','<Border Background="{DynamicResource CoffeeGroupBrush}" CornerRadius="20,20,0,0" ClipToBounds="True">');write(p,s)
print('Studio controls removed; Home density, rounded media and custom cursor/preset precedence corrected.')
