#!/usr/bin/env python3
"""Independent OBJ material Native repeats and engine-only property/render gate."""
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
spec_compare = importlib.util.spec_from_file_location('model_compare', PROBE/'compare.py')
driver_compare = importlib.util.module_from_spec(spec_compare)
spec_compare.loader.exec_module(driver_compare)


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
    def capture(directory):
        result = load(directory/'mtl.jsonl')
        for key, value in load(directory/'light-default.jsonl').items():
            if key in result:
                raise ValueError('duplicate key '+key)
            result[key] = value
        return result
    a, b = capture(reference), capture(candidate)
    differences = {k: {'expected': a.get(k), 'actual': b.get(k),
                       'missing': k not in b, 'extra': k not in a}
                   for k in sorted(a.keys() | b.keys()) if k not in a or k not in b or not driver_compare.equal(a[k], b[k])}
    images = {}
    image_failures = 0
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
        maximum = max((max(abs(a-b) for a,b in zip(row['expected'], row['actual'])) for row in pixels), default=0)
        image_failures += int(maximum > 8)
        errors = [max(abs(a-b) for a,b in zip(row['expected'], row['actual'])) for row in pixels]
        mean = sum(errors) / (x.width * x.height)
        over = sum(error > 8 for error in errors)
        print(f'{name}: mean {mean:.2f}, worst {maximum}, over 8: {over}')
        if all(pixel == (0, 0, 0) for pixel in rgb_pixels(x)):
            images[name] = {'invalidReference': 'expected visible assigned faces', 'differences': pixels}
    report = candidate/'mtl-comparison.json'
    report.write_text(json.dumps({'values': differences, 'images': images}, indent=1)+'\n')
    print(f'OBJ materials: {len(a.keys() | b.keys())-len(differences)}/{len(a.keys() | b.keys())} values match, '
          f'{len(paths)-len(images)}/{len(paths)} images exact; {report}')
    for key in differences:
        print('FAIL', key)
    for key in images:
        print('FAIL image', key, len(images[key]) if isinstance(images[key], list) else images[key])
    return int(bool(differences or image_failures or any(not isinstance(v, list) for v in images.values())))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('editor', 'publish', 'unbound', 'gate', 'check'))
    parser.add_argument('--release', required=True)
    parser.add_argument('--label', required=True)
    args = parser.parse_args()
    if Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)/'mtl-properties'
    reference = reference_root(PROBE)/'mtl-properties'
    work.mkdir(parents=True, exist_ok=True)
    if args.mode == 'editor':
        jobs = []
        for space in ('Gamma', 'Linear'):
          for repeat in ('native1', 'native2'):
            project = driver.copy_project(work/'projects'/(space+'-'+repeat), space, material_slots=True, mtl=True)
            out = work/repeat/space
            out.mkdir(parents=True, exist_ok=True)
            jobs.append(EditorJob(project, out/'editor.log', out, batch=True,
                args=['-executeMethod', 'ProbeRunner.Record'], graphics=True,
                env={'MTL_OUT': str(out), 'MODEL_IMPORT_DESCRIPTIONS_OUT': str(out/'descriptions.txt')}, timeout=900,
                progress_files=[out/'mtl.jsonl'], progress_timeout=600))
        results = run_parallel(jobs)
        print('editor exits', [r['exit_code'] for r in results])
        return int(any(r['exit_code'] != 0 for r in results))
    if args.mode == 'publish':
        def files(directory):
            return [str(p.relative_to(directory)) for p in sorted(directory.rglob('*'))
                    if p.name in ('mtl.jsonl', 'light-default.jsonl') or p.suffix == '.png']
        def write(target, first):
            for name in files(first):
                (target/name).parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(first/name, target/name)
        result = 0
        for space in ('Gamma', 'Linear'):
            result |= publish([work/'native1'/space, work/'native2'/space], work/'publication'/space,
                              files, write,
                              complete=[lambda d: None if len(driver_compare.load(d/'mtl.jsonl')) == 1350
                                        and len(driver_compare.load(d/'light-default.jsonl')) == 1
                                        and len(list(d.glob('*.png'))) == 30 else 'incomplete capture'])
        if result:
            return result
        # Add only new reference files; existing oracle bytes must remain identical.
        for space in ('Gamma', 'Linear'):
            staged = work/'publication'/space
            for name in files(reference/space):
                if (reference/space/name).read_bytes() != (staged/name).read_bytes():
                    raise ValueError('existing oracle changed: '+space+'/'+name)
            for name in files(staged):
                target = reference/space/name
                if not target.exists():
                    target.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copyfile(staged/name, target)
        return 0
    result = 0
    for space in ('Gamma', 'Linear'):
        out = work/'unbound'/space
        if args.mode in ('unbound', 'gate'):
            project = driver.copy_project(work/'projects'/('unbound-'+space), space, material_slots=True, mtl=True)
            if out.exists():
                shutil.rmtree(out)
            out.mkdir(parents=True)
            cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(driver.REPO/'target/release/unbound')
            with (out/'unbound.log').open('w') as log:
                code = subprocess.run([str(driver.LAUNCHER), 'timeout', '600', cli, 'run', '-p', str(project),
                                       '--no-watch', '--frames', '10', '--size', '160x64'],
                                      env=dict(os.environ, MTL_OUT=str(out)), stdout=log, stderr=subprocess.STDOUT).returncode
            if code or not (out/'mtl.jsonl').exists():
                print('engine capture failed', code, out/'unbound.log')
                return code or 1
        comparison = compare(reference/space, out)
        result |= comparison
    return result


if __name__ == '__main__':
    raise SystemExit(main())
