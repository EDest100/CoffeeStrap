# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
"""Revision 0.3 migration, preserving settings bindings and resource keys."""
import re
from pathlib import Path
root = Path(__file__).resolve().parents[1]
p = root / 'Froststrap/UI/Elements/Settings/MainWindow.axaml'
s = p.read_text(encoding='utf-8')
breadcrumbs = re.search(r'<ItemsControl\s+IsVisible="\{Binding HasBreadcrumbs\}".*?</ItemsControl>', s, re.S)[0].replace('FontSize="24"', 'FontSize="13"')
transition = re.search(r'<TransitioningContentControl\s+x:Name="PageContentControl".*?</TransitioningContentControl>', s, re.S)[0]
alert = re.search(r'<ui:FAInfoBar\s+x:Name="AlertBar".*?/>', s, re.S)[0]
topbar = '<controls:TitleBar Grid.Row="0" Height="52" Title="CoffeeStrap" Icon="avares://CoffeeStrap/CoffeeStrap.png"><controls:TitleBar.CenterContent>'+breadcrumbs+'</controls:TitleBar.CenterContent></controls:TitleBar>'
s = re.sub(r'<Grid\s+Grid.Row="0"\s+Height="36".*?</Grid>', topbar, s, count=1, flags=re.S)
s = s.replace('RowDefinitions="Auto,*,64"', 'RowDefinitions="52,*,74"')
s = re.sub(r'<ui:FANavigationView.Content>.*?</ui:FANavigationView.Content>', '<controls:CoffeeNavigationView.Content><Grid RowDefinitions="Auto,*">'+alert+'<Panel Grid.Row="1">'+transition+'</Panel></Grid></controls:CoffeeNavigationView.Content>', s, flags=re.S)
s = s.replace('controls:HoverNavigationView', 'controls:CoffeeNavigationView').replace('ui:FANavigationView.', 'controls:CoffeeNavigationView.').replace('ui:FANavigationViewItem.Icon', 'controls:CoffeeNavigationItem.Icon')
s = s.replace('<ui:FANavigationViewItemSeparator />', '')
s = s.replace('ui:FANavigationViewItem', 'controls:CoffeeNavigationItem')
for name in ('IsBackButtonVisible','IsSettingsVisible','IsPaneToggleButtonVisible','PaneDisplayMode'):
    s = re.sub(r'\s+'+name+r'="[^"]*"', '', s)
s = s.replace('CompactPaneLength="52"', 'CompactPaneLength="68"').replace('Height="64"', 'Height="74"').replace('BorderThickness="0,1,0,0"','BorderThickness="0"')
p.write_text(s, encoding='utf-8', newline='\r\n')
p = root / 'Froststrap/UI/Elements/Settings/MainWindow.axaml.cs'
s = p.read_text(encoding='utf-8').replace('FANavigationViewItemInvokedEventArgs','CoffeeNavigationInvokedEventArgs').replace('FANavigationViewItem','CoffeeNavigationItem').replace('FindControl<FANavigationView>', 'FindControl<CoffeeNavigationView>')
s = s.replace('            InitializeComponent();','            InitializeComponent();\n            WindowDecorations = WindowDecorations.BorderOnly;\n            ExtendClientAreaToDecorationsHint = true;\n            ExtendClientAreaTitleBarHeightHint = 52;')
p.write_text(s, encoding='utf-8', newline='\r\n')
# Group whole, existing option elements without reconstructing their binding expressions.
p = root / 'Froststrap/UI/Elements/Settings/Pages/IntegrationsPage.axaml'
s = p.read_text(encoding='utf-8')
stack, nodes = [], []
for m in re.finditer(r'<!--.*?-->|<[^>]+>',s,re.S):
    t=m[0]
    if t.startswith(('<!--','<?','<!')):continue
    if t.startswith('</'):
        if stack: stack.pop()['end']=m.end()
    else:
        n=dict(name=re.match(r'<([^\s/>]+)',t)[1],start=m.start(),end=m.end(),parent=stack[-1] if stack else None)
        nodes.append(n)
        if not t.endswith('/>'):stack.append(n)
panel=next(n for n in nodes if n['name']=='controls:SettingsCardPanel')
children=[n for n in nodes if n['parent'] is panel]
def body(n):return s[n['start']:n['end']]
def group(master, dependents):
    header=body(master).replace('<controls:OptionControl','<controls:OptionControl Classes="master"',1)
    return '<controls:SettingsGroup><controls:SettingsGroup.Master>'+header+'</controls:SettingsGroup.Master><controls:SettingsCardPanel>'+''.join(body(n) for n in dependents)+'</controls:SettingsCardPanel></controls:SettingsGroup>'
activity=next(i for i,n in enumerate(children) if 'x:Name="ActivityTrackingOption"' in body(n))
discord=next(i for i,n in enumerate(children) if 'x:Name="DiscordActivityOption"' in body(n))
studio=next(i for i,n in enumerate(children) if 'x:Name="StudioActivityOption"' in body(n))
custom=next(i for i,n in enumerate(children) if n['name']=='TextBlock' and 'Menu_Integrations_Custom_Title' in body(n))
edits=[(children[activity]['start'],children[activity+6]['end'],group(children[activity],children[activity+1:activity+7])),
       (children[discord]['start'],children[studio-1]['end'],group(children[discord],children[discord+1:studio])),
       (children[studio]['start'],children[custom-1]['end'],group(children[studio],children[studio+1:custom]))]
for a,b,value in sorted(edits,reverse=True):s=s[:a]+value+s[b:]
p.write_text(s,encoding='utf-8',newline='\r\n')
print('Reworked shell and grouped existing master/dependent cards.')
