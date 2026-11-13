#!/usr/bin/env python3
"""Engine side of the tree billboard probe: `run.py check` runs Unbound on a copy of the project and
prints the image measurements against Reference/unity-<release>/ (the suite tracks them; there is
no tolerance). It fails when the engine fails, when a record differs, or when an image is missing."""
import argparse
from collections import Counter
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
from probe_paths import cache_artifact_path, reference_root, run_root  # noqa: E402


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=['check'])
    parser.add_argument('--label', default='check')
    args = parser.parse_args()
    cli = os.environ.get('UNBOUND') or os.environ.get('UNBOUND_CLI')
    if not cli:
        parser.error('set UNBOUND or UNBOUND_CLI')
    reference = reference_root(PROBE)
    work = cache_artifact_path(run_root(PROBE, args.label)).resolve()
    if work.exists():
        shutil.rmtree(work)
    project = work/'project'
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, project/part)
    out = work/'out'
    env = dict(os.environ, TREE_BILLBOARD_PROBE_OUT=str(out))
    command = [str(Path.home()/'.cache/compat/with-heavy-job'), cli, 'run', '-p', str(project),
               '--frames', '120', '--size', '480x270', '--no-watch']
    with (work/'engine.log').open('w') as log:
        if subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=1800).returncode != 0:
            print(f'treebillboards: engine failed; log {work/"engine.log"}')
            return 1
    expected = [json.loads(line) for line in (reference/'treebillboards.jsonl').read_text().splitlines()]
    actual_path = out/'treebillboards.jsonl'
    actual = [json.loads(line) for line in actual_path.read_text().splitlines()] if actual_path.is_file() else []
    failed = expected != actual
    for row in expected:
        if row not in actual:
            print(f'treebillboards: missing record {json.dumps(row)}')
    for row in actual:
        if row not in expected:
            print(f'treebillboards: extra record {json.dumps(row)}')
    if failed and all(row in actual for row in expected) and all(row in expected for row in actual):
        same = Counter(map(json.dumps, expected)) == Counter(map(json.dumps, actual))
        print('treebillboards: records out of order' if same else 'treebillboards: records repeated a different number of times')
    missing = [p.name for p in reference.glob('*.png') if not (out/p.name).is_file()]
    if missing:
        print('treebillboards: missing images: ' + ', '.join(missing))
        failed = True
    images = subprocess.run([sys.executable, str(PROBE.parent/'particles/compare_images.py'), str(reference), str(out)],
                            capture_output=True, text=True)
    print(images.stdout, end='')
    matching = sum(row in actual for row in expected)
    print(f'treebillboards: {matching}/{len(expected)} records, {len(missing)} images missing')
    return int(failed)


if __name__ == '__main__':
    sys.exit(main())
