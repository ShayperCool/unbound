#!/usr/bin/env python3
"""Compare a modules-probe run (run.py's result.json) with the release's Editor reference.

compile: the assemblies that compile and the compiler errors. The Editor's log holds the
compiler's raw message and the console's; where the two differ (CS1069 naming a disabled built-in
package) the console's is the one Unity reports and the one Unbound must report. Every missing
and extra error is listed.
play: every row the probe recorded, exactly.
"""
import argparse
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import reference_root

RAW_HINT = ' Consider adding a reference to that assembly.'


def console_errors(errors):
    by_place = {}
    for e in errors:
        by_place.setdefault((e['file'], e['line'], e['column'], e['id']), []).append(e['message'])
    out = set()
    for place, messages in by_place.items():
        rewritten = [m for m in messages if not m.endswith(RAW_HINT)]
        for m in (rewritten if rewritten and len(messages) > 1 else messages):
            out.add((*place, m))
    return out


p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
p.add_argument('result', type=Path)
a = p.parse_args()
ours = json.loads(a.result.read_text())
scenario = ours['scenario']
theirs = json.loads((reference_root(ROOT) / f'{scenario}.json').read_text())
failures = []
if scenario == 'compile':
    if ours['compiledAssemblies'] != theirs['compiledAssemblies']:
        failures.append(f"compiled assemblies: Unity {theirs['compiledAssemblies']}, Unbound {ours['compiledAssemblies']}")
    unity = console_errors(theirs['errors'])
    unbound = console_errors(ours['errors'])
    for e in sorted(unity - unbound):
        failures.append(f'missing error: {e}')
    for e in sorted(unbound - unity):
        failures.append(f'extra error: {e}')
    checked = len(unity) + 1
else:
    rows, expected = ours['rows'] or [], theirs['rows']
    for i in range(max(len(rows), len(expected))):
        u = expected[i] if i < len(expected) else None
        o = rows[i] if i < len(rows) else None
        if u != o:
            failures.append(f'row {i}: Unity {u}, Unbound {o}')
    checked = len(expected)
for f in failures:
    print(f)
print(f'{scenario}: {checked} values checked, {len(failures)} differences')
sys.exit(1 if failures else 0)
