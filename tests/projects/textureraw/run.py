#!/usr/bin/env python3
"""Small CPU-only paired Native capture and explicitly supplied runtime replay.

Each command creates private projects/logs under the validation cache. This driver
wraps each heavy child once; do not wrap the driver in with-heavy-job.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import editor_path, reference_file, run_root, shared_runtime
from editor_runner import EditorJob, load_result, result_problem, run as run_editor

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def project(dest, release):
    if dest.exists():
        raise SystemExit(f'refusing to replace project: {dest}')
    for part in ('Packages', 'ProjectSettings'):
        shutil.copytree(PROBE.parent/'hello'/part, dest/part)
    (dest/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+release+'\n')
    shutil.copytree(PROBE/'Assets', dest/'Assets')
    # Keep explicit safety checks active in runtime-compiled game code as in Native.
    (dest/'Assets/csc.rsp').write_text('-define:ENABLE_UNITY_COLLECTIONS_CHECKS\n')
    (dest/'Assets/Scenes').mkdir()
    # Retain the fixture camera; remove its scripted GameObject.
    scene = (PROBE.parent/'hello/Assets/Scenes/Main.unity').read_text()
    (dest/'Assets/Scenes/Main.unity').write_text(scene.split('--- !u!1 &200\n')[0])
    shutil.copyfile(PROBE.parent/'hello/Assets/Scenes/Main.unity.meta', dest/'Assets/Scenes/Main.unity.meta')
    return dest


def run(work, name, command, output):
    log = work/(name+'.log')
    if output.exists() or log.exists():
        raise SystemExit('refusing to replace capture/log')
    env = dict(os.environ, TEXTURE_RAW_PROBE_OUT=str(output),
               DOTNET_CLI_USE_MSBUILD_SERVER='0', MSBUILDDISABLENODEREUSE='1',
               UseSharedCompilation='false')
    with (work/(name+'-command.json')).open('x') as f:
        json.dump({'command':list(map(str, command)), 'output':str(output),
                   'source':{str(p.relative_to(PROBE)):digest(p) for p in sorted((PROBE/'Assets').rglob('*')) if p.is_file()}}, f, indent=2)
    with log.open('x') as f:
        result = subprocess.run(list(map(str, command)), env=env, stdout=f, stderr=subprocess.STDOUT)
    if result.returncode or not output.exists():
        raise SystemExit(f'{name}: exit={result.returncode}, capture={output.exists()}; see {log}')
    print(name+' complete', flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('native', 'publish', 'engine'))
    parser.add_argument('--release', required=True)
    parser.add_argument('--label', required=True)
    parser.add_argument('--runtime', type=Path, help='explicit matching CLI/managed runtime directory')
    parser.add_argument('--manifest', type=Path, help='immutable runtime hash manifest')
    args = parser.parse_args()
    if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
        parser.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE, args.label)
    work.mkdir(parents=True, exist_ok=True)
    if args.mode == 'native':
        for name in ('native1', 'native2'):
            dest = project(work/('project-'+name), args.release)
            output = work/(name+'.jsonl')
            job = EditorJob(dest, work/(name+'-editor.log'), work/(name+'-editor'),
                            args=['-executeMethod', 'RawProbeRunner.Record'], graphics=False,
                            env={'TEXTURE_RAW_PROBE_OUT': str(output)}, timeout=300,
                            progress_files=[output], progress_timeout=180)
            (work/(name+'-command.json')).write_text(json.dumps({
                'command': job.command(), 'output': str(output),
                'source': {str(p.relative_to(PROBE)): digest(p)
                           for p in sorted((PROBE/'Assets').rglob('*')) if p.is_file()}}, indent=2)+'\n')
            result = run_editor(job)
            if result['exit_code'] != 0 or result.get('forced_exit') or not output.exists():
                raise SystemExit(f'{name}: incomplete Editor run; see {job.out}')
            print(name+' complete', flush=True)
    elif args.mode == 'publish':
        for name in ('native1', 'native2'):
            reason = result_problem(work/(name+'-editor'), gpu=False)
            if not reason and load_result(work/(name+'-editor')).get('forced_exit'):
                reason = 'forced exits are not accepted by this probe'
            if reason:
                raise SystemExit(name+': '+reason+'; not publishing')
        a, b = work/'native1.jsonl', work/'native2.jsonl'
        from compare import compare
        report = compare(a, b)
        with (work/'native-pair-union.json').open('x') as f:
            json.dump(report, f, indent=2)
        if report['missing'] or report['extra'] or report['failing'] or a.read_bytes() != b.read_bytes():
            raise SystemExit('independent Native captures differ')
        rows = {r['k']:r['v'] for r in map(json.loads, a.read_text().splitlines())}
        if rows.get('done') != 'ok' or any(v != 'ok' for k,v in rows.items() if k.endswith('.case')):
            raise SystemExit('Native capture has a failed case')
        target = reference_file(PROBE, 'jsonl')
        target.parent.mkdir(parents=True, exist_ok=True)
        if target.exists():
            raise SystemExit('refusing to replace canonical reference')
        shutil.copyfile(a, target)
        print('published', target)
    else:
        if args.runtime is None or args.manifest is None:
            parser.error('engine requires --runtime and --manifest; never mix CLI/Core versions')
        manifest = json.loads(args.manifest.read_text())
        for rel, expected in manifest.items():
            if digest(args.runtime/rel) != expected:
                raise SystemExit('runtime hash mismatch: '+rel)
        private = shared_runtime(args.runtime/'unbound', work/'runtime',
                                 managed=args.runtime/'managed', manifest=manifest)
        (work/'runtime-manifest.json').write_text(json.dumps(manifest, indent=2)+'\n')
        dest = project(work/'project-engine', args.release)
        os.environ['UNBOUND_MANAGED_DIR'] = str(private/'managed')
        run(work, 'engine', [LAUNCHER, '--kind', 'engine-cpu', 'timeout', '180', private/'unbound', 'run', '-p', dest,
                            '--target', 'Standalone', '--headless', '1000', '--no-watch'], work/'engine.jsonl')


if __name__ == '__main__':
    main()
