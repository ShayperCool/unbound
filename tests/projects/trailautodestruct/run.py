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
    (project/'Assets/Resources').mkdir()
    shutil.copy2(fixture/'Assets/Resources/LineColor.shader', project/'Assets/Resources/LineColor.shader')
    # The Editor generates missing metadata; source-only Resources lookup needs it delivered.
    shutil.copy2(fixture/'Assets/Resources/LineColor.shader.meta', project/'Assets/Resources/LineColor.shader.meta')
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
    assert {x['step'] for x in rows if x.get('phase') == 'Update' and x.get('kind') == 'state'} == set(range(50))
    for step in range(49):
        for phase in ('Update', 'LateUpdate', 'before.PlayerSendFrameStarted', 'after.PlayerSendFrameStarted',
                      'before.UpdateAllRenderers', 'after.UpdateAllRenderers', 'before.Camera.Render',
                      'after.Camera.Render', 'before.PlayerSendFrameComplete', 'after.PlayerSendFrameComplete',
                      'before.TriggerEndOfFrameCallbacks', 'after.TriggerEndOfFrameCallbacks'):
            assert len([x for x in rows if x.get('kind') == 'state' and x.get('step') == step and x.get('phase') == phase]) == 6, ('missing phase', step, phase)
    return rows

def exercised(out):
    rows = complete(out)
    for name in ('auto.short', 'auto.long'):
        assert any(x.get('name') == name and x.get('kind') == 'state' and x.get('count', 0) > 1 for x in rows), ('trail never populated', name)
        assert any(x.get('name') == name and x.get('kind') == 'state' and x.get('alive') and x.get('count') == 0 and x.get('step', -1) > 2 for x in rows), ('expiry not exercised', name)
        assert any(x.get('name') == name and x.get('kind') == 'OnDestroy' for x in rows), ('automatic destruction not exercised', name)
    for name in ('auto.short', 'auto.long', 'passive', 'manual.update', 'manual.late', 'manual.eof'):
        assert any(x.get('name') == name and x.get('kind') == 'rendered' and x.get('pixels', 0) > 0 for x in rows), ('trail not rendered', name)
    assert any(x.get('name') == 'passive' and x.get('alive') and x.get('count') == 0 and x.get('step', -1) > 2 for x in rows), 'passive trail never expired'
    for name in ('manual.update', 'manual.late', 'manual.eof'):
        assert sorted(x['kind'] for x in rows if x.get('name') == name and x.get('kind') not in ('state', 'rendered')) == sorted(['request', 'OnDisable', 'returned', 'OnDestroy']), ('manual observations incomplete', name)

if a.mode == 'native':
    jobs = []
    for repeat in ('native1', 'native2'):
        out, project = prepare(repeat)
        jobs.append(EditorJob(project, out/'editor.log', out, args=['-executeMethod', 'ProbeRunner.Record'],
            batch=True, graphics=True, env={'TRAIL_AUTODESTRUCT_OUT': str(out/'events.jsonl')},
            timeout=600, progress_files=[out/'events.jsonl'], progress_timeout=300))
    results = run_parallel(jobs)
    receipt = {'complete': False, 'repeats': [], 'problems': []}
    for out, result in zip((work/'native1', work/'native2'), results):
        item = {'name': out.name, 'result': result}
        try:
            assert not result_problem(out, gpu=True) and not result.get('forced_exit'), result
            item.update(sha256=sha(out/'events.jsonl'), rows=len(complete(out)))
            exercised(out)
            item['exercised'] = True
        except (AssertionError, OSError, ValueError) as error:
            item['problem'] = str(error)
            receipt['problems'].append({'name': out.name, 'problem': str(error)})
        receipt['repeats'].append(item)
    if not receipt['problems'] and (work/'native1/events.jsonl').read_bytes() != (work/'native2/events.jsonl').read_bytes():
        receipt['problems'].append({'problem': 'independent repeats differ'})
    receipt['complete'] = not receipt['problems']
    write(work/'pair.json', receipt)
    assert receipt['complete'], receipt['problems']
elif a.mode == 'publish':
    for repeat in ('native1', 'native2'):
        assert not result_problem(work/repeat, gpu=True)
        assert not json.loads((work/repeat/'editor-run.json').read_text()).get('forced_exit')
        exercised(work/repeat)
    assert (work/'native1/events.jsonl').read_bytes() == (work/'native2/events.jsonl').read_bytes()
    ref = ROOT/'Reference'/('unity-'+a.release+'.jsonl')
    ref.parent.mkdir(exist_ok=True)
    with ref.open('xb') as f: f.write((work/'native1/events.jsonl').read_bytes())
else:
    assert a.cli and a.runtime
    out, project = prepare('engine')
    cmd = [str(Path.home()/'.cache/compat/with-heavy-job'), 'timeout', '--kill-after=15', '180', str(a.cli.resolve()),
        'run', '-p', str(project), '--frames', '100', '--size', '128x128', '--no-watch']
    write(out/'command.json', {'command': cmd, 'runtime': {str(f.relative_to(a.runtime)): sha(f) for f in a.runtime.rglob('*') if f.is_file()}, 'cli': sha(a.cli)})
    with (out/'stdout.log').open('x') as log:
        result = subprocess.run(cmd, env=dict(os.environ, TRAIL_AUTODESTRUCT_OUT=str(out/'events.jsonl'),
            UNBOUND_MANAGED_DIR=str(a.runtime.resolve()), LD_LIBRARY_PATH=str(a.runtime.resolve())), stdout=log, stderr=subprocess.STDOUT)
    write(out/'exit.json', {'exit': result.returncode})
    assert result.returncode == 0
    complete(out)
print(work)
