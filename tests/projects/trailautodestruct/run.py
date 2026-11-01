#!/usr/bin/env python3
"""A bounded public control for trail expiry and manual destruction phases."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import cache_artifact_path
from editor_runner import EditorJob, run_parallel, result_problem

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('mode', choices=['native', 'engine', 'publish'])
p.add_argument('--release', required=True)
p.add_argument('--work', required=True, type=Path)
p.add_argument('--label', default='native-pair')
p.add_argument('--cli', type=Path)
p.add_argument('--runtime', type=Path)
a = p.parse_args()
cache_artifact_path(a.work)
assert Path(a.label).name == a.label and a.label not in ('.', '..')
work = a.work.resolve()/a.label
os.environ['UNBOUND_PROBE_UNITY_VERSION'] = a.release

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def write(path, data): path.write_text(json.dumps(data, indent=2)+'\n')
def prepare(label):
    out = work/label
    out.mkdir(parents=True, exist_ok=False)
    project = out/'project'
    fixture = ROOT.parent/'lines'
    shutil.copytree(fixture/'ProjectSettings', project/'ProjectSettings')
    shutil.copytree(fixture/'Assets/Scenes', project/'Assets/Scenes')
    (project/'Packages').mkdir()
    write(project/'Packages/manifest.json', {'dependencies': {}})
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+a.release+'\n')
    (project/'Assets/Scripts').mkdir()
    (project/'Assets/Editor').mkdir()
    shutil.copy2(ROOT/'Sources/TrailAutodestructProbe.cs', project/'Assets/Scripts/TrailAutodestructProbe.cs')
    shutil.copy2(ROOT/'Sources/ProbeRunner.cs', project/'Assets/Editor/ProbeRunner.cs')
    write(out/'inputs.json', {str(f.relative_to(project)): sha(f) for f in sorted(project.rglob('*')) if f.is_file()})
    return out, project

def complete(out):
    rows = [json.loads(x) for x in (out/'events.jsonl').read_text().splitlines()]
    assert rows[-1] == {'complete': True} and sum(x.get('complete', False) for x in rows) == 1
    assert {x['step'] for x in rows if x.get('phase') == 'Update' and x.get('kind') == 'state'} == set(range(15))
    return rows

def exercised(out):
    rows = complete(out)
    for name in ('auto.short', 'auto.long'):
        assert any(x.get('name') == name and x.get('kind') == 'state' and x.get('alive') and x.get('count') == 0 and x.get('step', -1) > 2 for x in rows), ('expiry not exercised', name)
        assert any(x.get('name') == name and x.get('kind') == 'OnDestroy' for x in rows), ('automatic destruction not exercised', name)

if a.mode == 'native':
    jobs = []
    for repeat in ('native1', 'native2'):
        out, project = prepare(repeat)
        jobs.append(EditorJob(project, out/'editor.log', out, args=['-executeMethod', 'ProbeRunner.Record'],
            batch=True, graphics=False, env={'TRAIL_AUTODESTRUCT_OUT': str(out/'events.jsonl')},
            timeout=600, progress_files=[out/'events.jsonl'], progress_timeout=300))
    results = run_parallel(jobs)
    for out, result in zip((work/'native1', work/'native2'), results):
        assert not result_problem(out) and not result.get('forced_exit'), result
        exercised(out)
    assert (work/'native1/events.jsonl').read_bytes() == (work/'native2/events.jsonl').read_bytes(), 'independent repeats differ'
    write(work/'pair.json', {'complete': True, 'sha256': sha(work/'native1/events.jsonl'), 'rows': len(complete(work/'native1'))})
elif a.mode == 'publish':
    for repeat in ('native1', 'native2'):
        assert not result_problem(work/repeat)
        exercised(work/repeat)
    assert (work/'native1/events.jsonl').read_bytes() == (work/'native2/events.jsonl').read_bytes()
    ref = ROOT/'Reference'/('unity-'+a.release+'.jsonl')
    ref.parent.mkdir(exist_ok=True)
    with ref.open('xb') as f: f.write((work/'native1/events.jsonl').read_bytes())
else:
    assert a.cli and a.runtime
    out, project = prepare('engine')
    cmd = [str(Path.home()/'.cache/compat/with-heavy-job'), 'timeout', '--kill-after=15', '180', str(a.cli.resolve()),
        'run', '-p', str(project), '--headless', '20', '--no-watch']
    write(out/'command.json', {'command': cmd, 'runtime': {str(f.relative_to(a.runtime)): sha(f) for f in a.runtime.rglob('*') if f.is_file()}, 'cli': sha(a.cli)})
    with (out/'stdout.log').open('x') as log:
        result = subprocess.run(cmd, env=dict(os.environ, TRAIL_AUTODESTRUCT_OUT=str(out/'events.jsonl'),
            UNBOUND_MANAGED_DIR=str(a.runtime.resolve()), LD_LIBRARY_PATH=str(a.runtime.resolve())), stdout=log, stderr=subprocess.STDOUT)
    write(out/'exit.json', {'exit': result.returncode})
    assert result.returncode == 0
    complete(out)
print(work)
