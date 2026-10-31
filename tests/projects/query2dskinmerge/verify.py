#!/usr/bin/env python3
"""Verify stage hashes and every archived query comparison without engine runs."""
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent
for name,digest in json.loads((ROOT/'Evidence/manifest.json').read_text()).items():
    assert hashlib.sha256((ROOT/name).read_bytes()).hexdigest()==digest,name
E = ROOT/'Evidence/integration'
load = lambda path: json.loads(path.read_text())
summary = load(E/'summary.json')
for name,digest in summary['evidence_sha256'].items():
    assert hashlib.sha256((E/name).read_bytes()).hexdigest()==digest,name
spec = importlib.util.spec_from_file_location('query_compare', ROOT.parent/'query2dparity/compare.py')
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)
prior = ROOT.parent/'query2dcontinuation/Evidence'
refs = ROOT.parent/'query2dparity/Evidence'
natives = {'queries':refs/'native1.json','geometry':refs/'geometry-native1.json',
           'selection':refs/'selection-native1.json','selection-fine':refs/'fine-native1.json',
           'boundary':refs/'boundary-native1.json','tests':refs/'tests-native1.json',
           'skin':ROOT.parent/'colliderskin2d/Evidence/native1.json','initial':prior/'initial-native1.json'}
bad = lambda report: {(v['scenario'],v['key']) for v in report['failures']}
for name,native in natives.items():
    assert native.read_bytes()==native.with_name(native.name.replace('native1','native2')).read_bytes()
    reference = load(native)
    actual = mod.compare(reference,load(E/'captures'/(name+'-polygon.json')))
    archived = load(E/'comparisons'/(name+'-polygon.json'))
    assert all(actual[key]==archived[key] for key in actual),name
    for base,field in [('accepted','accepted'),('final','query_start')]:
        before = mod.compare(reference,load(prior/'captures'/(name+'-'+base+'.json')))
        expected = dict(resolved=sorted(bad(before)-bad(actual)),
                        retained=sorted(bad(before)&bad(actual)),introduced=sorted(bad(actual)-bad(before)))
        assert archived['vs_'+field]==json.loads(json.dumps(expected)),name
        assert not expected['introduced'],name
ties = ROOT.parent/'query2dties/Evidence'
assert (ties/'native1.json').read_bytes()==(ties/'native2.json').read_bytes()
actual = mod.compare(load(ties/'native1.json'),load(ties/'polygon.json'))
archived = load(ties/'polygon-comparison.json')
assert all(actual[key]==archived[key] for key in actual)
assert not archived['vs_accepted']['introduced']
gates = load(ROOT/'Evidence/gates/summary.json')
assert gates['physics']['matched']==11581 and gates['physics']['failures']==45
assert gates['tests']['passed']==gates['tests']['total']=='494'
assert gates['matrix']['matched']==5055
print('All query comparisons verified: zero new failing values; remaining failures retained.')
