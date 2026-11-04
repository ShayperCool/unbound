#!/usr/bin/env python3
"""Structural, schema, protocol, context and run guards for the activation mechanism probe.

    guards.py values VALUES.jsonl --release R --device D     the record's schema/protocol problems
    guards.py capture CAPTURE_DIR --release R --device D     every problem of one Editor capture

The schema mirrors Assets/Scripts/ActivationMechanismsProbe.cs record by record, in emission order
(`expected`): every read once, under its key or under key.exception; a step only as
step.exception; every block ends with block.log (a block.exception is a probe fault); the record
ends with capture.strayCalls, capture.unscopedLogs and the single completion marker. Values must
have the type the probe writes. Each request must be the probe's authored constant. A set-up or
built-in case that could not build its nodes, Physics2D globals not restored, wrong context,
probe faults, unscoped error logs and fatal records are problems.

No callback order, callback presence, count, flag, contact/touching state, particle state,
simulation result, stray callback, returned value or per-read exception is a guard: every
mechanism hypothesis (and every physical outcome, including "no contact") is data, never a gate.

Every function returns ALL problems it finds (a list); none stops at the first.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys

MARKER = {'k': 'complete', 'v': True}
OUTPUT_VARIABLE = 'UNBOUND_PROBE_ACTIVATIONMECHANISMS_OUT'

# The probe's constants (ActivationMechanismsProbe.cs); Controls/controls.py checks they are equal.
DRIVER = 'driver'
UNLABELLED = 'unlabelled'
RECORDER, OBSERVER, DRIVER_TYPE = 'recorder', 'observer', 'driver'
OBSERVER_SLOT, DRIVER_SLOT = 'o', 'd'
SCENE_LABELS = ('S', 'S1', 'S2', 'S21', 'S3')
NODES = (('base', 'P', None, True), ('base', 'C1', 'P', True), ('base', 'G', 'C1', True), ('base', 'C2', 'P', True),
         ('base', 'U', None, True),
         ('offA', 'A', None, True), ('offA', 'A1', 'A', True), ('offA', 'A11', 'A1', True), ('offA', 'A2', 'A', True),
         ('offA', 'A21', 'A2', True),
         ('mirror', 'M', None, True), ('mirror', 'M1', 'M', True), ('mirror', 'M11', 'M1', True), ('mirror', 'M2', 'M', True),
         ('mirror', 'M21', 'M2', True),
         ('on1', 'R', None, True), ('on1', 'RY', 'R', True), ('on1', 'RX', 'R', False), ('on1', 'RZ', 'R', True),
         ('on3', 'L', None, True), ('on3', 'LY', 'L', True), ('on3', 'LX', 'L', False), ('on3', 'LZ', 'L', True))
CREATE_ORDER = ('G', 'C2', 'P', 'C1', 'U', 'A21', 'A1', 'A', 'A11', 'A2', 'M11', 'M2', 'M', 'M21', 'M1',
                'RZ', 'RX', 'R', 'RY', 'LZ', 'LX', 'L', 'LY')
ADD_ORDER = ('C2', 'C1', 'P', 'G', 'U', 'A1', 'A21', 'A', 'A11', 'A2', 'M2', 'M11', 'M', 'M21', 'M1',
             'RX', 'R', 'RZ', 'RY', 'LX', 'L', 'LZ', 'LY')
WAKE_ORDER = ('RX', 'LX')
TOGGLE_ORDER = ('L',)
SCENE_ADDS = (('S1', 'rt'),)
OPS = (('baseOff', 'base', 'P', False), ('baseOn', 'base', 'P', True),
       ('offAOff', 'offA', 'A', False), ('offAOn', 'offA', 'A', True),
       ('mirrorOff', 'mirror', 'M', False), ('mirrorOn', 'mirror', 'M', True),
       ('on1Off', 'on1', 'R', False), ('on1On', 'on1', 'R', True),
       ('on3Off', 'on3', 'L', False), ('on3On', 'on3', 'L', True),
       ('on2Off', 'scene', 'S', False), ('on2On', 'scene', 'S', True))
CONTACTS = (('solidOnScripts', False, True, True), ('solidOnBuiltins', False, True, False),
            ('triggerOnScripts', True, True, True), ('triggerOnBuiltins', True, True, False),
            ('solidOffScripts', False, False, True), ('solidOffBuiltins', False, False, False),
            ('triggerOffScripts', True, False, True), ('triggerOffBuiltins', True, False, False))
PARTICLES = (('psScripts', True), ('psBuiltins', False))
EMITTED = (2, 3)
SETTLE_FRAMES = 2
END_FRAMES = 5

LABELS = tuple(n[1] for n in NODES)
CORE_LABELS = LABELS + SCENE_LABELS


def contact_labels(index):
    return (f'B{index}P', f'B{index}C', f'B{index}E')


def particle_labels(index):
    return (f'F{index}P', f'F{index}C')


ALL_LABELS = set(CORE_LABELS) | {lab for i in range(len(CONTACTS)) for lab in contact_labels(i)} \
    | {lab for i in range(len(PARTICLES)) for lab in particle_labels(i)}


def tree_labels(tree):
    return SCENE_LABELS if tree == 'scene' else tuple(n[1] for n in NODES if n[0] == tree)


RECORDER_CALLBACKS = ('Awake', 'OnEnable', 'Start', 'OnDisable')
OBSERVER_CALLBACKS = ('OnCollisionEnter2D', 'OnCollisionExit2D', 'OnTriggerEnter2D', 'OnTriggerExit2D')
CALLBACKS = {RECORDER: RECORDER_CALLBACKS, OBSERVER: OBSERVER_CALLBACKS, DRIVER_TYPE: ('Start',)}
ENTRY_KEYS = ['n', 'r', 't', 'cb', 'other', 'contacts', 'self', 'peer']
PHYSICS_KEYS = ['colliderEnabled', 'colliderActive', 'bodySimulated', 'attached', 'partnerActive', 'touching',
                'colliderContacts', 'partnerContacts']
PARTICLE_KEYS = ['isPlaying', 'isEmitting', 'isPaused', 'isStopped', 'particleCount']

GROUPS = ('context', 'boot', 'setup', 'settle', 'op', 'contact', 'particles', 'capture')

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
def is_true(v): return v is True
def is_exception(v): return isinstance(v, dict) and list(v) == ['exception'] and is_str(v['exception'])


def is_label(v):
    return v in ALL_LABELS or v in (DRIVER, UNLABELLED)


def is_flags(v):
    return isinstance(v, list) and len(v) == 4 and all(is_bool(b) for b in v)


def is_field(v, check):
    return check(v) or is_exception(v)


def is_physics(v):
    return isinstance(v, dict) and list(v) == PHYSICS_KEYS and all(
        is_field(v[k], is_int if k.endswith('Contacts') else is_bool) for k in PHYSICS_KEYS)


def is_particle_set(v):
    return isinstance(v, dict) and all(is_label(k) for k in v) and all(
        isinstance(s, dict) and list(s) == PARTICLE_KEYS
        and all(is_field(s[k], is_int if k == 'particleCount' else is_bool) for k in PARTICLE_KEYS)
        for s in v.values())


def is_peer(v):
    return v is None or is_exception(v) or is_physics(v) or is_particle_set(v)


def is_entry(v):
    if not (isinstance(v, dict) and list(v) == ENTRY_KEYS):
        return False
    return (is_label(v['n']) and is_str(v['r']) and v['t'] in CALLBACKS and v['cb'] in CALLBACKS[v['t']]
            and (v['other'] is None or is_str(v['other'])) and (v['contacts'] is None or is_int(v['contacts']))
            and is_flags(v['self']) and is_peer(v['peer']))


def is_calls(v):
    return isinstance(v, list) and all(is_entry(e) for e in v)


def is_strays(v):
    return isinstance(v, list) and all(isinstance(s, dict) and list(s) == ['at', 'calls'] and is_str(s['at'])
                                       and is_calls(s['calls']) for s in v)


STATE_KEYS = ['activeSelf', 'activeInHierarchy', 'parent', 'sibling', 'recorders']
RECORDER_STATE_KEYS = ['r', 'enabled', 'isActiveAndEnabled', 'counts']


def is_state(v):
    return (isinstance(v, dict) and list(v) == STATE_KEYS and is_bool(v['activeSelf']) and is_bool(v['activeInHierarchy'])
            and (v['parent'] is None or is_label(v['parent'])) and is_int(v['sibling'])
            and isinstance(v['recorders'], list) and all(
                isinstance(r, dict) and list(r) == RECORDER_STATE_KEYS and is_str(r['r']) and is_bool(r['enabled'])
                and is_bool(r['isActiveAndEnabled']) and isinstance(r['counts'], dict)
                and list(r['counts']) == list(RECORDER_CALLBACKS)
                and all(is_int(c) and c >= 0 for c in r['counts'].values()) for r in v['recorders']))


def is_tree(v):
    return isinstance(v, list) and all(
        isinstance(r, dict) and list(r) == ['n', 'parent', 'sibling', 'depth', 'children', 'components'] and is_label(r['n'])
        and (r['parent'] is None or is_label(r['parent'])) and all(is_int(r[k]) for k in ('sibling', 'depth', 'children'))
        and is_strings(r['components']) for r in v)


def is_case_state(labels, key, check):
    def ok(v):
        return (isinstance(v, dict) and list(v) == ['nodes', key] and isinstance(v['nodes'], dict)
                and list(v['nodes']) == list(labels) and all(is_state(s) or is_exception(s) for s in v['nodes'].values())
                and is_field(v[key], check))
    return ok


def is_components(labels):
    return lambda v: isinstance(v, dict) and list(v) == list(labels) and all(is_strings(c) for c in v.values())


def is_globals(v):
    return isinstance(v, dict) and list(v) == ['simulationMode', 'callbacksOnDisable'] and is_str(v['simulationMode']) \
        and is_bool(v['callbacksOnDisable'])


def op_request(op):
    return {'target': op[2], 'value': op[3]}


def contact_request(case, index):
    return {'trigger': case[1], 'callbacksOnDisable': case[2], 'layout': 'scriptsFirst' if case[3] else 'builtinsFirst',
            'target': contact_labels(index)[0]}


def particle_request(case, index):
    return {'layout': 'scriptsFirst' if case[1] else 'builtinsFirst', 'target': particle_labels(index)[0]}


# ---------------------------------------------------------------- expected emission

def expected():
    """The ordered expectations of a record (the probe's emission order): (kind, key, check)."""
    items = []

    def read(key, check): items.append(('read', key, check))
    def step(key): items.append(('step', key, None))
    def value(key, check): items.append(('value', key, check))
    def block(key): items.append(('block', key, None))

    for key, check in (('unityVersion', is_str), ('isBatchMode', is_bool), ('platform', is_str),
                       ('graphicsDeviceType', is_str)):
        read('context.'+key, check)
    block('context')
    value('boot.calls', is_calls)
    read('boot.sceneRoots', is_strings)
    step('boot.step[find]')
    read('boot.scene', is_tree)
    block('boot')
    for label in CREATE_ORDER:
        step(f'setup.{label}.step[create]')
    for _, label, parent, _ in NODES:
        if parent is not None:
            step(f'setup.{label}.step[parent]')
    for _, label, _, active in NODES:
        if not active:
            step(f'setup.{label}.step[deactivate]')
    for label in ADD_ORDER:
        step(f'setup.{label}.step[recorder]')
    for label in WAKE_ORDER:
        step(f'setup.{label}.step[wake]')
    for label in TOGGLE_ORDER:
        step(f'setup.{label}.step[toggle]')
    for label, slot in SCENE_ADDS:
        step(f'setup.{label}.step[recorder:{slot}]')
    value('setup.calls', is_calls)
    read('setup.tree', is_tree)
    block('setup')
    value('settle.calls', is_calls)
    read('settle.relativeFrame', is_int)
    for label in CORE_LABELS:
        read(f'settle.state.{label}', is_state)
    block('settle')
    for op in OPS:
        key = f'op.{op[0]}'
        want = op_request(op)
        value(key+'.request', lambda v, want=want: v == want)
        read(key+'.relativeFrame', is_int)
        for label in tree_labels(op[1]):
            read(f'{key}.before.{label}', is_state)
        read(key+'.returned', is_str)
        value(key+'.calls', is_calls)
        for label in tree_labels(op[1]):
            read(f'{key}.after.{label}', is_state)
        block(key)
    for index, case in enumerate(CONTACTS):
        key = f'contact.{case[0]}'
        labels = contact_labels(index)
        want = contact_request(case, index)
        value(key+'.request', lambda v, want=want: v == want)
        read(key+'.globals', is_globals)
        for s in ('globals', 'build', 'components'):
            step(f'{key}.step[{s}]')
        value(key+'.setup.calls', is_calls)
        read(key+'.components', is_components(labels))
        for s in (1, 2):
            read(f'{key}.sim{s}.returned', is_bool)
            value(f'{key}.sim{s}.calls', is_calls)
        read(key+'.before', is_case_state(labels, 'physics', is_physics))
        read(key+'.returned', is_str)
        value(key+'.calls', is_calls)
        read(key+'.after', is_case_state(labels, 'physics', is_physics))
        read(key+'.sim3.returned', is_bool)
        value(key+'.sim3.calls', is_calls)
        read(key+'.final', is_case_state(labels, 'physics', is_physics))
        step(key+'.step[restore]')
        read(key+'.restored', is_globals)
        block(key)
    for index, case in enumerate(PARTICLES):
        key = f'particles.{case[0]}'
        labels = particle_labels(index)
        want = particle_request(case, index)
        value(key+'.request', lambda v, want=want: v == want)
        for s in ('build', 'components', 'configure'):
            step(f'{key}.step[{s}]')
        value(key+'.setup.calls', is_calls)
        read(key+'.components', is_components(labels))
        step(key+'.step[emit]')
        read(key+'.before', is_case_state(labels, 'particles', is_particle_set))
        read(key+'.returned', is_str)
        value(key+'.calls', is_calls)
        read(key+'.after', is_case_state(labels, 'particles', is_particle_set))
        block(key)
    value('capture.strayCalls', is_strays)
    read('capture.unscopedLogs', is_strings)
    items.append(('marker', 'complete', is_true))
    return items


def schema_keys():
    """Every key the schema admits (reads and their .exception, steps' .exception, block logs)."""
    keys = set()
    for kind, key, _ in expected():
        if kind == 'block':
            keys |= {key+'.log', key+'.exception'}
        elif kind == 'step':
            keys.add(key+'.exception')
        elif kind == 'read':
            keys |= {key, key+'.exception'}
        else:
            keys.add(key)
    return keys


# The steps and reads that build a case's nodes: an exception there means the probe could not set
# up what it measures (a structural failure, not an answer).
SETUP_FAILURE = re.compile(r'^(setup\..*|boot\.step\[find\]|boot\.scene|setup\.tree|'
                           r'(contact|particles)\.\w+\.(step\[(globals|build|components|configure|emit|restore)\]|components))'
                           r'\.exception$')


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
    values = {}
    for r in records:
        values.setdefault(r['k'], r['v'])
    index = 0

    def resync(wanted):
        nonlocal index
        ahead = next((j for j in range(index, len(records)) if records[j]['k'] in wanted), None)
        if ahead is None:
            return False
        problems.extend(f'unexpected record {r["k"]}' for r in records[index:ahead])
        index = ahead
        return True

    for kind, key, check in expected():
        here = records[index]['k'] if index < len(records) else None
        if kind in ('read', 'value', 'marker') and here not in (key, key+'.exception'):
            resync({key, key+'.exception'})
            here = records[index]['k'] if index < len(records) else None
        elif kind == 'block' and here not in (key+'.log', key+'.exception'):
            resync({key+'.log', key+'.exception'})
            here = records[index]['k'] if index < len(records) else None
        if kind == 'step':
            if here == key+'.exception':
                if not is_str(records[index]['v']):
                    problems.append(f'{here}: exception is not a string')
                index += 1
            continue
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
        if here == key:
            if not check(records[index]['v']):
                what = 'not the authored request' if key.endswith('.request') else 'wrong type'
                problems.append(f'{key}: {what} {json.dumps(records[index]["v"])[:120]}')
            index += 1
        elif kind == 'read' and here == key+'.exception':
            if not is_str(records[index]['v']):
                problems.append(f'{here}: exception is not a string')
            index += 1
        else:
            problems.append(f'missing {key} (found {here!r})')
    if index < len(records):
        extra = [r['k'] for r in records[index:]]
        problems.append(f'{len(extra)} unexpected records after the schema: {extra[:8]}')
    admitted = schema_keys()
    unknown = [k for k in keys if k not in admitted]
    if unknown:
        problems.append(f'{len(unknown)} keys outside the schema: {unknown[:8]}')
    groups = {k.split('.', 1)[0] for k in keys}
    problems += [f'group {g} is missing' for g in GROUPS if g not in groups]
    failed_setup = sorted(k for k in keys if SETUP_FAILURE.match(k))
    if failed_setup:
        problems.append(f'the probe could not build its nodes: {failed_setup[:5]}')
    for case in CONTACTS:
        key = f'contact.{case[0]}'
        if key+'.globals' in values and values.get(key+'.restored') != values[key+'.globals']:
            problems.append(f'{key}: the Physics2D globals were not restored '
                            f'({json.dumps(values.get(key+".restored", values.get(key+".restored.exception")))})')
    want = {'context.unityVersion': release, 'context.isBatchMode': True, 'context.graphicsDeviceType': device}
    for key, value in want.items():
        if key not in values or values[key] != value:
            problems.append(f'{key} is {json.dumps(values.get(key, values.get(key+".exception")))}, not {json.dumps(value)}')
    unscoped = values.get('capture.unscopedLogs')
    if is_strings(unscoped):
        bad = [line for line in unscoped if line.startswith(UNSCOPED_ERROR_TYPES)]
        if bad:
            problems.append(f'unexpected errors outside the probe blocks: {bad[:5]}')
    return problems


def diagnostic_phases():
    return (['start', 'ids:boot', 'ids:setup'] + [f'ids:contact.{c[0]}' for c in CONTACTS]
            + [f'ids:particles.{c[0]}' for c in PARTICLES] + ['written'])


def diagnostics_problems(path):
    """The raw diagnostics (absolute frames, real times, device, instance ids) must exist and parse;
    never compared."""
    try:
        rows = [json.loads(line) for line in Path(path).read_text().splitlines()]
    except (OSError, json.JSONDecodeError) as error:
        return [f'diagnostics unreadable: {error}']
    whats = [r.get('what') for r in rows]
    want = diagnostic_phases()
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
        if any(re.search(pattern, line) for pattern in EDITOR_LOG_FATAL):
            found.append(f'Editor log {number}: {line.strip()[:160]}')
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
    """Changed or removed fixture files under Assets (the scripts, the hand-authored scene and its
    metas) are problems; settings/package changes and Editor-written files are listed in
    authored-delta.json, not refused."""
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
    sources, references (the module graph), defines, analyzers and options. Only the build-graph
    directory name is neutral and the additional file names the project copy."""
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
