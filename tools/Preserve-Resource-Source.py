# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
"""Keep upstream byte layout and URLs while applying the current translated values."""
import re,subprocess,xml.etree.ElementTree as E
from pathlib import Path
root=Path(__file__).resolve().parents[1]
pattern=re.compile(r'<data\b.*?</data>',re.S)
url=re.compile(r'https?://[^\s<>\)\"]+')
def entries(text):return {re.search(r'name="([^"]+)"',m[0])[1]:m[0] for m in pattern.finditer(text)}
def val(block):return re.search(r'<value>(.*?)</value>',block,re.S)
for p in (root/'Froststrap/Resources').glob('Strings*.resx'):
 current=p.read_bytes().decode('utf-8');current_map=entries(current)
 old=subprocess.run(['git','show','HEAD:'+p.relative_to(root).as_posix()],cwd=root,check=True,capture_output=True).stdout.decode('utf-8')
 old_map=entries(old)
 def replace(m):
  block=m[0];key=re.search(r'name="([^"]+)"',block)[1]
  if key not in current_map:return block
  oldvalue=val(block);newvalue=val(current_map[key])
  if oldvalue is None or newvalue is None:return block
  inner=newvalue[1]
  old_urls=url.findall(oldvalue[1]);new_urls=url.findall(inner)
  if len(old_urls)==len(new_urls):
   for a,b in zip(old_urls,new_urls):
    if 'coffeestrap' in b.lower() and 'froststrap' in a.lower():inner=inner.replace(b,a)
  if E.fromstring('<value>'+oldvalue[1]+'</value>').text==E.fromstring('<value>'+inner+'</value>').text:return block
  return block[:oldvalue.start(1)]+inner+block[oldvalue.end(1):]
 result=pattern.sub(replace,old)
 extra=[v for k,v in current_map.items() if k not in old_map]
 if extra:result=result.replace('</root>','\n'.join(extra)+'\n</root>')
 p.write_bytes(result.encode('utf-8'))
print('Preserved original resource byte layout, keys, and upstream URLs.')
