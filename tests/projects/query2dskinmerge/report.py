#!/usr/bin/env python3
"""Archive all query deltas and exact expanded-motion preservation."""
import gzip
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
WORK = Path(os.environ['QUERY_WORK'])
OUT = ROOT / 'Evidence/integration'
OUT.mkdir(parents=True)
spec = importlib.util.spec_from_file_location('query_compare', ROOT.parent/'query2dparity/compare.py')
cmp = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cmp)


def load(path):
    return json.loads(path.read_text())


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def archive(path, relative):
    dest = OUT / relative
    dest.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(path, dest)


def dump(relative, value):
    path = OUT / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2)+'\n')


def bad(report):
    return {(item['scenario'], item['key']) for item in report['failures']}


prior = ROOT.parent/'query2dcontinuation/Evidence'
refs = ROOT.parent/'query2dparity/Evidence'
natives = {'queries': refs/'native1.json', 'geometry': refs/'geometry-native1.json',
           'selection': refs/'selection-native1.json', 'selection-fine': refs/'fine-native1.json',
           'boundary': refs/'boundary-native1.json', 'tests': refs/'tests-native1.json',
           'skin': ROOT.parent/'colliderskin2d/Evidence/native1.json',
           'initial': prior/'initial-native1.json'}
summary = {'accepted_parent': '88a53c477dc43e646c21522f6c402c0cec98c6c1',
           'candidate_source': subprocess.check_output(['git','rev-parse','HEAD'], text=True).strip(),
           'unity': '6000.0.71f1 Linux', 'main_abi': 5, 'physx_shim': 8,
           'shader_cache': 13, 'suites': {}, 'full_query_parity': False}
for name, native in natives.items():
    assert native.read_bytes() == native.with_name(native.name.replace('native1','native2')).read_bytes()
    reference = load(native)
    accepted = cmp.compare(reference, load(prior/'captures'/(name+'-accepted.json')))
    starting = cmp.compare(reference, load(prior/'captures'/(name+'-final.json')))
    stages = {}
    for label in ['skin-merged', 'polygon']:
        capture = WORK / ('skin/'+label+'.json' if name=='skin' else
                          'initial-'+label+'.json' if name=='initial' else name+'-'+label+'.json')
        report = cmp.compare(reference, load(capture))
        for base_name, base in [('accepted', accepted), ('query_start', starting)]:
            report['vs_'+base_name] = dict(resolved=sorted(bad(base)-bad(report)),
                                           retained=sorted(bad(base)&bad(report)),
                                           introduced=sorted(bad(report)-bad(base)))
        archive(capture, Path('captures')/(name+'-'+label+'.json'))
        dump(Path('comparisons')/(name+'-'+label+'.json'), report)
        stages[label] = {k: len(v) if isinstance(v,list) else
                         {key:len(value) for key,value in v.items()} if isinstance(v,dict) else v
                         for k,v in report.items()}
        if label=='polygon':
            assert not report['vs_accepted']['introduced'], name
            assert not report['vs_query_start']['introduced'], name
    summary['suites'][name] = dict(native_sha256=sha(native), stages=stages)

ties = ROOT.parent/'query2dties/Evidence'
ties_report = load(ties/'polygon-comparison.json')
assert not ties_report['vs_accepted']['introduced']
assert not ties_report['vs_query_start']['introduced']
summary['ties'] = dict(records=ties_report['records'], matched=ties_report['matched'],
                       exact=ties_report['exact'], retained=len(ties_report['failures']),
                       introduced=0, evidence='../query2dties/Evidence/polygon-comparison.json')

# The complete expanded motion payload is compared, not a sampled subset.
motion_prior = ROOT.parent/'physics2dmotion/Evidence/final/final.json.gz'
with gzip.open(motion_prior, 'rt') as handle:
    before = json.load(handle)
actual_path = WORK/'motion/combined.json'
actual = load(actual_path)
same, changed = [], []
for scenario in sorted(before.keys() | actual.keys()):
    (same if before.get(scenario)==actual.get(scenario) else changed).append(scenario)
assert changed == ['2d.casts.inside','2d.queries','2d.queries.options'], changed
summary['expanded_motion'] = dict(identical_scenarios=same,
    identical_observations=sum(len(actual[name]) for name in same), changed_scenarios=changed,
    prior_gzip_sha256=sha(motion_prior), actual_sha256=sha(actual_path),
    inherited_native_comparison='../physics2dmotion/Evidence/final/comparison.json.gz')
with actual_path.open('rb') as source, gzip.open(OUT/'motion-final.json.gz','wb') as dest:
    shutil.copyfileobj(source,dest)
archive(WORK/'motion/source-delta.json','motion-source-delta.json')

summary['snapshots'] = {}
for base in sorted((Path.home()/'.cache/compat').glob('parity-baseline*')):
    manifest = base/'manifest.json'
    if not manifest.is_file():
        continue
    data = load(manifest)
    hashes = data.get('sha256') or {k:v['sha256'] for k,v in data.items()
                                   if isinstance(v,dict) and 'sha256' in v}
    assert hashes and all(sha(base/path)==digest for path,digest in hashes.items()), base
    summary['snapshots'][str(base)] = dict(files=len(hashes), manifest_sha256=sha(manifest))
artifacts = [WORK/'unbound'] + list((WORK/'managed-polygon').glob('*.dll'))
artifacts += list((WORK/'managed-polygon').glob('*.so'))
artifacts += list((WORK/'test-build').rglob('libunbound_physx.so'))
for path in artifacts:
    if path.name=='libunbound_physx.so':
        assert sha(path)=='4a543241a78444ce2595cf8ce42f370c692e993388d42479913558b875d7a208'
summary['artifact_sha256'] = {str(path):sha(path) for path in artifacts}
for name in ['build-skin.log','build-polygon.log','tests.log','replay-skin.log','replay-polygon.log']:
    archive(WORK/name, Path('logs')/name)
for name in ['gates.log','gates-resume.log']:
    archive(WORK/'validation'/name, Path('logs')/name)
summary['evidence_sha256'] = {str(path.relative_to(OUT)):sha(path)
                             for path in sorted(OUT.rglob('*')) if path.is_file()}
dump('summary.json', summary)
print('All query suites have zero introductions against the accepted and starting runtimes.')
print('Expanded motion unchanged outside the three original query scenarios.')
