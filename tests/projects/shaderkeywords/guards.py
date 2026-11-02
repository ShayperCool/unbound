#!/usr/bin/env python3
"""Structural, schema, context and run guards for the shader keyword probe.

    guards.py values VALUES.jsonl --release R --device D     the record's schema/context problems
    guards.py capture CAPTURE_DIR --release R --device D     every problem of one Editor capture

The schema mirrors Assets/Scripts/ShaderKeywordsProbe.cs case by case, in emission order (see
`expected`). Every read appears once, under its key or under key.exception; a step appears only
as step.exception; every block ends with block.log (a block.exception is a probe fault); the
record ends with capture.unscopedLogs and the single completion marker. Values must have the
type the probe writes. Two kinds of data decide which keys follow: a `.found` read that is not
`true` ends its shader/asset, and a `.keywordNames` list adds its `.find[name]` cases (an
exception adds none). No observed keyword, order or API answer is a guard: per-case exceptions
and logged errors are data. Probe-shader and fixture assets that are not found, wrong context,
probe faults, unscoped error logs and fatal records are problems.

Every function returns ALL problems it finds (a list); none stops at the first.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import sys

MARKER = {'k': 'complete', 'v': True}

PROBE_SHADERS = ['Probe/KwForms', 'Probe/KwPlain', 'Probe/KwHlsl', 'Probe/KwSurface', 'Probe/KwFixed']
BUILTIN_SHADERS = [
    'Standard', 'Standard (Specular setup)', 'Autodesk Interactive', 'Unlit/Color', 'Unlit/Texture',
    'Unlit/Transparent', 'Unlit/Transparent Cutout', 'UI/Default', 'UI/Unlit/Text', 'Sprites/Default',
    'Sprites/Mask', 'Particles/Standard Unlit', 'Particles/Standard Surface', 'Legacy Shaders/Diffuse',
    'Legacy Shaders/VertexLit', 'Legacy Shaders/Particles/Additive', 'Legacy Shaders/Transparent/Cutout/Diffuse',
    'Mobile/Diffuse', 'Mobile/Particles/Additive', 'Skybox/Procedural', 'Skybox/6 Sided', 'Skybox/Cubemap',
    'Nature/Terrain/Standard', 'Nature/Terrain/Diffuse', 'GUI/Text Shader', 'Hidden/Internal-Colored',
    'Hidden/InternalErrorShader', 'Hidden/BlitCopy', 'Hidden/Internal-ScreenSpaceShadows',
    'Hidden/Internal-DeferredShading', 'Hidden/Internal-StencilWrite', 'Hidden/Internal-GUITexture',
]
PROBES = ['GLOBAL_A', 'LOCAL_A', 'SHARED_KW', 'DYN_GLOBAL', 'DYN_LOCAL_A', 'LIGHTMAP_ON', 'DIRECTIONAL',
          'INSTANCING_ON', 'STEREO_INSTANCING_ON', 'PLAIN_INCLUDE_KW', 'D3D_ONLY', 'NOT_DECLARED', '']
COMPUTE_PROBES = ['CS_GLOBAL_A', 'CS_LOCAL', 'CS_DYN', 'LIGHTMAP_ON', 'NOT_DECLARED', '']
GLOBAL_NAMES = ['GLOBAL_A', 'LOCAL_A', 'SHARED_KW', 'DYN_GLOBAL', 'DYN_LOCAL_A', 'INCLUDED_GLOBAL',
                'INCLUDED_LOCAL', 'PLAIN_INCLUDE_KW', 'SUB2_GLOBAL', 'CS_GLOBAL_A', 'CS_LOCAL', 'CS_DYN',
                'DIRECTIONAL', 'LIGHTMAP_ON', 'FOG_LINEAR', 'STEREO_INSTANCING_ON', '_NORMALMAP',
                'UNITY_UI_CLIP_RECT', 'NEVER_SEEN_KW', 'CREATED_KW', 'STRING_ENABLED_KW']
VARIANT_PROPS = ['_Color', '_Value', '_Other', '_MainTex', '_Missing']
FAMILY = ['VarParent', 'VarChild', 'VarGrand']
# The eight data groups and the capture context; every one must be present.
GROUPS = ('context', 'probe', 'builtin', 'compute', 'local', 'global', 'mat', 'saved', 'variant')

# Unexpected-error log lines: compile errors, shader errors and native faults.
EDITOR_LOG_FATAL = [
    r'error CS\d{4}', r'Scripts have compiler errors', r"^Shader error in '", r'Caught fatal signal',
    r'Got a SIGSEGV', r'Native Crash Reporting', r'Exiting early due to double fault',
    r'Assertion failed on expression', r'Crash!!!', r'Aborting batchmode due to failure',
]
UNSCOPED_ERROR_TYPES = ('Error:', 'Exception:', 'Assert:')

# Runner transitions that must occur exactly once, in this order (ProbeRunner.cs).
RUNNER_SEQUENCE = ['record', 'enter', 'finished', 'leave', 'exit']
RUNNER_OPTIONAL = {'domain', 'state', 'unpause'}


# ---------------------------------------------------------------- record types

def is_str(v): return isinstance(v, str)
def is_str_or_null(v): return v is None or isinstance(v, str)
def is_bool(v): return isinstance(v, bool)
def is_int(v): return isinstance(v, int) and not isinstance(v, bool)
def is_strings(v): return isinstance(v, list) and all(isinstance(i, str) for i in v)
def is_float(v): return (isinstance(v, (int, float)) and not isinstance(v, bool)) or v in ('NaN', 'Infinity', '-Infinity')
def is_color(v): return isinstance(v, list) and len(v) == 4 and all(is_float(i) for i in v)


def is_keyword(v):
    if not isinstance(v, dict) or not is_str_or_null(v.get('name')) or 'isValid' not in v:
        return False
    if v['isValid'] is False:
        return set(v) == {'name', 'isValid'}
    return (v['isValid'] is True and set(v) == {'name', 'type', 'isDynamic', 'isOverridable', 'isValid'}
            and is_str(v['type']) and is_bool(v['isDynamic']) and is_bool(v['isOverridable']))


def is_keywords(v): return isinstance(v, list) and all(is_keyword(i) for i in v)
def is_builtin_types(v): return isinstance(v, dict) and all(is_str(k) and is_strings(n) for k, n in v.items())
def is_true(v): return v is True


# ---------------------------------------------------------------- expected emission

class Expect:
    """The probe's emission order. `found(key)`/`names(key)` read the record being checked."""

    def __init__(self, values):
        self.values = values
        self.items = []    # (kind, key, type check)

    def read(self, key, check):
        self.items.append(('read', key, check))

    def step(self, key):
        self.items.append(('step', key, None))

    def block(self, key, body):
        body()
        self.items.append(('block', key, None))

    def found(self, key):
        return self.values.get(key) is True

    def names(self, key):
        names = self.values.get(key)
        return names if is_strings(names) else []


def space(e, key, extra):
    e.read(key+'.keywordCount', is_int)
    e.read(key+'.keywordNames', is_strings)
    e.read(key+'.keywords', is_keywords)
    for name in dict.fromkeys(e.names(key+'.keywordNames')+list(extra)):
        e.read(key+'.find['+name+']', is_keyword)


def shader(e, key, extra, details):
    e.read(key+'.found', is_bool)
    if not e.found(key+'.found'):
        return

    def meta():
        for suffix, check in (('subshaderCount', is_int), ('passCount', is_int), ('propertyCount', is_int),
                              ('properties', is_strings), ('passCountInSubshader', is_strings),
                              ('subshaderTags', is_strings), ('passLightModes', is_strings)):
            e.read(key+'.'+suffix, check)
    e.block(key+'.meta', meta)
    e.block(key+'.space', lambda: space(e, key+'.space', extra))
    if details:
        def out_of_range():
            e.read(key+'.passCountInSubshader[-1]', is_int)
            e.read(key+'.passCountInSubshader[count]', is_int)
            e.read(key+'.subshaderTag[count]', is_str_or_null)
            e.read(key+'.subshaderTag[-1]', is_str_or_null)
        e.block(key+'.outOfRange', out_of_range)


def describe(e, key):
    e.read(key+'.shaderKeywords', is_strings)
    e.read(key+'.enabledKeywords', is_keywords)


def variant(e, key):
    e.read(key+'.isVariant', is_bool)
    e.read(key+'.parent', is_str_or_null)
    for f in FAMILY:
        e.read(key+'.IsChildOf('+f+')', is_bool)
    for p in VARIANT_PROPS:
        for what in ('IsPropertyOverriden', 'IsPropertyLocked', 'IsPropertyLockedByAncestor'):
            e.read(f'{key}.{what}({p})', is_bool)
    e.read(key+'.color', is_color)
    e.read(key+'.value', is_float)
    e.read(key+'.other', is_float)
    e.read(key+'.renderQueue', is_int)
    e.read(key+'.shaderKeywords', is_strings)


def expected(values):
    """The ordered expectations for a record whose parsed key->value map is `values`."""
    e = Expect(values)
    r, s, b = e.read, e.step, e.block

    def context():
        for key, check in (('unityVersion', is_str), ('isBatchMode', is_bool), ('platform', is_str),
                           ('graphicsDeviceType', is_str), ('graphicsShaderLevel', is_int),
                           ('qualityLevel', is_int), ('qualityName', is_str), ('qualityShadows', is_str),
                           ('colorSpace', is_str), ('renderPipeline', is_str_or_null)):
            r('context.'+key, check)
    b('context', context)
    for name in PROBE_SHADERS:
        shader(e, 'probe.'+name, PROBES, True)
    for name in BUILTIN_SHADERS:
        shader(e, 'builtin.'+name, [], False)

    r('compute.found', is_bool)
    if e.found('compute.found'):
        b('compute.space', lambda: space(e, 'compute.space', COMPUTE_PROBES))

        def compute():
            for step in ('keywordSpace', 'EnableKeyword(kw CS_LOCAL)', 'SetKeyword(kw CS_GLOBAL_A, true)',
                         'EnableKeyword(NOT_DECLARED)'):
                s('compute.step['+step+']')
            r('compute.shaderKeywords', is_strings)
            r('compute.enabledKeywords', is_strings)
            r('compute.IsKeywordEnabled(NOT_DECLARED)', is_bool)
            r('compute.IsKeywordEnabled(kw CS_LOCAL)', is_bool)
            r('compute.space==', is_bool)
        b('compute.keywords', compute)

    def local_default():
        r('local.default.keyword', is_keyword)
        r('local.default.ToString', is_str_or_null)
    b('local.default', local_default)

    def local_construct():
        s('local.step[ctor(LOCAL_A)]')
        s('local.step[find(LOCAL_A)]')
        r('local.ctor(LOCAL_A)', is_keyword)
        r('local.ctor==find', is_bool)
        r('local.ctor.Equals', is_bool)
        r('local.ToString', is_str_or_null)
        r('local.ctor(NOT_DECLARED)', is_keyword)
        r('local.ctor(GLOBAL_A)', is_keyword)
        s('local.step[ctor(plain, LOCAL_A)]')
        r('local.ctor(plain, LOCAL_A)', is_keyword)
        r('local.otherSpace==', is_bool)
        r('local.space==', is_bool)
        r('local.space!=other', is_bool)
        r('local.ctor(null shader)', is_keyword)
    b('local.construct', local_construct)

    def global_initial():
        r('global.enabled.initial', is_strings)
        s('global.step[globalKeywords]')
        for n in GLOBAL_NAMES:
            r('global.all.contains['+n+']', is_bool)
    b('global.initial', global_initial)
    b('global.builtin', lambda: r('global.builtin.types', is_builtin_types))

    def global_create():
        s('global.step[Create(CREATED_KW)]')
        r('global.create.name', is_str_or_null)
        r('global.create.ToString', is_str_or_null)
        r('global.create.inAll', is_bool)
        r('global.create.enabled', is_bool)
        r('global.create.again', is_str_or_null)
        r('global.create.count', is_int)
    b('global.create', global_create)

    def global_ctor():
        r('global.ctor.unknown.name', is_str_or_null)
        r('global.ctor.unknown.inAll', is_bool)
        r('global.ctor.known.name', is_str_or_null)
    b('global.ctor', global_ctor)

    def global_enable():
        s('global.step[EnableKeyword(STRING_ENABLED_KW)]')
        s('global.step[EnableKeyword(gk GLOBAL_A)]')
        s('global.step[SetKeyword(Create(SET_KW), true)]')
        r('global.enabled.after', is_strings)
        r('global.after.contains[STRING_ENABLED_KW]', is_bool)
        r('global.IsKeywordEnabled(GLOBAL_A)', is_bool)
        r('global.IsKeywordEnabled(gk STRING_ENABLED_KW)', is_bool)
        s('global.step[DisableKeyword(STRING_ENABLED_KW)]')
        s('global.step[DisableKeyword(GLOBAL_A)]')
        s('global.step[SetKeyword(gk SET_KW, false)]')
        r('global.enabled.restored', is_strings)
    b('global.enable', global_enable)

    def mat_new():
        s('mat.new.step[new Material(forms)]')
        describe(e, 'mat.new')
        s('mat.new.step[SetKeyword(kw LOCAL_A, true)]')
        s('mat.new.step[EnableKeyword(kw GLOBAL_A)]')
        s('mat.new.step[EnableKeyword(kw DYN_LOCAL_A)]')
        describe(e, 'mat.local')
        r('mat.IsKeywordEnabled(kw LOCAL_A)', is_bool)
        r('mat.IsKeywordEnabled(LOCAL_A)', is_bool)
        s('mat.new.step[EnableKeyword(NOT_DECLARED)]')
        s('mat.new.step[EnableKeyword(LOCAL_B)]')
        describe(e, 'mat.string')
        r('mat.IsKeywordEnabled(NOT_DECLARED)', is_bool)
        r('mat.IsKeywordEnabled(kw NOT_DECLARED)', is_bool)
        s('mat.new.step[DisableKeyword(kw LOCAL_A)]')
        s('mat.new.step[SetKeyword(kw GLOBAL_A, false)]')
        describe(e, 'mat.disabled')
        s('mat.new.step[shader = plain]')
        describe(e, 'mat.toPlain')
        r('mat.toPlain.IsKeywordEnabled(LOCAL_B)', is_bool)
        s('mat.new.step[shader = forms]')
        describe(e, 'mat.back')
    b('mat.new', mat_new)

    def mat_keywords():
        s('mat.shaderKeywords.step[new Material(forms)]')
        s('mat.shaderKeywords.step[shaderKeywords =]')
        describe(e, 'mat.set')
        s('mat.shaderKeywords.step[enabledKeywords =]')
        describe(e, 'mat.setEnabled')
    b('mat.shaderKeywords', mat_keywords)

    def mat_other():
        s('mat.otherSpace.step[new Material(forms)]')
        s('mat.otherSpace.step[SetKeyword(plain LOCAL_A, true)]')
        describe(e, 'mat.otherSpace')
    b('mat.otherSpace', mat_other)

    def mat_toggle():
        s('mat.toggle.step[new Material(forms)]')
        r('mat.toggle.keyword', is_bool)
        s('mat.toggle.step[SetFloat(_Toggle, 1)]')
        r('mat.toggle.afterSetFloat', is_bool)
    b('mat.toggle', mat_toggle)

    def saved():
        r('saved.found', is_bool)
        if not e.found('saved.found'):
            return
        describe(e, 'saved')
        r('saved.IsKeywordEnabled(STALE_KW)', is_bool)
        s('saved.step[new Material(saved)]')
        describe(e, 'saved.copy')
        s('saved.step[copy.shader = KwHlsl]')
        describe(e, 'saved.copy.toHlsl')
    b('saved', saved)

    r('variant.found', is_bool)
    if e.found('variant.found'):
        b('variant.assets', lambda: [variant(e, 'variant.'+k) for k in ('parent', 'child', 'grand')])

        def instance():
            s('variant.instance.step[Instantiate(child)]')
            variant(e, 'variant.instance')
            s('variant.instance.step[new Material(grand)]')
            variant(e, 'variant.newFrom')
        b('variant.instance', instance)

        def revert():
            s('variant.revert.step[Instantiate(grand)]')
            s('variant.revert.step[RevertPropertyOverride(_Value)]')
            variant(e, 'variant.revert.value')
            s('variant.revert.step[RevertPropertyOverride(_Missing)]')
            s('variant.revert.step[RevertAllPropertyOverrides]')
            variant(e, 'variant.revert.all')
            s('variant.revert.step[SetFloat(_Other, 1.75)]')
            variant(e, 'variant.revert.set')
            s('variant.revert.step[SetPropertyLock(_Other, true)]')
            variant(e, 'variant.revert.lock')
        b('variant.revert', revert)

        def script():
            s('variant.script.step[new Material(forms)]')
            s('variant.script.step[parent = VarParent]')
            variant(e, 'variant.script')
            s('variant.script.step[SetColor(_Color)]')
            variant(e, 'variant.script.set')
            s('variant.script.step[parent = null]')
            variant(e, 'variant.script.detached')
        b('variant.script', script)
    r('capture.unscopedLogs', is_strings)
    e.items.append(('marker', 'complete', is_true))
    return e.items


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
    """Every schema/structure/context problem of a values.jsonl (empty list: it can back a reference)."""
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
    items = expected(values)
    index = 0

    def resync(wanted):
        """Skips unexpected records up to the next one in `wanted`, reporting each skipped key."""
        nonlocal index
        ahead = next((j for j in range(index, len(records)) if records[j]['k'] in wanted), None)
        if ahead is None:
            return False
        problems.extend(f'unexpected record {r["k"]}' for r in records[index:ahead])
        index = ahead
        return True

    for kind, key, check in items:
        here = records[index]['k'] if index < len(records) else None
        if kind != 'step' and here not in (key, key+'.exception', key+'.log'):
            resync({key+'.log', key+'.exception'} if kind == 'block' else {key, key+'.exception'})
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
                problems.append(f'{key}: wrong type {json.dumps(records[index]["v"])[:120]}')
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
    expected_keys = set()
    for kind, key, _ in items:
        expected_keys |= {key+'.log', key+'.exception'} if kind == 'block' else {key, key+'.exception'}
    unknown = [k for k in keys if k not in expected_keys]
    if unknown:
        problems.append(f'{len(unknown)} keys outside the schema: {unknown[:8]}')
    groups = {k.split('.', 1)[0] for k in keys}
    problems += [f'group {g} is missing' for g in GROUPS if g not in groups]
    for name in PROBE_SHADERS:
        if values.get('probe.'+name+'.found') is not True:
            problems.append(f'probe shader {name} was not found (fixture)')
    for key in ('compute.found', 'saved.found', 'variant.found'):
        if values.get(key) is not True:
            problems.append(f'{key} is not true (fixture)')
    want = {'context.unityVersion': release, 'context.isBatchMode': True, 'context.graphicsDeviceType': device,
            'context.renderPipeline': None}
    for key, value in want.items():
        if key not in values or values[key] != value:
            problems.append(f'{key} is {json.dumps(values.get(key, values.get(key+".exception")))}, not {json.dumps(value)}')
    unscoped = values.get('capture.unscopedLogs')
    if is_strings(unscoped):
        bad = [line for line in unscoped if line.startswith(UNSCOPED_ERROR_TYPES)]
        if bad:
            problems.append(f'unexpected errors outside the probe cases: {bad[:5]}')
    return problems


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
    command = result.get('command') or []
    problems += command_problems(command, release)
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
    if env.get('UNBOUND_PROBE_SHADERKEYWORDS_OUT') != str((directory/'capture').resolve()):
        problems.append(f"capture variable {env.get('UNBOUND_PROBE_SHADERKEYWORDS_OUT')!r} is not this capture")
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
    return found


def authored_problems(directory, project):
    """Changed or removed fixture Assets files are problems; other authored deltas are listed
    in authored-delta.json by the runner and reported, not refused."""
    prepared = json.loads((Path(directory)/'inputs.json').read_text())['files']
    problems = []
    now = {('assets/'+k): v for k, v in tree(Path(project)/'Assets').items()}
    for key, digest in prepared.items():
        if key.startswith('assets/') and now.get(key) != digest:
            problems.append(f'fixture file {key} changed or was removed by the Editor')
    return problems


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
    if project is not None and Path(project).exists():
        problems += authored_problems(directory, project)
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
