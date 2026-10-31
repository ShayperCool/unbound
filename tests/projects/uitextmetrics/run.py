#!/usr/bin/env python3
"""Stage the UI fixture in cache, add measurement observations, and wrap the heavy child."""
import argparse, hashlib, json, os, shutil, subprocess, sys
from pathlib import Path
ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import cache_artifact_path
p = argparse.ArgumentParser(description=__doc__)
p.add_argument('mode', choices=['native', 'engine'])
p.add_argument('--release', required=True, choices=['6000.0.71f1'])
p.add_argument('--output', type=Path, required=True)
p.add_argument('--cli', type=Path)
p.add_argument('--managed', type=Path)
a = p.parse_args()
output = cache_artifact_path(a.output.expanduser().resolve())
if output.exists(): p.error('output must be fresh')
output.mkdir(parents=True)
project = output/'project'
for name in ['Assets', 'Packages', 'ProjectSettings']:
    shutil.copytree(ROOT.parent/'uitk'/name, project/name)
shutil.copy2(ROOT/'Assets/Scripts/TextMetricsProbe.cs', project/'Assets/Scripts/TextMetricsProbe.cs')
source = project/'Assets/Scripts/UitkProbe.cs'
s = source.read_text()
for phase in ['initial', 'final', 'scaled']:
    old = f'Render("{phase}");'
    assert s.count(old) == 1
    s = s.replace(old, old + f'\n        TextMetricsProbe.Observe("{phase}", document);')
old = 'File.WriteAllText(output, m_Out.ToString());'
assert s.count(old) == 1
source.write_text(s.replace(old, 'TextMetricsProbe.Run(document);\n            ' + old))
env = dict(os.environ, UITK_PROBE_OUT=str(output/'capture/record.jsonl'), UITEXTMETRICS_OUT=str(output/'capture'))
env.pop('WT', None)
if a.mode == 'native':
    command = ['xvfb-run', '-a', 'vglrun', '-d', 'egl', str(Path.home()/f'Unity/Hub/Editor/{a.release}/Editor/Unity'), '-batchmode', '-projectPath', str(project), '-executeMethod', 'ProbeRunner.Record', '-logFile', str(output/'editor.log')]
else:
    if not a.cli or not a.managed: p.error('engine requires cli and managed')
    env['UNBOUND_MANAGED_DIR'] = str(a.managed.resolve())
    env['LD_LIBRARY_PATH'] = str(a.managed.resolve())
    command = [str(a.cli.resolve()), 'run', '-p', str(project), '--frames', '35', '--size', '960x540', '--no-watch']
hashes = {str(x.resolve()): hashlib.sha256(x.read_bytes()).hexdigest() for x in [a.cli, a.managed/'UnityEngine.CoreModule.dll', a.managed/'libunbound_physx.so']} if a.mode == 'engine' else {}
(output/'execution.json').write_text(json.dumps(dict(command=command, sha256=hashes, release=a.release), indent=2)+'\n')
with (output/'process.log').open('w') as log:
    subprocess.run([str(Path.home()/'.cache/compat/with-heavy-job'), 'timeout', '600', *command], env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
for path, digest in hashes.items():
    assert hashlib.sha256(Path(path).read_bytes()).hexdigest() == digest, path
assert (output/'capture/metrics.jsonl').is_file(), 'missing capture; inspect editor/process log'
