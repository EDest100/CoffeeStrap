# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
from pathlib import Path
import re
root=Path(__file__).resolve().parents[1]
def read(name):return (root/name).read_text(encoding='utf-8-sig')
def write(name,text):(root/name).write_text(text,encoding='utf-8')
def replace_method(source,signature,replacement):
 start=source.index(signature);brace=source.index('{',start);depth=1;end=brace+1
 while depth:
  if source[end]=='{':depth+=1
  elif source[end]=='}':depth-=1
  end+=1
 return source[:start]+replacement+source[end:]
# Retired settings are inert even when an older settings file contains true.
p='Froststrap/Models/Persistable/Settings.cs';s=read(p)
retired=['EnableActivityTracking','ShowServerDetails','ShowServerUptime','AutoRejoin','ShowGameHistoryMenu','PlaytimeCounter','ShowUsingFroststrapRPC','UseDiscordRichPresence','StudioRPC','EnableBetterMatchmaking']
for name in retired:
 s,n=re.subn(r'public bool '+name+r' \{ get; set; \}(?: = (?:true|false);)?', '[JsonIgnore] public bool '+name+' { get => false; set { } }',s);assert n==1,name
s=re.sub(r'public TrayDoubleClickAction DoubleClickAction.*?;', '[JsonIgnore] public TrayDoubleClickAction DoubleClickAction { get => TrayDoubleClickAction.None; set { } }',s,count=1)
# The old auto-integration list cannot launch executables after its UI is removed.
s=s.replace('public ObservableCollection<CustomIntegration> CustomIntegrations { get; set; } = [];','[JsonIgnore] public ObservableCollection<CustomIntegration> CustomIntegrations { get => []; set { } }')
s=s.replace('public UpdateCheck UpdateChecks { get; set; } = UpdateCheck.Stable;','[JsonIgnore] public UpdateCheck UpdateChecks { get => UpdateCheck.Disabled; set { } }')
s=s.replace('public bool UseFastFlagManager { get; set; } = true;','[JsonIgnore] public bool UseFastFlagManager { get => true; set { } }')
write(p,s)
# Explicit play routes share validation and minimal last-place persistence.
p='Froststrap/UI/ViewModels/Settings/QuickPlayViewModel.cs';s=read(p)
s=replace_method(s,'    private static void LaunchRoblox(','''    private static void LaunchRoblox(long placeId, string? jobId = null, string? accessCode = null, string? linkCode = null)
    {
        if (placeId <= 0) return;
        Froststrap.Services.GameLaunchService.Default.Launch(placeId, jobId: jobId, accessCode: accessCode, linkCode: linkCode);
    }''')
write(p,s)
# Default leaves every override absent; unknown old entries are kept in local backup.
p='Froststrap/FastFlagManager.cs';s=read(p)
s=s.replace('''            if (GetPreset("Rendering.ManualFullscreen") != "False")
                SetPreset("Rendering.ManualFullscreen", "False");''','''            var accepted = Services.CuratedFlagRegistry.Filter(Prop, out var held);
            Services.CuratedFlagRegistry.PreserveHeld(held);
            Prop = accepted;''')
# Existing Save implementation below handles platform sync; gate before that save.
s=s.replace('public override bool Save()\n        {','public override bool Save()\n        {\n            var accepted = Services.CuratedFlagRegistry.Filter(Prop, out var held);\n            Services.CuratedFlagRegistry.PreserveHeld(held);\n            Prop = accepted;',1)
write(p,s)
p='Froststrap/Bootstrapper/Bootstrapper.cs';s=read(p)
s=s.replace('''        var fileRestoreMap = new Dictionary<string, List<string>>();''','''        Services.CuratedFlagRegistry.SanitizeFile(Path.Combine(clientSettingsBaseDirectory, "ClientSettings", "ClientAppSettings.json"));
        var fileRestoreMap = new Dictionary<string, List<string>>();''',1)
# Record only a place from an explicit launch, independently of activity watchers.
s=s.replace('''    private async Task PrepareWatcherIfNeededAsync(List<int> autoclosePids, string? logFileName = null, string? logDirectory = null)
    {''','''    private async Task PrepareWatcherIfNeededAsync(List<int> autoclosePids, string? logFileName = null, string? logDirectory = null)
    {
        if (!IsStudioLaunch && _joinData.PlaceId is > 0) Services.GameLaunchService.RecordPlace((long)_joinData.PlaceId.Value);''')
write(p,s)
# Replace duplicated navigation factories and string-based view lookup with the route registry.
p='Froststrap/UI/ViewModels/Settings/MainWindowViewModel.cs';s=read(p)
start=s.index('        private static readonly Dictionary<string, LucideIconNames> _pageIconMap');end=s.index('        private object? _currentPage;',start);s=s[:start]+s[end:]
start=s.index('            NavigateToIntegrationsCommand =');end=s.index('            SelectedLaunchMode = App.State.Prop.LastLaunchMode;',start)
aliases={'NavigateToIntegrationsCommand':'home','NavigateToBehaviourCommand':'behaviour','NavigateToLinuxSettingsCommand':'shortcuts','NavigateToPresetModsCommand':'mods','NavigateToFastFlagsCommand':'fastflags','NavigateToAppearanceCommand':'shortcuts','NavigateToRegionSelectorCommand':'home','NavigateToGlobalSettingsCommand':'globalsettings','NavigateToShortcutsCommand':'shortcuts','NavigateToQuickPlayCommand':'quickplay','NavigateToChannelsCommand':'shortcuts','NavigateToGlobalSettingsEditorCommand':'globalsettingseditor','NavigateToFastFlagEditorCommand':'fastflags','NavigateToCommunityModsCommand':'mods','NavigateToModGeneratorCommand':'modgenerator','NavigateToMyModsCommand':'custommods'}
s=s[:start]+''.join(f'            {command} = new RelayCommand(() => NavigateRegistered("{route}"));\n' for command,route in aliases.items())+'            NavigateRegistered("home");\n\n'+s[end:]
s=s.replace('CurrentPageIcon = _pageIconMap.TryGetValue(pageId, out var icon) ? icon : LucideIconNames.CircleQuestionMark;','CurrentPageIcon = Froststrap.UI.Utility.NavigationRegistry.Find(pageId)?.Icon ?? LucideIconNames.CircleQuestionMark;')
s=replace_method(s,'        private void NavigateToLastPage(','''        public void NavigateRegistered(string tag)
        {
            var route = Froststrap.UI.Utility.NavigationRegistry.Find(tag);
            if (route == null) return;
            var model = route.CreateModel(this);
            if (model is ModsPresetsViewModel mods)
            {
                mods.OpenModsEvent += (_, _) => NavigateRegistered("custommods");
                mods.OpenModGeneratorEvent += (_, _) => NavigateRegistered("modgenerator");
            }
            var old = CurrentPage;
            Navigate(route.Tag, route.Title, "", model);
            if (old is IDisposable disposable) disposable.Dispose();
        }''')
write(p,s)
p='Froststrap/UI/Elements/Settings/MainWindow.axaml.cs';s=read(p)
s=replace_method(s,'        private Action? GetNavigationAction(','''        private Action? GetNavigationAction(string pageTag) => NavigationRegistry.Find(pageTag) != null
            ? () => _viewModel?.NavigateRegistered(pageTag) : null;''')
start=s.index('        private readonly Dictionary<string, (string Title, LucideIconNames Icon)> _pageInfo');end=s.index('        private void UpdatePageView(',start)
s=s[:start]+'''        private readonly Dictionary<string, (string Title, LucideIconNames Icon)> _pageInfo =
            NavigationRegistry.Pages.ToDictionary(page => page.Tag, page => (page.Title, page.Icon));

'''+s[end:]
start=s.index('        private static readonly Dictionary<string, Type> ViewTypeCache');end=s.index('        public void LoadState()',start)
s=s[:start]+'''        private static Control? ResolveViewForViewModel(object viewModel) => NavigationRegistry.CreateView(viewModel);

'''+s[end:]
# Keep only registered searchable routes when generating the live control index.
start=s.index('            var allPageTags =');end=s.index('            var pagesToIndex =',start)
s=s[:start]+'''            var allPageTags = NavigationRegistry.Pages.Where(page => page.Searchable).Select(page => page.Tag).ToList();

'''+s[end:]
start=s.index('                        object? vm = pageTag switch');end=s.index('                        if (vm == null) continue;',start)
s=s[:start]+'''                        object? vm = NavigationRegistry.Find(pageTag)?.CreateModel(_viewModel);

'''+s[end:]
s=s.replace('''                        view.DataContext = null;
                        stagingArea.UpdateLayout();''','''                        view.DataContext = null;
                        if (vm is IDisposable disposable) disposable.Dispose();
                        stagingArea.UpdateLayout();''')
write(p,s)
print('Retired settings, explicit launch path, flag gate and route architecture migrated.')
