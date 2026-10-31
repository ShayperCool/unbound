#!/usr/bin/env python3
"""Verify archived observations, deltas and hashes without running an engine."""
import hashlib
import importlib.util
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parent
E=ROOT/'Evidence'
load=lambda p:json.loads(p.read_text())
s=load(E/'summary.json')
for name,digest in s['evidence_sha256'].items():
    assert hashlib.sha256((E/name).read_bytes()).hexdigest()==digest,name
spec=importlib.util.spec_from_file_location('cmp',ROOT.parent/'query2dparity/compare.py')
mod=importlib.util.module_from_spec(spec);spec.loader.exec_module(mod)
natives={
    'queries':ROOT.parent/'query2dparity/Evidence/native1.json',
    'geometry':ROOT.parent/'query2dparity/Evidence/geometry-native1.json',
    'selection':ROOT.parent/'query2dparity/Evidence/selection-native1.json',
    'selection-fine':ROOT.parent/'query2dparity/Evidence/fine-native1.json',
    'boundary':ROOT.parent/'query2dparity/Evidence/boundary-native1.json',
    'tests':ROOT.parent/'query2dparity/Evidence/tests-native1.json',
    'skin':ROOT.parent/'colliderskin2d/Evidence/native1.json',
    'initial':E/'initial-native1.json',
}
bad=lambda r:{(v['scenario'],v['key']) for v in r['failures']}
introduced=0
for name,native in natives.items():
    assert hashlib.sha256(native.read_bytes()).hexdigest()==s['suites'][name]['native_sha256'],name
    pair=native.with_name(native.name.replace('native1','native2'))
    assert native.read_bytes()==pair.read_bytes(),name
    reports={}
    for stage in ['accepted','starting','final']:
        actual=mod.compare(load(native),load(E/'captures'/(name+'-'+stage+'.json')))
        archived=load(E/'comparisons'/(name+'-'+stage+'.json'))
        assert all(actual[k]==archived[k] for k in actual),(name,stage)
        reports[stage]=actual
    for base,field in [('accepted','vs_canonical_accepted'),('starting','vs_starting_candidate')]:
        archived=load(E/'comparisons'/(name+'-final.json'))[field]
        expected=dict(resolved=sorted(bad(reports[base])-bad(reports['final'])),retained=sorted(bad(reports[base])&bad(reports['final'])),introduced=sorted(bad(reports['final'])-bad(reports[base])))
        assert archived==json.loads(json.dumps(expected)),(name,base)
        if base=='starting':assert not archived['introduced'],name
        else:introduced+=len(archived['introduced'])
assert introduced==67 and s['strict_acceptance'] is False
assert s['suites']['queries']['stages']['final']['matched']==52059
assert s['suites']['initial']['stages']['final']['failures']==0
print('All archived union-key comparisons and hashes verified; inherited 67 introductions remain unaccepted.')
