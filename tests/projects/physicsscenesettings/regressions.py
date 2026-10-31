#!/usr/bin/env python3
"""Run existing fixtures without writing to their tracked evidence or references."""
from pathlib import Path
import os,pathlib,subprocess,sys
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_file, run_root

repo=pathlib.Path(__file__).resolve().parents[3]
ROOT=pathlib.Path(__file__).resolve().parent
work=pathlib.Path(os.environ.get('SCENE_WORK', run_root(ROOT, 'matrix'))); phase=sys.argv[1]
base=work/('regressions-'+phase); base.mkdir(parents=True,exist_ok=True)
heavy=pathlib.Path.home()/'.cache/compat/with-heavy-job'
cli=pathlib.Path.home()/'.cache/compat/parity-baseline-native6/unbound' if phase=='baseline' else pathlib.Path(os.environ.get('SCENE_SETTINGS_CLI',str(work/'cargo/release/unbound')))
cases=[('physicsdefaults','PHYSICS_DEFAULTS_OUT',600,'jsonl'),('physics','PHYSICS_PROBE_OUT',600,'json'),('joints','JOINTS_PROBE_OUT',300000,''),('controller','CONTROLLER_PROBE_OUT',300000,''),('forces','FORCES_PROBE_OUT',30000,''),('rayhits','RAYHITS_PROBE_OUT',30000,'')]
for name,var,frames,ext in cases:
    dest=base/(name+('.'+ext if ext else '')); fixture=repo/'tests/projects'/name
    if not ext: dest.mkdir(exist_ok=True)
    env=dict(os.environ,**{var:str(dest)})
    with (base/(name+'.log')).open('w') as f:
        code=subprocess.run([str(heavy),'timeout','240',str(cli),'run','-p',str(fixture),'--headless',str(frames),'--no-watch'],env=env,stdout=f,stderr=subprocess.STDOUT).returncode
    ref=reference_file(fixture, ext) if ext else fixture/'Reference'
    with (base/(name+'-comparison.txt')).open('w') as f:
        compare=subprocess.run(['python3',str(fixture/'compare.py'),str(ref),str(dest)],stdout=f,stderr=subprocess.STDOUT).returncode
    print(name,'engine',code,'comparison',compare,flush=True)
    if code: raise SystemExit(code)
