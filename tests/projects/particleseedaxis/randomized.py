#!/usr/bin/env python3
"""Replay the broad oracle's six randomized shape scenarios without omitting geometry."""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import cache_artifact_path
spec = importlib.util.spec_from_file_location('particle_full_compare', ROOT.parent/'particletrigger/verify.py')
compare = importlib.util.module_from_spec(spec)
spec.loader.exec_module(compare)
NAMES = {'random_sphere', 'random_hemisphere', 'random_box', 'random_cone',
         'shape_cone_volume_loop', 'shape_donut_loop'}


def load(path):
    return {key: value for key, value in compare.load(path).items() if key[0] in NAMES}


def leaves(value, path=''):
    if isinstance(value, dict):
        for key, child in value.items():
            yield from leaves(child, path+'/'+key)
    elif isinstance(value, list):
        for index, child in enumerate(value):
            yield from leaves(child, path+'/'+str(index))
    else:
        yield path, value


def full_union(reference, baseline, candidate):
    before, after = compare.report(reference, baseline), compare.report(reference, candidate)
    b = {(tuple(f['key']), f['path']): f for f in before['failures']}
    a = {(tuple(f['key']), f['path']): f for f in after['failures']}
    raw = []
    for key in sorted(reference.keys() | baseline.keys() | candidate.keys()):
        r = dict(leaves(reference.get(key, 'MISSING')))
        l = dict(leaves(baseline.get(key, 'MISSING')))
        n = dict(leaves(candidate.get(key, 'MISSING')))
        for path in sorted(r.keys() | l.keys() | n.keys()):
            expected, old, new = r.get(path, 'MISSING'), l.get(path, 'MISSING'), n.get(path, 'MISSING')
            if old == new:
                continue
            color = '/startColor/' in path or '/color/' in path
            item = dict(key=key, path=path, expected=expected, baseline=old, candidate=new,
                        baseline_accepted=compare.particles.close(expected, old, color),
                        candidate_accepted=compare.particles.close(expected, new, color))
            if all(isinstance(v, (int, float)) and not isinstance(v, bool) for v in [expected, old, new]):
                item.update(baseline_error=abs(old-expected), candidate_error=abs(new-expected),
                            error_increase=abs(new-expected)-abs(old-expected))
            raw.append(item)
    return dict(baseline=before, candidate=after,
        union={name: [dict(key=key, path=path, baseline=b.get((key, path)), candidate=a.get((key, path)))
                      for key, path in sorted(keys)] for name, keys in [
                          ('resolved', b.keys()-a.keys()), ('retained', b.keys() & a.keys()), ('introduced', a.keys()-b.keys())]},
        changed_retained=[dict(baseline=b[key], candidate=a[key]) for key in sorted(b.keys() & a.keys()) if b[key] != a[key]],
        raw_delta=raw)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('mode', choices=['prepare', 'engine', 'verify'])
    p.add_argument('--unity-version', required=True)
    p.add_argument('--work', type=Path, required=True)
    p.add_argument('--cli', type=Path)
    p.add_argument('--runtime', type=Path)
    p.add_argument('--baseline', type=Path)
    a = p.parse_args()
    cache_artifact_path(a.work)
    work = a.work.resolve(); work.mkdir(parents=True, exist_ok=True)
    project = work/'project'
    if a.mode == 'prepare':
        for name in ['Assets', 'Packages', 'ProjectSettings']:
            shutil.copytree(ROOT.parent/'particles'/name, project/name)
        source = (project/'Assets/Scripts/ParticlesProbe.cs').read_text()
        # Preserve original configuration, stepping, and recording code verbatim.
        loop = source[source.index('        // Shapes with their direction modes'):source.index('        ps = Make("shape_arc_burst_spread"')]
        loop = loop.replace('            ("shape_cone_loop", ParticleSystemShapeType.Cone),\n', '').replace('            ("shape_circle_loop", ParticleSystemShapeType.Circle),\n', '').replace('            ("shape_edge_loop", ParticleSystemShapeType.SingleSidedEdge),\n', '')
        random = source[source.index('        // Shapes with random placement'):source.index('        // Emit, SetParticles')]
        start = source.index('    void Start()')
        end = source.index('    // ------------------------------------------------------------------ images', start)
        source = source[:start]+'''    void Start()
    {
        ParticleSystem ps;
'''+loop+random+'''        File.WriteAllText(Environment.GetEnvironmentVariable("PARTICLES_PROBE_OUT"), s_Out.ToString());
        Application.Quit();
    }

'''+source[end:]
        (project/'Assets/Scripts/ParticlesProbe.cs').write_text(source)
        (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+a.unity_version+'\n')
        (work/'inputs.json').write_text(json.dumps({str(f.relative_to(project)): hashlib.sha256(f.read_bytes()).hexdigest()
            for name in ['Assets', 'Packages', 'ProjectSettings'] for f in sorted((project/name).rglob('*')) if f.is_file()}, indent=2)+'\n')
    elif a.mode == 'engine':
        if not a.cli or not a.runtime: p.error('engine requires --cli and --runtime')
        env = dict(os.environ, UNBOUND_CACHE=str(work/'cache'), UNBOUND_MANAGED_DIR=str(a.runtime.resolve()),
                   LD_LIBRARY_PATH=str(a.runtime.resolve()), PARTICLES_PROBE_OUT=str(work/'capture.jsonl'))
        command = [str(Path.home()/'.cache/compat/with-heavy-job'), 'timeout', '300', str(a.cli.resolve()),
                   'run', '-p', str(project), '--headless', '30', '--no-watch']
        (work/'command.json').write_text(json.dumps(command, indent=2)+'\n')
        with (work/'engine.log').open('x') as log:
            subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
    else:
        if not a.baseline: p.error('verify requires --baseline')
        ref = load(ROOT.parent/'particles/Reference'/('unity-'+a.unity_version+'.jsonl'))
        result = full_union(ref, load(a.baseline), load(work/'capture.jsonl'))
        (work/'full-union.json').write_text(json.dumps(result, indent=2)+'\n')
        print({name: len(values) for name, values in result['union'].items()})
        print('raw deltas:', len(result['raw_delta']))
        return int(bool(result['candidate']['failures']))
    return 0


if __name__ == '__main__':
    sys.exit(main())
