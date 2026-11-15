#!/usr/bin/env python3
"""Sets the probe's Standalone Tier2 settings by hand once: runs ProbeRunner.Create in an Editor
on a copy of the project and copies the GraphicsSettings it saved back into the probe. The
output is committed; the gate never runs the Editor.

    python3 tests/projects/tiersettings/create.py --label <L>
"""
import argparse
from pathlib import Path
import shutil
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import editor_runner  # noqa: E402
from probe_paths import run_root  # noqa: E402
import value_probe  # noqa: E402


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--label', required=True)
    args = parser.parse_args()
    out = run_root(PROBE, 'create-'+args.label)
    project = value_probe.assemble(PROBE, out)
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-quit', '-executeMethod', 'ProbeRunner.Create'],
                                  batch=True, graphics=True, timeout=900, label=PROBE.name+'-create')
    print(editor_runner.summary(editor_runner.run(job)))
    problem = editor_runner.result_problem(out, gpu=True)
    if problem:
        raise SystemExit(f'{out}: {problem}')
    shutil.copy2(project/'ProjectSettings/GraphicsSettings.asset', PROBE/'ProjectSettings/GraphicsSettings.asset')
    print('saved', PROBE/'ProjectSettings/GraphicsSettings.asset')


if __name__ == '__main__':
    main()
