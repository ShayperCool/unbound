#!/usr/bin/env python3
"""Records the scene activation witness in the Unity Editor (one predeclared pair).

    run.py editor  --release R --label L --repeat native1   one windowed GPU Editor capture
    run.py editor  --release R --label L --repeat native2   the independent repeat, admitted only
                                                            after native1 passes `check`
    run.py check   --release R --label L --repeat native1   the capture's gates; on success it
                                                            writes canonical.jsonl, ids.jsonl and
                                                            analysis.json next to raw.jsonl
    run.py publish --release R --label L                    both repeats pass and are byte-equal
                                                            in canonical form: copies it to
                                                            Reference/unity-<release>.jsonl
    run.py editor ... --dry-run                             prints the exact Editor job only

The fixture is authored for 6000.0.71f1 (ProjectSettings/ProjectVersion.txt); another release is
refused. Each repeat copies Assets/Packages/ProjectSettings byte for byte to
$XDG_CACHE_HOME/unbound/validation/runs/sceneactivation/<label>/projects/<repeat>, checks the copy
against fixture.json's manifest before the launch and hashes it again after the Editor exits.
Editors run through tests/editor_runner.py (one GPU lease each; do not wrap this driver in
with-heavy-job). See contract.py for the record schema, the gate and the canonical form.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_file, run_root  # noqa: E402
from editor_runner import EditorJob, jsonl_complete, load_result, publish as publish_reference, run, summary  # noqa: E402

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(PROBE))
import contract  # noqa: E402
import fixture as fixture_tool  # noqa: E402

REPEATS = ('native1', 'native2')
RELEASE = '6000.0.71f1'
OUT = 'SCENE_ACTIVATION_OUT'
# Hard deadline of the whole admitted run. The fixture is tiny; the comparable windowed
# editorcallbacks captures took ~40 s, and cold refreshes of 140 s were seen elsewhere.
HARD_TIMEOUT = 900
# raw.jsonl is written in Edit mode ~30 Editor ticks after the import and then through Play mode;
# the silence budget includes the cold import before its first line.
PROGRESS_TIMEOUT = 300
RUNNER_SOURCES = ('run.py', 'contract.py', 'fixture.py', 'fixture.json', 'predictions.json', 'make_fixture.py')


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def tree_hashes(root):
    return {str(p.relative_to(root)): sha(p) for part in ('Assets', 'Packages', 'ProjectSettings')
            for p in sorted((Path(root)/part).rglob('*')) if p.is_file()}


def committed_fixture():
    """fixture.json/predictions.json as recomputed from the current bytes; refuses a stale one."""
    fixture, predictions = fixture_tool.build()
    committed = json.loads((PROBE/'fixture.json').read_text())
    preds = json.loads((PROBE/'predictions.json').read_text())
    preds.pop('audit', None)
    if fixture != committed or predictions != preds:
        sys.exit('fixture.json/predictions.json do not match the fixture bytes; not launching')
    if predictions['collisions']:
        sys.exit('the candidate orders are not pairwise distinct; not launching')
    return fixture


def job(work, repeat):
    project, out = work/'projects'/repeat, work/repeat
    paths = {'assets': project/'Assets', 'packages': project/'Packages', 'settings': project/'ProjectSettings',
             'supervisor': REPO/'tests/editor_runner.py', 'profile': REPO/'data/profiles/unity-6000.0.json'}
    for name in RUNNER_SOURCES:
        paths[name] = PROBE/name
    return EditorJob(project, out/'editor.log', out, env={OUT: str(out/'raw.jsonl')}, timeout=HARD_TIMEOUT,
                     progress_files=[out/'raw.jsonl'], progress_timeout=PROGRESS_TIMEOUT,
                     label='sceneactivation-'+repeat,
                     launch_inputs={'release': RELEASE, 'paths': {k: str(v) for k, v in paths.items()}})


def editor(work, repeat, dry_run=False):
    fixture = committed_fixture()
    j = job(work, repeat)
    if dry_run:
        print(json.dumps({'label': j.label, 'kind': j.kind, 'command': j.command(), 'env': j.env,
                          'timeout': j.timeout, 'progress_timeout': j.progress_timeout,
                          'progress_files': j.progress_files, 'project': str(j.project), 'out': str(j.out),
                          'launch_inputs': j.launch_inputs}, indent=1))
        return 0
    if repeat == 'native2':
        problem = capture_problem(work/'native1', fixture)
        if problem:
            sys.exit(f'native1 did not pass ({problem}); the repeat is not admitted')
    if j.out.exists() or j.project.exists():
        sys.exit(f'refusing to overwrite {j.out} or {j.project}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, j.project/part)
    before = tree_hashes(j.project)
    j.out.mkdir(parents=True)
    (j.out/'fixture-before.json').write_text(json.dumps(before, indent=1)+'\n')
    if before != fixture['inputs']:
        sys.exit('the project copy is not the committed fixture; not launching')
    result = run(j)
    after = tree_hashes(j.project)
    (j.out/'fixture-after.json').write_text(json.dumps(after, indent=1)+'\n')
    print(summary(result))
    changed = sorted(k for k in set(before) | set(after) if before.get(k) != after.get(k))
    print('fixture files changed by the Editor:', changed or 'none')
    return int(result.get('exit_code') != 0 or bool(result.get('forced_exit')))


# ---------------------------------------------------------------- gates

def editor_problem(directory):
    """Strict: exit 0, no forced stop of any kind, NVIDIA, shutdown logged, no watchdog/compile error."""
    result = load_result(directory)
    if result is None:
        return 'no Editor result'
    if result.get('forced_exit'):
        return f"forced exit {result['forced_exit']}"
    if result.get('exit_code') != 0:
        return f"exit {result.get('exit_code')}"
    if 'NVIDIA' not in (result.get('renderer') or '') or not result.get('renderer_ok', True):
        return f"renderer {result.get('renderer')!r} is not the NVIDIA GPU"
    if not result.get('shutdown_logged'):
        return 'the Editor did not log its shutdown'
    if (Path(directory)/'raw.jsonl.watchdog').exists():
        return 'the probe watchdog fired'
    log = Path(directory)/'editor.log'
    if not log.exists():
        return 'editor.log is missing'
    if any('error CS' in line for line in log.read_text(errors='replace').splitlines()):
        return 'editor.log has a compile error'
    return None


def input_problem(directory, fixture):
    """The launch's recorded inputs are the committed fixture and runner; the Editor left the
    authored Assets/Packages bytes unchanged (the scenes equal and unrewritten)."""
    directory = Path(directory)
    path = directory/'prelaunch-inputs.json'
    if not path.exists():
        return 'prelaunch-inputs.json is missing'
    receipt = json.loads(path.read_text())
    if receipt.get('selected_release') != RELEASE:
        return f"selected release {receipt.get('selected_release')}"
    prefix = {'assets': 'Assets', 'packages': 'Packages', 'settings': 'ProjectSettings'}
    launched = {}
    for key, entry in receipt['files'].items():
        label, _, rel = key.partition('/')
        if label in prefix:
            launched[prefix[label]+'/'+rel] = entry['sha256']
        elif label in RUNNER_SOURCES and entry['sha256'] != sha(PROBE/label):
            return f'the launch used another {label}'
    if launched != fixture['inputs']:
        diff = sorted(k for k in set(launched) | set(fixture['inputs']) if launched.get(k) != fixture['inputs'].get(k))
        return f'the launched project is not the committed fixture: {diff[:5]}'
    after_path = directory/'fixture-after.json'
    if not after_path.exists():
        return 'fixture-after.json is missing'
    after = json.loads(after_path.read_text())
    authored = {k: v for k, v in fixture['inputs'].items() if k.startswith(('Assets/', 'Packages/'))}
    left = {k: v for k, v in after.items() if k.startswith(('Assets/', 'Packages/'))}
    if left != authored:
        diff = sorted(k for k in set(left) | set(authored) if left.get(k) != authored.get(k))
        return f'the Editor changed authored files: {diff[:5]} (preserved; the discriminator is refused)'
    if after['Assets/Scenes/Wake.unity'] != after['Assets/Scenes/WakeCopy.unity']:
        return 'the two scene copies differ'
    return None


def capture_problem(directory, fixture=None):
    fixture = fixture or contract.load_fixture()
    problem = editor_problem(directory) or input_problem(directory, fixture)
    if problem:
        return problem
    rows, problem = contract.read_rows(Path(directory)/'raw.jsonl')
    if problem:
        return problem
    problems = contract.gate(rows, fixture)
    return '; '.join(problems) if problems else None


def derive(directory):
    """canonical.jsonl, ids.jsonl, analysis.json of a passing capture (write-once; a rerun must agree)."""
    directory = Path(directory)
    rows, _ = contract.read_rows(directory/'raw.jsonl')
    text, ids = contract.canonical(rows)
    analysis = contract.analyze(rows)
    analysis['raw_ids'] = contract.raw_id_facts(rows)
    analysis['hashes'] = {'raw.jsonl': sha(directory/'raw.jsonl'),
                          'canonical.jsonl': hashlib.sha256(text.encode()).hexdigest(),
                          'ids.jsonl': hashlib.sha256(ids.encode()).hexdigest()}
    outputs = {'canonical.jsonl': text, 'ids.jsonl': ids, 'analysis.json': json.dumps(analysis, indent=1)+'\n'}
    for name, data in outputs.items():
        path = directory/name
        if path.exists():
            if path.read_text() != data:
                sys.exit(f'{path} exists with other content')
        else:
            path.write_text(data)
    return analysis


def check(work, repeat):
    problem = capture_problem(work/repeat)
    print(f'{repeat}:', problem or 'passes')
    if problem:
        return 1
    analysis = derive(work/repeat)
    for phase, entry in analysis['phases'].items():
        for ev, cmp in entry['events'].items():
            print(f'  {phase:8} {ev:8} {cmp["verdict"]:8} {cmp["matches"]}')
    return 0


def publish(work, target=None):
    target = Path(target) if target else reference_file(PROBE, 'jsonl')
    directories = [work/r for r in REPEATS]
    report = {'target': str(target), 'repeats': {}}
    for directory in directories:
        problem = capture_problem(directory)
        report['repeats'][directory.name] = {'problem': problem}
        if problem:
            write_report(work, report, f'{directory.name}: {problem}')
            sys.exit(f'{directory.name}: {problem}; not publishing')
        derive(directory)
        report['repeats'][directory.name].update({n: sha(directory/n) for n in
                                                  ('raw.jsonl', 'canonical.jsonl', 'ids.jsonl', 'editor.log')})
    first, second = [(d/'canonical.jsonl').read_text().splitlines() for d in directories]
    raw1, raw2 = [(d/'raw.jsonl').read_text().splitlines() for d in directories]
    report['canonical_differences'] = [{'line': i, 'native1': a, 'native2': b}
                                       for i, (a, b) in enumerate(zip(first, second)) if a != b]
    report['canonical_lengths'] = [len(first), len(second)]
    report['raw_differing_lines'] = sum(a != b for a, b in zip(raw1, raw2)) + abs(len(raw1)-len(raw2))
    if first != second:
        write_report(work, report, 'the canonical forms differ; nothing published')
        sys.exit('the canonical forms differ; nothing published')

    def write(path, directory):
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(directory/'canonical.jsonl', path)
        print('published', path)
    try:
        code = publish_reference(directories, target, lambda d: ['canonical.jsonl'], write,
                                 complete=[jsonl_complete('canonical.jsonl')], gpu=True)
    except SystemExit as refusal:
        write_report(work, report, str(refusal))
        raise
    report['published'] = sha(target)
    write_report(work, report, 'published')
    return code


def write_report(work, report, outcome):
    report['outcome'] = outcome
    path = work/'publication.json'
    with path.open('x') as stream:
        stream.write(json.dumps(report, indent=1)+'\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('editor', 'check', 'publish'))
    parser.add_argument('--label', required=True)
    parser.add_argument('--release', required=True)
    parser.add_argument('--repeat', choices=REPEATS)
    parser.add_argument('--dry-run', action='store_true')
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    if args.release != RELEASE:
        parser.error(f'the fixture is authored for {RELEASE}')
    if args.mode in ('editor', 'check') and not args.repeat:
        parser.error('--repeat is required')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    if args.mode == 'editor':
        return editor(work, args.repeat, args.dry_run)
    if args.mode == 'check':
        return check(work, args.repeat)
    return publish(work)


if __name__ == '__main__':
    sys.exit(main())
