# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
"""One-time, span-preserving XAML migration. Keeps bindings and localized resources intact."""
import re
from pathlib import Path

root = Path(__file__).resolve().parents[1] / 'Froststrap/UI/Elements/Settings/Pages'
for path in root.rglob('*.axaml'):
    text = path.read_text(encoding='utf-8')
    if 'xmlns:controls=' not in text:
        continue
    nodes, stack = [], []
    for match in re.finditer(r'<!--.*?-->|<[^>]+>', text, re.S):
        tag = match.group()
        if tag.startswith(('<!--', '<?', '<!')):
            continue
        if tag.startswith('</'):
            if stack:
                node = stack.pop()
                node['end'] = match
        else:
            name = re.match(r'<([^\s/>]+)', tag)[1]
            node = dict(name=name, start=match, end=match, children=[], parent=stack[-1] if stack else None)
            nodes.append(node)
            if stack: stack[-1]['children'].append(node)
            if not tag.endswith('/>'): stack.append(node)
    edits = []
    for node in nodes:
        name, tag = node['name'], node['start'].group()
        if name == 'ScrollViewer':
            edits.append((node['start'].start(), node['start'].end(), tag.replace('<ScrollViewer', '<controls:SmoothScrollViewer', 1)))
            if node['end'] != node['start']:
                edits.append((node['end'].start(), node['end'].end(), '</controls:SmoothScrollViewer>'))
        if name == 'StackPanel' and 'Orientation="Horizontal"' not in tag:
            direct_options = sum(c['name'] == 'controls:OptionControl' for c in node['children'])
            if direct_options >= 2:
                start = tag.replace('<StackPanel', '<controls:SettingsCardPanel', 1)
                start = re.sub(r'\s+Spacing="[^"]*"', '', start)
                edits.append((node['start'].start(), node['start'].end(), start))
                edits.append((node['end'].start(), node['end'].end(), '</controls:SettingsCardPanel>'))
    if path.name == 'IntegrationsPage.axaml':
        # Card names already identify these features; preserve the custom editor's heading.
        for node in nodes:
            if node['parent'] and node['parent']['name'] == 'StackPanel' and 'CoffeePageMargin' in node['parent']['start'].group():
                body = text[node['start'].start():node['end'].end()]
                if (node['name'] in ('TextBlock', 'controls:MarkdownTextBlock', 'Separator')
                    and 'Menu_Integrations_Custom_' not in body):
                    edits.append((node['start'].start(), node['end'].end(), ''))
    if path.name == 'AppearancePage.axaml':
        for node in nodes:
            body = text[node['start'].start():node['end'].end()]
            if node['name'] in ('TextBlock', 'controls:MarkdownTextBlock') and (
                'Text="CoffeeStrap"' in body or 'Menu_Appearance_Froststrap_Description' in body):
                edits.append((node['start'].start(), node['end'].end(), ''))
    for start, end, replacement in sorted(edits, reverse=True):
        text = text[:start] + replacement + text[end:]
    if edits:
        text = re.sub(r'[ \t]+(?=\n)', '', text)
        path.write_text(text, encoding='utf-8', newline='\r\n')
        print(f'{path.name}: {len(edits)} localized edits')
