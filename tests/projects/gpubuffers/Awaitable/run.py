#!/usr/bin/env python3
"""Prepare, capture and compare the small readback Awaitable control.

All outputs except an independently checked canonical reference live in the cache.
The parent probe's project, windowed runner and timing comparator are reused.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

HERE = Path(__file__).resolve().parent
PROBE = HERE.parent
sys.path.insert(0, str(PROBE.parents[1]))
from probe_paths import cache_artifact_path, editor_path, validation_cache


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


base = load('buffer_runner', PROBE/'run.py')
compare = load('buffer_compare', PROBE/'compare.py')
CASES = ('compute', 'compute.range', 'graphics', 'graphics.range', 'texture',
         'texture.format', 'texture.graphicsFormat', 'texture.region',
         'texture.region.format', 'texture.region.graphicsFormat',
         'invalid.range', 'invalid.mip', 'invalid.null', 'invalid.released')
# Keep the accepted callback completion contract, including its upper bound.
RANGES = {'a': (1, 2), 'c': (1, 2)}


def prepare(work, name):
    project = base.copy_project(work/'projects'/name)
    shutil.copy2(HERE/'GpuBuffersProbe.cs', project/'Assets/Scripts/GpuBuffersProbe.cs')
    return project


def capture(work):
    # Sequential independent processes; never queue the second while the first is alive.
    for name in ('native1', 'native2'):
        project = work/'projects'/name
        if not project.exists():
            project = prepare(work, name)
        output = base.fresh_output(work/name)
        display, server = base.display()
        env = dict(os.environ, DISPLAY=display, GPUBUFFERS_PROBE_OUT=str(output),
                   PWD=str(project), UseSharedCompilation='false',
                   MSBUILDDISABLENODEREUSE='1', DOTNET_CLI_USE_MSBUILD_SERVER='0')
        env.pop('XAUTHORITY', None)
        try:
            with (output/'stdout.log').open('w') as log:
                result = subprocess.run([str(base.LAUNCHER), 'timeout', '-k', '15', '600',
                                         'vglrun', '-d', 'egl', str(editor_path()),
                                         '-projectPath', str(project), '-logFile', str(output/'editor.log')],
                                        cwd=project, env=env, stdout=log, stderr=subprocess.STDOUT)
            (output/'exit.txt').write_text(str(result.returncode)+'\n')
        finally:
            server.terminate()
            server.wait()
            base.stop_compilers(project)
        if result.returncode:
            return result.returncode
        validate(output, native=True)
    return 0


def validate(directory, native=False):
    if native:
        reason = base.incomplete(directory)
        if reason:
            raise ValueError(f'{directory}: {reason}')
    values = compare.rows(directory/'values.jsonl')
    timing = compare.rows(directory/'timing.jsonl')
    if values.get('complete') is not True or 'fatal' in values:
        raise ValueError(f'{directory}: incomplete capture')
    if set(timing) != set(CASES):
        raise ValueError(f'{directory}: timing keys missing={sorted(set(CASES)-set(timing))}, '
                         f'extra={sorted(set(timing)-set(CASES))}')
    for case in CASES:
        if values.get(case+'.finished') is not True or case+'.logs' not in values:
            raise ValueError(f'{directory}: unfinished case {case}')
        for side in ('callback', 'async'):
            keys = (case+'.callback.return', case+'.callback.throw') if side == 'callback' else (
                case+'.async.returnCompleted', case+'.async.throw')
            if sum(key in values for key in keys) != 1:
                raise ValueError(f'{directory}: missing or contradictory admission outcome {case}/{side}')
        returned = case+'.async.returnCompleted' in values
        if returned and (case+'.await.outcome' not in values or case+'.consumeAgain' not in values):
            raise ValueError(f'{directory}: missing consumption outcome {case}')
        for side, needed in (('callback', case+'.callback.return' in values),
                             ('await', values.get(case+'.await.outcome') == 'request')):
            if needed:
                for suffix in ('.state', '.data', '.dataAgain', '.expired.state', '.expired.data', '.expired.dataAgain'):
                    if case+'.'+side+suffix not in values:
                        raise ValueError(f'{directory}: missing result field {case}.{side}{suffix}')
        phases = []
        for event in timing[case]:
            match = compare.EVENT.fullmatch(event)
            if not match or match[1] not in ('a', 'c', 'r'):
                raise ValueError(f'{directory}: malformed event {case}: {event}')
            phases.append(match[1])
            if match[2] != '0':
                low, high = RANGES.get(match[1], (0, 0))
                if not low <= int(match[2]) <= high:
                    raise ValueError(f'{directory}: event outside 1-2 frame contract {case}: {event}')
        expected = (['c'] if case+'.callback.return' in values else []) + (['r', 'a'] if returned else [])
        if sorted(phases) != sorted(expected):
            raise ValueError(f'{directory}: missing or duplicate completion event {case}: {timing[case]}')
    return values, timing


def differences(reference, actual, reference_timing, actual_timing):
    report = {'missing': [], 'extra': [], 'failing': [], 'timing': []}
    for key in sorted(set(reference) | set(actual)):
        if key not in actual:
            report['missing'].append({'key': key, 'reference': reference[key]})
        elif key not in reference:
            report['extra'].append({'key': key, 'actual': actual[key]})
        elif reference[key] != actual[key]:
            report['failing'].append({'key': key, 'reference': reference[key], 'actual': actual[key]})
    for key in sorted(set(reference_timing) | set(actual_timing)):
        if key not in reference_timing or key not in actual_timing or not compare.timing_ok(
                reference_timing[key], actual_timing[key], RANGES):
            report['timing'].append({'key': key, 'reference': reference_timing.get(key),
                                     'actual': actual_timing.get(key)})
    return report


def compare_capture(reference, actual):
    def actual_rows(name):
        path = actual/name
        return compare.rows(path) if path.exists() else {}
    return differences(compare.rows(reference/'values.jsonl'), actual_rows('values.jsonl'),
                       compare.rows(reference/'timing.jsonl'), actual_rows('timing.jsonl'))


def write_report(path, report):
    cache_artifact_path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(report, indent=2)+'\n')
    # Keep every complete value in both the report and console output.
    print(json.dumps(report, indent=2))
    return int(any(report.values()))


def publish(work, reference):
    if (work/'native1').resolve() == (work/'native2').resolve():
        raise ValueError('Native repeats must be independent capture directories')
    first, first_t = validate(work/'native1', native=True)
    second, second_t = validate(work/'native2', native=True)
    # Compare both complete unions before accepting either repeat.
    # Both timings have passed the same bound. Keep every independently observed
    # sequence, as the parent probe does, including any relative completion order.
    report = differences(first, second, {}, {})
    if write_report(work/'native-repeat-comparison.json', report):
        return 1
    # Preserve the parent's byte-identical values requirement as well.
    if (work/'native1/values.jsonl').read_bytes() != (work/'native2/values.jsonl').read_bytes():
        raise ValueError('Native value records differ in order or representation')
    if reference.exists():
        raise ValueError(f'refusing to overwrite canonical reference: {reference}')
    reference.mkdir(parents=True)
    shutil.copy2(work/'native1/values.jsonl', reference/'values.jsonl')
    with (reference/'timing.jsonl').open('w') as out:
        for key, sequence in first_t.items():
            sequences = [sequence]
            if second_t[key] != sequence:
                sequences.append(second_t[key])
            out.write(json.dumps({'k': key, 'v': sequences}, separators=(',', ':'))+'\n')
    return 0


def replay(work, runtime):
    # A complete matching Root artifact is required. No fresh Core / frozen CLI mixture.
    manifest = json.loads((runtime/'runtime-manifest.json').read_text())
    for required in ('unbound', 'managed/UnityEngine.CoreModule.dll', 'managed/libunbound_physx.so'):
        if required not in manifest:
            raise ValueError(f'runtime manifest lacks {required}')
    for name, expected in manifest.items():
        path = runtime/name
        if not path.resolve().is_relative_to(runtime.resolve()):
            raise ValueError(f'invalid runtime entry: {name}')
        with path.open('rb') as source:
            if hashlib.file_digest(source, 'sha256').hexdigest() != expected:
                raise ValueError(f'runtime hash mismatch: {name}')
    # v14 has the ten stubs and cannot validate their v15 implementation.
    if manifest['unbound'] == '276a8e7b546a25ef1c22f872f40c020c997147575ef66baf4f37dcfd67f2394e':
        raise ValueError('runtime15 replay requires the matching Root artifact; frozen14 is a control only')
    project = prepare(work, 'unbound')
    output = base.fresh_output(work/'unbound')
    env = dict(os.environ, GPUBUFFERS_PROBE_OUT=str(output), UNBOUND_MANAGED_DIR=str(runtime/'managed'),
               UseSharedCompilation='false', MSBUILDDISABLENODEREUSE='1', DOTNET_CLI_USE_MSBUILD_SERVER='0')
    with (output/'unbound.log').open('w') as log:
        result = subprocess.run([str(base.LAUNCHER), 'timeout', '-k', '15', '180', str(runtime/'unbound'),
                                 'run', '-p', str(project), '--no-watch', '--frames', '200', '--size', '64x32'],
                                env=env, stdout=log, stderr=subprocess.STDOUT)
    (output/'exit.txt').write_text(str(result.returncode)+'\n')
    return result.returncode


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('prepare', 'editor', 'publish', 'compare', 'unbound'))
    parser.add_argument('--release', required=True)
    parser.add_argument('--label', required=True)
    parser.add_argument('--runtime', type=Path)
    args = parser.parse_args()
    for value in (args.label, args.release):
        if not value or Path(value).name != value or value in ('.', '..'):
            parser.error('label and release must be single directory names')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = validation_cache()/'runs/gpubuffers-awaitable'/args.label
    reference = PROBE/'Reference'/('unity-'+args.release)/'awaitable'
    if args.mode == 'prepare':
        for name in ('native1', 'native2'):
            print(prepare(work, name))
        return 0
    if args.mode == 'editor':
        return capture(work)
    if args.mode == 'publish':
        return publish(work, reference)
    if args.mode == 'compare':
        return write_report(work/'comparison.json', compare_capture(reference, work/'unbound'))
    if args.runtime is None:
        parser.error('unbound requires --runtime with a matching runtime-manifest.json')
    return replay(work, args.runtime.resolve())


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (ValueError, OSError) as error:
        sys.exit(str(error))
