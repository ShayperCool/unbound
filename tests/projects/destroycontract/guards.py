#!/usr/bin/env python3
"""Structural, schema, protocol, context and run guards for the manual Destroy contract probe.

    guards.py values VALUES.jsonl --release R --device D     the record's schema/protocol problems
    guards.py capture CAPTURE_DIR --release R --device D [--project P]   every problem of one capture

The schema mirrors Assets/Scripts/DestroyContractProbe.cs record by record, in emission order (see
`expected`): context, the authored subject/case tables, the PlayerLoop listing and its verified
installation, then for every probe step the three snapshot points (update, late, eof), each
followed by the before/after reads of the cases requested there, then the bracket integrity
checks, the per-step frames, every subject's raw lifecycle trace, the global order, the logs, the
probe faults and the single completion marker, which must be the last line.

Gates are instrumentation and protocol only: the authored tables, the context (release,
batchmode, Editor, OpenGLCore, scene, capture frame rate), a verified marker installation, balanced
markers, every non-fixed leaf once per frame, request/returned events exactly where the case table
puts them, positions naming recorded leaves, a global order consistent with the per-subject traces,
no probe fault and no Exception/Assert log. No state read, callback, count, position of any other
callback, or Error/Warning log is a gate: the answers to the precommitted hypotheses are data.

Every function returns ALL problems it finds (a list); none stops at the first.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys

MARKER = {'k': 'complete', 'v': True}
OUTPUT_VARIABLE = 'UNBOUND_PROBE_DESTROYCONTRACT_OUT'
FINAL_STEP = 22
CAPTURE_FRAMERATE = 50
POINTS = ('update', 'late', 'eof')

# The probe's tables (DestroyContractProbe.cs); Controls/controls.py checks they are equal.
SUBJECTS = (
    ('keep', None, 'active'),
    ('go.update', None, 'active'),
    ('go.late', None, 'active'),
    ('go.eof', None, 'active'),
    ('comp.update', None, 'active'),
    ('comp.disabled', None, 'disabled'),
    ('go.inactive', None, 'inactive'),
    ('tree.parent', None, 'active'),
    ('tree.child', 'tree.parent', 'active'),
    ('same.update', None, 'active'),
    ('same.late', None, 'active'),
    ('same.eof', None, 'active'),
    ('reenter', None, 'reenter'),
)
LABELS = tuple(s[0] for s in SUBJECTS)
CASES = (
    ('go.update', 2, 'update', 'go.update', 'gameObject'),
    ('go.late', 4, 'late', 'go.late', 'gameObject'),
    ('go.eof', 6, 'eof', 'go.eof', 'gameObject'),
    ('comp.update', 8, 'update', 'comp.update', 'component'),
    ('comp.disabled', 10, 'update', 'comp.disabled', 'gameObject'),
    ('go.inactive', 12, 'update', 'go.inactive', 'gameObject'),
    ('tree.parent', 14, 'update', 'tree.parent', 'gameObject'),
    ('same.update', 16, 'update', 'same.update', 'gameObject'),
    ('same.late', 16, 'late', 'same.late', 'gameObject'),
    ('same.eof', 16, 'eof', 'same.eof', 'gameObject'),
    ('reenter', 19, 'update', 'reenter', 'gameObject'),
)
# Where the driver's request points run (instrumentation, not an answer).
POINT_POSITION = {'update': 'in:Update.ScriptRunBehaviourUpdate',
                  'late': 'in:PreLateUpdate.ScriptRunBehaviourLateUpdate',
                  'eof': 'after:PostLateUpdate.TriggerEndOfFrameCallbacks'}
REQUIRED_LEAVES = ('Update.ScriptRunBehaviourUpdate', 'PreLateUpdate.ScriptRunBehaviourLateUpdate',
                   'PostLateUpdate.TriggerEndOfFrameCallbacks')
EVENTS = ('Awake', 'OnEnable', 'OnDisable', 'OnDestroy', 'request', 'returned', 'request2', 'returned2')
STATE_FIELDS = ('alive', 'activeSelf', 'activeInHierarchy', 'find', 'comp', 'enabled', 'isActiveAndEnabled',
                'getComponent', 'listed', 'parent', 'childCount', 'cb')
CB_FIELDS = ('awake', 'enable', 'start', 'update', 'late', 'eof', 'disable', 'destroy')
LOG_TYPES = ('Error', 'Assert', 'Warning', 'Log', 'Exception')
FAULT_LOG_TYPES = ('Exception', 'Assert')

# Unexpected-error lines in the Editor log: compile errors and native faults.
EDITOR_LOG_FATAL = [
    r'error CS\d{4}', r'Scripts have compiler errors', r'Caught fatal signal', r'Got a SIGSEGV',
    r'Native Crash Reporting', r'Exiting early due to double fault', r'Assertion failed on expression',
    r'Crash!!!', r'Aborting batchmode due to failure',
]
# Lines that are listed with a capture for review and never refused.
EDITOR_LOG_LISTED = [r'[Dd]estroy', r'DestroyContract', r'PlayerLoop']
# Lines every complete Editor log has.
EDITOR_LOG_EXPECTED = {'renderer': r'^Renderer:'}

# Runner transitions that must occur exactly once, in this order (ProbeRunner.cs).
RUNNER_SEQUENCE = ['record', 'enter', 'finished', 'leave', 'exit']
RUNNER_OPTIONAL = {'domain', 'state', 'unpause'}


# ---------------------------------------------------------------- record types

def is_str(v): return isinstance(v, str)
def is_bool(v): return isinstance(v, bool)
def is_int(v): return isinstance(v, int) and not isinstance(v, bool)
def is_number(v): return isinstance(v, (int, float)) and not isinstance(v, bool)
def is_strings(v): return isinstance(v, list) and all(isinstance(i, str) for i in v)
def is_exception(v): return isinstance(v, str) and v.startswith('!') and len(v) > 1


def bool_or_exception(v): return is_bool(v) or is_exception(v)
def same_or_exception(v): return v in ('same', 'other', 'none') or is_exception(v)


def is_state(v):
    if not isinstance(v, dict) or tuple(v) != STATE_FIELDS:
        return False
    cb = v['cb']
    return (is_bool(v['alive']) and bool_or_exception(v['activeSelf']) and bool_or_exception(v['activeInHierarchy'])
            and v['find'] in ('same', 'other', 'none') and is_bool(v['comp']) and bool_or_exception(v['enabled'])
            and bool_or_exception(v['isActiveAndEnabled']) and same_or_exception(v['getComponent'])
            and is_bool(v['listed']) and (v['parent'] is None or is_str(v['parent']))
            and (is_int(v['childCount']) or is_exception(v['childCount']))
            and isinstance(cb, dict) and tuple(cb) == CB_FIELDS and all(is_int(cb[k]) and cb[k] >= 0 for k in CB_FIELDS))


def is_position(v):
    return isinstance(v, str) and (v == 'boot' or v.startswith(('in:', 'after:')))


def is_event_name(e):
    """A recorded event name; a reentrant Destroy that throws records `threw2.<exception type>`."""
    return isinstance(e, str) and (e in EVENTS or re.fullmatch(r'threw2\.[A-Za-z_][A-Za-z0-9_`]*', e) is not None)


def is_event(v):
    return (isinstance(v, dict) and tuple(v) == ('e', 's', 'f', 'p') and is_event_name(v['e']) and is_int(v['s'])
            and -1 <= v['s'] <= FINAL_STEP and is_int(v['f']) and is_position(v['p']))


def is_log(v):
    return (isinstance(v, dict) and tuple(v) == ('t', 'm', 's', 'f', 'p') and v['t'] in LOG_TYPES and is_str(v['m'])
            and is_int(v['s']) and is_int(v['f']) and is_position(v['p']))


def list_of(check):
    return lambda v: isinstance(v, list) and all(check(i) for i in v)


def case_reads(subject):
    """The subjects a case reads before and after its request: the subject and its children."""
    return [label for label, parent, _ in SUBJECTS if label == subject or parent == subject]


def subjects_value():
    return [list(s) for s in SUBJECTS]


def cases_value():
    return [list(c) for c in CASES]


# ---------------------------------------------------------------- expected emission

def expected():
    """The ordered (key, check, kind) expectations of a record (the probe's emission order).
    kind 'value' marks authored/protocol constants whose mismatch is a protocol problem."""
    items = []

    def read(key, check):
        items.append((key, check, 'read'))

    def value(key, want):
        items.append((key, lambda v, want=want: v == want, 'value'))
    for key, check in (('unityVersion', is_str), ('isBatchMode', is_bool), ('isEditor', is_bool), ('platform', is_str),
                       ('graphicsDeviceType', is_str), ('scene', is_str), ('captureFramerate', is_int),
                       ('fixedDeltaTime', is_number)):
        read('context.'+key, check)
    value('setup.subjects', subjects_value())
    value('setup.cases', cases_value())
    value('setup.finalStep', FINAL_STEP)
    read('loop.groups', is_strings)
    read('loop.leaves', is_strings)
    value('loop.installed', True)
    value('loop.installProblems', [])
    for step in range(FINAL_STEP+1):
        for point in POINTS:
            for label in LABELS:
                read(f'snap.{step}.{point}.{label}', is_state)
            for name, s, p, subject, _ in CASES:
                if s == step and p == point:
                    for when in ('before', 'after'):
                        for label in case_reads(subject):
                            read(f'case.{name}.{when}.{label}', is_state)
    value('loop.balanced', True)
    value('loop.everyLeafOncePerFrame', True)
    value('loop.frameChecks', FINAL_STEP)
    read('steps.frame', lambda v: isinstance(v, list) and len(v) == FINAL_STEP+1 and all(is_int(i) for i in v))
    for label in LABELS:
        read('trace.'+label, list_of(is_event))
    read('order', is_strings)
    read('logs', list_of(is_log))
    value('faults', [])
    items.append(('complete', lambda v: v is True, 'marker'))
    return items


def schema_keys():
    return [key for key, _, _ in expected()]


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
    lines = text.split('\n')[:-1] if text.endswith('\n') else text.split('\n')
    for number, line in enumerate(lines, 1):
        try:
            row = json.loads(line)
        except json.JSONDecodeError as error:
            problems.append(f'line {number}: not JSON ({error})')
            continue
        if not isinstance(row, dict) or list(row) != ['k', 'v'] or not isinstance(row['k'], str):
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
    values = {}
    for r in records:
        values.setdefault(r['k'], r['v'])
    items = expected()
    index = 0
    for key, check, kind in items:
        here = records[index]['k'] if index < len(records) else None
        if here != key:
            ahead = next((j for j in range(index, len(records)) if records[j]['k'] == key), None)
            if ahead is None:
                problems.append(f'missing {key} (found {here!r})')
                continue
            problems.extend(f'unexpected record {r["k"]}' for r in records[index:ahead])
            index = ahead
        v = records[index]['v']
        if not check(v):
            what = {'value': 'not the authored/protocol value', 'marker': 'not the marker'}.get(kind, 'wrong type')
            problems.append(f'{key}: {what} {json.dumps(v)[:160]}')
        index += 1
    if index < len(records):
        extra = [r['k'] for r in records[index:]]
        problems.append(f'{len(extra)} unexpected records after the schema: {extra[:8]}')
    admitted = set(schema_keys())
    unknown = [k for k in keys if k not in admitted]
    if unknown:
        problems.append(f'{len(unknown)} keys outside the schema: {unknown[:8]}')
    want = {'context.unityVersion': release, 'context.isBatchMode': True, 'context.isEditor': True,
            'context.graphicsDeviceType': device, 'context.scene': 'Probe',
            'context.captureFramerate': CAPTURE_FRAMERATE}
    for key, value in want.items():
        if key in values and values[key] != value:
            problems.append(f'{key} is {json.dumps(values[key])}, not {json.dumps(value)}')
    problems += protocol_problems(values)
    return problems


def protocol_problems(values):
    """Instrumentation and protocol consistency of a parsed record (answers are never checked)."""
    problems = []
    leaves = values.get('loop.leaves')
    if is_strings(leaves):
        if len(set(leaves)) != len(leaves):
            problems.append('loop.leaves lists a leaf twice')
        for leaf in REQUIRED_LEAVES:
            if leaf not in leaves:
                problems.append(f'loop.leaves has no {leaf}')
        positions = {'boot'} | {'in:'+leaf for leaf in leaves} | {'after:'+leaf for leaf in leaves}
    else:
        positions = None
    traces = {label: values.get('trace.'+label) for label in LABELS}
    for label, trace in traces.items():
        if not isinstance(trace, list) or not all(is_event(e) for e in trace):
            continue
        if positions is not None:
            bad = [e['p'] for e in trace if e['p'] not in positions]
            if bad:
                problems.append(f'trace.{label}: positions outside the recorded loop {bad[:3]}')
    for name, step, point, subject, _ in CASES:
        trace = traces.get(subject)
        if not isinstance(trace, list) or not all(is_event(e) for e in trace):
            continue
        for what in ('request', 'returned'):
            hits = [e for e in trace if e['e'] == what]
            if len(hits) != 1:
                problems.append(f'case {name}: {len(hits)} {what} events, not one')
            elif hits[0]['s'] != step or hits[0]['p'] != POINT_POSITION[point]:
                problems.append(f'case {name}: {what} at step {hits[0]["s"]} {hits[0]["p"]}, not step {step} {POINT_POSITION[point]}')
        requests = [i for i, e in enumerate(trace) if e['e'] == 'request']
        returns = [i for i, e in enumerate(trace) if e['e'] == 'returned']
        if requests and returns and returns[0] < requests[0]:
            problems.append(f'case {name}: returned before request')
    order = values.get('order')
    if is_strings(order) and all(isinstance(t, list) and all(is_event(e) for e in t) for t in traces.values()):
        projected = {label: [] for label in LABELS}
        bad = []
        for token in order:
            label, _, what = token.rpartition(':')
            if label not in projected:
                bad.append(token)
            else:
                projected[label].append(what)
        if bad:
            problems.append(f'order names unknown subjects {bad[:3]}')
        for label in LABELS:
            if projected[label] != [e['e'] for e in traces[label]]:
                problems.append(f'order is inconsistent with trace.{label}')
    logs = values.get('logs')
    if isinstance(logs, list):
        faults = [entry for entry in logs if is_log(entry) and entry['t'] in FAULT_LOG_TYPES]
        if faults:
            problems.append(f'{len(faults)} Exception/Assert logs during the capture: {[f["m"][:100] for f in faults[:3]]}')
    return problems


def diagnostics_problems(path):
    """The raw diagnostics (real times, frames, instance IDs, device names) must exist and parse."""
    try:
        rows = [json.loads(line) for line in Path(path).read_text().splitlines()]
    except (OSError, json.JSONDecodeError) as error:
        return [f'diagnostics unreadable: {error}']
    whats = [r.get('what') for r in rows]
    return [] if whats == ['start', 'written'] else [f'diagnostics phases {whats} instead of start, written']


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
    if ('-executeMethod', 'ProbeRunner.Record') not in list(zip(command, command[1:])):
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
    """record, enter, finished 0, leave, exit 0 exactly once and in order; contiguous sequence;
    exit is the one closed last event."""
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


def log_lines(path, patterns):
    lines = Path(path).read_text(errors='replace').splitlines()
    found = []
    for number, line in enumerate(lines, 1):
        for pattern in patterns:
            if re.search(pattern, line):
                found.append(f'Editor log {number}: {line.strip()[:160]}')
                break
    return lines, found


def log_problems(path):
    try:
        lines, found = log_lines(path, EDITOR_LOG_FATAL)
    except OSError as error:
        return [f'Editor log unreadable: {error}']
    for name, pattern in EDITOR_LOG_EXPECTED.items():
        if not any(re.search(pattern, line) for line in lines):
            found.append(f'the Editor log has no {name} line')
    return found


def listed_log_lines(path):
    """Destroy/PlayerLoop/probe lines of the Editor log: reported with a capture, never refused."""
    try:
        return log_lines(path, EDITOR_LOG_LISTED)[1]
    except OSError:
        return []


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
    """Changed or removed fixture files under Assets are problems; Editor-written settings/package
    changes and new files are listed in authored-delta.json, not refused."""
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
