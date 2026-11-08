#!/usr/bin/env python3
"""Record two supervised GPU Editor captures, publish identical repeats, or replay in Unbound."""
import argparse
import gzip
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
sys.path.insert(0, str(REPO/'tests'))
from probe_paths import run_root, reference_root
from editor_runner import EditorJob, run_parallel, publish, summary, differences, result_problem


def copy_project(dest):
    if dest.exists():
        raise SystemExit(f'refusing to overwrite {dest}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    (dest/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+os.environ['UNBOUND_PROBE_UNITY_VERSION']+'\n')
    return dest


def files(out):
    return ['values.jsonl', *sorted(p.name for p in out.glob('*.rgba'))]


def complete(out):
    if (out/'watchdog.txt').exists() or not (out/'values.jsonl').is_file():
        return 'incomplete capture'
    try:
        rows = [json.loads(s) for s in (out/'values.jsonl').read_text().splitlines()]
    except json.JSONDecodeError:
        return 'invalid JSON record'
    keys = [r['k'] for r in rows]
    if not rows or rows[-1] != {'k': 'complete', 'v': True} or 'fatal' in keys or len(keys) != len(set(keys)):
        return 'fatal, duplicate keys, or missing terminal record'
    expected = {r['k'][:-7]: 16*r['v'] for r in rows if r['k'].endswith('.pixels')}
    actual = {p.stem: p.stat().st_size for p in out.glob('*.rgba')}
    return None if expected == actual else 'incomplete images'


def write_reference(target, first):
    target.mkdir(parents=True)
    shutil.copyfile(first/'values.jsonl', target/'values.jsonl')
    for path in first.glob('*.rgba'):
        (target/(path.name+'.gz')).write_bytes(gzip.compress(path.read_bytes(), mtime=0))


def compare(out, reference):
    spec = importlib.util.spec_from_file_location('line_compare', PROBE.parent/'lines/compare.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    failures, count = module.failures(reference, out)
    for k, reason in failures.items():
        print(k, reason)
    print(f'{count-len(failures)}/{count} keys match')
    (out/'comparison.json').write_text(json.dumps({'keys': count, 'failures': failures}, indent=2)+'\n')
    return bool(failures)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('mode', choices=['editor', 'publish', 'engine', 'compare'])
    p.add_argument('--release', required=True)
    p.add_argument('--label', required=True)
    p.add_argument('--cli', type=Path)
    p.add_argument('--append', help='publish: add this new key prefix after verifying all existing records and images')
    a = p.parse_args()
    if Path(a.label).name != a.label or a.label in ('.', '..'):
        p.error('label must be a single directory name')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = a.release
    work = run_root(PROBE, a.label)
    reference = reference_root(PROBE)
    if a.mode == 'editor':
        jobs=[]
        for repeat in ('native1','native2'):
            project=copy_project(work/'projects'/repeat)
            out=work/repeat
            out.mkdir(parents=True)
            jobs.append(EditorJob(project,out/'editor.log',out,args=['-executeMethod','ProbeRunner.Record'],env={'RENDER_API_OUT':str(out)},
                timeout=900,shutdown_grace=120,progress_files=[out/'values.jsonl'],progress_timeout=480,label='renderapi-'+repeat,
                launch_inputs={'release': a.release, 'paths': {'assets': str(project/'Assets'),
                    'packages': str(project/'Packages'), 'settings': str(project/'ProjectSettings'),
                    'runner': str(Path(__file__).resolve()), 'supervisor': str(REPO/'tests/editor_runner.py')}}))
        results=run_parallel(jobs)
        for result in results: print(summary(result))
        return int(any(r['exit_code'] != 0 or r.get('forced_exit') for r in results))
    if a.mode == 'publish' and a.append:
        repeats=[work/r for r in ('native1','native2')]
        for out in repeats:
            reason=result_problem(out,gpu=True) or complete(out)
            if reason: raise SystemExit(reason)
        problems=differences(repeats,files)
        if problems: raise SystemExit('repeats differ: '+str(problems))
        captured=(repeats[0]/'values.jsonl').read_bytes().splitlines(keepends=True)
        old=(reference/'values.jsonl').read_bytes().splitlines(keepends=True)
        if any(json.loads(row)['k'].startswith(a.append) for row in old):
            raise SystemExit('prefix already present')
        kept=[row for row in captured if not json.loads(row)['k'].startswith(a.append)]
        if kept!=old: raise SystemExit('existing reference records changed')
        if len(kept)==len(captured): raise SystemExit('no new records')
        present=sorted(p.name.removesuffix('.gz') for p in reference.glob('*.rgba.gz'))
        if present!=sorted(p.name for p in repeats[0].glob('*.rgba') if not p.stem.startswith(a.append)):
            raise SystemExit('image set changed')
        for name in present:
            if gzip.decompress((reference/(name+'.gz')).read_bytes())!=(repeats[0]/name).read_bytes():
                raise SystemExit('existing image changed: '+name)
        for path in repeats[0].glob('*.rgba'):
            if path.stem.startswith(a.append):
                (reference/(path.name+'.gz')).write_bytes(gzip.compress(path.read_bytes(),mtime=0))
        (reference/'values.jsonl').write_bytes(b''.join(captured))
        print('published new prefix',a.append,'with all existing records and images unchanged')
        return 0
    if a.mode == 'publish':
        return publish([work/r for r in ('native1','native2')], reference, files, write_reference, complete=[complete], gpu=True)
    if a.mode == 'engine':
        if not a.cli: p.error('--cli is required')
        cli=a.cli.resolve()
        project=copy_project(work/'projects'/'engine')
        out=work/'engine'; out.mkdir(parents=True)
        managed = Path(os.environ['UNBOUND_MANAGED_DIR']) if os.environ.get('UNBOUND_MANAGED_DIR') else next(
            (d for d in [cli.parent/'managed', cli.parent.parent/'managed'] if (d/'Unbound.Host.dll').is_file()), None)
        if managed is None: p.error('cannot locate the built managed runtime; set UNBOUND_MANAGED_DIR')
        env=dict(os.environ,RENDER_API_OUT=str(out),UNBOUND_MANAGED_DIR=str(managed),
            LD_LIBRARY_PATH=str(managed)+os.pathsep+os.environ.get('LD_LIBRARY_PATH',''))
        command=[str(Path.home()/'.cache/compat/with-heavy-job'),'--kind','engine-gpu','timeout','600',str(cli),'run','-p',str(project),'--no-watch','--frames','120','--size','96x64']
        with (out/'engine.log').open('w') as log:
            result=subprocess.run(command,env=env,stdout=log,stderr=subprocess.STDOUT)
        if result.returncode or complete(out):
            print('engine failed', result.returncode, complete(out), out/'engine.log');return 1
    return int(compare(work/'engine',reference))

if __name__ == '__main__':
    sys.exit(main())
