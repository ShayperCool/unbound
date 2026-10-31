#!/usr/bin/env python3
"""Check actual integration captures against Unity; keep all reports outside Git."""
import argparse
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(ROOT.parents[1]))
from probe_paths import reference_file, reference_root, run_root
from query_references import comparator

SCENE = ROOT.parent/'physicsscenesettings'
spec = importlib.util.spec_from_file_location('scene_comparison', SCENE/'compare.py')
scene = importlib.util.module_from_spec(spec)
spec.loader.exec_module(scene)
FOCUSED = ['clone-placement', 'transform-clone-placement', 'sleep-inactive', 'inactive-sequences',
           'sleep', 'triggers', 'flags', 'trigger-pairs', 'ignore-lifecycle', 'kinematic',
           'order', 'component', 'barrier']
TEST_CLASSES = ['PhysicsTests', 'Physics2DTests', 'UIElements', 'YogaLayout',
                'CanvasOrderTests', 'CoreTests', 'Physics2DQueryParityTests']


def load(path):
    return json.loads(path.read_text())


def bad(result):
    return {(row['scenario'], row['key']) for row in result['failures']}


def delta(before, after):
    return dict(resolved=sorted(bad(before)-bad(after)), retained=sorted(bad(before)&bad(after)),
                introduced=sorted(bad(after)-bad(before)))


def flat_comparison(native, candidate):
    a, b = scene.read(str(native)), scene.read(str(candidate))
    result = dict(records=len(a.keys()|b.keys()), exact=0, matched=0, failures=[], passing_rounding=[])
    for key in sorted(a.keys()|b.keys()):
        row = dict(key=key, reference=a.get(key), candidate=b.get(key))
        if key in a and key in b and a[key] == b[key]:
            result['exact'] += 1
            result['matched'] += 1
        elif scene.matches(key, a.get(key), b.get(key)):
            result['matched'] += 1
            result['passing_rounding'].append(row)
        else:
            result['failures'].append(row)
    return result


def baseline_capture(base, name):
    options = [base/'regressions'/(name+'.json')]
    options += [base/'scene/regressions-final/physics.json'] if name == 'physics' else [base/'trigger'/(name+'-final.json')]
    return next((p for p in options if p.exists()), options[0])


def main(probe=ROOT, combined=False):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--work', type=Path, default=Path(os.environ.get('QUERY_WORK', run_root(probe))))
    parser.add_argument('--output', type=Path, default=run_root(probe, 'gate-reports'))
    parser.add_argument('--baseline', type=Path, default=os.environ.get('QUERY_GATE_BASELINE'),
                        help='Optional previous accepted output directory; required to accept retained physics differences')
    parser.add_argument('--starting-physics', type=Path, default=os.environ.get('QUERY_STARTING_PHYSICS'),
                        help='Optional earlier candidate capture for a diagnostic comparison')
    for name, environment, folder in [('scene-work', 'SCENE_WORK', 'scene'),
                                       ('scene-parent-work', 'SCENE_PARENT_WORK', 'scene-parent'),
                                       ('trigger-work', 'TRIGGER_WORK', 'trigger'),
                                       ('loading-work', 'LOADING_WORK', 'loading')]:
        parser.add_argument('--'+name, type=Path, default=os.environ.get(environment), help='Defaults to --work/'+folder)
    parser.add_argument('--trx', type=Path, help='Defaults to tests/combined.trx or validation/integrated.trx under --work')
    parser.add_argument('--api', type=Path, help='API log; combined gates require zero reported errors')
    args = parser.parse_args()
    work, output = args.work.resolve(), args.output.resolve()
    if output.is_relative_to(REPO) or output == work or output.is_relative_to(work):
        parser.error('--output must be outside the repository and the input workspace')
    scene_work = args.scene_work or work/'scene'
    focused_work = args.scene_parent_work or work/'scene-parent'
    trigger_work = args.trigger_work or work/'trigger'
    loading_work = args.loading_work or work/'loading'
    trx = args.trx or (work/'tests/combined.trx' if (work/'tests/combined.trx').exists() else work/'validation/integrated.trx')
    api = args.api or (work/'api-check.log' if combined or (work/'api-check.log').exists() else None)
    input_directories = [work, scene_work, focused_work, trigger_work, loading_work]+([args.baseline] if args.baseline else [])
    if any(output == p.resolve() or output.is_relative_to(p.resolve()) for p in input_directories):
        parser.error('--output must be separate from every supplied input directory')
    starting = args.starting_physics or (work/'physics-starting.json' if (work/'physics-starting.json').exists() else None)
    captures = {'physics': scene_work/'regressions-final/physics.json',
                'timing': trigger_work/'timing-final.json', 'order': trigger_work/'order-final.json'}
    natives = {'physics': reference_file(ROOT.parent/'physics'),
               'timing': reference_root(ROOT.parent/'trigger2dtiming')/'timing.json',
               'order': reference_root(ROOT.parent/'trigger2dtiming')/'order.json'}
    matrix_refs = {p.stem:p for p in reference_root(SCENE).glob('*.jsonl')}
    for group in ['contacts', 'islands']:
        matrix_refs.update({group+'-'+p.stem:p for p in (reference_root(SCENE)/group).glob('*.jsonl')})
    fixtures = {}
    for name in ['physicsdefaults', 'joints', 'controller', 'forces', 'rayhits', 'joints2d',
                 'effectors', 'graphics', 'canvaslifecycle', 'messages']:
        fixture = ROOT.parent/name
        if name in ['joints2d', 'effectors', 'canvaslifecycle']:
            candidate = trigger_work/name
        elif name in ['graphics', 'messages']:
            candidate = trigger_work/(name+('.json' if name == 'graphics' else '.jsonl'))
        else:
            candidate = scene_work/'regressions-final'/(name+('.jsonl' if name == 'physicsdefaults' else ''))
        native = reference_file(fixture, candidate.suffix[1:]) if candidate.suffix else fixture/'Reference'
        fixtures[name] = (fixture, native, candidate)
    required = [*captures.values(), *natives.values(), trx, trigger_work/'matrix/summary.json',
                trigger_work/'lifecycle.jsonl', reference_file(ROOT.parent/'lifecycle', 'jsonl')]
    for name in ['lifecycle', 'receivers']:
        required += [reference_root(ROOT.parent/'trigger2dlifecycle')/(name+'.json'), trigger_work/(name+'-final.json')]
    for order in ['transform-first', 'rigidbody-first']:
        required += [loading_work/(order+'-final.jsonl')]
    required += [reference_root(ROOT.parent/'inactive3dloading')/'loading.jsonl']
    for name in FOCUSED:
        required += [reference_root(SCENE)/'focused'/(name+'.jsonl'), focused_work/(name+'-final.jsonl')]
    for name, native in matrix_refs.items():
        required += [native, trigger_work/'matrix'/(name+'.jsonl')]
    for fixture, native, candidate in fixtures.values():
        required += [native, candidate]
    if api: required.append(api)
    if starting: required.append(starting)
    if args.baseline:
        required += [baseline_capture(args.baseline, name) for name in captures]
        required += [args.baseline/'matrix'/(name+'.jsonl') if (args.baseline/'matrix').exists()
                     else args.baseline/'trigger/matrix'/(name+'.jsonl') for name in matrix_refs]
    missing = sorted({str(p) for p in required if not p.exists()})
    if missing:
        parser.exit(2, 'Missing actual gate inputs:\n  '+'\n  '.join(missing)+
                    '\nSupply --work and any explicit path overrides. For canonical reference validation, run '+str(probe/'verify.py')+'.\n')
    output.mkdir(parents=True, exist_ok=True)
    errors, summary = [], {}

    def require(condition, message):
        if not condition: errors.append(message)

    def dump(relative, data):
        dest = output/relative
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_text(json.dumps(data, indent=2)+'\n')

    def archive(source, relative):
        dest = output/relative
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, dest)

    for name, candidate in captures.items():
        native = load(natives[name])
        result = comparator.compare(native, load(candidate))
        if args.baseline:
            before = comparator.compare(native, load(baseline_capture(args.baseline, name)))
            result.update(delta(before, result))
            require(not result['introduced'], name+' introduced failing observations')
        else:
            require(not result['failures'], name+' has failures; supply --baseline to verify retained differences')
        if name == 'physics' and starting:
            earlier = comparator.compare(native, load(starting))
            result['vs_starting_candidate'] = delta(earlier, result)
            dump('physics-starting-comparison.json', earlier)
            archive(starting, Path('captures/physics-starting.json'))
        if name != 'physics': require(not result['failures'], name+' must pass every observation')
        dump(name+'-comparison.json', result)
        archive(candidate, Path('captures')/candidate.name)
        summary[name] = {key:len(value) if isinstance(value, list) else value
                         for key,value in result.items() if key != 'vs_starting_candidate'}
        if 'vs_starting_candidate' in result:
            summary[name]['vs_starting_candidate'] = {k:len(v) for k,v in result['vs_starting_candidate'].items()}
    for name in ['lifecycle', 'receivers']:
        candidate = trigger_work/(name+'-final.json')
        result = comparator.compare(load(reference_root(ROOT.parent/'trigger2dlifecycle')/(name+'.json')), load(candidate))
        require(result['exact'] == result['records'], 'contact '+name+' must be exact')
        dump('contact-'+name+'-comparison.json', result)
        summary['contact-'+name] = dict(records=result['records'], exact=result['exact'])
        archive(candidate, Path('captures')/('contact-'+candidate.name))
    summary['loading'] = {}
    for order in ['transform-first', 'rigidbody-first']:
        candidate = loading_work/(order+'-final.jsonl')
        result = flat_comparison(reference_root(ROOT.parent/'inactive3dloading')/'loading.jsonl', candidate)
        require(result['exact'] == result['records'] == 4656, order+' loading must be exact at 4656 records')
        dump('loading/'+order+'-comparison.json', result)
        summary['loading'][order] = dict(records=result['records'], exact=result['exact'])
        archive(candidate, Path('loading')/candidate.name)
    summary['focused'] = {}
    for name in FOCUSED:
        candidate = focused_work/(name+'-final.jsonl')
        result = flat_comparison(reference_root(SCENE)/'focused'/(name+'.jsonl'), candidate)
        require(not result['failures'], 'focused '+name+' differs')
        if name not in ['order', 'component', 'barrier']:
            require(result['exact'] == result['records'], 'focused '+name+' must be exact')
        dump('focused/'+name+'-comparison.json', result)
        summary['focused'][name] = dict(records=result['records'], exact=result['exact'])
        archive(candidate, Path('focused')/(name+'.jsonl'))
    recorded_matrix = load(trigger_work/'matrix/summary.json')
    profiles = []
    require(recorded_matrix['matched'] == 5055 and recorded_matrix['exact'] == 4803,
            'recorded matrix totals must retain 5055 matches and 4803 exact')
    require(len(recorded_matrix['profiles']) == len(matrix_refs) == 21, 'matrix must include all 21 profiles')
    recorded_profiles = {row['profile']:row for row in recorded_matrix['profiles']}
    require(recorded_profiles.keys() == matrix_refs.keys(), 'recorded matrix profile names differ')
    for name, native in matrix_refs.items():
        candidate = trigger_work/'matrix'/(name+'.jsonl')
        result = flat_comparison(native, candidate)
        require(not result['failures'], 'matrix '+name+' differs from Unity')
        if args.baseline:
            previous = (args.baseline/'matrix' if (args.baseline/'matrix').exists() else args.baseline/'trigger/matrix')/(name+'.jsonl')
            identical = candidate.read_bytes() == previous.read_bytes()
        else:
            identical = recorded_profiles.get(name, {}).get('byte_identical_to_accepted') is True
        require(identical, 'matrix '+name+' must remain byte-identical to prior acceptance')
        profiles.append(dict(profile=name, records=result['records'], matched=result['matched'], exact=result['exact'],
                             byte_identical_to_accepted=identical))
        dump('matrix/'+name+'-comparison.json', result)
    summary['matrix'] = dict(profiles=profiles, records=sum(p['records'] for p in profiles),
                             matched=sum(p['matched'] for p in profiles), exact=sum(p['exact'] for p in profiles))
    require(summary['matrix']['matched'] == summary['matrix']['records'] == 5055 and summary['matrix']['exact'] == 4803,
            'actual matrix totals must retain 5055 matches and 4803 exact')
    for source in (trigger_work/'matrix').glob('*'):
        if source.is_file(): archive(source, Path('matrix')/source.name)
    for name, (fixture, native, candidate) in fixtures.items():
        with (output/(name+'-comparison.log')).open('w') as log:
            status = subprocess.run([sys.executable, str(fixture/'compare.py'), str(native), str(candidate)],
                                    stdout=log, stderr=subprocess.STDOUT).returncode
        require(status == 0, name+' existing comparator failed')
        summary[name] = dict(comparator_exit=status)
        if candidate.is_file(): archive(candidate, Path('regressions')/candidate.name)
        else:
            for raw in candidate.glob('*.jsonl'): archive(raw, Path('regressions')/name/raw.name)
    lifecycle = trigger_work/'lifecycle.jsonl'
    require(lifecycle.read_bytes() == reference_file(ROOT.parent/'lifecycle', 'jsonl').read_bytes(),
            'lifecycle must remain byte-identical to Unity')
    archive(lifecycle, Path('regressions')/lifecycle.name)
    summary['lifecycle'] = 'byte-identical to Native' if not any('lifecycle must' in x for x in errors) else 'differs'
    tests = ET.parse(trx)
    counters = tests.find('.//{*}Counters').attrib
    require(int(counters['total']) > 0 and counters['total'] == counters['passed'] and
            counters['failed'] == counters['notExecuted'] == '0', 'managed tests must all pass without skips')
    required_classes = TEST_CLASSES+(['Physics2DMotionParityTests'] if combined else [])
    successful_ids = {node.attrib['testId'] for node in tests.findall('.//{*}UnitTestResult') if node.attrib.get('outcome') == 'Passed'}
    classes = {method.attrib['className'] for test in tests.findall('.//{*}UnitTest')
               for method in test.findall('{*}TestMethod') if test.attrib['id'] in successful_ids}
    for name in required_classes:
        prefix = 'UnityEngine.Tests.'+name
        present = any(c.startswith(prefix) if name in ['UIElements', 'YogaLayout'] else c == prefix for c in classes)
        require(present, 'TRX missing passing required test class '+name)
    if combined: require(int(counters['passed']) >= 494, 'combined test gate requires at least the original 494 passing tests')
    summary['tests'] = dict(counters, required_classes=required_classes)
    archive(trx, Path('validation')/trx.name)
    if api:
        text = api.read_text()
        require(re.search(r'\b0 errors in \d+ hand-written types checked\b', text) is not None and
                all(int(n) == 0 for n in re.findall(r'\b(\d+) errors?\b', text)), 'API check must report zero errors')
        summary['api'] = text
        archive(api, Path('validation/api-check.log'))
    else:
        summary['api'] = 'Parent-owned; no API log supplied'
    summary['errors'] = errors
    dump('summary.json', summary)
    print(json.dumps({key:value for key,value in summary.items() if key not in ['matrix', 'focused']}, indent=2))
    print('Gate reports:', output)
    return int(bool(errors))


if __name__ == '__main__':
    raise SystemExit(main())
