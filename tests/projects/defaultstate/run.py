#!/usr/bin/env python3
"""Capture default-state rows in two GPU Editors, or compare the matching Unbound inventory."""
import argparse
import hashlib
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
from probe_paths import MACOS_ARM64, project_copy, device_reference, reference_root, run_root, unity_version, cache_artifact_path
from compare import rows, entries, compare, check_constructor_stores, host_decided, matches, validate_controls

WORK = run_root(ROOT, 'runs')
HEAVY = Path.home()/'.cache/compat/with-heavy-job'


def cli():
    return os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(REPO/'target/release/unbound')


def inventory(out):
    out.mkdir(parents=True, exist_ok=False)
    command = [str(HEAVY), '--kind', 'tool', cli(), 'api-defaults', '-p', str(project_copy(ROOT, out/'inventory-project'))]
    with (out/'inventory.json').open('w') as output, (out/'inventory.log').open('w') as log:
        subprocess.run(command, cwd=REPO, stdout=output, stderr=log, check=True)
    report = json.loads((out/'inventory.json').read_text())
    if report['release'] != unity_version():
        raise ValueError('project release differs from selected probe release')
    return report


# `inventory` exit status when the built inventory differs from the base commit's reference.
INVENTORY_CHANGED = 3


def type_changes(before, after):
    """The types an inventory adds, removes or changes (category, module or status), by name."""
    old = {row['type']: row for row in before}
    new = {row['type']: row for row in after}
    added = sorted(set(new) - set(old))
    removed = sorted(set(old) - set(new))
    changed = sorted(name for name in set(old) & set(new) if old[name] != new[name])
    return added, removed, changed


# What an inventory is made from: `api-defaults` compiles the checkout's hand-written sources
# (managed/) and reflects over them with its own code (commands.rs) against the release's assemblies.
INVENTORY_SOURCES = ('managed', 'crates/unbound/src/commands.rs')


def git_output(*args):
    return subprocess.run(['git', *args], cwd=REPO, check=True, capture_output=True, text=True).stdout.strip()


def inventory_key(rev):
    """A content key of what `rev`'s inventory is made from (sources and release)."""
    objects = [git_output('rev-parse', f'{rev}:{path}') for path in INVENTORY_SOURCES]
    return hashlib.sha256('\n'.join([*objects, unity_version()]).encode()).hexdigest()


def sources_clean():
    """Whether the checkout's inventory sources are HEAD's (then its inventory is HEAD's)."""
    return git_output('status', '--porcelain', '--', *INVENTORY_SOURCES) == ''


def inventory_cache():
    return WORK/'inventories'


def cached_inventory(key):
    path = inventory_cache()/f'{key}.json'
    return json.loads(path.read_text()) if path.is_file() else None


def keep_inventory(key, report):
    """Record an inventory under its content key (the next gate's base when this tree lands)."""
    cache = inventory_cache()
    cache.mkdir(parents=True, exist_ok=True)
    scratch = cache/f'.{key}-{os.getpid()}'
    scratch.write_text(json.dumps({'types': report['types'], 'constructorStores': report.get('constructorStores')}))
    os.replace(scratch, cache/f'{key}.json')


def base_inventory(base):
    """The base's inventory: the one recorded for its sources (by an earlier gate of that tree),
    else its committed reference inventory, said so (None without either)."""
    recorded = cached_inventory(inventory_key(base))
    if recorded is not None:
        print(f'base inventory: recorded for the sources of {base}')
        return recorded
    relative = (reference_root(ROOT)/'inventory.json').relative_to(REPO).as_posix()
    shown = subprocess.run(['git', 'show', f'{base}:{relative}'], cwd=REPO, capture_output=True, text=True)
    if shown.returncode:
        return None
    print(f'base inventory: none recorded for {base}, its committed reference {relative} '
          '(a type main added without rows shows here too)')
    return json.loads(shown.stdout)


def inventory_changed(base, label):
    """Whether the checkout's public type inventory (types and constructor store differences)
    differs from the base's (land.py's cheap check of whether a branch changes the inventory: no
    Editor and no engine run, the same `api-defaults` report the probe compares first). 0 unchanged
    or nothing to compare with, INVENTORY_CHANGED when it differs. A gate's clean checkout records
    its inventory for later bases (UNBOUND_INVENTORY_RECORD=1)."""
    clean = sources_clean()
    if clean and inventory_key('HEAD') == inventory_key(base):
        print(f'public type inventory unchanged: the inventory sources are {base}\'s')
        return 0
    before = base_inventory(base)
    report = inventory(WORK/label)
    # Only a gate records: its CLI is built from this checkout, so the report is of these sources.
    if clean and os.environ.get('UNBOUND_INVENTORY_RECORD') == '1':
        keep_inventory(inventory_key('HEAD'), report)
    if before is None:
        print(f'no inventory of {base}: nothing to compare')
        return 0
    added, removed, changed = type_changes(before['types'], report['types'])
    # The constructor stores' differences (what `check` compares; their other fields are counts
    # that move with any constructor). A reference inventory has none to compare.
    differences = lambda inventory: (inventory.get('constructorStores') or {}).get('differences', [])
    stores = 'constructorStores' in before and differences(before) != differences(report)
    if stores:
        print('constructor store differences changed')
    if not (added or removed or changed or stores):
        print(f'public type inventory unchanged ({len(report["types"])} types)')
        return 0
    for kind, names in (('added', added), ('removed', removed), ('changed', changed)):
        for name in names:
            print(kind, name)
    print(f'public type inventory changed: {len(added)} added, {len(removed)} removed, {len(changed)} changed '
          f'(capture their Editor rows: README.md)')
    return INVENTORY_CHANGED


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
               '--no-watch', '--frames', '4000', '--size', '640x480']
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, cwd=REPO, env=dict(os.environ, **environment(out)), stdout=log, stderr=subprocess.STDOUT).returncode
    print('engine exit', code, 'log', out/'engine.log')
    return code or int(not (out/'values.jsonl').is_file())


def publish(labels):
    if len(labels) < 2:
        raise ValueError('publishing requires at least two independent GPU Editor captures')
    directories = [(WORK/label).resolve() for label in labels]
    if len(set(directories)) != len(directories):
        raise ValueError('publishing requires distinct resolved capture directories')
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


def host_source(probe, file, host):
    """`probe`'s reference `file` from the Linux Editor, or (`host`) from this host's Editor: on
    macOS arm64 its Metal counterpart, which must exist."""
    probe = REPO/'tests/projects'/probe
    linux = reference_root(probe)/file
    path = device_reference(linux, probe) if host else linux
    if host and MACOS_ARM64 and path == linux:
        raise ValueError(f'no macOS Editor reference for {linux}')
    return path


def expected_rows():
    """The reference rows, with the rows the host OS decides as this host's Editor measured them."""
    expected = rows(reference_root(ROOT)/'values.jsonl')
    hosts = host_decided(json.loads((ROOT/'hostdecided.json').read_text()), unity_version(), expected, host_source)
    changed = [{'key': key, 'reference': expected[key], 'host': row} for key, row in hosts.items() if row != expected[key]]
    expected.update(hosts)
    return expected, changed


def check(candidate):
    candidate = cache_artifact_path(candidate)
    debt_document = json.loads((ROOT/'debt.json').read_text())
    debt = entries(debt_document, unity_version())
    expected, hosts = expected_rows()
    report = compare(expected, rows(candidate/'values.jsonl'), debt)
    report['hostDecided'] = hosts
    stores = json.loads((candidate/'inventory.json').read_text())['constructorStores']
    report['constructorFailures'] = check_constructor_stores(stores, debt_document['constructorStores'])
    (candidate/'comparison.json').write_text(json.dumps(report, indent=2)+'\n')
    for row in hosts:
        print('HOST', json.dumps(row, sort_keys=True))
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
    parser.add_argument('mode', choices=['native', 'engine', 'publish', 'compare', 'check', 'inventory'])
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--label')
    parser.add_argument('--candidate', type=Path)
    parser.add_argument('--base', help='inventory: the commit to compare with (default HEAD)')
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
    if args.mode == 'inventory':
        return inventory_changed(args.base or 'HEAD', time.strftime('inventory-%Y%m%d-%H%M%S-')+str(os.getpid()))
    label = time.strftime('check-%Y%m%d-%H%M%S-')+str(os.getpid())
    return engine(label) or check(WORK/label)


if __name__ == '__main__':
    sys.exit(main())
