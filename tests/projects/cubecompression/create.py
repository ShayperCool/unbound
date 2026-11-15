#!/usr/bin/env python3
"""Authors the probe's cubemaps and scene once: runs ProbeRunner.Create in an Editor on a copy
of the project and copies what it wrote (Assets/Resources, Assets/Scenes, the build settings)
back into the probe. The outputs are committed; the gate never runs the Editor.

    python3 tests/projects/cubecompression/create.py --label <L>
"""
import argparse
from pathlib import Path
import shutil
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import editor_runner  # noqa: E402
from probe_paths import run_root, unity_version  # noqa: E402
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
    for part in ('Assets/Resources', 'Assets/Scenes'):
        shutil.copytree(project/part, PROBE/part, dirs_exist_ok=True)
    shutil.copy2(project/'Assets/Scenes.meta', PROBE/'Assets/Scenes.meta')
    shutil.copy2(project/'ProjectSettings/EditorBuildSettings.asset', PROBE/'ProjectSettings/EditorBuildSettings.asset')
    print('created from', unity_version(), 'into', PROBE)


if __name__ == '__main__':
    main()
