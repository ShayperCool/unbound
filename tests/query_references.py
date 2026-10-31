"""Canonical query suites and unchanged union-key comparisons."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import sys

from probe_paths import reference_file, reference_root, run_root, unity_version

PROJECTS = Path(__file__).resolve().parent/'projects'
spec = importlib.util.spec_from_file_location('query_compare', PROJECTS/'query2dparity/compare.py')
comparator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(comparator)
QUERY_NAMES = ['queries','geometry','selection','selection-fine','boundary','tests']


def references(include_initial=True, include_ties=True, include_skin=True):
    root = reference_root(PROJECTS/'query2dparity')
    result = {name:root/('isolated.json' if name=='tests' else name+'.json') for name in QUERY_NAMES}
    if include_skin:
        result['skin'] = reference_file(PROJECTS/'colliderskin2d')
    if include_initial:
        result['initial'] = reference_file(PROJECTS/'query2dcontinuation')
    if include_ties:
        result['ties'] = reference_file(PROJECTS/'query2dties')
    return result


def load(path):
    return json.loads(Path(path).read_text())


def capture(work, name, label):
    work = Path(work)
    if name in ['skin','ties']:
        return work/name/(label+'.json')
    return work/('initial-'+label+'.json' if name=='initial' else name+'-'+label+'.json')


def validate_references(items):
    records = 0
    for name,path in items.items():
        data = load(path)
        if not isinstance(data,dict) or not data or not all(isinstance(v,dict) for v in data.values()):
            raise ValueError('invalid canonical reference: '+str(path))
        records += sum(map(len,data.values()))
    return records


def verify_single(probe):
    parser = argparse.ArgumentParser(description='Validate the canonical Unity reference; optionally compare an engine capture.')
    parser.add_argument('--actual',type=Path)
    parser.add_argument('--report',type=Path)
    args = parser.parse_args()
    native = reference_file(probe)
    records = validate_references({Path(probe).name:native})
    if args.actual is None:
        print(f'{Path(probe).name}: Unity {unity_version()} reference valid, {records} observations; no engine capture compared.')
        return 0
    report = comparator.compare(load(native),load(args.actual))
    output = args.report or run_root(probe)/'comparison.json'
    output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(json.dumps(report,indent=2)+'\n')
    print({k:v if isinstance(v,int) else len(v) for k,v in report.items()})
    return int(bool(report['failures']))


def report_suites(probe, include_initial=True, include_ties=True, include_skin=True):
    parser = argparse.ArgumentParser(description='Compare current query outputs with release references; write reports only to the cache.')
    parser.add_argument('--work',type=Path,default=Path(os.environ.get('QUERY_WORK',str(run_root(probe)))))
    parser.add_argument('--label',default='final')
    parser.add_argument('--baseline-work',type=Path)
    parser.add_argument('--baseline-label',default='final')
    parser.add_argument('--output',type=Path,default=run_root(probe)/'reports')
    parser.add_argument('--references-only',action='store_true')
    parser.add_argument('--no-regressions',action='store_true',help='Require zero introduced failures against --baseline-work; retain and report every existing failure.')
    args = parser.parse_args()
    if args.no_regressions and args.baseline_work is None:
        parser.error('--no-regressions requires --baseline-work')
    items = references(include_initial,include_ties,include_skin)
    count = validate_references(items)
    if args.references_only:
        print(f'{len(items)} Unity {unity_version()} reference suites valid: {count} observations; no engine capture compared.')
        return 0
    args.output.mkdir(parents=True,exist_ok=True)
    summary = {}
    bad = lambda r: {(v['scenario'],v['key']) for v in r['failures']}
    failures = 0
    introduced = 0
    for name,native in items.items():
        reference = load(native)
        report = comparator.compare(reference,load(capture(args.work,name,args.label)))
        if args.baseline_work:
            before = comparator.compare(reference,load(capture(args.baseline_work,name,args.baseline_label)))
            report['vs_baseline'] = dict(resolved=sorted(bad(before)-bad(report)),
                retained=sorted(bad(before)&bad(report)),introduced=sorted(bad(report)-bad(before)))
        (args.output/(name+'.json')).write_text(json.dumps(report,indent=2)+'\n')
        summary[name] = {k:len(v) if isinstance(v,list) else v for k,v in report.items() if k!='vs_baseline'}
        if 'vs_baseline' in report:
            summary[name]['vs_baseline'] = {k:len(v) for k,v in report['vs_baseline'].items()}
            introduced += len(report['vs_baseline']['introduced'])
        failures += len(report['failures'])
    (args.output/'summary.json').write_text(json.dumps(summary,indent=2)+'\n')
    print(json.dumps(summary,indent=2))
    return int(bool(introduced if args.no_regressions else failures))
