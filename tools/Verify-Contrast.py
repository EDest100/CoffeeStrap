# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
"""Check opaque semantic colors from the generated production palettes."""
import json
import xml.etree.ElementTree as ET
from pathlib import Path

root = Path(__file__).resolve().parents[1]
results = []
def luminance(value):
    rgb = [int(value[i:i+2], 16) / 255 for i in (1, 3, 5)]
    linear = [v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in rgb]
    return sum(v * w for v, w in zip(linear, (.2126, .7152, .0722)))

def check(name, foreground, background, minimum):
    a, b = sorted((luminance(foreground), luminance(background)))
    ratio = (b + .05) / (a + .05)
    results.append(dict(name=name, foreground=foreground, background=background,
                        ratio=round(ratio, 2), minimum=minimum, passed=ratio >= minimum))

for mode in ('Dark', 'Light'):
    document = ET.parse(root / f'Froststrap/UI/AppThemes/ResourceDictionarys/{mode}.axaml')
    palette = {item.attrib.get('{http://schemas.microsoft.com/winfx/2006/xaml}Key'):
               item.attrib.get('Color', item.text) for item in document.getroot()}
    for surface in ('ControlFillColorDefaultBrush', 'ControlFillColorSecondaryBrush',
                    'CoffeePageBrush', 'CoffeeGroupBrush', 'CoffeeChromeBrush',
                    'CoffeeNavigationHighlightBrush'):
        for text in ('TextFillColorPrimaryBrush', 'TextFillColorSecondaryBrush'):
            check(f'{mode} {text} / {surface}', palette[text], palette[surface], 4.5)
    check(f'{mode} accent text', palette['TextOnAccentFillColorPrimaryBrush'],
          palette['AccentFillColorDefaultBrush'], 4.5)
    for track in ('ToggleSwitchFillOn', 'ToggleSwitchFillOff', 'ToggleSwitchFillOnPointerOver'):
        check(f'{mode} thumb / {track}', '#FFF9F2', palette[track], 3)

output = root / '.build/verification/contrast-checks.json'
output.write_text(json.dumps(results, indent=2), encoding='utf-8')
failed = [result for result in results if not result['passed']]
print(f'{len(results) - len(failed)}/{len(results)} opaque palette contrast checks passed.')
if failed:
    print(json.dumps(failed, indent=2))
    raise SystemExit(1)
