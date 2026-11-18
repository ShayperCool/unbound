#!/usr/bin/env python3
"""Authors svgsprites' Editor inputs once: the fixture in a private project, the Editor's
SvgSpritesSetup.Author, the result recorded as Authored/unity-<release>/svg (tests/authored_project.py).
This driver is what the authored set is keyed on; run.py captures, publishes and checks.

    UNBOUND_PROBE_UNITY_VERSION=R python3 tests/projects/svgsprites/author.py --label L
"""
import argparse
from pathlib import Path
import shutil
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
import authored_project  # noqa: E402
import editor_runner  # noqa: E402
from probe_paths import run_root, unity_version  # noqa: E402


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--label', required=True)
    label = parser.parse_args().label
    out = run_root(ROOT, 'runs')/label
    if out.exists():
        raise SystemExit(f'{out} already exists')
    project = out/'project'
    for part in authored_project.PARTS:
        shutil.copytree(ROOT/part, project/part)
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-quit', '-executeMethod', 'SvgSpritesSetup.Author'],
                                  batch=True, graphics=True, timeout=1200, label='svgsprites-'+label)
    result = editor_runner.run(job)
    print(editor_runner.summary(result), flush=True)
    if result['exit_code'] != 0:
        return 1
    authored_project.record(ROOT.name, unity_version(), 'svg', project,
                            'python3 tests/projects/svgsprites/author.py --label '+label)
    return 0


if __name__ == '__main__':
    sys.exit(main())
