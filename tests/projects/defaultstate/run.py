#!/usr/bin/env python3
"""Capture default-state rows in two GPU Editors, or compare the matching Unbound inventory."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(REPO/'tests'))
import editor_runner
from probe_paths import reference_root, run_root, unity_version, cache_artifact_path
from compare import rows, entries, compare, check_constructor_stores, matches, validate_controls

WORK = run_root(ROOT, 'runs')
HEAVY = Path.home()/'.cache/compat/with-heavy-job'


def cli():
    return os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')


def inventory(out):
    out.mkdir(parents=True, exist_ok=False)
    command = [str(HEAVY), '--kind', 'tool', cli(), 'api-defaults', '-p', str(ROOT)]
    with (out/'inventory.json').open('w') as output, (out/'inventory.log').open('w') as log:
        subprocess.run(command, cwd=REPO, stdout=output, stderr=log, check=True)
    report = json.loads((out/'inventory.json').read_text())
    if report['release'] != unity_version():
        raise ValueError('project release differs from selected probe release')
    return report


def project_at(out):
    project = out/'project'
    for folder in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(ROOT/folder, project/folder)
    return project


def environment(out):
    return dict(DEFAULT_STATE_OUT=str(out/'values.jsonl'), DEFAULT_STATE_PROGRESS=str(out/'progress.txt'),
                DEFAULT_STATE_MANIFEST=str(out/'inventory.json'))


def native(label):
    out = WORK/label
    inventory(out)
    project = project_at(out)
    job = editor_runner.EditorJob(project, out/'editor.log', out, ['-executeMethod', 'ProbeRunner.Record'],
                                  batch=True, graphics=True, env=environment(out),
                                  progress_files=[out/'values.jsonl', out/'progress.txt'],
                                  progress_timeout=180, timeout=1200, label='defaultstate-'+label)
    result = editor_runner.run(job)
    print(editor_runner.summary(result))
    if editor_runner.result_problem(out, gpu=True):
        return 1
    validate_controls(rows(out/'values.jsonl'))
    return 0


def engine(label):
    out = WORK/label
    report = inventory(out)
    canonical_path = reference_root(ROOT)/'inventory.json'
    if canonical_path.exists() and report['types'] != json.loads(canonical_path.read_text())['types']:
        raise ValueError(f'type inventory changed; capture and review new Editor rows: {out}/inventory.json')
    project = project_at(out)
    command = [str(HEAVY), '--kind', 'engine-gpu', 'timeout', '900', cli(), 'run', '-p', str(project),
               '--no-watch', '--frames', '4000', '--size', '96x64']
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, cwd=REPO, env=dict(os.environ, **environment(out)), stdout=log, stderr=subprocess.STDOUT).returncode
    print('engine exit', code, 'log', out/'engine.log')
    return code or int(not (out/'values.jsonl').is_file())


def publish(labels):
    if len(labels) < 2:
        raise ValueError('publishing requires at least two independent GPU Editor captures')
    target = reference_root(ROOT)
    if target.exists():
        raise ValueError(f'refusing to overwrite {target}')
    debt = entries(json.loads((ROOT/'debt.json').read_text()), unity_version())
    captures = []
    inventories = []
    for label in labels:
        out = WORK/label
        problem = editor_runner.result_problem(out, gpu=True)
        if problem:
            raise ValueError(f'{label}: {problem}')
        capture = rows(out/'values.jsonl')
        validate_controls(capture)
        captures.append(capture)
        inventories.append(json.loads((out/'inventory.json').read_text())['types'])
    if any(types != inventories[0] for types in inventories):
        raise ValueError('the repeat inventories differ')
    for other in captures[1:]:
        if other.keys() != captures[0].keys():
            raise ValueError('repeat row keys differ')
        for key in other:
            entry = debt.get(key)
            if entry and 'expectedPattern' in entry:
                if not matches(captures[0][key], entry, 'expected') or not matches(other[key], entry, 'expected'):
                    raise ValueError('repeat outside its recorded variable-value policy: ' + key)
            elif other[key] != captures[0][key]:
                raise ValueError('unclassified repeat difference: ' + key)
    target.mkdir(parents=True)
    shutil.copyfile(WORK/labels[0]/'values.jsonl', target/'values.jsonl')
    (target/'inventory.json').write_text(json.dumps({'types': inventories[0]}, indent=2)+'\n')
    print(f'published {len(captures[0])} raw rows after {len(labels)} GPU repeats; variable rows remain queued')
    return 0


def check(candidate):
    candidate = cache_artifact_path(candidate)
    debt_document = json.loads((ROOT/'debt.json').read_text())
    debt = entries(debt_document, unity_version())
    report = compare(rows(reference_root(ROOT)/'values.jsonl'), rows(candidate/'values.jsonl'), debt)
    stores = json.loads((candidate/'inventory.json').read_text())['constructorStores']
    report['constructorFailures'] = check_constructor_stores(stores, debt_document['constructorStores'])
    (candidate/'comparison.json').write_text(json.dumps(report, indent=2)+'\n')
    for row in report['differences']:
        print('QUEUED' if 'reason' in row else 'FAIL', json.dumps(row, sort_keys=True))
    for key in report['stale']:
        print('STALE', key)
    for row in report['constructorFailures']:
        print('FAIL constructor store', json.dumps(row, sort_keys=True))
    failures = len(report['failures']) + len(report['stale']) + len(report['constructorFailures'])
    print(f"{report['matched']} exact rows; {len(report['differences'])} queued/different; {failures} new/stale failures; "
          f"{len(report['missing'])} missing; {len(report['extra'])} extra")
    print('constructor stores:', json.dumps(stores, sort_keys=True))
    print('full comparison', candidate/'comparison.json')
    return int(failures != 0)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['native', 'engine', 'publish', 'compare', 'check'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    parser.add_argument('--candidate', type=Path)
    args = parser.parse_args()
    for label in args.labels + ([args.label] if args.label else []):
        if Path(label).name != label or label in ('.', '..'):
            parser.error('labels must be single directory names')
    if args.mode in ('native', 'engine') and not args.label:
        parser.error('--label is required')
    if args.mode == 'compare' and args.candidate is None:
        parser.error('--candidate is required')
    if args.mode == 'native': return native(args.label)
    if args.mode == 'engine': return engine(args.label)
    if args.mode == 'publish': return publish(args.labels)
    if args.mode == 'compare': return check(args.candidate)
    label = time.strftime('check-%Y%m%d-%H%M%S-')+str(os.getpid())
    return engine(label) or check(WORK/label)


if __name__ == '__main__':
    sys.exit(main())
