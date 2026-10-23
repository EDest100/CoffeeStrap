from pathlib import Path
from xml.dom import minidom as D
root=Path.cwd()
def read(p):return (root/p).read_text(encoding='utf-8-sig')
def write(p,s):(root/p).write_text(s,encoding='utf-8')
header='// SPDX-FileCopyrightText: 2026 Froststrap; 2026 CoffeeStrap contributors\n// SPDX-License-Identifier: MPL-2.0\n'
for name in ('ShortcutsPage','ModsPresetsPage'):
 write('Froststrap/UI/Elements/Settings/Pages/'+name+'.axaml.cs',header+'using Avalonia.Controls;\nnamespace Froststrap.UI.Elements.Settings.Pages;\ninternal partial class '+name+' : UserControl { public '+name+'() => InitializeComponent(); }\n')
p='Froststrap/UI/ViewModels/Settings/ModsPresetsViewModel.cs';s=read(p).replace('internal class ModsPresetsViewModel : NotifyPropertyChangedViewModel','internal class ModsPresetsViewModel : NotifyPropertyChangedViewModel, IDisposable')
s=s.replace('        public event EventHandler? OpenModsEvent;','        public Mods.CommunityModsViewModel Community { get; } = new();\n        public ICommand RestoreCursorCommand => new RelayCommand(() => { CursorTypeTask.NewState = Enums.CursorType.Default; if (HasCustomCursors) RemoveCustomCursorMod(); });\n        public void Dispose() => Community.Dispose();\n        public event EventHandler? OpenModsEvent;',1)
write(p,s)
p='Froststrap/UI/Elements/Settings/Pages/ModsPresetsPage.axaml';s=read(p).replace('PNG, JPEG, BMP or ICO image.','PNG, ICO, CUR or ANI cursor.').replace('MP3, OGG or WAV sound.','OGG sound.');write(p,s)
# Reuse the existing robust Roblox link parser and autocomplete behavior with a new compact layout.
p='Froststrap/UI/Elements/Settings/Pages/QuickPlayPage.axaml';doc=D.parseString(read(p));top=doc.documentElement
search=next(n for n in doc.getElementsByTagName('AutoCompleteBox') if n.getAttribute('x:Name')=='GameSearchAutoCompleteBox')
search.setAttribute('Watermark','Search games or paste a Roblox link');search.setAttribute('MinHeight','48');search.setAttribute('Margin','0');search.setAttribute('HorizontalAlignment','Stretch')
card='''<DataTemplate x:Key="GameCardTemplate" x:DataType="models:QuickPlayGameItem"><controls:Card Padding="0" CornerRadius="20" ClipToBounds="True"><Grid RowDefinitions="150,Auto,Auto">
<Border Background="{DynamicResource CoffeeGroupBrush}"><Grid><ic:Lucide Icon="Gamepad2" Width="36" Height="36" HorizontalAlignment="Center" VerticalAlignment="Center"/><Image Source="{Binding ThumbnailBitmap}" Stretch="UniformToFill" RenderOptions.BitmapInterpolationMode="HighQuality" /></Grid></Border>
<ToggleButton HorizontalAlignment="Right" VerticalAlignment="Top" Margin="10" Width="34" Height="34" Padding="7" IsChecked="{Binding IsPinned, Mode=TwoWay}" AutomationProperties.Name="Pin to main menu" ToolTip.Tip="Pin to main menu"><ic:Lucide Icon="Star" Width="18" Height="18" /></ToggleButton>
<TextBlock Grid.Row="1" Text="{Binding Name}" FontWeight="Bold" FontSize="15" TextAlignment="Center" TextWrapping="Wrap" Margin="14,12" />
<Button Grid.Row="2" Content="Play" Margin="12,0,12,12" Height="38" HorizontalAlignment="Stretch" Command="{Binding #RootPage.((vm:QuickPlayViewModel)DataContext).JoinGameCommand}" CommandParameter="{Binding}" />
</Grid></controls:Card></DataTemplate>'''
attrs='\n    '.join(top.attributes.item(i).name+'="'+top.attributes.item(i).value.replace('&','&amp;').replace('"','&quot;')+'"' for i in range(top.attributes.length))
items=lambda source: '<ItemsControl ItemsSource="{Binding '+source+'}" ItemTemplate="{StaticResource GameCardTemplate}"><ItemsControl.ItemsPanel><ItemsPanelTemplate><controls:ResponsiveCardGrid MinimumCardWidth="240" /></ItemsPanelTemplate></ItemsControl.ItemsPanel></ItemsControl>'
body='''<controls:SmoothScrollViewer><StackPanel Margin="{DynamicResource CoffeePageMargin}" Spacing="20">'''+search.toxml()+'''
<ContentControl Content="{Binding SelectedGame}" ContentTemplate="{StaticResource GameCardTemplate}" IsVisible="{Binding HasSelectedGame}" MaxWidth="360" HorizontalAlignment="Left" />
<controls:CardExpander Header="Join options" Description="Optional instance or private server" IsExpanded="False"><StackPanel Spacing="12" Margin="0,12"><TextBox Text="{Binding ServerId, UpdateSourceTrigger=PropertyChanged}" Watermark="Server ID or private link"/><Button Content="Join" Command="{Binding JoinServerByIdCommand}"/><Button Content="Private servers" Command="{Binding ShowPrivateServersFromSearchCommand}"/><Button Content="Public servers" Command="{Binding ViewRobloxServersCommand}" CommandParameter="{Binding SelectedGame}" IsEnabled="{Binding HasSelectedGame}" /></StackPanel></controls:CardExpander>
<TabControl SelectedIndex="{Binding SelectedTabIndex, Mode=TwoWay}"><TabItem Header="Continue">'''+items('RecentGames')+'''</TabItem><TabItem Header="Discover">'''+items('RecommendedGames')+'''</TabItem><TabItem Header="Favorites">'''+items('FavoriteGames')+'''</TabItem></TabControl>
<TextBlock Text="No games to show yet." IsVisible="{Binding ShowRecentEmpty}" Foreground="{DynamicResource TextFillColorSecondaryBrush}" HorizontalAlignment="Center"/>
</StackPanel></controls:SmoothScrollViewer>'''
write(p,'<!-- SPDX-FileCopyrightText: 2026 Froststrap; 2026 CoffeeStrap contributors\n SPDX-License-Identifier: MPL-2.0 -->\n<UserControl '+attrs+'>\n<UserControl.Resources>'+card+'</UserControl.Resources>\n'+body+'\n</UserControl>')
# Actual account-independent last launch is also useful without the retired activity cache.
p='Froststrap/UI/ViewModels/Settings/QuickPlayViewModel.cs';s=read(p)
s=s.replace('if (item != null) LaunchRoblox(item.PlaceId);','if (item != null) Services.GameLaunchService.Default.Launch(item.PlaceId, item.Name);',1)
s=s.replace('        _allHistory = await Task.Run(() => LoadLocalHistory(_cachePath));','        _allHistory = []; // Activity tracking is retired; no history log is collected or loaded here.')
s=s.replace('''            await SetRecentGamesFromSources(localGames, apiGames);''','''            if (App.State.Prop.LastPlaceId > 0 && !localGames.Any(game => game.PlaceId == App.State.Prop.LastPlaceId))
                localGames.Insert(0, new QuickPlayGameItem { PlaceId = App.State.Prop.LastPlaceId, Name = App.State.Prop.LastGameName ?? "Last launched place" });
            await SetRecentGamesFromSources(localGames, apiGames);''',1)
write(p,s)
print('Quick Play and picker surfaces connected to shared state and commands.')
