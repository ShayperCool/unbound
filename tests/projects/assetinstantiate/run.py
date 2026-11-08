#!/usr/bin/env python3
"""Paired GPU Editor recordings and engine-only asset instantiation gate."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from editor_runner import EditorJob, run as run_editor, publish
from probe_paths import reference_file, run_root
from authored_project import assemble
PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'


def project(dest, authored=False):
    if authored:
        assemble('assetinstantiate', os.environ['UNBOUND_PROBE_UNITY_VERSION'], 'followups', dest)
        return dest
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, dest/part)
    (dest/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+os.environ['UNBOUND_PROBE_UNITY_VERSION']+'\n')
    return dest


def complete(directory):
    file = directory/'values.jsonl'
    if not file.exists():
        return 'missing output'
    rows = dict((r['k'], r['v']) for r in map(json.loads, file.read_text().splitlines()))
    return None if rows.get('done') == 'ok' else 'missing completion marker'


def compare(a,b,report):
    import math
    def read(p):
        rows = list(map(json.loads,p.read_text().splitlines()))
        result = {r['k']:r['v'] for r in rows}
        if len(result) != len(rows):
            raise SystemExit('duplicate output key: '+str(p))
        return result
    expected, actual = read(a),read(b)
    rows=[]
    for k in sorted(expected.keys()|actual.keys()):
        x,y=expected.get(k),actual.get(k)
        ok=k in expected and k in actual and x==y
        if not ok and x is not None and y is not None:
            try:
                ok=math.isfinite(float(x)) and math.isfinite(float(y)) and abs(float(x)-float(y))<=1e-5
            except ValueError:
                pass
        rows.append({'key':k,'expected':x,'actual':y,'pass':ok})
    result={'total':len(rows),'passing':sum(r['pass'] for r in rows),'missing':sorted(expected.keys()-actual.keys()),'extra':sorted(actual.keys()-expected.keys()),'failures':[r for r in rows if not r['pass']],'union':rows}
    report.write_text(json.dumps(result,indent=2)+'\n')
    print(f"{result['passing']}/{result['total']} records match; {len(result['failures'])} failures; report {report}",flush=True)
    return int(bool(result['failures']))


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('mode',choices=('editor','publish','check'))
    p.add_argument('--release',required=True)
    modes=p.add_mutually_exclusive_group()
    modes.add_argument('--followups',action='store_true')
    modes.add_argument('--overrides',action='store_true')
    p.add_argument('--label',default=None)
    args=p.parse_args()
    os.environ['UNBOUND_PROBE_UNITY_VERSION']=args.release
    if args.followups:
        os.environ['ASSET_INSTANTIATE_FOLLOWUPS']='1'
    if args.overrides:
        os.environ['ASSET_INSTANTIATE_OVERRIDES']='1'
    reference = PROBE/'Reference'/('unity-'+args.release+('.overrides' if args.overrides else '.followups' if args.followups else '')+'.jsonl')
    label=args.label or ('engine-'+str(time.time_ns()))
    if Path(label).name!=label or label in ('.','..'):
        p.error('label must be a single directory name')
    work=run_root(PROBE,label);work.mkdir(parents=True,exist_ok=True)
    if args.mode=='editor':
        for name in ('native1','native2'):
            dest=project(work/('project-'+name))
            out=work/name;out.mkdir()
            job=EditorJob(dest,out/'editor.log',out,args=['-executeMethod','ProbeRunner.Record'],batch=True,graphics=True,env={'ASSET_INSTANTIATE_OUT':str(out/'values.jsonl')},timeout=900)
            result=run_editor(job)
            if result['exit_code']!=0 or complete(out):
                raise SystemExit(f'{name}: incomplete capture; see {out}')
            print(name+' complete',flush=True)
    elif args.mode=='publish':
        target=reference
        def write(target,first):
            target.parent.mkdir(exist_ok=True,parents=True)
            shutil.copyfile(first/'values.jsonl',target)
        return publish([work/'native1',work/'native2'],target,lambda d:['values.jsonl'],write,complete=[complete],gpu=True)
    else:
        dest=project(work/'project-engine', authored=args.followups or args.overrides);out=work/'engine';out.mkdir()
        cli=os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND')
        if not cli:
            p.error('set UNBOUND_CLI to this checkout release build')
        with (out/'engine.log').open('w') as log:
            r=subprocess.run([str(LAUNCHER),'--kind','engine-gpu','timeout','300',cli,'run','-p',str(dest),*(['--scene','Assets/Scenes/Followup.unity'] if args.followups else []),'--no-watch','--frames','2000' if args.followups else '300','--size','128x128'],env=dict(os.environ,ASSET_INSTANTIATE_OUT=str(out/'values.jsonl')),stdout=log,stderr=subprocess.STDOUT)
        if r.returncode or complete(out):
            raise SystemExit(f'engine exit {r.returncode}; see {out}')
        return compare(reference,out/'values.jsonl',out/'comparison.json')


if __name__=='__main__':
    sys.exit(main() or 0)
