#!/usr/bin/env python3
"""Structural, schema, protocol, context and run guards for the shader pass count probe.

    guards.py values VALUES.jsonl --release R --device D     the record's schema/protocol problems
    guards.py capture CAPTURE_DIR --release R --device D     every problem of one Editor capture

The schema mirrors Assets/Scripts/ShaderPassCountProbe.cs record by record, in emission order (see
`expected`): every read once, under its key or under key.exception; a step only as
step.exception; every block ends with block.log (a block.exception is a probe fault); the
record ends with capture.unscopedLogs and the single completion marker. Values must have the type
the probe writes. Each case's authored settings (`case.N.set`) must be the probe's constants
(protocol). Fixture shaders that are not found, a failed set-up step (no material, camera or quad),
wrong context, probe faults, unscoped error logs and fatal records are problems.

No pass count, support flag, tag, FindPass index or colour is a guard: the answers to the pass
count hypotheses, per-case exceptions and logged API errors are data, never gates.

Every function returns ALL problems it finds (a list); none stops at the first.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys

MARKER = {'k': 'complete', 'v': True}
OUTPUT_VARIABLE = 'UNBOUND_PROBE_SHADERPASSCOUNT_OUT'

# The probe's constants (ShaderPassCountProbe.cs); Controls/controls.py checks they are equal.
KEYS = ('lod', 'fallback', 'tagged', 'surface')
NAMES = ('Probe/PassLod', 'Probe/PassFallback', 'Probe/PassTagged', 'Probe/PassSurface')
PASS_NAMES = {
    'lod': ('L0P0', 'L0P1', 'L0P2', 'L1P0', 'L2P0', 'L2P1', 'F0P0', 'F0P1', 'F0P2', 'F0P3',
            'F1P0', 'F1P1', 'F1P2', 'F1P3', 'F1P4'),
    'fallback': ('F0P0', 'F0P1', 'F0P2', 'F0P3', 'F1P0', 'F1P1', 'F1P2', 'F1P3', 'F1P4'),
    'tagged': ('T0P0', 'T0P1', 'T0P2', 'T1P0', 'T1P1'),
    'surface': ('FORWARD', 'FORWARD_DELTA', 'DEFERRED', 'META', 'S1P0', 'S1P1', 'S1P2'),
}
RESTORE = 'initial'
# (name, lod, fallback, surface, global): the maximum LODs each case sets (None: unchanged;
# 'initial': the value read before the first case). The settings accumulate case by case.
CASES = (
    ('default', None, None, None, None),
    ('shader350', 350, None, None, None),
    ('shader250', 250, None, None, None),
    ('shader175', 175, None, None, None),
    ('shader100', 100, None, None, None),
    ('shader25', 25, None, None, None),
    ('shaderRestored', RESTORE, None, None, None),
    ('global250', None, None, None, 250),
    ('global100', None, None, None, 100),
    ('global25', None, None, None, 25),
    ('global25shader350', 350, None, None, None),
    ('global25shader100', 100, None, None, None),
    ('globalRestored', RESTORE, None, None, RESTORE),
    ('surface250', None, None, 250, None),
    ('surfaceRestored', None, None, RESTORE, None),
    ('fallback100shader175', 175, 100, None, None),
    ('fallbackRestored', RESTORE, RESTORE, None, None),
)
SET_FIELDS = ('lod', 'fallback', 'surface', 'global')
SET_STEPS = {'lod': 'lod.maximumLOD', 'fallback': 'fallback.maximumLOD', 'surface': 'surface.maximumLOD',
             'global': 'globalMaximumLOD'}
SIZE = 16

# Groups that must all be present.
GROUPS = ('context', 'setup', 'decl', 'case', 'cleanup', 'capture')

# Unexpected-error log lines: compile errors, shader errors and native faults.
EDITOR_LOG_FATAL = [
    r'error CS\d{4}', r'Scripts have compiler errors', r"^Shader error in '", r'Shader compiler crashed',
    r'Caught fatal signal', r'Got a SIGSEGV', r'Native Crash Reporting', r'Exiting early due to double fault',
    r'Assertion failed on expression', r'Crash!!!', r'Aborting batchmode due to failure',
]
# Shader messages that are listed in the check report but do not refuse a capture.
EDITOR_LOG_LISTED = [r"^Shader warning in '", r'Shader .* is not supported', r'No subshaders can run']
UNSCOPED_ERROR_TYPES = ('Error:', 'Exception:', 'Assert:')

# Runner transitions that must occur exactly once, in this order (ProbeRunner.cs).
RUNNER_SEQUENCE = ['record', 'enter', 'finished', 'leave', 'exit']
RUNNER_OPTIONAL = {'domain', 'state', 'unpause'}


# ---------------------------------------------------------------- record types

def is_str(v): return isinstance(v, str)
def is_str_or_null(v): return v is None or isinstance(v, str)
def is_bool(v): return isinstance(v, bool)
def is_int(v): return isinstance(v, int) and not isinstance(v, bool)
def is_ints(v): return isinstance(v, list) and all(is_int(i) for i in v)
def is_strings(v): return isinstance(v, list) and all(isinstance(i, str) for i in v)
def is_true(v): return v is True


def is_color(v):
    return (isinstance(v, dict) and set(v) == {'rgba', 'uniform'} and is_bool(v['uniform'])
            and isinstance(v['rgba'], list) and len(v['rgba']) == 4 and all(is_int(c) and 0 <= c <= 255 for c in v['rgba']))


def is_list_of(length, check):
    return lambda v: isinstance(v, list) and len(v) == length and all(check(i) for i in v)


def set_value(case):
    """The `case.N.set` object the probe writes for a case."""
    return {f: v for f, v in zip(SET_FIELDS, case[1:]) if v is not None}


# ---------------------------------------------------------------- expected emission

class Expect:
    def __init__(self):
        self.items = []    # (kind, key, check)

    def read(self, key, check):
        self.items.append(('read', key, check))

    def step(self, key):
        self.items.append(('step', key, None))

    def value(self, key, check):
        self.items.append(('value', key, check))

    def block(self, key, body):
        body()
        self.items.append(('block', key, None))


def expected():
    """The ordered expectations of a record (the probe's emission order)."""
    e = Expect()
    r, s, b = e.read, e.step, e.block

    def context():
        for key, check in (('unityVersion', is_str), ('isBatchMode', is_bool), ('platform', is_str),
                           ('graphicsDeviceType', is_str), ('graphicsShaderLevel', is_int),
                           ('qualityLevel', is_int), ('qualityName', is_str), ('colorSpace', is_str),
                           ('renderPipeline', is_str_or_null), ('defaultRenderPipeline', is_str_or_null),
                           ('qualityRenderPipeline', is_str_or_null), ('globalRenderPipeline', is_str_or_null)):
            r('context.'+key, check)
    b('context', context)

    def setup():
        for k in KEYS:
            r(f'setup.{k}.found', is_bool)
            s(f'setup.{k}.step[new Material]')
            r(f'setup.{k}.maximumLOD', is_int)
        r('setup.globalMaximumLOD', is_int)
        s('setup.step[ambient]')
        s('setup.step[camera]')
        r('setup.actualRenderingPath', is_str)
        r('setup.background', is_color)
    b('setup', setup)
    for k in KEYS:
        def declarations(k=k):
            key = 'decl.'+k
            r(key+'.subshaderCount', is_int)
            r(key+'.passCountInSubshader', is_ints)
            r(key+'.subIds', is_strings)
            r(key+'.renderPipelineTags', is_strings)
            r(key+'.passIds', is_strings)
            r(key+'.passLightModes', is_strings)
        b('decl.'+k, declarations)
    for case in CASES:
        def body(case=case):
            key = 'case.'+case[0]
            want = set_value(case)
            e.value(key+'.set', lambda v, want=want: v == want)
            for field in SET_FIELDS:
                if field in want:
                    s(f'{key}.step[{SET_STEPS[field]}]')
            r(key+'.globalMaximumLOD', is_int)
            for k in KEYS:
                p = f'{key}.{k}'
                r(p+'.maximumLOD', is_int)
                r(p+'.passCount', is_int)
                r(p+'.isSupported', is_bool)
                r(p+'.passCountInSubshader', is_ints)
                r(p+'.mat.passCount', is_int)
                s(p+'.step[new Material]')
                r(p+'.newMat.passCount', is_int)
                r(p+'.mat.subTag', is_str_or_null)
                r(p+'.mat.subTagFallbacks', is_str_or_null)
                r(p+'.mat.findPass', is_list_of(len(PASS_NAMES[k]), is_int))
                r(p+'.color', is_color)
                r(p+'.passCount.after', is_int)
                r(p+'.mat.passCount.after', is_int)
        b('case.'+case[0], body)
    b('cleanup', lambda: None)
    r('capture.unscopedLogs', is_strings)
    e.items.append(('marker', 'complete', is_true))
    return e.items


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
    items = expected()
    index = 0

    def resync(wanted):
        nonlocal index
        ahead = next((j for j in range(index, len(records)) if records[j]['k'] in wanted), None)
        if ahead is None:
            return False
        problems.extend(f'unexpected record {r["k"]}' for r in records[index:ahead])
        index = ahead
        return True

    for kind, key, check in items:
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
                what = 'not the authored setting' if kind == 'value' else 'wrong type'
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
    for k in KEYS:
        if values.get(f'setup.{k}.found') is not True:
            problems.append(f'probe shader {k} was not found (fixture)')
    failed_setup = sorted(k for k in keys if k.startswith('setup.') and k.endswith('.exception'))
    if failed_setup:
        problems.append(f'the probe could not set up its shaders, materials or camera: {failed_setup[:5]}')
    want = {'context.unityVersion': release, 'context.isBatchMode': False, 'context.graphicsDeviceType': device,
            'context.renderPipeline': None, 'context.defaultRenderPipeline': None,
            'context.qualityRenderPipeline': None}
    for key, value in want.items():
        if key not in values or values[key] != value:
            problems.append(f'{key} is {json.dumps(values.get(key, values.get(key+".exception")))}, not {json.dumps(value)}')
    unscoped = values.get('capture.unscopedLogs')
    if is_strings(unscoped):
        bad = [line for line in unscoped if line.startswith(UNSCOPED_ERROR_TYPES)]
        if bad:
            problems.append(f'unexpected errors outside the probe cases: {bad[:5]}')
    return problems


def diagnostics_problems(path):
    """The raw diagnostics (real times, frames, device names) must exist and parse; never compared."""
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
    shutdown, the planned windowed GPU command."""
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
    if '-batchmode' in command:
        problems.append('-batchmode must not be passed')
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
    if 'batchmode=false' not in record:
        problems.append(f'the runner did not record in windowed mode: {record!r}')
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
    if not any(line.startswith('Renderer:') for line in lines):
        found.append('the Editor log names no renderer')
    return found


def listed_log_lines(path):
    """Shader warnings and support messages: reported with a capture, never refused."""
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
    """Changed or removed fixture files under Assets are problems; settings/package changes (the
    runner's asyncShaderCompilation setting) and Editor-written files are listed in
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
