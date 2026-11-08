#!/usr/bin/env python3
"""Unclaimed complete stream/decompressed clock and PCM experiment."""
import argparse
from pathlib import Path
import shutil
import sys

HERE = Path(__file__).resolve().parent
FIXTURE = HERE.parents[1]
REPO = FIXTURE.parents[2]
sys.path.insert(0, str(REPO/'tests'))
from editor_runner import EditorJob, run, summary, result_problem

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--release', required=True)
p.add_argument('--work', type=Path, required=True)
a = p.parse_args()
if a.release != '6000.0.71f1': p.error('this experiment is defined for 6000.0.71f1')
a.work = a.work.resolve(); a.work.mkdir(parents=True, exist_ok=False)
project = a.work/'project'
for part in ('Packages', 'ProjectSettings'): shutil.copytree(FIXTURE/part, project/part)
for name, part in [('StreamProbe.cs', 'Scripts'), ('StreamRunner.cs', 'Editor')]:
    (project/'Assets'/part).mkdir(parents=True, exist_ok=True)
    shutil.copy2(HERE/name, project/'Assets'/part/name)
result = run(EditorJob(project, a.work/'editor.log', a.work/'editor', batch=True,
                       args=['-executeMethod', 'StreamRunner.Record'], env={'Q158_OUT': str(a.work/'capture')},
                       editor=Path.home()/'Unity/Hub/Editor'/a.release/'Editor/Unity', timeout=600))
print(summary(result), flush=True)
problem = result_problem(a.work/'editor', gpu=True)
if problem: raise SystemExit(problem)
if not (a.work/'capture/capture.json').is_file(): raise SystemExit('incomplete stream clock experiment')
