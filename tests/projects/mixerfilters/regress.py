#!/usr/bin/env python3
"""Private audio-import/physics/graphics gates with complete Native and before/after unions."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import json
import math
import os
from pathlib import Path
import shutil
import subprocess

MISSING = '<missing>'

def union(a, b, tolerance, path=()):
    failures = []
    tested = 0
    if isinstance(a, dict) and isinstance(b, dict):
        for key in sorted(a.keys() | b.keys()):
            n, f = union(a.get(key, MISSING), b.get(key, MISSING), tolerance, path+(key,))
            tested += n; failures.extend(f)
    elif isinstance(a, list) and isinstance(b, list):
        for i in range(max(len(a), len(b))):
            n, f = union(a[i] if i < len(a) else MISSING, b[i] if i < len(b) else MISSING, tolerance, path+(i,))
            tested += n; failures.extend(f)
    elif a == MISSING and isinstance(b, (dict, list)):
        n, f = union({} if isinstance(b, dict) else [], b, tolerance, path)
        tested += n; failures.extend(f)
    elif b == MISSING and isinstance(a, (dict, list)):
        n, f = union(a, {} if isinstance(a, dict) else [], tolerance, path)
        tested += n; failures.extend(f)
    else:
        tested = 1
        numeric = isinstance(a, (int, float)) and isinstance(b, (int, float)) and not isinstance(a, bool) and not isinstance(b, bool)
        ok = abs(a-b) <= tolerance(path, a) if numeric and math.isfinite(a) and math.isfinite(b) else a == b
        if not ok: failures.append(dict(path=list(path), reference=a, candidate=b))
    return tested, failures


def read(kind, path):
    if kind == 'bundleaudio':
        return {r['case']+':'+r.get('name',''): r for r in map(json.loads, path.read_text().splitlines())}
    data = json.loads(path.read_text())
    return {r['name']: r for r in data['cases']} if kind == 'graphics' else data


def report(kind, reference, candidate, out):
    def tolerance(path, value):
        if kind == 'physics': return 1e-3 * max(1, abs(value))
        if kind == 'bundleaudio': return max(1e-4, (1e-3 if path[-1] == 'energy' else 1e-5) * abs(value))
        if 'floats' in path: return 1e-4
        if 'pixels' in path:
            pixels = reference[path[0]].get('pixels', [])
            return 1e-3 if any(v < 0 or v > 1 for pixel in pixels for v in pixel) else 2/255 + 1e-4
        return 0
    tested, failures = union(reference, candidate, tolerance)
    out.write_text(json.dumps(dict(tested=tested, failed=len(failures), failures=failures), indent=2)+'\n')
    print(f'{out.name}: {tested} values, {len(failures)} failures', flush=True)
    return {tuple(f['path']) for f in failures}


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--release', required=True)
    p.add_argument('--before', type=Path, required=True)
    p.add_argument('--after', type=Path, required=True)
    p.add_argument('--work', type=Path, required=True)
    p.add_argument('--reuse-before', type=Path, help='Existing immutable before gate directory')
    a = p.parse_args()
    a.work.mkdir(parents=True, exist_ok=False)
    repo = Path(__file__).resolve().parents[3]
    launcher = Path.home()/'.cache/compat/with-heavy-job'
    commands = []
    def run(item):
        kind, label, runtime = item
        root = a.work/(kind+'-'+label); root.mkdir()
        fixture = repo/'tests/projects'/kind
        assert (fixture/'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0] == 'm_EditorVersion: '+a.release
        project = root/'project'
        for name in ('Assets','Packages','ProjectSettings'):
            shutil.copytree(fixture/name, project/name)
        if kind == 'bundleaudio': shutil.copytree(fixture/'Reference/bundles', project/'Reference/bundles')
        output = root/('audio.jsonl' if kind == 'bundleaudio' else kind+'.json')
        variable = {'bundleaudio':'AUDIO_PROBE_OUT','physics':'PHYSICS_PROBE_OUT','graphics':'GRAPHICS_PROBE_OUT'}[kind]
        env = dict(os.environ, UNBOUND_MANAGED_DIR=str(runtime/'managed'), LD_LIBRARY_PATH=str(runtime/'managed'))
        env[variable] = str(root) if kind == 'bundleaudio' else str(output)
        command = [str(launcher),'timeout','600',str(runtime/'unbound'),'run','-p',str(project),'--scene','Assets/Scenes/Probe.unity','--no-watch','--fixed-fps','60']
        command += ['--frames','40','--size','960x540'] if kind == 'graphics' else ['--headless','600']
        (root/'command.json').write_text(json.dumps(command,indent=2)+'\n')
        with (root/'engine.log').open('w') as log: subprocess.run(command,env=env,stdout=log,stderr=subprocess.STDOUT,check=True)
        if not output.exists(): raise RuntimeError('No capture: '+str(root))
        return kind, label, read(kind, output)
    captures = {}
    if a.reuse_before:
        for kind in ('bundleaudio','physics','graphics'):
            path = a.reuse_before/(kind+'-before')/('audio.jsonl' if kind == 'bundleaudio' else kind+'.json')
            captures[kind,'before'] = read(kind,path)
    jobs = [(kind,label,runtime) for kind in ('bundleaudio','physics','graphics') for label,runtime in (('before',a.before),('after',a.after)) if not (a.reuse_before and label == 'before')]
    with ThreadPoolExecutor(max_workers=2) as pool: captures.update({(kind,label):data for kind,label,data in pool.map(run,jobs)})
    summaries = {}
    for kind in ('bundleaudio','physics','graphics'):
        fixture = repo/'tests/projects'/kind
        reference = fixture/'Reference'/('audio.jsonl' if kind == 'bundleaudio' else 'unity-'+a.release+'.json')
        native = read(kind, reference)
        before = report(kind, native, captures[kind,'before'], a.work/(kind+'-before-native.json'))
        after = report(kind, native, captures[kind,'after'], a.work/(kind+'-after-native.json'))
        report(kind, captures[kind,'before'], captures[kind,'after'], a.work/(kind+'-before-after.json'))
        summaries[kind] = dict(introduced=[list(p) for p in sorted(after-before,key=str)], retained=[list(p) for p in sorted(before&after,key=str)], resolved=[list(p) for p in sorted(before-after,key=str)])
    (a.work/'unions.json').write_text(json.dumps(summaries,indent=2)+'\n')

if __name__ == '__main__': main()
