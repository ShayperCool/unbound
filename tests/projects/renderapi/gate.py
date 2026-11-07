#!/usr/bin/env python3
"""Run the engine and retain the strict comparison, with two named Editor-view residuals."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import uuid

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO/'tests'))
from probe_paths import run_root

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--release', required=True)
p.add_argument('--cli', default=os.environ.get('UNBOUND'))
a = p.parse_args()
if not a.cli: p.error('--cli or UNBOUND is required')
os.environ['UNBOUND_PROBE_UNITY_VERSION'] = a.release
label = 'gate-'+uuid.uuid4().hex[:12]
result = subprocess.run([sys.executable,str(PROBE/'run.py'),'engine','--release',a.release,'--label',label,'--cli',a.cli])
out = run_root(PROBE,label)/'engine'
report = out/'comparison.json'
if result.returncode not in (0,1) or not report.is_file():
    raise SystemExit(f'capture failed: {out}')
raw = json.loads(report.read_text())
# Unbound has no Game-view gizmo toggle or repaint callbacks. Keep both raw differences;
# require its unsupported setting to leave the frame black, and require the doctor limitation.
expected = {'gizmos.game.True.callback','gizmos.game.True.pixels'}
if set(raw['failures']) != expected:
    raise SystemExit('unexpected strict failures: '+json.dumps(raw['failures'],sort_keys=True))
spec = importlib.util.spec_from_file_location('comparison', PROBE.parent/'lines/compare.py')
comparison = importlib.util.module_from_spec(spec)
spec.loader.exec_module(comparison)
rows = comparison.load(out)
if rows['gizmos.game.True.callback'] is not False:
    raise SystemExit('unexpected callback')
if comparison.image(out,'gizmos.game.True') != comparison.image(out,'gizmos.game.False'):
    raise SystemExit('unsupported gizmos changed the frame')
doctor = subprocess.run([str(Path.home()/'.cache/compat/with-heavy-job'),str(Path(a.cli).resolve()),
    'doctor','-p',str(out.parent/'projects'/'engine'),'--json'],capture_output=True,text=True)
if doctor.returncode:
    raise SystemExit('doctor failed: '+doctor.stderr)
(out/'doctor.json').write_text(doctor.stdout)
limitations = json.loads(doctor.stdout).get('renderingLimitations',[])
if not any('gizmo' in s for s in limitations) or not any('GPU mesh editing' in s for s in limitations):
    raise SystemExit('missing renderer limitation in doctor')
print(f"renderapi gate: {raw['keys']-2}/{raw['keys']} match; 2 retained Game-view gizmo residuals; full report {report}")
