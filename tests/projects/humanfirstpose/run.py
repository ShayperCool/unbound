#!/usr/bin/env python3
"""Stage the committed humanoid fixture and capture only its zero-time HumanA pose.

All generated projects, outputs and logs live in the validation cache. This driver
already wraps each Editor/engine command with with-heavy-job; do not wrap the driver.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
FIXTURE = PROBE.parent / 'humanoid'
sys.path.insert(0, str(PROBE.parents[1]))
from probe_paths import editor_path, reference_file, run_root
from compare import compare, load, validate

LAUNCHER = Path.home() / '.cache/compat/with-heavy-job'


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def replace_once(source, old, new):
    if source.count(old) != 1:
        raise ValueError(f'fixture changed; expected one occurrence of {old!r}')
    return source.replace(old, new)


def stage(dest, release):
    if dest.exists():
        raise ValueError(f'refusing to overwrite {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(FIXTURE / part, dest / part)
    # Keep the original traits/model/avatar loading prelude and first-play setup.
    # No muscle sweeps: only the initial handler read is needed before first play.
    source = (FIXTURE / 'Assets/Scripts/HumanoidProbe.cs').read_text()
    start = source.index('        void Write(', source.index('    static void Handler('))
    end = source.index('        handler.Dispose();', start)
    source = source[:start] + source[end:]
    source = replace_once(source, 'for (int step = 0; step <= 28; step++)', 'for (int step = 0; step == 0; step++)')
    source = replace_once(source, '            animator.Update(step == 0 ? 0f : step <= 24 ? 1f / 30f : 0.1f);',
                          '            HumanFirstPoseCapture.Snapshot(key + ".before", animator);\n'
                          '            animator.Update(0f);\n'
                          '            HumanFirstPoseCapture.Snapshot(key + ".after", animator);')
    begin = source.index('            Play("playA",')
    end = source.index('\n        }\n        catch', begin)
    source = source[:begin] + (
        '            HumanFirstPoseCapture.Curves();\n'
        '            Play("playA", "HumanA", "HumanA");\n'
        '            Play("ik", "HumanA", "HumanAIK", animator => animator.gameObject.AddComponent<HumanoidIK>());\n'
        '            HumanFirstPoseCapture.Finish();') + source[end:]
    source = replace_once(source, '            Put("exception", S(e.ToString()));',
                          '            HumanFirstPoseCapture.Fail(e);\n            Put("exception", S(e.ToString()));')
    (dest / 'Assets/Scripts/HumanoidProbe.cs').write_text(source)
    ik = (FIXTURE / 'Assets/Scripts/HumanoidIK.cs').read_text()
    ik = replace_once(ik, '        var animator = GetComponent<Animator>();',
                       '        var animator = GetComponent<Animator>();\n        HumanFirstPoseCapture.Callback(animator, layerIndex);')
    (dest / 'Assets/Scripts/HumanoidIK.cs').write_text(ik)
    shutil.copyfile(PROBE / 'HumanFirstPoseCapture.cs', dest / 'Assets/Scripts/HumanFirstPoseCapture.cs')
    # The existing Record method opens the original empty scene and enters Play mode.
    (dest / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: ' + release + '\n')
    originals = {str(p.relative_to(FIXTURE)): digest(p) for p in sorted((FIXTURE / 'Assets/Resources').glob('*')) if p.is_file()}
    assert all(digest(dest / name) == sha for name, sha in originals.items())
    (dest.parent / (dest.name + '-authored-inputs.json')).write_text(json.dumps(originals, indent=2) + '\n')
    staged = {str(p.relative_to(dest)): digest(p) for p in sorted(dest.rglob('*')) if p.is_file()}
    (dest.parent / (dest.name + '-staged-inputs.json')).write_text(json.dumps(staged, indent=2) + '\n')
    return dest


def capture(work, name, release, runtime=None):
    project = stage(work / 'projects' / name, release)
    out = work / name
    out.mkdir(parents=True, exist_ok=False)
    env = dict(os.environ, HUMAN_FIRST_POSE_OUT=str(out / 'values.jsonl'), HUMANOID_OUT=str(out / 'fixture.jsonl'),
               DOTNET_CLI_HOME=str(work / 'dotnet-home'), UseSharedCompilation='false',
               DOTNET_CLI_USE_MSBUILD_SERVER='0', MSBUILDDISABLENODEREUSE='1',
               CARGO_TARGET_DIR=str(work / 'cargo-target'))
    if runtime:
        env.update(UNBOUND_MANAGED_DIR=str(runtime / 'managed'), LD_LIBRARY_PATH=str(runtime / 'managed'),
                   UNBOUND_CACHE=str(work / 'engine-cache'))
        command = [str(LAUNCHER), 'timeout', '300', str(runtime / 'unbound'), 'run', '-p', str(project),
                   '--headless', '2', '--no-watch']
    else:
        command = [str(LAUNCHER), 'timeout', '600', str(editor_path()), '-batchmode', '-nographics',
                   '-projectPath', str(project), '-executeMethod', 'ProbeRunner.Record', '-logFile', str(out / 'editor.log')]
    (out / 'command.json').write_text(json.dumps({'argv': command, 'env': {k: env[k] for k in env if k not in os.environ or env[k] != os.environ[k]}}, indent=2) + '\n')
    with (out / 'driver.log').open('w') as log:
        result = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT)
    (out / 'exit.json').write_text(json.dumps({'exit': result.returncode}) + '\n')
    if result.returncode:
        raise RuntimeError(f'{name} exited {result.returncode}; inspect {out}')
    try:
        validate(load(out / 'values.jsonl'))
    except (ValueError, KeyError, TypeError) as error:
        (out / 'validation.json').write_text(json.dumps({'valid': False, 'error': str(error)}) + '\n')
        raise
    (out / 'validation.json').write_text(json.dumps({'valid': True}) + '\n')
    return out / 'values.jsonl'


def copy_runtime(source, work):
    manifest = json.loads((source / 'runtime-manifest.json').read_text())
    if len(manifest) != 33:
        raise ValueError('expected the complete 33-file frozen runtime manifest')
    dest = work / 'runtime'
    if dest.exists():
        raise ValueError(f'refusing to overwrite runtime {dest}')
    checks = []
    for name, sha in manifest.items():
        if Path(name).is_absolute() or '..' in Path(name).parts:
            raise ValueError(f'unsafe manifest name {name}')
        actual = digest(source / name)
        if actual != sha:
            raise ValueError(f'frozen input hash mismatch: {name}: {actual} != {sha}')
        target = dest / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source / name, target)
        copied = digest(target)
        if copied != sha:
            raise ValueError(f'private copy hash mismatch: {name}')
        checks.append({'path': name, 'expected': sha, 'source': actual, 'private': copied})
    (work / 'runtime-manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    (work / 'runtime-verification.json').write_text(json.dumps(checks, indent=2) + '\n')
    return dest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('editor', 'publish', 'unbound'))
    parser.add_argument('--release', required=True)
    parser.add_argument('--label', required=True)
    parser.add_argument('--frozen-runtime', type=Path)
    parser.add_argument('--repeat', choices=('native1', 'native2'), help='resume an independent Native repeat')
    args = parser.parse_args()
    if Path(args.label).name != args.label or args.label in ('', '.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label).resolve()
    work.mkdir(parents=True, exist_ok=True)
    if args.mode == 'editor':
        # Independent clean projects, sequentially, each admitted by the shared launcher.
        for name in ([args.repeat] if args.repeat else ('native1', 'native2')):
            capture(work, name, args.release)
    elif args.mode == 'unbound':
        if not args.frozen_runtime:
            parser.error('unbound requires --frozen-runtime, never a shared build target')
        capture(work, 'unbound', args.release, copy_runtime(args.frozen_runtime.resolve(), work))
    else:
        a, b = work / 'native1/values.jsonl', work / 'native2/values.jsonl'
        report = compare(load(a), load(b))
        (work / 'native-repeat-union.json').write_text(json.dumps(report, indent=2) + '\n')
        validate(load(a))
        validate(load(b))
        if a.read_bytes() != b.read_bytes() or report['differences']:
            raise ValueError('Native repeats differ; not publishing')
        # Require the original first body, including its small nonzero components.
        old = {r['k']: r['v'] for r in map(json.loads, (FIXTURE / 'Reference' / ('unity-' + args.release + '.jsonl')).read_text().splitlines())}
        new = load(a)
        from compare import float_leaves, float_bits
        baseline = []
        for case in ('playA', 'ik'):
            for field in ('bodyPosition', 'bodyRotation', 'rootPosition', 'rootRotation'):
                for path, expected in float_leaves(old[case + '.step0.body'][field]):
                    actual = dict(float_leaves(new[case + '.after'][field]))[path]
                    baseline.append({'field': case + '.' + field + path, 'reference': expected, 'actual': actual,
                                     'reference_bits': float_bits(expected), 'actual_bits': actual['bits'],
                                     'reference_zero_sign_unknown': expected == 0,
                                     'equal': expected == 0 and actual['f'] == 0 or float_bits(expected) == actual['bits']})
        (work / 'old-first-body-union.json').write_text(json.dumps(baseline, indent=2) + '\n')
        if not all(row['equal'] for row in baseline):
            raise ValueError('first body differs from original canonical; not publishing')
        target = reference_file(PROBE, 'jsonl')
        if target.exists():
            raise ValueError(f'refusing to overwrite canonical {target}')
        target.parent.mkdir(exist_ok=True)
        shutil.copyfile(a, target)
        print('published', target)
    return 0


if __name__ == '__main__':
    sys.exit(main())
