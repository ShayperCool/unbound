#!/usr/bin/env python3
"""Record callback thread context and retain the complete real-device reset buffers."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

HERE = Path(__file__).resolve().parent
FIXTURE = HERE.parents[1]
REPO = FIXTURE.parents[2]
sys.path[:0] = [str(REPO/'tests'), str(FIXTURE)]
from editor_runner import EditorJob, run, summary, result_problem
from delivery import verify
from pack import pack

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('mode', choices=('native', 'engine'))
p.add_argument('--release', required=True)
p.add_argument('--work', type=Path, required=True)
p.add_argument('--project', type=Path)
p.add_argument('--runtime', type=Path)
a = p.parse_args()
if a.release != '6000.0.71f1': p.error('defined for 6000.0.71f1')
a.work = a.work.resolve(); a.work.mkdir(parents=True, exist_ok=False)
project = a.work/'project'
source = (FIXTURE/'Controls/ResetDeliveryProbe.cs').read_text()
source = source.replace('    public readonly Blocks Listener', '    public static Action<string> BeforeExit;\n    public readonly Blocks Listener')
source = source.replace('#if UNITY_EDITOR\n        UnityEditor.EditorApplication.Exit(0);',
                        '        BeforeExit?.Invoke(output);\n#if UNITY_EDITOR\n        UnityEditor.EditorApplication.Exit(0);')
inputs = {'Scripts/ResetDeliveryProbe.cs': source,
          'Scripts/ResetDeliveryThreads.cs': (HERE/'ResetDeliveryThreads.cs').read_text(),
          'Editor/ResetDeliveryThreadRunner.cs': (HERE/'ResetDeliveryThreadRunner.cs').read_text()}
env = dict(os.environ, SUSPEND_PROBE_RATE='48000', SUSPEND_PROBE_OUT=str(a.work/'capture'),
           SUSPEND_THREAD_OUT=str(a.work/'threads.json'))
if a.mode == 'native':
    for part in ('Assets', 'Packages', 'ProjectSettings'): shutil.copytree(FIXTURE/part, project/part)
    for name, text in inputs.items():
        (project/'Assets'/name).parent.mkdir(parents=True, exist_ok=True)
        (project/'Assets'/name).write_text(text)
    result = run(EditorJob(project, a.work/'editor.log', a.work/'editor', batch=True,
                           args=['-executeMethod', 'ResetDeliveryThreadRunner.Record'], env=env,
                           editor=Path.home()/'Unity/Hub/Editor'/a.release/'Editor/Unity', timeout=600))
    print(summary(result), flush=True)
    problem = result_problem(a.work/'editor', gpu=True)
    if problem: raise SystemExit(problem)
else:
    for name, text in inputs.items():
        if (a.project/'Assets'/name).read_text() != text: raise SystemExit('diagnostic sources changed; record again')
    for part in ('Assets', 'Packages', 'ProjectSettings'): shutil.copytree(a.project/part, project/part)
    env.update(UNBOUND_MANAGED_DIR=str(a.runtime/'managed'), LD_LIBRARY_PATH=str(a.runtime/'managed'),
               UNBOUND_CACHE=str(a.work/'cache'))
    with (a.work/'engine.log').open('w') as log:
        subprocess.run([str(Path.home()/'.cache/compat/with-heavy-job'), '--kind', 'engine-cpu', 'timeout', '600',
                        str(a.runtime/'unbound'), 'run', '-p', str(project), '--scene', 'Assets/Scenes/ResetDeliveryThreads.unity',
                        '--headless', '30', '--no-watch', '--fixed-fps', '60'], env=env, stdout=log,
                       stderr=subprocess.STDOUT, check=True)
verify(a.work/'capture', a.release, 48000)
metadata = json.loads((a.work/'capture/capture.json').read_text())
threads = json.loads((a.work/'threads.json').read_text())
for key in ('source', 'listener'):
    assert len(threads[key]) == len(metadata[key]['callbacks']), key
    assert all(len(row) == 3 and row[1] > 0 and row[2] in (0, 1) for row in threads[key])
    # The documented audio-thread guarantee distinguishes the closing callback from main-thread Render.
    assert any(row[0] == 1 for row in threads[key]), key
    assert all(row[2] == 0 for row in threads[key] if row[0] == 1), key
metadata['threads'] = threads
(a.work/'capture/capture.json').write_text(json.dumps(metadata, separators=(',', ':'))+'\n')
pack(a.work/'capture')
print('Closing source/listener callbacks are off the main thread; complete PCM and callback rows retained.', flush=True)
