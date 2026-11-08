#!/usr/bin/env python3
"""Unclaimed complete unscaled snapshot clock and PCM experiment."""
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
for part in ('Assets', 'Packages', 'ProjectSettings'): shutil.copytree(FIXTURE/part, project/part)
src=project/'Assets/Scripts/DuckProbe.cs';s=src.read_text().replace('public static readonly string[][] DiagnosticCases = {','''public static readonly string[][] DiagnosticCases = {
        new[] { "timed_send_unscaled_half", "voice=step0:0.5", "change=Send Level", "to=-12", "snapshot=1", "duration=0.1", "scale=0.5", "unscaled=1" },
        new[] { "timed_volume_unscaled_half", "Music:Att", "Voice:Att", "change=Volume", "to=-18", "snapshot=1", "duration=0.1", "scale=0.5", "unscaled=1", "finalmix=1" },
        new[] { "timed_volume_unscaled_pause", "Music:Att", "Voice:Att", "change=Volume", "to=-18", "snapshot=1", "duration=0.1", "scale=0", "unscaled=1", "finalmix=1" },''').replace('if (timed && mixer.GetFloat', 'Debug.Log("Clock " + key + " " + block + " unscaled=" + Time.unscaledDeltaTime.ToString("R",System.Globalization.CultureInfo.InvariantCulture));\n                    if (timed && mixer.GetFloat')
src.write_text(s)
keys='timed_send_unscaled_pause,timed_threshold_unscaled_pause,timed_send_unscaled_half,timed_volume_unscaled_half,timed_volume_unscaled_pause'
result = run(EditorJob(project, a.work/'editor.log', a.work/'editor', batch=True,
                       args=['-executeMethod', 'ProbeRunner.Record'],
                       env={'DUCK_PROBE_OUT': str(a.work/'capture'), 'DUCK_PROBE_RATE': '48000', 'DUCK_PROBE_CASES': keys},
                       editor=Path.home()/'Unity/Hub/Editor'/a.release/'Editor/Unity', timeout=600))
print(summary(result), flush=True)
problem = result_problem(a.work/'editor', gpu=True)
if problem: raise SystemExit(problem)
if not (a.work/'capture/capture.json').is_file(): raise SystemExit('incomplete unscaled clock experiment')
