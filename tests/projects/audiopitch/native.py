#!/usr/bin/env python3
"""Create an independent private project and capture offline Unity Pitch Shifter audio."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess

p = argparse.ArgumentParser()
p.add_argument('--release', required=True)
p.add_argument('--work', type=Path, required=True)
p.add_argument('--editor', type=Path)
p.add_argument('--rate', type=int, choices=(44100, 48000), required=True)
p.add_argument('--buffer', type=int, choices=(256, 512, 1024, 2048), default=1024)
p.add_argument('--cases', help='Comma-separated subset, for diagnostics only')
a = p.parse_args()
fixture = Path(__file__).resolve().parent
if (fixture/'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0] != 'm_EditorVersion: '+a.release:
    p.error('release does not match fixture')
a.work.mkdir(parents=True, exist_ok=False)
project = a.work/'project'
shutil.copytree(fixture, project, ignore=shutil.ignore_patterns('Reference', 'Library', 'Logs', '__pycache__', '*.py', '*.md'))
output = a.work/'capture'
editor = a.editor or Path.home()/'Unity/Hub/Editor'/a.release/'Editor/Unity'
import sys
sys.path.insert(0, str(fixture.parents[1]))
from editor_runner import EditorJob, run
from common import cases, header
selected = a.cases or ','.join([key for key in cases() if not key.startswith('buffer_')] + cases(True))
job = EditorJob(project, a.work/'editor.log', a.work/'editor-run',
                args=['-executeMethod', 'ProbeRunner.Record'], batch=True, graphics=True,
                editor=editor, env=dict(EFFECT_PROBE_OUT=str(output), EFFECT_PROBE_RATE=str(a.rate),
                    EFFECT_PROBE_CASES=selected, EFFECT_PROBE_BUFFER=str(a.buffer)), timeout=900)
result = run(job)
if result['exit_code'] != 0: raise SystemExit('Editor capture failed: '+str(result))
if not a.cases:
    from common import split_contexts
    split_contexts(output)
    merged = header(output)
    for size in (256, 512, 2048):
        part = a.work/('buffer-'+str(size))
        subprocess.run([sys.executable, str(fixture/'native.py'), '--release', a.release,
                        '--rate', str(a.rate), '--work', str(part), '--buffer', str(size),
                        '--cases', 'buffer_'+str(size), '--editor', str(editor)], check=True)
        for key, value in header(part/'capture').items():
            if key in ('release', 'rate'):
                assert value == merged[key], key
                continue
            assert key not in merged, key
            merged[key] = value
            if isinstance(value, dict) and 'file' in value:
                shutil.copy2(part/'capture'/value['file'], output/value['file'])
    (output/'capture.json').write_text(json.dumps(merged, indent=2)+'\n')
    import sys
    sys.path.insert(0, str(fixture))
    from verify import verify
    verify(output, a.release, a.rate)
    verify(a.work/'contexts', a.release, a.rate, contexts=True)

else:
    from verify import verify
    verify(output, a.release, a.rate, selected=a.cases.split(','))
