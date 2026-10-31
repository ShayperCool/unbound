#!/usr/bin/env python3
"""Capture real Native island inputs or compare independent pointer-preserving traces."""
import argparse
import hashlib
import itertools
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import editor_path, run_root, unity_version

# Offsets in island_trace.gdb were read from these exact Linux binaries' symbols
# and disassembly. Refuse to apply their layout to a different Editor.
BINARIES = {'Unity':'262ee370258db6a9610701e7a82d576a52d3a05c0713485230ea45734c53abf3',
            'Unity_s.debug':'65071886ec5fe7d1836e53cad6ff16864ce5d19d51b6a4b4f4137d188e37fa30'}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def normalized(path):
    """Rename pointer identities bijectively; retain order, reuse, poses and all fields."""
    maps = {kind:{} for kind in ['world', 'body', 'fixture', 'contact']}

    def identity(kind, pointer):
        mapping = maps[kind]
        if pointer not in mapping:
            mapping[pointer] = len(mapping)
        return mapping[pointer]

    def body(value):
        return dict(value, ptr=identity('body', value['ptr']))

    with path.open() as stream:
        for line in stream:
            row = json.loads(line)
            yield dict(row, world=identity('world', row['world']), bodies=list(map(body, row['bodies'])),
                       contacts=[dict(c, ptr=identity('contact', c['ptr']),
                                      fixtureA=identity('fixture', c['fixtureA']),
                                      fixtureB=identity('fixture', c['fixtureB']),
                                      bodyA=body(c['bodyA']), bodyB=body(c['bodyB'])) for c in row['contacts']])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='mode', required=True)
    capture = sub.add_parser('capture')
    capture.add_argument('--project', type=Path, required=True, help='Prepared private Unity project')
    capture.add_argument('--work', type=Path, default=run_root(ROOT, 'traces'))
    capture.add_argument('--label', required=True)
    compare = sub.add_parser('compare')
    compare.add_argument('first', type=Path)
    compare.add_argument('second', type=Path)
    compare.add_argument('--output', type=Path, default=run_root(ROOT, 'traces')/'agreement.json')
    args = parser.parse_args()
    repo = ROOT.parents[2]
    destination = args.work if args.mode == 'capture' else args.output.parent
    destination = destination.resolve()
    if destination.is_relative_to(repo):
        parser.error('Native traces and reports must be outside the repository')
    destination.mkdir(parents=True, exist_ok=True)
    if args.mode == 'compare':
        differences = []
        count = 0
        for count, (a, b) in enumerate(itertools.zip_longest(normalized(args.first), normalized(args.second)), 1):
            if a != b:
                differences.append(dict(record=count, first=a, second=b))
        result = dict(records=count, agreed=count > 0 and not differences, differences=differences,
                      method='Bijective pointer renaming only; all other fields exact',
                      sha256={str(p.resolve()):sha(p) for p in [args.first, args.second]})
        args.output.write_text(json.dumps(result, indent=2)+'\n')
        print(count, 'records;', len(differences), 'different')
        return 0 if result['agreed'] else 1
    if args.project.resolve().is_relative_to(repo):
        parser.error('--project must be a disposable copy outside the repository')
    if Path(args.label).name != args.label or args.label in ['.', '..']:
        parser.error('--label must be a single filename component')
    editor = Path(os.environ.get('UNITY_EDITOR', editor_path())).resolve()
    if unity_version() != '6000.0.71f1' or any(sha(editor.with_name(k)) != v for k, v in BINARIES.items()):
        parser.error('Native layout is measured only for the documented Unity 6000.0.71f1 Linux binaries')
    if not shutil.which('gdb'):
        parser.error('gdb is required for Native island tracing')
    outputs = {suffix:destination/(args.label+suffix) for suffix in ['.json', '.jsonl', '-gdb.log', '-unity.log', '-command.json']}
    if any(p.exists() for p in outputs.values()):
        parser.error('Refusing to overwrite '+args.label)
    env = dict(os.environ, DYNAMICS_TRACE_EDITOR=str(editor), PHYSICS_PROBE_OUT=str(outputs['.json']),
               DYNAMICS_TRACE_OUT=str(outputs['.jsonl']))
    command = [str(Path.home()/'.cache/compat/with-heavy-job'), 'timeout', '600', 'gdb', '-batch', '-x',
               str(ROOT/'island_trace.gdb'), '--args', str(editor), '-batchmode', '-nographics',
               '-projectPath', str(args.project.resolve()), '-executeMethod', 'ProbeRunner.Record',
               '-logFile', str(outputs['-unity.log'])]
    outputs['-command.json'].write_text(json.dumps(dict(command=command, editor_sha256=BINARIES,
        project=str(args.project.resolve()), trace_source_sha256=sha(ROOT/'island_trace.gdb')), indent=2)+'\n')
    with outputs['-gdb.log'].open('w') as log:
        result = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT)
    if result.returncode or not outputs['.json'].exists() or not outputs['.jsonl'].stat().st_size:
        raise SystemExit('Native trace failed; inspect '+str(outputs['-gdb.log']))
    print(outputs['.jsonl'])
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
