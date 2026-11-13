#!/usr/bin/env python3
"""Record and compare where game code's float arithmetic keeps a wider intermediate.

    run.py record --profile P --label L     one capture with this host's Editor (Linux x86_64 or
                                            macOS arm64; batch mode, no graphics device)
    run.py publish A B --profile P [--host H]
                                            two byte-identical captures of host H (default: this one)
                                            -> Reference/unity-<release>/P.jsonl and P.H.jsonl
    run.py gate --profile P [--label L]     the engine on a fresh copy with code optimization P,
                                            compared with Reference/

P is `release` or `debug`, the Editor's Code Optimization mode (`-releaseCodeOptimization`,
`-debugCodeOptimization`; `UNBOUND_CODE_OPTIMIZATION` for the engine). The Linux x86_64 and macOS
arm64 Editors record the same values for script code, so both hosts compare with one reference per
mode; the `native/` rows (Unity's native math) can differ between the hosts and come from P.H.jsonl,
H being `linux` or `macos`. Every
missing, extra and differing key is printed; values compare exactly. residuals.json lists known
residuals under their cause as "<profile>/<key>", or "<host>:<profile>/<key>" for one host only.
"""
import argparse
import json
import os
from pathlib import Path
import platform
import subprocess
import sys
import time
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
import editor_runner  # noqa: E402
import value_probe  # noqa: E402
from probe_paths import reference_root, run_root, unity_version  # noqa: E402

PROBE = Path(__file__).resolve().parent
VARIABLE = 'FLOAT_ROUNDING_OUT'
PROFILES = ('release', 'debug')


def host():
    if sys.platform == 'darwin' and platform.machine() == 'arm64':
        return 'macos'
    if sys.platform.startswith('linux') and platform.machine() in ('x86_64', 'AMD64'):
        return 'linux'
    raise SystemExit(f'floatrounding: no Editor measured on {sys.platform}/{platform.machine()}')


NATIVE = 'native/'


def reference(profile):
    return reference_root(PROBE)/(profile+'.jsonl')


def host_reference(profile, name):
    return reference_root(PROBE)/(profile+'.'+name+'.jsonl')


def references(profile):
    """The values this host's Editor records: the shared rows and this host's native rows."""
    shared = value_probe.read_values(reference(profile))
    native = value_probe.read_values(host_reference(profile, host()))
    if any(key.startswith(NATIVE) for key in shared) or any(not key.startswith(NATIVE) for key in native):
        raise SystemExit(f'floatrounding: {profile} references mix shared and {NATIVE} rows')
    return {**shared, **native}


def split(capture):
    """A capture's lines: the rows every host shares, and the native rows, each list ending complete."""
    lines = [line for line in capture.decode().splitlines(keepends=True) if line.strip()]
    rows, complete = lines[:-1], lines[-1]
    native = [line for line in rows if json.loads(line)['k'].startswith(NATIVE)]
    shared = [line for line in rows if not json.loads(line)['k'].startswith(NATIVE)]
    return ''.join(shared+[complete]).encode(), ''.join(native+[complete]).encode()


def macos_editor():
    return Path(os.environ.get('UNBOUND_PROBE_UNITY', '/Applications/Unity/Hub/Editor/'+unity_version()
                               + '/Unity.app/Contents/MacOS/Unity'))


def record(profile, label):
    out = run_root(PROBE, profile+'-'+label)
    project = value_probe.assemble(PROBE, out)
    capture = out/'capture'
    capture.mkdir(parents=True)
    arguments = ['-executeMethod', 'ProbeRunner.Record', '-'+profile+'CodeOptimization']
    if host() == 'macos':
        command = [str(macos_editor()), '-batchmode', '-nographics', '-projectPath', str(project),
                   *arguments, '-logFile', str(out/'editor.log')]
        code = subprocess.run(command, env={**os.environ, VARIABLE: str(capture)}, timeout=900).returncode
    else:
        job = editor_runner.EditorJob(project, out/'editor.log', out, arguments, batch=True, graphics=False,
                                      env={VARIABLE: str(capture)}, timeout=900, label='floatrounding-'+profile+'-'+label)
        result = editor_runner.run(job)
        print(editor_runner.summary(result))
        code = result.get('exit_code')
        problem = editor_runner.result_problem(out, gpu=False)
        if problem:
            raise SystemExit(f'{out}: {problem}')
    try:
        values = value_probe.read_values(capture/value_probe.VALUES)
    except (OSError, ValueError) as error:
        raise SystemExit(f'{out}: no complete capture ({error}); Editor exit {code}')
    if 'error' in values:
        raise SystemExit(f'{out}: the probe failed: {values["error"]}')
    if values.get('codeOptimization', '').lower() != profile:
        raise SystemExit(f'{out}: the Editor ran in {values.get("codeOptimization")} code optimization, not {profile}')
    print('captured', capture)
    return 0


def publish(profile, labels, name):
    if len(labels) != 2:
        raise SystemExit('publish takes the labels of two captures')
    first, second = (run_root(PROBE, profile+'-'+label)/'capture'/value_probe.VALUES for label in labels)
    value_probe.read_values(first)
    if first.read_bytes() != second.read_bytes():
        raise SystemExit('the two captures differ; not publishing')
    shared, native = split(first.read_bytes())
    target = reference(profile)
    target.parent.mkdir(parents=True, exist_ok=True)
    if target.exists():
        # The other host published the shared rows: they must be the same.
        if target.read_bytes() != shared:
            raise SystemExit(f'{target}: this host\'s shared rows differ from the published ones; not publishing')
    else:
        with target.open('xb') as f:
            f.write(shared)
        print('published', target)
    native_target = host_reference(profile, name)
    with native_target.open('xb') as f:
        f.write(native)
    print('published', native_target)
    return 0


def gate(profile, label):
    out = run_root(PROBE, profile+'-'+label)
    project = value_probe.assemble(PROBE, out)
    capture = out/'engine'
    capture.mkdir(parents=True)
    cli = os.environ.get('UNBOUND') or os.environ.get('UNBOUND_CLI') or str(value_probe.REPO/'target/release/unbound')
    env = {**os.environ, VARIABLE: str(capture), 'UNBOUND_CODE_OPTIMIZATION': profile}
    with (out/'engine.log').open('w') as log:
        code = subprocess.run([str(value_probe.LAUNCHER), '--kind', 'engine-gpu', 'timeout', '600', cli, 'run', '-p',
                               str(project), '--no-watch', '--headless', '600'],
                              env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    expected = references(profile)
    try:
        actual = value_probe.read_values(capture/value_probe.VALUES)
    except (OSError, ValueError) as error:
        print(f'floatrounding: no complete engine capture ({error}); engine exit {code}; log {out}/engine.log')
        return 1
    matched, failures = value_probe.compare(expected, actual, 0)
    known = {}
    for key, reason in value_probe.residuals(PROBE).items():
        scope, _, rest = key.partition(':') if ':' in key.split('/', 1)[0] else ('', '', key)
        if scope not in ('', 'linux', 'macos'):
            raise SystemExit(f'floatrounding: residual {key} names an unknown host')
        if scope in ('', host()) and rest.startswith(profile+'/'):
            if rest[len(profile)+1:] in known:
                raise SystemExit(f'floatrounding: residual {rest} is listed for all hosts and for {host()}')
            known[rest[len(profile)+1:]] = reason
    residual = [f for f in failures if f['key'] in known]
    failures = [f for f in failures if f['key'] not in known]
    for failure in failures:
        print(json.dumps(failure))
    for failure in residual:
        print(json.dumps({**failure, 'residual': known[failure['key']]}))
    differing = {f['key'] for f in residual}
    for key in sorted(k for k in known if k not in differing):
        print(f'residual resolved (remove it from residuals.json): {profile}/{key}')
    print(f'floatrounding {profile}: {matched}/{len(expected)} values match, {len(failures)} failures, '
          f'{len(residual)} known residuals; engine exit {code}; logs {out}')
    return 1 if failures or code else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--profile', choices=PROFILES, required=True)
    parser.add_argument('--label', default=None)
    parser.add_argument('--host', choices=('linux', 'macos'), default=None, help='publish: the host that recorded the captures')
    args = parser.parse_args()
    label = args.label or time.strftime('check-%Y%m%d-%H%M%S-')+str(os.getpid())
    if args.mode == 'record':
        return record(args.profile, label)
    if args.mode == 'publish':
        return publish(args.profile, args.labels, args.host or host())
    return gate(args.profile, label)


if __name__ == '__main__':
    sys.exit(main())
