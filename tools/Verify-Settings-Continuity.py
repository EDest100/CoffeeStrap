# SPDX-FileCopyrightText: 2026 CoffeeStrap contributors
# SPDX-License-Identifier: MPL-2.0
"""Compare settings controls and nonconstant bindings against the checked-out upstream baseline."""
import collections
import json
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

root = Path(__file__).resolve().parents[1]
results = []

def summarize(text):
    tree = ET.fromstring(text)
    controls = collections.Counter()
    bindings = collections.Counter()
    for element in tree.iter():
        name = element.tag.rsplit('}', 1)[-1]
        if name in ('OptionControl', 'ToggleSwitch', 'ComboBox', 'TextBox', 'NumericUpDown', 'CardExpander', 'Button', 'IconButton'):
            controls[name] += 1
        for value in element.attrib.values():
            if value.startswith('{Binding') and 'Source={x:Static resources:' not in value:
                bindings[' '.join(value.split())] += 1
    return controls, bindings

for path in (root / 'Froststrap/UI/Elements/Settings/Pages').rglob('*.axaml'):
    relative = path.relative_to(root).as_posix()
    baseline = subprocess.run(['git', 'show', 'HEAD:' + relative], cwd=root, check=True,
                              capture_output=True, encoding='utf-8').stdout
    old_controls, old_bindings = summarize(baseline)
    new_controls, new_bindings = summarize(path.read_text(encoding='utf-8'))
    missing_controls = dict(old_controls - new_controls)
    missing_bindings = dict(old_bindings - new_bindings)
    results.append(dict(file=relative, passed=not missing_controls and not missing_bindings,
                        controls=dict(new_controls), missing_controls=missing_controls, missing_bindings=missing_bindings))
destination = root / 'artifacts/previews/settings-continuity.json'
destination.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
failures = [result for result in results if not result['passed']]
if failures:
    print(json.dumps(failures, ensure_ascii=False, indent=2))
    raise SystemExit(1)
print(f'Preserved settings control counts and nonconstant bindings across {len(results)} page files.')
