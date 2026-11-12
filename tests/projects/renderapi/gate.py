#!/usr/bin/env python3
"""Run the engine and require the full API and frame comparison."""
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
if raw['failures']:
    raise SystemExit('strict failures: '+json.dumps(raw['failures'],sort_keys=True))
doctor = subprocess.run([str(Path.home()/'.cache/compat/with-heavy-job'),'--kind','engine-cpu',str(Path(a.cli).resolve()),
    'doctor','-p',str(out.parent/'projects'/'engine'),'--json'],capture_output=True,text=True)
if doctor.returncode:
    raise SystemExit('doctor failed: '+doctor.stderr)
(out/'doctor.json').write_text(doctor.stdout)
limitations = json.loads(doctor.stdout).get('renderingLimitations',[])
if any('GPU mesh editing' in s or 'non-finite PixelPerfectRendering' in s or 'active Cloth' in s for s in limitations):
    raise SystemExit('obsolete renderer limitation in doctor')
if not any('DrawIcon' in s and 'DrawGUITexture' in s and 'DrawFrustum' in s for s in limitations):
    raise SystemExit('missing remaining renderer limitations in doctor')
print(f"renderapi gate: {raw['keys']}/{raw['keys']} match; full report {report}")
