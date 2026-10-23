# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
"""Verify authorized removals, retained bindings and centralized routes alongside runtime checks."""
from pathlib import Path
import json,re,xml.etree.ElementTree as E
root=Path(__file__).resolve().parents[1]
results=[]
def check(name,passed,evidence=None):results.append(dict(name=name,passed=passed,evidence=evidence))
def text(name):return (root/name).read_text(encoding='utf-8-sig')
registry=text('Froststrap/UI/Utility/NavigationRegistry.cs')
check('removed pages have no route factories', all(('new("'+tag+'",') not in registry for tag in ('integrations','appearance','channels','regionselector','fastflageditor')))
check('curated tab is named Game Tweaks','new("fastflags", "Game Tweaks"' in registry)
advanced=text('Froststrap/UI/Elements/Settings/Pages/ShortcutsPage.axaml')
check('Advanced Settings has no Studio settings or old shortcut controls',not any(key in advanced for key in ('StudioChannel','StudioVersionOverride','CreateShortcut','DeleteShortcut')))
old=E.fromstring(text('tools/References/ChannelPage.upstream.axaml'))
old_bindings=set()
for element in old.iter():
 for value in element.attrib.values():
  old_bindings.update(re.findall(r'\{Binding\s+([A-Za-z][\w.]*)',value))
required={'ForceRobloxReinstallation','UpdateRoblox','PlayerVersionOverrideEnabled','PlayerVersionOverrideHash','MaxThreadDownload','PlayerChannel','ChannelChangeMode'}
check('Roblox deployment bindings survive migration', all(name in advanced for name in required), sorted(required))
for p in (root/'Froststrap/UI/Elements').rglob('*.axaml'):E.parse(p)
check('all application XAML remains valid XML',True)
texture=text('Froststrap/UI/Elements/Settings/Pages/ModsPresetsPage.axaml')
check('one customization surface without old avatar or cursor-set editor',all(key not in texture for key in ('OldAvatarBackgroundTask','CustomCursorSets','AddCustomCursorSet')) and 'CommunityModsPage' in texture)
check('default handler registration is not used for explicit games','LaunchEntry.CreateStartInfo' in text('Froststrap/Services/GameLaunchService.cs'))
check('removed integration settings cannot be re-enabled from legacy JSON',all('[JsonIgnore] public bool '+key+' { get => false;' in text('Froststrap/Models/Persistable/Settings.cs') for key in ('EnableActivityTracking','AutoRejoin','ShowUsingFroststrapRPC','UseDiscordRichPresence','StudioRPC','EnableBetterMatchmaking')))
check('known automatic provider initialization removed','apis.rovalra.com' not in text('Froststrap/UI/ViewModels/Onboarding/Page4ViewModel.cs') and 'GetDatacentersAsync' not in text('Froststrap/UI/ViewModels/Settings/BehaviourViewModel.cs'))
check('Discord invitation is the requested destination','https://discord.gg/CZ3P4Rw9D4' in text('Froststrap/UI/Elements/Controls/DiscordButton.cs'))
check('retired shortcut setup step cannot be reached','typeof(Page5)' not in text('Froststrap/UI/Elements/Onboarding/MainWindow.axaml.cs') and not (root/'Froststrap/UI/Elements/Onboarding/Pages/Page5.axaml').exists())
check('upstream alert feed cannot display a CoffeeStrap update notice','AlertBar' not in text('Froststrap/UI/Elements/Settings/MainWindow.axaml') and 'App.RemoteData.Subscribe' not in text('Froststrap/UI/Elements/Settings/MainWindow.axaml.cs'))
check('Global Settings has no active route', 'new("globalsettings",' not in registry)
check('compact startup menu remains separate from Settings', ': Base.AvaloniaWindow' in text('Froststrap/UI/Elements/Dialogs/LaunchMenuDialog.axaml.cs') and ': Settings.MainWindow' not in text('Froststrap/UI/Elements/Dialogs/LaunchMenuDialog.axaml.cs'))
launcher=text('Froststrap/UI/Elements/Settings/Pages/BehaviourPage.axaml')
check('Cleaner and background updates controls are absent', 'Menu_Bootstrapper_CleanUp_Title' not in launcher and 'Menu_Behaviour_BackgroundUpdates_Title' not in launcher)
check('old cleaner no longer traverses or deletes files', 'File.Delete' not in text('Froststrap/Integrations/Cleaner.cs') and 'RobloxLogs' not in text('Froststrap/Integrations/Cleaner.cs'))
check('font and cursor customization share responsive stacked cards', 'Cursor &amp; shift lock' in texture and 'FontSelectionLabel' in texture and 'ResponsiveCardGrid' in texture)
check('community heading and Find a game are removed', 'Text="Community mods"' not in texture and 'Find a game' not in text('Froststrap/UI/Elements/Settings/Pages/HomePage.axaml'))
check('Prevent Override survives in saved graphics group', 'Prevent Override' in text('Froststrap/UI/Elements/Controls/NativeGraphicsSettings.axaml'))
p=root/'.build/verification/scope-verification.json';p.write_text(json.dumps(results,indent=2),encoding='utf-8')
fail=[item for item in results if not item['passed']]
print(f'{len(results)-len(fail)}/{len(results)} simplification scope checks passed.')
if fail:print(json.dumps(fail,indent=2));raise SystemExit(1)
