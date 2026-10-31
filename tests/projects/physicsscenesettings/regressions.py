#!/usr/bin/env python3
"""Run existing fixtures without writing to their tracked evidence or references."""
import os,pathlib,subprocess,sys
repo=pathlib.Path(__file__).resolve().parents[3]
work=pathlib.Path(os.environ['SCENE_WORK']); phase=sys.argv[1]
base=work/('regressions-'+phase); base.mkdir(exist_ok=True)
heavy=pathlib.Path.home()/'.cache/compat/with-heavy-job'
cli=pathlib.Path.home()/'.cache/compat/parity-baseline-native6/unbound' if phase=='baseline' else work/'cargo/release/unbound'
cases=[('physicsdefaults','PHYSICS_DEFAULTS_OUT',600,'jsonl'),('physics','PHYSICS_PROBE_OUT',600,'json'),('joints','JOINTS_PROBE_OUT',300000,''),('controller','CONTROLLER_PROBE_OUT',300000,''),('forces','FORCES_PROBE_OUT',30000,''),('rayhits','RAYHITS_PROBE_OUT',30000,'')]
for name,var,frames,ext in cases:
    dest=base/(name+('.'+ext if ext else '')); fixture=repo/'tests/projects'/name
    if not ext: dest.mkdir(exist_ok=True)
    env=dict(os.environ,**{var:str(dest)})
    with (base/(name+'.log')).open('w') as f:
        code=subprocess.run([str(heavy),'timeout','240',str(cli),'run','-p',str(fixture),'--headless',str(frames),'--no-watch'],env=env,stdout=f,stderr=subprocess.STDOUT).returncode
    ref=fixture/'Reference'/('unity-6000.0.71f1.'+ext) if ext else fixture/'Reference'
    with (base/(name+'-comparison.txt')).open('w') as f:
        compare=subprocess.run(['python3',str(fixture/'compare.py'),str(ref),str(dest)],stdout=f,stderr=subprocess.STDOUT).returncode
    print(name,'engine',code,'comparison',compare,flush=True)
    if code: raise SystemExit(code)
