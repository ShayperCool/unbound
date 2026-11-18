#!/usr/bin/env python3
"""Metal references of probes in the original flat layout (images and values directly under
tests/projects/<probe>/Reference, recorded by an Editor method writing into the directory its
output variable names), on macOS arm64.

    flat_reference.py record  PROBE --variable VAR --label L [--method M] [--windowed]
    flat_reference.py publish PROBE L

`record` makes two independent captures (L-1, L-2), each in a fresh copy of the probe's
Assets/Packages/ProjectSettings under the validation cache, with the macOS Editor (batch mode
unless --windowed; the caller holds the machine's heavy-job lock). `publish` requires both to have
run on the Metal device, exited 0 (the probe's method quits the Editor, in Play mode for most) and written byte-identical files, then copies them to
Reference/metal (never over an existing one). The Linux references stay as recorded; compare
steps read Reference/metal on macOS arm64 (tests/probe_paths.py metal_counterpart).
"""
import argparse
from pathlib import Path
import shutil
import sys

TESTS = Path(__file__).resolve().parent
sys.path.insert(0, str(TESTS))
import editor_runner  # noqa: E402
from probe_paths import MACOS_ARM64, run_root, unity_version  # noqa: E402

PROJECTS = TESTS/'projects'


def captures(probe, label):
    return [run_root(PROJECTS/probe, label)/f'{label}-{i}' for i in (1, 2)]


def record(probe, variable, label, method, windowed):
    for out in captures(probe, label):
        if out.exists():
            raise SystemExit(f'refusing to overwrite {out}')
        project = out/'project'
        for part in ('Assets', 'Packages', 'ProjectSettings'):
            shutil.copytree(PROJECTS/probe/part, project/part)
        (project/'ProjectSettings/ProjectVersion.txt').write_text(f'm_EditorVersion: {unity_version()}\n')
        (out/'capture').mkdir()
        result = editor_runner.run(editor_runner.EditorJob(
            project, out/'editor.log', out, ['-executeMethod', method],
            batch=not windowed, env={variable: str(out/'capture')}, label=f'{probe}-{out.name}'))
        print(editor_runner.summary(result), flush=True)
    return 0


def files(directory):
    return sorted(p.relative_to(directory).as_posix() for p in directory.rglob('*') if p.is_file())


def publish(probe, label):
    first, second = captures(probe, label)
    for out in (first, second):
        problem = editor_runner.result_problem(out, gpu=True)
        if problem:
            raise SystemExit(f'{out.name}: {problem}; not publishing')
        if not files(out/'capture'):
            raise SystemExit(f'{out.name}: the capture is empty; not publishing')
    if files(first/'capture') != files(second/'capture') or any(
            (first/'capture'/n).read_bytes() != (second/'capture'/n).read_bytes() for n in files(first/'capture')):
        raise SystemExit('the two captures differ; not publishing')
    target = PROJECTS/probe/'Reference'/'metal'
    if target.exists():
        raise SystemExit(f'refusing to overwrite reference: {target}')
    shutil.copytree(first/'capture', target)
    print('published', target, len(files(target)), 'files')
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest='mode', required=True)
    r = sub.add_parser('record')
    r.add_argument('probe')
    r.add_argument('--variable', required=True)
    r.add_argument('--label', required=True)
    r.add_argument('--method', default='ProbeRunner.Record')
    r.add_argument('--windowed', action='store_true')
    p = sub.add_parser('publish')
    p.add_argument('probe')
    p.add_argument('label')
    args = parser.parse_args()
    if not MACOS_ARM64:
        raise SystemExit('flat_reference.py records Metal references on macOS arm64 only')
    if args.mode == 'record':
        return record(args.probe, args.variable, args.label, args.method, args.windowed)
    return publish(args.probe, args.label)


if __name__ == '__main__':
    sys.exit(main())
