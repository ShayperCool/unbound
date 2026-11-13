#!/usr/bin/env python3
"""Engine side of the default reflection probe: `run.py check` runs Unbound on a copy of the project
and compares it with Reference/unity-<release>/. It fails when the engine fails, when a record
differs, when an image is missing, or when an image's mean difference (the largest channel
difference per pixel, 0-255, averaged) exceeds MEAN_BOUND or more than OVER_BOUND of its pixels
differ by more than 8."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
from probe_paths import cache_artifact_path, reference_root, run_root  # noqa: E402
from value_probe import image_difference  # noqa: E402

# Measured on NVIDIA: the engine is at mean 0.02 (worst 6) on Data. A black reflection where the
# Editor reflects the sky differs by tens of levels over every smooth surface; the terrain layers'
# smoothness and metallic taken as the sliders alone give mean 0.58 (worst 9).
MEAN_BOUND = 0.25
OVER_BOUND = 0.01
# The images' size (DefaultReflectionProbe.Width x Height).
REFERENCE_PIXELS = 320 * 180


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
    env = dict(os.environ, DEFAULT_REFLECTION_PROBE_OUT=str(out))
    command = [str(Path.home()/'.cache/compat/with-heavy-job'), cli, 'run', '-p', str(project),
               '--frames', '60', '--size', '320x180', '--no-watch']
    with (work/'engine.log').open('w') as log:
        if subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT, timeout=1800).returncode != 0:
            print(f'defaultreflection: engine failed; log {work/"engine.log"}')
            return 1
    expected = [json.loads(line) for line in (reference/'defaultreflection.jsonl').read_text().splitlines()]
    actual_path = out/'defaultreflection.jsonl'
    actual = [json.loads(line) for line in actual_path.read_text().splitlines()] if actual_path.is_file() else []
    failed = expected != actual
    for row in expected:
        if row not in actual:
            print(f'defaultreflection: missing record {json.dumps(row)}')
    for row in actual:
        if row not in expected:
            print(f'defaultreflection: extra record {json.dumps(row)}')
    images = sorted(reference.glob('*.png'))
    failing = 0
    for image in images:
        candidate = out/image.name
        if not candidate.is_file():
            print(f'defaultreflection: {image.stem}: MISSING')
            failing += 1
            continue
        result = image_difference(image, candidate)
        if result is None:
            print(f'defaultreflection: {image.stem}: size differs')
            failing += 1
            continue
        mean, worst, over = result
        over /= REFERENCE_PIXELS
        bad = mean > MEAN_BOUND or over > OVER_BOUND
        failing += bad
        print(f'defaultreflection: {image.stem}: mean {mean:.2f} (bound {MEAN_BOUND}), worst {worst}, '
              f'over 8: {100 * over:.2f}% (bound {100 * OVER_BOUND:.0f}%){" FAIL" if bad else ""}')
    matching = sum(row in actual for row in expected)
    print(f'defaultreflection: {matching}/{len(expected)} records, {len(images) - failing}/{len(images)} images')
    return int(failed or failing > 0)


if __name__ == '__main__':
    sys.exit(main())
