# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
import re,json,html
from pathlib import Path
root=Path(__file__).resolve().parents[1]
changes=[];exceptions=[]
brand=re.compile(r'froststrap|frostrap|\u0424\u0440\u043e\u0441\u0442\u0441\u0442\u0440\u0430\u043f|\u0641\u0631\u0648\u0633\u062a\u0631\u0627\u0628',re.I)
for p in (root/'Froststrap/Resources').glob('Strings*.resx'):
 text=p.read_bytes().decode('utf-8')
 def data(m):
  block=m[0];key=re.search(r'name="([^"]+)"',block)[1]
  def value(v):
   content=v[1]
   if not brand.search(content):return v[0]
   if key.startswith('Menu.About.'):
    exceptions.append(dict(file=p.name,key=key,reason='upstream attribution'))
    return v[0]
   urls=[]
   def protect(u):urls.append(u[0]);return f'__URL_{len(urls)-1}__'
   protected=re.sub(r'https?://[^\s<>\)\"]+',protect,content)
   updated=brand.sub('CoffeeStrap',protected)
   for i,url in enumerate(urls):updated=updated.replace(f'__URL_{i}__',url)
   if updated!=content:changes.append(dict(file=p.name,key=key))
   if brand.search(updated):exceptions.append(dict(file=p.name,key=key,reason='upstream URL retained'))
   return '<value>'+updated+'</value>'
  return re.sub(r'<value>(.*?)</value>',value,block,flags=re.S)
 text=re.sub(r'<data\b.*?</data>',data,text,flags=re.S)
 p.write_bytes(text.encode('utf-8'))
# Caption resources use normal resource fallback for languages without these new keys.
p=root/'Froststrap/Resources/Strings.resx';text=p.read_text(encoding='utf-8')
for key,value in {'Coffee.Window.Minimize':'Minimize','Coffee.Window.Maximize':'Maximize','Coffee.Window.Restore':'Restore down'}.items():
 if f'name="{key}"' not in text:text=text.replace('</root>',f'<data name="{key}" xml:space="preserve"><value>{value}</value></data>\n</root>')
p.write_text(text,encoding='utf-8',newline='\n')
p=root/'Froststrap/Resources/Strings.Designer.cs';text=p.read_text(encoding='utf-8')
for key in ('Coffee.Window.Minimize','Coffee.Window.Maximize','Coffee.Window.Restore'):
 name=key.replace('.','_')
 if 'string '+name not in text:
  i=text.rfind('    }')
  text=text[:i]+f'        public static string {name} => ResourceManager.GetString("{key}", resourceCulture);\n'+text[i:]
p.write_text(text,encoding='utf-8',newline='\n')
report=root/'artifacts/revision3/branding-audit.json';report.parent.mkdir(parents=True,exist_ok=True)
report.write_text(json.dumps(dict(changes=changes,exceptions=exceptions),indent=2),encoding='utf-8')
print(f'Renamed {len(changes)} localized values; retained {len(exceptions)} attribution/URL references.')
