#!/usr/bin/env python3
"""Record and compare where game code's float arithmetic keeps a wider intermediate.

    run.py record --profile P --label L     one Editor capture of this host's Editor (batch mode,
                                            no graphics device), code optimization P
    run.py publish --profile P A B          two byte-identical captures -> Reference/unity-<release>/<host>/P.jsonl
    run.py gate --profile P [--label L]     the engine on a fresh copy with code optimization P,
                                            compared with this host's reference

P is `release` or `debug`, the Editor's Code Optimization mode (`-releaseCodeOptimization`,
`-debugCodeOptimization`; `UNBOUND_CODE_OPTIMIZATION` for the engine). <host> is `linux` (x86_64)
or `macos` (arm64): the script runtime's code generator differs by CPU, so each host is compared
with its own Editor. Every missing, extra and differing key is printed; values compare exactly.
Known residuals are listed in residuals.json as {"<profile>/<key>": ...} under their cause.
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
    raise SystemExit(f'floatrounding: no reference for {sys.platform}/{platform.machine()}')


def reference(profile):
    return reference_root(PROBE)/host()/(profile+'.jsonl')


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


def publish(profile, labels):
    if len(labels) != 2:
        raise SystemExit('publish takes the labels of two captures')
    first, second = (run_root(PROBE, profile+'-'+label)/'capture'/value_probe.VALUES for label in labels)
    value_probe.read_values(first)
    if first.read_bytes() != second.read_bytes():
        raise SystemExit('the two captures differ; not publishing')
    target = reference(profile)
    target.parent.mkdir(parents=True, exist_ok=True)
    with target.open('xb') as f:
        f.write(first.read_bytes())
    print('published', target)
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
    expected = value_probe.read_values(reference(profile))
    try:
        actual = value_probe.read_values(capture/value_probe.VALUES)
    except (OSError, ValueError) as error:
        print(f'floatrounding: no complete engine capture ({error}); engine exit {code}; log {out}/engine.log')
        return 1
    matched, failures = value_probe.compare(expected, actual, 0)
    known = {key[len(profile)+1:]: reason for key, reason in value_probe.residuals(PROBE).items()
             if key.startswith(profile+'/')}
    residual = [f for f in failures if f['key'] in known]
    failures = [f for f in failures if f['key'] not in known]
    for failure in failures:
        print(json.dumps(failure))
    for failure in residual:
        print(json.dumps({**failure, 'residual': known[failure['key']]}))
    differing = {f['key'] for f in residual}
    for key in sorted(k for k in known if k not in differing and k in expected):
        print(f'residual resolved (remove it from residuals.json): {profile}/{key}')
    print(f'floatrounding {host()} {profile}: {matched}/{len(expected)} values match, {len(failures)} failures, '
          f'{len(residual)} known residuals; engine exit {code}; logs {out}')
    return 1 if failures or code else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('mode', choices=('record', 'publish', 'gate'))
    parser.add_argument('labels', nargs='*')
    parser.add_argument('--profile', choices=PROFILES, required=True)
    parser.add_argument('--label', default=None)
    args = parser.parse_args()
    label = args.label or time.strftime('check-%Y%m%d-%H%M%S-')+str(os.getpid())
    if args.mode == 'record':
        return record(args.profile, label)
    if args.mode == 'publish':
        return publish(args.profile, args.labels)
    return gate(args.profile, label)


if __name__ == '__main__':
    sys.exit(main())
