#!/usr/bin/env python3
"""Record two Native repeats, publish once, or run the engine-only static FBX gate."""
import argparse
import json
import math
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
from editor_runner import EditorJob, run_parallel, publish
from probe_paths import reference_root, run_root

# Fixed before the first capture. Node/vertex counts, hierarchy and indices stay exact.
EPSILON = 1e-5
COUNT = 32

def load(path):
    values = {}
    for line in path.read_text().splitlines():
        row = json.loads(line)
        if row['k'] in values: raise ValueError('duplicate key '+row['k'])
        values[row['k']] = row['v']
    return values

def complete(directory):
    path = directory/'axis.jsonl'
    if not path.is_file(): return 'missing axis.jsonl'
    rows = load(path)
    if sum(k.endswith('/nodes') for k in rows) != COUNT: return 'missing model controls'
    if sum(k.endswith('/worldVertices') for k in rows) != COUNT: return 'missing mesh controls'
    if any(len(v) != 4 for k,v in rows.items() if k.endswith('/vertices')): return 'invalid tetrahedron'
    return None

def differences(a,b,path=(),numeric=True):
    if isinstance(a,list) and isinstance(b,list):
        result = []
        for i in range(max(len(a),len(b))):
            if i >= len(a): result.append(dict(index=path+(i,),extra=b[i]))
            elif i >= len(b): result.append(dict(index=path+(i,),missing=a[i]))
            else: result.extend(differences(a[i],b[i],path+(i,),numeric))
        return result
    if numeric and isinstance(a,(float,int)) and isinstance(b,(float,int)):
        if math.isfinite(a) and math.isfinite(b) and abs(a-b) <= EPSILON: return []
    elif type(a) == type(b) and a == b: return []
    return [dict(index=path,expected=a,actual=b)]

def compare(reference,candidate):
    a,b = load(reference/'axis.jsonl'),load(candidate/'axis.jsonl')
    report = {}
    for k in sorted(a.keys() | b.keys()):
        if k not in a: report[k] = dict(extra=b[k])
        elif k not in b: report[k] = dict(missing=a[k])
        else:
            diff = differences(a[k],b[k],numeric=not k.endswith(('/indices','/nodes')))
            if diff: report[k] = diff
    issue = complete(candidate)
    result = dict(values=report,invalid=issue,tolerance=EPSILON)
    output = candidate/'comparison.json'
    output.write_text(json.dumps(result,indent=1)+'\n')
    print(f'static FBX axis/units: {len(a.keys() | b.keys())-len(report)}/{len(a.keys() | b.keys())} keys within {EPSILON}; {output}')
    for k in report: print('FAIL',k)
    if issue: print('FAIL',issue)
    return int(bool(report or issue))

def copy_project(dest):
    dest.mkdir(parents=True,exist_ok=False)
    for part in ('Assets','Packages','ProjectSettings'):
        shutil.copytree(PROBE/part,dest/part)
    (dest/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+os.environ['UNBOUND_PROBE_UNITY_VERSION']+'\n')
    return dest

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode',choices=('editor','publish','unbound','check'))
    parser.add_argument('--release',required=True)
    parser.add_argument('--label',default='gate-'+str(time.time_ns()))
    args = parser.parse_args()
    if Path(args.label).name != args.label or args.label in ('.','..'): parser.error('invalid label')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    work = run_root(PROBE,args.label)
    reference = reference_root(PROBE)
    work.mkdir(parents=True,exist_ok=True)
    if args.mode == 'editor':
        jobs = []
        for repeat in ('native1','native2'):
            project = copy_project(work/'projects'/repeat)
            out = work/repeat
            out.mkdir()
            jobs.append(EditorJob(project,out/'editor.log',out,batch=True,graphics=True,
                args=['-executeMethod','AxisRunner.Record'],env={'AXIS_OUT':str(out)},timeout=900,
                progress_files=[out/'axis.jsonl'],progress_timeout=600))
        results = run_parallel(jobs)
        print('editor exits',[r['exit_code'] for r in results])
        return int(any(r['exit_code'] != 0 for r in results))
    if args.mode == 'publish':
        def write(target,first):
            target.mkdir(parents=True,exist_ok=True)
            shutil.copyfile(first/'axis.jsonl',target/'axis.jsonl')
        return publish([work/'native1',work/'native2'],reference,
            lambda d:['axis.jsonl'],write,complete=[complete])
    out = work/'unbound'
    if args.mode == 'unbound':
        project = copy_project(work/'projects'/'unbound')
        out.mkdir()
        cli = os.environ.get('UNBOUND_CLI') or os.environ['UNBOUND']
        launcher = Path.home()/'.cache/compat/with-heavy-job'
        with (out/'unbound.log').open('w') as log:
            code = subprocess.run([str(launcher),'timeout','600',cli,'run','-p',str(project),
                '--no-watch','--frames','10','--size','64x64'],env=dict(os.environ,AXIS_OUT=str(out)),
                stdout=log,stderr=subprocess.STDOUT).returncode
        if code or not (out/'axis.jsonl').exists():
            print('engine capture failed',code,out/'unbound.log')
            return code or 1
    return compare(reference,out)

if __name__ == '__main__': raise SystemExit(main())
