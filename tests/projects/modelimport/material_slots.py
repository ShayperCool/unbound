#!/usr/bin/env python3
"""Independent Native repeats, publication, and engine-only material binding gate."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
from editor_runner import EditorJob, run_parallel, publish
from probe_paths import reference_root, run_root

spec = importlib.util.spec_from_file_location('model_run', PROBE/'run.py')
driver = importlib.util.module_from_spec(spec)
spec.loader.exec_module(driver)


def rgb_pixels(image):
    data = image.tobytes()
    return zip(data[0::3], data[1::3], data[2::3])


def compare(reference, candidate):
    from PIL import Image
    def load(path):
        result = {}
        for line in path.read_text().splitlines():
            row = json.loads(line)
            if row['k'] in result:
                raise ValueError('duplicate key '+row['k'])
            result[row['k']] = row['v']
        return result
    a, b = load(reference/'slots.jsonl'), load(candidate/'slots.jsonl')
    differences = {k: {'expected': a.get(k), 'actual': b.get(k),
                       'missing': k not in b, 'extra': k not in a}
                   for k in sorted(a.keys() | b.keys()) if k not in a or k not in b or a[k] != b[k]}
    images = {}
    paths = {p.name for root in (reference, candidate) for p in root.glob('*.png')}
    for name in sorted(paths):
        if not (reference/name).exists() or not (candidate/name).exists():
            images[name] = {'missing': not (candidate/name).exists(), 'extra': not (reference/name).exists()}
            continue
        x, y = Image.open(reference/name).convert('RGB'), Image.open(candidate/name).convert('RGB')
        if x.size != y.size:
            images[name] = {'expectedSize': x.size, 'actualSize': y.size}
            continue
        pixels = [{'xy': [i % x.width, i // x.width], 'expected': u, 'actual': v}
                  for i, (u, v) in enumerate(zip(rgb_pixels(x), rgb_pixels(y))) if u != v]
        # Every pixel remains in the report; no alignment or material permutation is allowed.
        if pixels:
            images[name] = pixels
        if len(set(rgb_pixels(x))) < 3:
            images[name] = {'invalidReference': 'expected at least two face colors and background', 'differences': pixels}
    report = candidate/'slot-comparison.json'
    report.write_text(json.dumps({'values': differences, 'images': images}, indent=1)+'\n')
    print(f'material slots: {len(a.keys() | b.keys())-len(differences)}/{len(a.keys() | b.keys())} values exact, '
          f'{len(paths)-len(images)}/{len(paths)} images exact; {report}')
    for key in differences:
        print('FAIL', key)
    for key in images:
        print('FAIL image', key, len(images[key]) if isinstance(images[key], list) else images[key])
    return int(bool(differences or images))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('editor', 'publish', 'unbound', 'check'))
    parser.add_argument('--release', required=True)
    parser.add_argument('--label', required=True)
    args = parser.parse_args()
    if Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)/'material-slots'
    reference = reference_root(PROBE)/'material-slots'
    work.mkdir(parents=True, exist_ok=True)
    if args.mode == 'editor':
        jobs = []
        for repeat in ('native1', 'native2'):
            project = driver.copy_project(work/'projects'/repeat, 'Gamma', material_slots=True)
            out = work/repeat
            out.mkdir(parents=True, exist_ok=True)
            jobs.append(EditorJob(project, out/'editor.log', out, batch=True,
                args=['-executeMethod', 'ProbeRunner.Record'], graphics=True,
                env={'MATERIAL_SLOTS_OUT': str(out)}, timeout=900,
                progress_files=[out/'slots.jsonl'], progress_timeout=600))
        results = run_parallel(jobs)
        print('editor exits', [r['exit_code'] for r in results])
        return int(any(r['exit_code'] != 0 for r in results))
    if args.mode == 'publish':
        def complete(directory):
            from PIL import Image
            path = directory/'slots.jsonl'
            if not path.exists() or len(list(directory.glob('*.png'))) != 20:
                return 'incomplete material captures'
            rows = [json.loads(line) for line in path.read_text().splitlines()]
            if len(rows) != 306 or any(row['v'] < 1 for row in rows if row['k'].endswith('/rendererCount')):
                return 'incomplete renderer/mesh capture'
            if any(len(set(rgb_pixels(Image.open(p).convert('RGB')))) < 3 for p in directory.glob('*.png')):
                return 'image lacks visible face colors'
            return None
        def write(target, first):
            target.mkdir(parents=True, exist_ok=True)
            for name in ['slots.jsonl', *sorted(p.name for p in first.glob('*.png'))]:
                shutil.copyfile(first/name, target/name)
        return publish([work/'native1', work/'native2'], reference,
                       lambda d: ['slots.jsonl', *sorted(p.name for p in d.glob('*.png'))], write,
                       complete=[complete])
    out = work/'unbound'
    if args.mode == 'unbound':
        project = driver.copy_project(work/'projects'/'unbound', 'Gamma', material_slots=True)
        if out.exists():
            shutil.rmtree(out)
        out.mkdir(parents=True)
        cli = os.environ.get('UNBOUND_CLI', str(driver.REPO/'target/release/unbound'))
        with (out/'unbound.log').open('w') as log:
            code = subprocess.run([str(driver.LAUNCHER), 'timeout', '600', cli, 'run', '-p', str(project),
                                   '--no-watch', '--frames', '10', '--size', '160x64'],
                                  env=dict(os.environ, MATERIAL_SLOTS_OUT=str(out)), stdout=log, stderr=subprocess.STDOUT).returncode
        if code or not (out/'slots.jsonl').exists():
            print('engine capture failed', code, out/'unbound.log')
            return code or 1
    return compare(reference, out)


if __name__ == '__main__':
    raise SystemExit(main())
