# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
import json,re,xml.etree.ElementTree as E
from pathlib import Path
root=Path(__file__).resolve().parents[1]
brand=re.compile(r'froststrap|frostrap|\u0424\u0440\u043e\u0441\u0442\u0441\u0442\u0440\u0430\u043f|\u0641\u0631\u0648\u0633\u062a\u0631\u0627\u0628',re.I)
urls=re.compile(r'https?://[^\s<>\)\"]+')
unexpected=[];retained=[];cultures=0
for p in (root/'Froststrap/Resources').glob('Strings*.resx'):
 cultures+=1
 for item in E.fromstring(p.read_bytes()).findall('data'):
  key=item.get('name');value=item.findtext('value') or ''
  if not brand.search(value):continue
  if key.startswith('Menu.About.'):
   retained.append(dict(file=p.name,key=key,reason='required upstream attribution'));continue
  if brand.search(urls.sub('',value)):unexpected.append(dict(file=p.name,key=key,value=value))
  else:retained.append(dict(file=p.name,key=key,reason='existing upstream URL'))
for p in (root/'Froststrap/UI').rglob('*.axaml'):
 for item in E.fromstring(p.read_bytes()).iter():
  for name,value in item.attrib.items():
   if name not in ('Text','Title','Header','Content','PlaceholderText','MarkdownText','ToolTip.Tip'):continue
   if value.startswith('{') or not brand.search(urls.sub('',value)):continue
   if '/About/' in p.as_posix():retained.append(dict(file=str(p.relative_to(root)),reason='source credits or licenses'))
   else:unexpected.append(dict(file=str(p.relative_to(root)),attribute=name,value=value))
for p in (root/'Froststrap/Enums').glob('*.cs'):
 for m in re.finditer(r'StaticName\s*=\s*"([^"]*)"',p.read_text(encoding='utf-8')):
  if brand.search(m[1]):unexpected.append(dict(file=str(p.relative_to(root)),value=m[1]))
for p in (root/'Froststrap').rglob('*.cs'):
 if any(part in ('bin','obj') for part in p.parts) or p.name=='Strings.Designer.cs':continue
 for number,line in enumerate(p.read_text(encoding='utf-8-sig').splitlines(),1):
  # Literal display properties, including native save/open pickers that static
  # XAML captures do not open. Namespaces, resource keys and service IDs are not labels.
  for value in re.findall(r'(?:Title|SuggestedFileName|PlaceholderText|LargeImageText|DisplayName)\s*=\s*\$?"([^"]*)"',line):
   if brand.search(urls.sub('',value)):unexpected.append(dict(file=str(p.relative_to(root)),line=number,value=value))
metadata=E.parse(root/'Froststrap/Froststrap.csproj').getroot()
for key in ('Company','Product','AssemblyName','Description','ApplicationIcon'):
 for item in metadata.iter(key):
  if brand.search(item.text or ''):unexpected.append(dict(file='Froststrap/Froststrap.csproj',key=key,value=item.text))
report=dict(cultures_checked=cultures,unexpected_visible_branding=unexpected,retained=retained,
 compatibility_exceptions=['Internal Froststrap namespaces, resource/serialization keys and legacy enum IDs remain for compatibility.',
 'Original source credits, license notices and upstream project links remain.',
 'Upstream service URLs, Roblox request compatibility identifiers and the legacy Discord application ID/hosted artwork remain. The Discord server-side application name cannot be renamed locally.'])
p=root/'.build/verification/branding-verification.json';p.parent.mkdir(parents=True,exist_ok=True)
p.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(f'Checked {cultures} resource cultures, visible XAML/enum/C# display labels and app metadata; unexpected brand strings: {len(unexpected)}.')
if unexpected:
 print(json.dumps(unexpected,ensure_ascii=True,indent=2));raise SystemExit(1)
