#!/usr/bin/env python3
"""Imported smoothing and take ranges (Assets/Resources/Smoothing, make_smoothing_models.py):
two independent Editor captures, publication when they agree, and the engine-only gate.

    python3 tests/projects/modelimport/smoothing.py editor --release 6000.0.71f1 --label <L>
    python3 tests/projects/modelimport/smoothing.py publish --release 6000.0.71f1 --label <L>
    UNBOUND_CLI=<unbound> python3 tests/projects/modelimport/smoothing.py unbound --release 6000.0.71f1 --label <L>

The comparison lists every missing, extra and differing value (numbers within the model probe's
1e-5, absolute or relative) and the material descriptions the importer handed the preprocessors.
"""
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


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


driver = load_module('model_run', PROBE/'run.py')
driver_compare = load_module('model_compare', PROBE/'compare.py')
FILES = ('smoothing.jsonl', 'descriptions.txt')


def compare(reference, candidate):
    a, b = driver_compare.load(reference/'smoothing.jsonl'), driver_compare.load(candidate/'smoothing.jsonl')
    differences = {k: {'expected': a.get(k), 'actual': b.get(k), 'missing': k not in b, 'extra': k not in a}
                   for k in sorted(a.keys() | b.keys())
                   if k not in a or k not in b or not driver_compare.equal(a[k], b[k])}
    report = candidate/'smoothing-comparison.json'
    report.write_text(json.dumps(differences, indent=1) + '\n')
    print(f'smoothing: {len(a.keys() | b.keys()) - len(differences)}/{len(a.keys() | b.keys())} values match; {report}')
    for key, row in differences.items():
        print('FAIL', key, 'missing' if row['missing'] else 'extra' if row['extra'] else '')
    described = subprocess.run([sys.executable, str(PROBE/'compare.py'), str(reference/'descriptions.txt'),
                                str(candidate/'descriptions.txt')]).returncode
    return int(bool(differences) or bool(described))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('editor', 'publish', 'unbound'))
    parser.add_argument('--release', required=True)
    parser.add_argument('--label', required=True)
    args = parser.parse_args()
    if Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)/'smoothing'
    reference = reference_root(PROBE)/'smoothing'
    work.mkdir(parents=True, exist_ok=True)
    if args.mode == 'editor':
        jobs = []
        for repeat in ('native1', 'native2'):
            project = driver.copy_project(work/'projects'/repeat, 'Gamma', smoothing=True)
            out = work/repeat
            out.mkdir(parents=True, exist_ok=True)
            (out/'descriptions.txt').unlink(missing_ok=True)
            jobs.append(EditorJob(project, out/'editor.log', out, batch=True,
                                  args=['-executeMethod', 'ProbeRunner.Record'], graphics=True,
                                  env={'SMOOTHING_OUT': str(out),
                                       'MODEL_IMPORT_DESCRIPTIONS_OUT': str(out/'descriptions.txt')},
                                  timeout=900, progress_files=[out/'smoothing.jsonl'], progress_timeout=600))
        results = run_parallel(jobs)
        print('editor exits', [r['exit_code'] for r in results])
        return int(any(r['exit_code'] != 0 for r in results))
    if args.mode == 'publish':
        def write(target, first):
            target.mkdir(parents=True)
            for name in FILES:
                shutil.copyfile(first/name, target/name)
        return publish([work/'native1', work/'native2'], reference, lambda d: list(FILES), write,
                       complete=[lambda d: None if (d/'smoothing.jsonl').stat().st_size else 'empty capture'])
    out = work/'unbound'
    project = driver.copy_project(work/'projects'/'unbound', 'Gamma', smoothing=True)
    if out.exists():
        shutil.rmtree(out)
    out.mkdir(parents=True)
    cli = os.environ.get('UNBOUND_CLI') or str(driver.REPO/'target/release/unbound')
    with (out/'unbound.log').open('w') as log:
        code = subprocess.run([str(driver.LAUNCHER), '--kind', 'engine-gpu', 'timeout', '600', cli, 'run',
                               '-p', str(project), '--no-watch', '--frames', '10', '--size', '64x64'],
                              env=dict(os.environ, SMOOTHING_OUT=str(out),
                                       MODEL_IMPORT_DESCRIPTIONS_OUT=str(out/'descriptions.txt')),
                              stdout=log, stderr=subprocess.STDOUT).returncode
    if code or not (out/'smoothing.jsonl').exists():
        print('engine capture failed', code, out/'unbound.log')
        return code or 1
    return compare(reference, out)


if __name__ == '__main__':
    raise SystemExit(main())
