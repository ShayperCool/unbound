#!/usr/bin/env python3
"""Stage the UI fixture in a fresh cache directory, replace its interaction run with the text
shadow and outline cases and run one Editor or engine capture. Wraps its heavy child itself.

    run.py native --release R --output DIR           record with the Unity Editor (GPU)
    run.py engine --release R --output DIR --cli C --managed M
    run.py gate --release R                          engine ($UNBOUND_CLI, $UNBOUND_MANAGED_DIR)
                                                     into a fresh cache run, then verify.py
    run.py publish --release R --captures A B        two byte-identical native captures become
                                                     Reference/unity-R/ (never overwritten)

The Editor runs through tests/editor_runner.py; on macOS arm64 that is the macOS Editor, which
draws with Metal, and publish and verify.py use Reference/unity-R/metal (tests/probe_paths.py
device_reference_root).
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import cache_artifact_path, device_reference_root, run_root  # noqa: E402
import editor_runner  # noqa: E402
import time  # noqa: E402


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('mode', choices=['native', 'engine', 'gate', 'publish'])
    p.add_argument('--captures', type=Path, nargs='*', help='publish: the native capture directories')
    p.add_argument('--release', required=True)
    p.add_argument('--output', type=Path, help='fresh directory under the Unbound cache')
    p.add_argument('--cli', type=Path)
    p.add_argument('--managed', type=Path)
    a = p.parse_args()
    if a.mode == 'gate':
        cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND')
        managed = os.environ.get('UNBOUND_MANAGED_DIR')
        if not cli or not managed:
            p.error('gate needs $UNBOUND_CLI (or $UNBOUND) and $UNBOUND_MANAGED_DIR')
        output = run_root('uitktextshadow', f'gate-{time.time_ns()}')
        subprocess.run([sys.executable, __file__, 'engine', '--release', a.release, '--output', str(output),
                        '--cli', cli, '--managed', managed], check=True)
        verify = subprocess.run([sys.executable, str(ROOT/'verify.py'), '--release', a.release,
                                 '--candidate', str(output/'capture')])
        sys.exit(verify.returncode)
    if a.mode == 'publish':
        sys.exit(publish(a.captures or []))
    if a.output is None:
        p.error('--output is required')
    output = a.output.expanduser().resolve()
    try:
        cache_artifact_path(output)
    except ValueError as error:
        p.error(str(error))
    if output.exists():
        p.error('--output must be fresh; previous captures are never overwritten')
    output.mkdir(parents=True)
    project = output/'project'
    for name in ['Assets', 'Packages', 'ProjectSettings']:
        shutil.copytree(ROOT.parent/'uitk'/name, project/name)
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+a.release+'\n')
    shutil.copy2(ROOT/'Assets/Scripts/TextShadowProbe.cs', project/'Assets/Scripts/TextShadowProbe.cs')
    source = project/'Assets/Scripts/UitkProbe.cs'
    text = source.read_text()
    old = 'Populate(root);'
    assert text.count(old) == 1
    source.write_text(text.replace(old, 'TextShadowProbe.Run(document, m_Target);\n        return;\n        ' + old))
    env = dict(os.environ, UITK_PROBE_OUT=str(output/'capture/record.jsonl'), UITK_SHADOW_OUT=str(output/'capture'))
    env.pop('WT', None)
    if a.mode == 'native':
        os.environ['UNBOUND_PROBE_UNITY_VERSION'] = a.release
        job = editor_runner.EditorJob(project, output/'editor.log', output, ['-executeMethod', 'ProbeRunner.Record'],
                                      batch=True, env={k: env[k] for k in ('UITK_PROBE_OUT', 'UITK_SHADOW_OUT')},
                                      timeout=600, label='uitktextshadow-'+output.name)
        print(editor_runner.summary(editor_runner.run(job)))
        if not (output/'capture/shadow.jsonl').is_file():
            raise SystemExit('missing capture; inspect the editor log in ' + str(output))
        return
    else:
        if not a.cli or not a.managed:
            p.error('engine requires --cli and --managed')
        kind = 'engine-gpu'
        env['UNBOUND_MANAGED_DIR'] = str(a.managed.resolve())
        env['LD_LIBRARY_PATH'] = str(a.managed.resolve())
        command = [str(a.cli.resolve()), 'run', '-p', str(project), '--frames', '10', '--size', '960x540', '--no-watch']
    hashes = {}
    if a.mode == 'engine':
        for path in [a.cli.resolve(), a.managed.resolve()/'UnityEngine.CoreModule.dll']:
            hashes[str(path)] = hashlib.sha256(path.read_bytes()).hexdigest()
    (output/'execution.json').write_text(json.dumps({'command': command, 'release': a.release, 'sha256': hashes}, indent=2)+'\n')
    with (output/'process.log').open('w') as log:
        subprocess.run([str(Path.home()/'.cache/compat/with-heavy-job'), '--kind', kind, 'timeout', '600', *command],
                       env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
    for path, digest in hashes.items():
        if hashlib.sha256(Path(path).read_bytes()).hexdigest() != digest:
            raise RuntimeError('runtime changed during capture: ' + path)
    if not (output/'capture/shadow.jsonl').is_file():
        raise SystemExit('missing capture; inspect the editor/process log in ' + str(output))


def publish(captures):
    """Two or more native captures with clean Editor results, byte-identical in shadow.jsonl and
    their images, become the reference set."""
    def names(directory):
        return ['shadow.jsonl', *sorted(p.name for p in (directory/'capture').glob('*.png'))]

    def complete(directory):
        return None if (directory/'capture/shadow.jsonl').is_file() else 'no shadow.jsonl'

    def write(target, first):
        target.mkdir(parents=True)
        for name in names(first):
            shutil.copyfile(first/'capture'/name, target/name)
        print('published', target)
    return editor_runner.publish([Path(c) for c in captures], device_reference_root(ROOT, publishing=True),
                                 lambda d: ['capture/'+n for n in names(d)], write, complete=[complete], gpu=True)


if __name__ == '__main__':
    main()
