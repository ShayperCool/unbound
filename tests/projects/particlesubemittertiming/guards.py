#!/usr/bin/env python3
"""Structural, schema, protocol and run guards for the sub-emitter first-age probe.

    guards.py values VALUES.jsonl --release R --device D     the record's schema/protocol problems
    guards.py capture CAPTURE_DIR --release R --device D     every problem of one Editor capture

The schema mirrors Assets/Scripts/SubEmitterTimingProbe.cs record by record, in emission order
(see `expected`): every key once, of the type the probe writes; every block ends with its
`.log`; a `.exception` (probe fault), a `fatal` record, a missing/extra/duplicate key, a wrong
type or a marker that is not single and last is a problem. Authored inputs (`*.input`,
`phase.*.set`) must be the probe's constants and the relative frame of each phase must be the
predeclared one (protocol). No particle, count, state or clock VALUE is gated: the answers to the
first-age hypotheses, their order and the actual clocks are data, never guards.

Every function returns ALL problems it finds (a list); none stops at the first.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys

MARKER = {'k': 'complete', 'v': True}
OUTPUT_VARIABLE = 'UNBOUND_PROBE_PARTICLESUBEMITTERTIMING_OUT'

# Authored inputs, as the probe writes them (SubEmitterTimingProbe.cs).
DEATH_CASES = {  # key: (callDelta, parentLife, relative frame)
    'case.baseline': (0.025, 0.013, 0),
    'case.d1': (0.035, 0.013, 0),
    'case.d2': (0.05, 0.038, 0),
    'case.d3': (0.025, 0.013, 2),
    'case.fixed': (0.025, 0.013, 4),
}
STARTUP_CASES = ('case.baseline', 'case.d1', 'case.d2')
BIRTH = 'case.birth'
BIRTH_CALLS = 15
STEPS = ('before', 'after1', 'after2')
SYSTEMS = ('parent', 'death', 'ordinary')
CAPTURE_DELTA = 1/30
SLOW_TIME_SCALE = 0.5
FIXED_CONTROL = 0.0075
BOUNDARY_FRAMES = 2
# The relative frame of each clock read (protocol: the phases happen at predeclared frames).
FRAME = {'clock.startup.clocks': 0, 'phase.d3.readback': 0, 'phase.fixed.readback': 2, 'restore.readback': 4}

CLOCK_FLOATS = ('deltaTime', 'unscaledDeltaTime', 'fixedDeltaTime', 'fixedUnscaledDeltaTime', 'captureDeltaTime',
                'timeScale', 'maximumDeltaTime', 'maximumParticleDeltaTime')
CLOCK_KEYS = set(CLOCK_FLOATS) | {'captureFramerate', 'inFixedTimeStep', 'frameOffset'}
STATE_KEYS = {'count', 'readCount', 'time', 'playing', 'emitting', 'stopped', 'paused', 'alive', 'particles'}
PARTICLE_KEYS = {'pos', 'vel', 'totalVel', 'animatedVel', 'axis', 'life', 'seed', 'startLife', 'startSize', 'size',
                 'startColor', 'color', 'rot', 'angVel'}
VECTORS = ('pos', 'vel', 'totalVel', 'animatedVel', 'axis', 'startSize', 'size', 'rot', 'angVel')

# Unexpected-error log lines: compile errors and native faults.
EDITOR_LOG_FATAL = [
    r'error CS\d{4}', r'Scripts have compiler errors', r'Caught fatal signal', r'Got a SIGSEGV',
    r'Native Crash Reporting', r'Exiting early due to double fault', r'Assertion failed on expression',
    r'Crash!!!', r'Aborting batchmode due to failure',
]
UNSCOPED_ERROR_TYPES = ('Error:', 'Exception:', 'Assert:')

# Runner transitions that must occur exactly once, in this order (ProbeRunner.cs).
RUNNER_SEQUENCE = ['record', 'enter', 'finished', 'leave', 'exit']
RUNNER_OPTIONAL = {'domain', 'state', 'unpause'}


# ---------------------------------------------------------------- record types

def is_str(v): return isinstance(v, str)
def is_bool(v): return isinstance(v, bool)
def is_int(v): return isinstance(v, int) and not isinstance(v, bool)
def is_strings(v): return isinstance(v, list) and all(isinstance(i, str) for i in v)
def is_float(v): return (isinstance(v, (int, float)) and not isinstance(v, bool)) or v in ('NaN', 'Infinity', '-Infinity')
def is_vector(v): return isinstance(v, list) and len(v) == 3 and all(is_float(i) for i in v)
def is_color32(v): return isinstance(v, list) and len(v) == 4 and all(is_int(i) and 0 <= i <= 255 for i in v)
def is_true(v): return v is True


def is_clocks(v):
    return (isinstance(v, dict) and set(v) == CLOCK_KEYS and all(is_float(v[k]) for k in CLOCK_FLOATS)
            and is_int(v['captureFramerate']) and is_bool(v['inFixedTimeStep']) and is_int(v['frameOffset']))


def is_particle(p):
    return (isinstance(p, dict) and set(p) == PARTICLE_KEYS and all(is_vector(p[k]) for k in VECTORS)
            and is_float(p['life']) and is_float(p['startLife']) and is_int(p['seed']) and p['seed'] >= 0
            and is_color32(p['startColor']) and is_color32(p['color']))


def is_state(v):
    return (isinstance(v, dict) and set(v) == STATE_KEYS and is_int(v['count']) and is_int(v['readCount'])
            and is_float(v['time']) and all(is_bool(v[k]) for k in ('playing', 'emitting', 'stopped', 'paused', 'alive'))
            and isinstance(v['particles'], list) and len(v['particles']) == v['readCount']
            and all(is_particle(p) for p in v['particles']))


def f32(x):
    """x rounded to float32 (the probe prints float32 values; the text is parsed as a double)."""
    import struct
    return struct.unpack('<f', struct.pack('<f', x))[0]


def same_authored(value, want):
    """Authored-input equality: the same keys/lengths and types; floats equal as float32."""
    if isinstance(want, bool) or want is None or isinstance(want, str):
        return type(value) is type(want) and value == want
    if isinstance(want, dict):
        return isinstance(value, dict) and set(value) == set(want) and all(same_authored(value[k], want[k]) for k in want)
    if isinstance(want, list):
        return isinstance(value, list) and len(value) == len(want) and all(same_authored(a, b) for a, b in zip(value, want))
    if isinstance(want, int) and not isinstance(value, float):
        return is_int(value) and value == want
    return is_float(value) and not isinstance(value, str) and f32(value) == f32(want)


def equals(want):
    def check(v):
        return same_authored(v, want)
    check.value = want
    return check


def death_input(call, life):
    return {'callDelta': call, 'calls': 2, 'parentLife': life, 'parentPosition': [0, 0, 0.46],
            'parentVelocity': [0, 0, 0], 'deathChildSpeed': 1, 'deathChildLifetime': 2.013, 'deathBurst': 1,
            'ordinarySpeed': 1, 'ordinaryLifetime': 2.013, 'withChildren': True, 'restart': False,
            'fixedTimeStep': False, 'seed': 12345}


BIRTH_INPUT = {'source': 'tests/projects/particles ParticlesProbe sub_emitters_birth', 'callDelta': 0.05,
               'calls': BIRTH_CALLS, 'withChildren': True, 'restartFirst': True, 'fixedTimeStep': False}


# ---------------------------------------------------------------- expected emission

def expected():
    """The ordered expectations: (kind, key, check); kind is read, block or marker."""
    items = []

    def read(key, check):
        items.append(('read', key, check))

    def block(key, body):
        body()
        items.append(('block', key, None))

    def context():
        read('context.unityVersion', is_str)
        read('context.isBatchMode', is_bool)
        read('context.platform', is_str)
        read('context.graphicsDeviceType', is_str)
    block('context', context)
    block('clock.startup', lambda: read('clock.startup.clocks', is_clocks))

    def death(key):
        call, life, _ = DEATH_CASES[key]

        def body():
            read(key+'.input', equals(death_input(call, life)))
            read(key+'.clock.before', is_clocks)
            for step in STEPS:
                for system in SYSTEMS:
                    read(f'{key}.{system}.{step}', is_state)
            read(key+'.clock.after', is_clocks)
        block(key, body)
    for key in STARTUP_CASES:
        death(key)

    def birth():
        read(BIRTH+'.input', equals(BIRTH_INPUT))
        read(BIRTH+'.clock.before', is_clocks)
        for i in range(BIRTH_CALLS):
            read(f'{BIRTH}.parent.{i}', is_state)
            read(f'{BIRTH}.child.{i}', is_state)
        read(BIRTH+'.clock.after', is_clocks)
    block(BIRTH, birth)

    def d3_phase():
        read('phase.d3.set', equals({'captureDeltaTime': CAPTURE_DELTA, 'timeScale': SLOW_TIME_SCALE,
                                     'boundaryFrames': BOUNDARY_FRAMES}))
        read('phase.d3.readback', is_clocks)
    block('phase.d3', d3_phase)
    death('case.d3')

    def fixed_phase():
        read('phase.fixed.set', equals({'fixedDeltaTime': FIXED_CONTROL, 'boundaryFrames': BOUNDARY_FRAMES}))
        read('phase.fixed.readback', is_clocks)
    block('phase.fixed', fixed_phase)
    death('case.fixed')
    block('restore', lambda: read('restore.readback', is_clocks))
    read('capture.unscopedLogs', is_strings)
    items.append(('marker', 'complete', is_true))
    return items


def frame_expectations():
    """Every clock read and the relative frame it must be taken in (protocol)."""
    frames = dict(FRAME)
    for key, (_, _, frame) in DEATH_CASES.items():
        frames[key+'.clock.before'] = frame
        frames[key+'.clock.after'] = frame
    frames[BIRTH+'.clock.before'] = 0
    frames[BIRTH+'.clock.after'] = 0
    return frames


# ---------------------------------------------------------------- record guard

def parse(path):
    """(records, problems): every line must be one {"k": str, "v": ...} object."""
    problems, records = [], []
    try:
        text = Path(path).read_text()
    except OSError as error:
        return [], [f'unreadable record: {error}']
    if text and not text.endswith('\n'):
        problems.append('the record does not end with a newline (truncated)')
    for number, line in enumerate(text.split('\n')[:-1] if text.endswith('\n') else text.split('\n'), 1):
        try:
            row = json.loads(line)
        except json.JSONDecodeError as error:
            problems.append(f'line {number}: not JSON ({error})')
            continue
        if not isinstance(row, dict) or set(row) != {'k', 'v'} or not isinstance(row['k'], str):
            problems.append(f'line {number}: not a {{"k","v"}} record')
            continue
        records.append(row)
    return records, problems


def record_problems(path, release, device):
    """Every schema/structure/protocol/context problem of a values.jsonl (empty: it can back a reference)."""
    records, problems = parse(path)
    if not records:
        return problems+['the record is empty']
    keys = [r['k'] for r in records]
    seen, duplicates = set(), []
    for k in keys:
        if k in seen:
            duplicates.append(k)
        seen.add(k)
    problems += [f'duplicate key {k}' for k in dict.fromkeys(duplicates)]
    if records[-1] != MARKER:
        problems.append('the completion marker is not the last line')
    if records.count(MARKER) != 1:
        problems.append(f'{records.count(MARKER)} completion markers')
    if 'fatal' in seen:
        problems.append('the probe recorded a fatal error: '+str(next(r['v'] for r in records if r['k'] == 'fatal'))[:300])
    items = expected()
    index = 0
    for kind, key, check in items:
        here = records[index]['k'] if index < len(records) else None
        wanted = {key+'.log', key+'.exception'} if kind == 'block' else {key}
        if here not in wanted:
            ahead = next((j for j in range(index, len(records)) if records[j]['k'] in wanted), None)
            if ahead is not None:
                problems.extend(f'unexpected record {r["k"]}' for r in records[index:ahead])
                index = ahead
                here = records[index]['k']
        if kind == 'block':
            if here == key+'.exception':
                problems.append(f'probe fault in block {key}: {records[index]["v"]}')
                index += 1
                here = records[index]['k'] if index < len(records) else None
            if here != key+'.log':
                problems.append(f'missing {key}.log (found {here!r})')
                continue
            if not is_strings(records[index]['v']):
                problems.append(f'{key}.log: not a list of strings')
            index += 1
            continue
        if here != key:
            problems.append(f'missing {key} (found {here!r})')
            continue
        if not check(records[index]['v']):
            want = getattr(check, 'value', None)
            problems.append(f'{key}: wrong type or authored input {json.dumps(records[index]["v"])[:160]}'
                            + (f' (probe constant {json.dumps(want)[:160]})' if want is not None else ''))
        index += 1
    if index < len(records):
        extra = [r['k'] for r in records[index:]]
        problems.append(f'{len(extra)} unexpected records after the schema: {extra[:8]}')
    expected_keys = set()
    for kind, key, _ in items:
        expected_keys |= {key+'.log'} if kind == 'block' else {key}
    unknown = [k for k in keys if k not in expected_keys and not k.endswith('.exception') and k != 'fatal']
    if unknown:
        problems.append(f'{len(unknown)} keys outside the schema: {unknown[:8]}')
    values = {}
    for r in records:
        values.setdefault(r['k'], r['v'])
    for key, frame in frame_expectations().items():
        clocks = values.get(key)
        if is_clocks(clocks) and clocks['frameOffset'] != frame:
            problems.append(f'{key} was read at relative frame {clocks["frameOffset"]}, not the predeclared {frame}')
    want = {'context.unityVersion': release, 'context.isBatchMode': True, 'context.graphicsDeviceType': device}
    for key, value in want.items():
        if key not in values or values[key] != value:
            problems.append(f'{key} is {json.dumps(values.get(key))}, not {json.dumps(value)}')
    unscoped = values.get('capture.unscopedLogs')
    if is_strings(unscoped):
        bad = [line for line in unscoped if line.startswith(UNSCOPED_ERROR_TYPES)]
        if bad:
            problems.append(f'unexpected errors outside the probe cases: {bad[:5]}')
    return problems


def diagnostics_problems(path):
    """The raw diagnostics (absolute frames and real times) must exist and parse; they are not compared."""
    try:
        rows = [json.loads(line) for line in Path(path).read_text().splitlines()]
    except (OSError, json.JSONDecodeError) as error:
        return [f'diagnostics unreadable: {error}']
    whats = [r.get('what') for r in rows]
    want = ['start', 'boundary d3 1', 'boundary d3 2', 'case.d3', 'boundary fixed 1', 'boundary fixed 2',
            'case.fixed', 'written']
    return [] if whats == want else [f'diagnostics phases {whats} instead of {want}']


# ---------------------------------------------------------------- run guards

def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def tree(root):
    root = Path(root)
    return {str(p.relative_to(root)): sha(p) for p in sorted(root.rglob('*')) if p.is_file()}


def editor_result_problems(directory, release):
    """Strict exit 0: no forced stop of any kind (not even the shutdown guard), NVIDIA, logged
    shutdown, the planned batchmode GPU command."""
    path = Path(directory)/'editor-run.json'
    if not path.exists():
        return ['no Editor result (editor-run.json)']
    result = json.loads(path.read_text())
    problems = []
    if result.get('forced_exit'):
        problems.append(f"forced exit {result['forced_exit']}: {result.get('forced_detail')}")
    if result.get('exit_code') != 0:
        problems.append(f"Editor exit {result.get('exit_code')} (signal {result.get('signal')})")
    if 'NVIDIA' not in (result.get('renderer') or '') or result.get('renderer_ok') is not True:
        problems.append(f"renderer {result.get('renderer')!r} is not the NVIDIA GPU")
    if not result.get('shutdown_logged'):
        problems.append('the Editor log has no shutdown line')
    if result.get('kind') != 'editor-gpu' or result.get('require_gpu') is not True:
        problems.append(f"kind {result.get('kind')} / require_gpu {result.get('require_gpu')}")
    launcher = result.get('launcher') or {}
    if launcher.get('exit') != 0:
        problems.append(f"launcher exit {launcher.get('exit')}")
    problems += command_problems(result.get('command') or [], release)
    return problems


def command_problems(command, release):
    problems = []
    if command[:3] != ['vglrun', '-d', 'egl']:
        problems.append(f'not a vglrun EGL command: {command[:3]}')
    editor = command[3] if len(command) > 3 else ''
    if not editor.endswith('/Editor/Unity') or f'/{release}/' not in editor:
        problems.append(f'Editor {editor!r} is not release {release}')
    if '-batchmode' not in command:
        problems.append('-batchmode is missing')
    for flag in ('-nographics', '-quit'):
        if flag in command:
            problems.append(f'{flag} must not be passed')
    pairs = list(zip(command, command[1:]))
    if ('-executeMethod', 'ProbeRunner.Record') not in pairs:
        problems.append('-executeMethod ProbeRunner.Record is missing')
    if command.count('-executeMethod') != 1:
        problems.append('more than one -executeMethod')
    return problems


def input_problems(directory, release):
    """The prelaunch receipt binds the release, the capture variable and the prepared inputs."""
    directory = Path(directory)
    problems = []
    try:
        receipt = json.loads((directory/'prelaunch-inputs.json').read_text())
        prepared = json.loads((directory/'inputs.json').read_text())
    except (OSError, json.JSONDecodeError) as error:
        return [f'no prelaunch/prepared input manifest: {error}']
    if receipt.get('selected_release') != release:
        problems.append(f"selected release {receipt.get('selected_release')} is not {release}")
    env = receipt.get('environment', {})
    if env.get(OUTPUT_VARIABLE) != str((directory/'capture').resolve()):
        problems.append(f"capture variable {env.get(OUTPUT_VARIABLE)!r} is not this capture")
    if env.get('UNBOUND_PROBE_UNITY_VERSION') != release:
        problems.append(f"UNBOUND_PROBE_UNITY_VERSION {env.get('UNBOUND_PROBE_UNITY_VERSION')!r}")
    launched = {k: v['sha256'] for k, v in receipt.get('files', {}).items()}
    for key, digest in prepared['files'].items():
        if launched.get(key) != digest:
            problems.append(f'launched input {key} differs from the prepared fixture')
    extra = sorted(k for k in launched if k.split('/', 1)[0] in ('assets', 'packages', 'settings') and k not in prepared['files'])
    if extra:
        problems.append(f'launched project inputs not in the prepared fixture: {extra[:5]}')
    return problems


def runner_event_problems(path):
    """record, enter, finished 0, leave, exit 0 exactly once and in order; contiguous sequence."""
    try:
        events = [json.loads(line) for line in Path(path).read_text().splitlines()]
    except (OSError, json.JSONDecodeError) as error:
        return [f'runner events unreadable: {error}']
    problems = []
    if [e.get('seq') for e in events] != list(range(len(events))):
        problems.append('runner event sequence numbers are not contiguous from 0')
    names = [e.get('e') for e in events]
    core = [n for n in names if n not in RUNNER_OPTIONAL]
    if core != RUNNER_SEQUENCE:
        problems.append(f'runner transitions {core} instead of {RUNNER_SEQUENCE}')
    detail = {e.get('e'): e.get('detail') for e in events}
    if detail.get('finished') != '0':
        problems.append(f"probe finished with {detail.get('finished')!r}")
    if not str(detail.get('exit')).startswith('0 '):
        problems.append(f"runner exit {detail.get('exit')!r}")
    if names and names[-1] != 'exit':
        problems.append(f'the last runner event is {names[-1]!r}, not exit')
    record = detail.get('record') or ''
    if 'batchmode=true' not in record:
        problems.append(f'the runner did not record in batchmode: {record!r}')
    return problems


def log_problems(path):
    try:
        lines = Path(path).read_text(errors='replace').splitlines()
    except OSError as error:
        return [f'Editor log unreadable: {error}']
    found = []
    for number, line in enumerate(lines, 1):
        for pattern in EDITOR_LOG_FATAL:
            if re.search(pattern, line):
                found.append(f'Editor log {number}: {line.strip()[:160]}')
                break
    if not any(line.startswith('Renderer:') for line in lines):
        found.append('the Editor log names no renderer')
    return found


def authored_delta(directory, project):
    """Authored files changed, removed or added by the Editor (assets, packages, settings)."""
    prepared = json.loads((Path(directory)/'inputs.json').read_text())['files']
    now = {}
    for label, part in (('assets', 'Assets'), ('packages', 'Packages'), ('settings', 'ProjectSettings')):
        now.update({label+'/'+k: v for k, v in tree(Path(project)/part).items()})
    return {'changed': sorted(k for k in prepared if k in now and now[k] != prepared[k]),
            'removed': sorted(k for k in prepared if k not in now),
            'added': sorted(k for k in now if k not in prepared)}


def authored_problems(directory, project):
    """Changed or removed fixture files under Assets are problems; settings/package changes and
    Editor-written files (script .meta files) are listed in authored-delta.json, not refused."""
    delta = authored_delta(directory, project)
    return [f'fixture file {k} changed or was removed by the Editor'
            for k in delta['changed']+delta['removed'] if k.startswith('assets/')]


def capture_problems(directory, project, release, device):
    """Every problem of one Editor capture directory (empty: it passes the strict gate)."""
    directory = Path(directory)
    capture = directory/'capture'
    problems = editor_result_problems(directory, release)
    problems += input_problems(directory, release)
    if (capture/'watchdog.txt').exists():
        problems.append('the Editor-side watchdog fired')
    if (capture/'values.jsonl.partial').exists():
        problems.append('a partial record was left behind')
    problems += runner_event_problems(capture/'runner-events.jsonl')
    problems += log_problems(directory/'editor.log')
    problems += [f'record: {p}' for p in record_problems(capture/'values.jsonl', release, device)]
    problems += [f'diagnostics: {p}' for p in diagnostics_problems(capture/'diagnostics.jsonl')]
    if project is not None and Path(project).exists():
        problems += authored_problems(directory, project)
    return problems


# ---------------------------------------------------------------- compile graph

DAG = re.compile(r'Library/Bee/artifacts/[^/"]+\.dag/')


def normalized(text):
    """A response file with the build graph's directory name made neutral."""
    return DAG.sub('Library/Bee/artifacts/<dag>/', text)


def actual_graph(project):
    """The Editor-written compile inputs of a launched project copy."""
    dags = sorted((Path(project)/'Library/Bee/artifacts').glob('*.dag'))
    dags = [d for d in dags if (d/'Assembly-CSharp.rsp').is_file()]
    if len(dags) != 1:
        return None, [f'{len(dags)} Bee graph directories with Assembly-CSharp.rsp in {project}']
    graph = {}
    for assembly in ('Assembly-CSharp', 'Assembly-CSharp-Editor'):
        for suffix in ('.rsp', '.rsp2', '.UnityAdditionalFile.txt'):
            path = dags[0]/(assembly+suffix)
            graph[assembly+suffix] = path.read_text() if path.is_file() else None
    return graph, []


def graph_problems(project, preflight_record):
    """The launched Editor's actual response files must be the preflight's rebound ones: the same
    sources, references, defines, analyzers and options. Only the build-graph directory name is
    neutral and the additional file names the project copy."""
    graph, problems = actual_graph(project)
    if graph is None:
        return problems
    for assembly, result in preflight_record.get('results', {}).items():
        for suffix, field in (('.rsp', 'rsp'), ('.rsp2', 'rsp2')):
            want = result.get(field, {}).get('rebound_text')
            got = graph.get(assembly+suffix)
            if got is None:
                problems.append(f'the launched copy has no {assembly}{suffix}')
            elif want is None or normalized(got) != normalized(want):
                problems.append(f'{assembly}{suffix} differs from the preflight-compiled graph')
        extra = graph.get(assembly+'.UnityAdditionalFile.txt')
        if extra is None or Path(extra.strip()).resolve() != Path(project).resolve():
            problems.append(f'{assembly}.UnityAdditionalFile.txt does not name the launched copy: {extra!r}')
    if set(preflight_record.get('results', {})) != {'Assembly-CSharp', 'Assembly-CSharp-Editor'}:
        problems.append('the preflight did not compile both assemblies')
    return problems


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('what', choices=('values', 'capture'))
    parser.add_argument('path', type=Path)
    parser.add_argument('--release', required=True)
    parser.add_argument('--device', required=True)
    parser.add_argument('--project', type=Path)
    args = parser.parse_args()
    if args.what == 'values':
        problems = record_problems(args.path, args.release, args.device)
    else:
        problems = capture_problems(args.path, args.project, args.release, args.device)
    for p in problems:
        print(p)
    print('passes' if not problems else f'{len(problems)} problems')
    return int(bool(problems))


if __name__ == '__main__':
    sys.exit(main())
