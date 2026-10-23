from pathlib import Path
from xml.dom import minidom as D
root=Path.cwd()
def read(p):return (root/p).read_text(encoding='utf-8-sig')
def write(p,s):(root/p).write_text(s,encoding='utf-8')
p='Froststrap/Services/GameLaunchService.cs';s=read(p).replace('if (placeId <= 0) throw new ArgumentOutOfRangeException(nameof(placeId));','ArgumentOutOfRangeException.ThrowIfNegativeOrZero(placeId);');write(p,s)
p='Froststrap/Services/CuratedFlagRegistry.cs';s=read(p).replace('    public const string Source', '    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };\n    public const string Source').replace('new JsonSerializerOptions { WriteIndented = true }','JsonOptions');write(p,s)
p='Froststrap/UI/ViewModels/Settings/BehaviourViewModel.cs';s=read(p).replace('private Task LoadAvailableRegionsAsync()', 'private static Task LoadAvailableRegionsAsync()');write(p,s)
p='Froststrap/UI/ViewModels/Settings/QuickPlayViewModel.cs';s=read(p).replace('    private readonly string _cachePath = Path.Combine(Paths.Cache, "GameHistory.json");\n','');write(p,s)
p='Froststrap/UI/ViewModels/Settings/MainWindowViewModel.cs';s=read(p).replace('            SearchBar = new();','''            SearchBar = new();
            SearchBar.SetSearchIndex(Froststrap.UI.Utility.NavigationRegistry.Pages.Where(page => page.Sidebar && (page.Tag != "globalsettings" || GBSEnabled))
                .Select(page => new SearchBarItem { DisplayName = page.Title, PageName = page.Title, PageTag = page.Tag, Category = "Navigation" }).ToList());''',1);write(p,s)
p='Froststrap/UI/Elements/Settings/MainWindow.axaml.cs';s=read(p)
s='#pragma warning disable CA1001 // Avalonia owns window lifetime; disposable search state is released in OnClosed.\n'+s
s=s.replace('        protected override async void OnOpened(EventArgs e)','''        protected override void OnClosed(EventArgs e)
        {
            _searchRevealCancellation?.Cancel(); _searchRevealCancellation?.Dispose();
            (_viewModel?.CurrentPage as IDisposable)?.Dispose(); _viewModel?.SearchBar.Dispose();
            base.OnClosed(e);
        }

        protected override async void OnOpened(EventArgs e)''',1)
s=s.replace('NavigationRegistry.Pages.Where(page => page.Searchable)', 'NavigationRegistry.Pages.Where(page => page.Searchable && (page.Tag != "globalsettings" || _viewModel.GBSEnabled))')
s=s.replace('group.IsExpanded = true;', 'group.SetCurrentValue(CardExpander.IsExpandedProperty, true);')
write(p,s)
p='Froststrap/UI/Elements/Settings/Pages/ShortcutsPage.axaml';s=read(p)
# Reveal state must not mutate the master setting when search opens a group.
s=s.replace('IsExpanded="{Binding PlayerVersionOverrideEnabled, Mode=TwoWay}"','IsExpanded="{Binding PlayerVersionOverrideEnabled, Mode=OneWay}"');write(p,s)
p='Froststrap/UI/Elements/Settings/Pages/GlobalSettingsPage.axaml';s=read(p).replace('Mark the local GlobalBasicSettings file read-only to prevent Roblox from overwriting these values. Turn this off before changing them in Roblox.','Make the local GlobalBasicSettings file read-only to help preserve these values. Roblox settings may not save until you turn this off.');write(p,s)
p='Froststrap/UI/ViewModels/Settings/GlobalSettingsViewModel.cs';s=read(p).replace('return $"Read-only must be enabled for {settingsList} to not be forgotten";','return $"Roblox may overwrite {settingsList} while Prevent Override is off.";');write(p,s)
# Reuse the single Discord logo geometry and invitation instead of text buttons.
p='Froststrap/UI/Elements/Settings/Pages/HomePage.axaml';doc=D.parseString(read(p));button=next(n for n in doc.getElementsByTagName('Button') if n.getAttribute('CommandParameter')=='https://discord.gg/CZ3P4Rw9D4');geometry=button.getElementsByTagName('Path')[0].getAttribute('Data')
write('Froststrap/UI/Elements/Controls/DiscordButton.cs','''// SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
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
        Content = new Path { Width = 22, Height = 22, Stretch = Stretch.Uniform, Data = Geometry.Parse("'''+geometry+'''"), Fill = Foreground };
        PropertyChanged += (_, change) => { if (change.Property == ForegroundProperty && Content is Path path) path.Fill = Foreground; };
    }
}
''')
for child in list(button.childNodes):button.removeChild(child)
replacement=doc.createElement('controls:DiscordButton')
for key in ('Grid.Column','Width','Height','Padding','VerticalAlignment'):
 if button.hasAttribute(key):replacement.setAttribute(key,button.getAttribute(key))
button.parentNode.replaceChild(replacement,button);write(p,doc.documentElement.toxml())
p='Froststrap/UI/Elements/About/Pages/AboutPage.axaml';doc=D.parseString(read(p))
for n in list(doc.getElementsByTagName('Button'))+list(doc.getElementsByTagName('controls:IconButton')):
 if n.getAttribute('CommandParameter')=='https://discord.gg/KdR9vpRcUN':
  logo=doc.createElement('controls:DiscordButton');logo.setAttribute('Width','40');logo.setAttribute('Height','34');n.parentNode.replaceChild(logo,n)
write(p,'<!-- SPDX-FileCopyrightText: 2026 Froststrap; 2026 CoffeeStrap contributors\n SPDX-License-Identifier: MPL-2.0 -->\n'+doc.documentElement.toxml())
write('branding/Discord-NOTICE.txt','Discord glyph: Simple Icons (CC0-1.0). Source: https://github.com/simple-icons/simple-icons/blob/develop/icons/discord.svg\nLicense: https://github.com/simple-icons/simple-icons/blob/develop/LICENSE.md\nDiscord trademarks remain owned by Discord.\n')
print('Binding lifetimes, search route eligibility, master-group reveal and shared Discord control refined.')
