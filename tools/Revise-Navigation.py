from pathlib import Path
from xml.dom import minidom as D
import re
root=Path.cwd()
def read(p):return (root/p).read_text(encoding='utf-8-sig')
def write(p,s):(root/p).write_text(s,encoding='utf-8')
# One shell supports both the main menu and settings; switching pages keeps the window and transitions.
p='Froststrap/UI/Elements/Dialogs/LaunchMenuDialog.axaml';backup=root/'tools/Preview/History/LaunchMenuDialog.v0.3.axaml.txt'
backup.write_text(read(p),encoding='utf-8');(root/p).unlink()
p='Froststrap/UI/Elements/Dialogs/LaunchMenuDialog.axaml.cs';write(p,'''// SPDX-FileCopyrightText: 2026 Froststrap; 2026 CoffeeStrap contributors
// SPDX-License-Identifier: MPL-2.0
namespace Froststrap.UI.Elements.Dialogs;
internal sealed class LaunchMenuDialog : Settings.MainWindow
{
    public LaunchMenuDialog() : base(false) { }
}
''')
p='Froststrap/UI/Elements/Settings/MainWindow.axaml';doc=D.parseString(read(p));nav=doc.getElementsByTagName('controls:CoffeeNavigationView')[0]
menu=doc.getElementsByTagName('controls:CoffeeNavigationView.MenuItems')[0]
for child in list(menu.childNodes):menu.removeChild(child)
search=doc.getElementsByTagName('controls:CoffeeNavigationView.PaneToggleRowContent')[0]
for child in list(search.childNodes):search.removeChild(child)
search.appendChild(D.parseString('<controls:InlineSettingsSearch xmlns:controls="using:Froststrap.UI.Elements.Controls" Name="SettingsSearch" DataContext="{Binding SearchBar}"/>').documentElement)
for overlay in list(doc.getElementsByTagName('elements:SearchDialog')):overlay.parentNode.removeChild(overlay)
# Keep the footer/account row stable between Home and Settings; hide redundant action buttons on Home.
for node in doc.getElementsByTagName('Button')+doc.getElementsByTagName('controls:IconButton'):
 if node.hasAttribute('Command') and any(key in node.getAttribute('Command') for key in ('SaveAndLaunchSettings','RestartApp','SaveSettings','CloseWindow')):node.setAttribute('IsVisible','{Binding !IsHome}')
write(p,'<!-- SPDX-FileCopyrightText: 2026 Froststrap; 2026 CoffeeStrap contributors\n SPDX-License-Identifier: MPL-2.0 -->\n'+doc.documentElement.toxml())
p='Froststrap/UI/ViewModels/Settings/MainWindowViewModel.cs';s=read(p)
s=s.replace('public string SelectedPage { get => _selectedPage; set => SetProperty(ref _selectedPage, value); }','public string SelectedPage { get => _selectedPage; set { if (SetProperty(ref _selectedPage, value)) OnPropertyChanged(nameof(IsHome)); } }\n        public bool IsHome => SelectedPage == "home";')
write(p,s)
p='Froststrap/UI/Elements/Settings/MainWindow.axaml.cs';s=read(p)
s=s.replace('''            InitializeComponent();
            WindowDecorations''','''            InitializeComponent();
            foreach (var route in NavigationRegistry.Pages.Where(route => route.Sidebar))
                NavView.MenuItems.Add(new CoffeeNavigationItem { Content = route.Title, Tag = route.Tag, Icon = new LucideAvalonia.Lucide { Icon = route.Icon } });
            WindowDecorations''',1)
start=s.index('            var overlay = this.FindControl<SearchDialog>("SearchOverlay");');end=s.index('            App.Logger.Debug("Initializing settings window");',start)
s=s[:start]+'''            var search = this.FindControl<InlineSettingsSearch>("SettingsSearch");
            if (search != null) search.IndexRequested += (_, _) => StartIndexingAllPagesIfNeeded();

'''+s[end:]
s=s.replace('            gbs.IsEnabled = _viewModel.GBSEnabled;','            var globalItem = NavView.MenuItems.FirstOrDefault(item => Equals(item.Tag, "globalsettings"));\n            if (globalItem != null) globalItem.IsEnabled = _viewModel.GBSEnabled;')
write(p,s)
p='Froststrap/UI/Utility/NavigationRegistry.cs';s=read(p).replace('owner => new HomeViewModel(Services.GameLaunchService.Default, owner.SaveAndLaunchSettings,','owner => new HomeViewModel(Services.GameLaunchService.Default, () => { owner.SelectedLaunchMode = LaunchMode.Player; owner.SaveAndLaunchSettings(); },');write(p,s)
# Search navigation entries come from the same routes; deleted pages cannot leak into results.
p='Froststrap/UI/Utility/SearchIndexBuilder.cs';s=read(p)
start=s.index('            var navigationItems = new[]');end=s.index('            _navigationItemsCache = [];',start)
s=s[:start]+'''            var navigationItems = NavigationRegistry.Pages.Where(page => page.Sidebar)
                .Select(page => new { Display = page.Title, Tag = page.Tag });

'''+s[end:];write(p,s)
# Do not query a third-party geolocation provider from Launcher settings initialization.
p='Froststrap/UI/ViewModels/Settings/BehaviourViewModel.cs';s=read(p)
start=s.index('        private async Task LoadAvailableRegionsAsync(');brace=s.index('{',start);end=brace+1;depth=1
while depth:
 if s[end]=='{':depth+=1
 elif s[end]=='}':depth-=1
 end+=1
old=s[start:end];s=s[:start]+'''        private Task LoadAvailableRegionsAsync()
        {
            return Task.CompletedTask; // Region providers are unavailable pending verified terms and consent.
        }'''+s[end:];write(p,s)
print('Shared home/settings shell and route-driven sidebar/search wired; implicit region-provider request removed.')
