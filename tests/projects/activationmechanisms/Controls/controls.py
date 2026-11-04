#!/usr/bin/env python3
"""Offline controls of the activation mechanism probe's guards, runner protocol, analyzer,
comparator, preflight judgement and admission/publish gates. No Editor, engine, Host or Unity
compiler runs here; every Unity answer below is FAKE.

    ~/.cache/compat/with-heavy-job --kind tool python3 Controls/controls.py --out NEW_CACHE_DIR

1. Source guards on the probe scripts and fixture: tables equal the guards' schema and the
   analyzer's parse; each class's message set (recorder: the admitted four; observer: 2D begin/end
   only; driver: Start); no sorting, Editor API, destruction, execution-order attribute, Stay
   callback, RNG or other clock; ids, absolute frames, real time and device names only in
   diagnostics; built-in cases restore the Physics2D globals in finally; the runner is the admitted
   activation order runner with only the names changed; Packages/ProjectSettings/metas are the
   sprite import fixture's bytes; the hand-authored scene's ids, metas and references are complete
   and its document, fileID, hierarchy and component-list orders differ.
2. Compiles the probe's two real scripts at C# 9 against FakeUnity.cs with the .NET SDK's Roslyn and
   runs them through Driver.cs in many scenarios, each with chosen fake answers.
3. The guards pass the normal captures and every answer (positive) and refuse every mocked
   negative: record mutations, run results, inputs, runner events, logs, globals not restored.
4. The analyzer regenerates PREDICTIONS.json byte for byte, holds its claimed separations, selects
   each scenario's fake model, uses only the capture's own ids, and returns NONE/NOT_EVALUABLE for
   unpredicted answers and broken preconditions. The capture/check/publish path never reads it.
5. Runner, pair, admission seal, cleanup journal, compile-graph and preflight identity/judgement
   controls with a fake Editor launcher (no Editor starts).
6. The comparator keeps the full union with values, groups and record-order changes.

Writes NEW_CACHE_DIR/controls.json with every control's expected and actual result; exit 0 only
when all hold.
"""
import argparse
import contextlib
import copy
import hashlib
import io
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
from unittest import mock

HERE = Path(__file__).resolve().parent
PROBE = HERE.parent
REPO = PROBE.parents[2]
SOURCE_FIXTURE = REPO/'tests/projects/spriteimport'
ADMITTED = REPO/'tests/projects/activationorder'
sys.path.insert(0, str(PROBE))
sys.path.insert(0, str(REPO/'tests'))
os.environ['UNBOUND_PROBE_UNITY_VERSION'] = '6000.0.71f1'
import analyze  # noqa: E402
import compare  # noqa: E402
import guards  # noqa: E402
import preflight  # noqa: E402
import run  # noqa: E402
from probe_paths import cache_artifact_path  # noqa: E402

RELEASE = '6000.0.71f1'
DEVICE = 'OpenGLCore'
SCRIPTS = [PROBE/'Assets/Scripts/ActivationMechanismsProbe.cs', PROBE/'Assets/Editor/ProbeRunner.cs']
SCENARIOS = ('normal', 'normal-repeat', 'revlevel', 'pre', 'firstawake', 'lastenable', 'builtinsfirst', 'scriptsfirst',
             'deferred', 'noexit', 'flags', 'noparticle', 'nocontact', 'scenehier', 'noreload', 'pause', 'watchdog',
             'setupfault', 'nooutput')
COMPLETE = [s for s in SCENARIOS if s not in ('watchdog', 'setupfault', 'nooutput')]
UNITY_MESSAGES = ('Awake', 'OnEnable', 'Start', 'OnDisable', 'OnDestroy', 'Update', 'LateUpdate', 'FixedUpdate',
                  'Reset', 'OnValidate', 'OnApplicationQuit', 'OnApplicationPause', 'OnApplicationFocus',
                  'OnTransformParentChanged', 'OnTransformChildrenChanged', 'OnBeforeTransformParentChanged',
                  'OnBecameVisible', 'OnBecameInvisible', 'OnGUI', 'OnRenderObject', 'OnDidApplyAnimationProperties',
                  'OnCollisionEnter2D', 'OnCollisionExit2D', 'OnCollisionStay2D', 'OnTriggerEnter2D', 'OnTriggerExit2D',
                  'OnTriggerStay2D', 'OnCollisionEnter', 'OnCollisionExit', 'OnTriggerEnter', 'OnTriggerExit',
                  'OnParticleSystemStopped', 'OnParticleCollision', 'OnParticleTrigger', 'OnJointBreak2D')
results = []


def control(name, expect, actual, passed):
    results.append({'control': name, 'expect': expect, 'actual': actual, 'passed': bool(passed)})


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def body(code, signature):
    """The body of the declaration matching `signature` (brace-balanced)."""
    m = re.search(signature, code)
    if not m:
        return None
    i = code.index('{', m.end()-1)
    depth = 0
    for j in range(i, len(code)):
        depth += {'{': 1, '}': -1}.get(code[j], 0)
        if depth == 0:
            return code[i+1:j]
    return None


def squash(text):
    return re.sub(r'\s+', ' ', text or '').strip()


# ---------------------------------------------------------------- 1. source guards

def source_guards():
    probe = SCRIPTS[0].read_text()
    runner = SCRIPTS[1].read_text()
    code = re.sub(r'//[^\n]*', '', probe)
    t = analyze.source_tables()
    want = {'nodes': [list(n) for n in guards.NODES], 'create': list(guards.CREATE_ORDER), 'add': list(guards.ADD_ORDER),
            'wake': list(guards.WAKE_ORDER), 'toggle': list(guards.TOGGLE_ORDER),
            'scene_adds': [list(x) for x in guards.SCENE_ADDS], 'scene_labels': list(guards.SCENE_LABELS),
            'ops': [list(o) for o in guards.OPS], 'contacts': [list(c) for c in guards.CONTACTS],
            'particles': [list(p) for p in guards.PARTICLES], 'emitted': list(guards.EMITTED)}
    got = {k: [list(x) if isinstance(x, tuple) else x for x in v] for k, v in t.items()}
    control('source: every authored table is the guards\' schema and the analyzer\'s parse', want, got, want == got)
    const = dict(re.findall(r'const (?:int|string|float) (\w+) = ([^;]+);', code))
    want = {'OutputVariable': '"'+guards.OUTPUT_VARIABLE+'"', 'DriverLabel': '"'+guards.DRIVER+'"',
            'Unlabelled': '"'+guards.UNLABELLED+'"', 'Recorder': '"'+guards.RECORDER+'"', 'Observer': '"'+guards.OBSERVER+'"',
            'Driver': '"'+guards.DRIVER_TYPE+'"', 'ObserverSlot': '"'+guards.OBSERVER_SLOT+'"',
            'DriverSlot': '"'+guards.DRIVER_SLOT+'"', 'SettleFrames': str(guards.SETTLE_FRAMES),
            'EndFrames': str(guards.END_FRAMES), 'Step2D': '0.02f'}
    control('source: probe constants', want, const, const == want)
    labels = [n[1] for n in guards.NODES]
    control('source: every runtime node is created once and gets one recorder; wakes act on self-inactive nodes',
            [sorted(labels)] * 2, [sorted(guards.CREATE_ORDER), sorted(guards.ADD_ORDER)],
            sorted(guards.CREATE_ORDER) == sorted(labels) == sorted(guards.ADD_ORDER)
            and all(not dict((n[1], n[3]) for n in guards.NODES)[w] for w in guards.WAKE_ORDER))
    classes = {name: body(code, r'public (?:static )?class ' + name + r'\b[^{]*\{')
               for name in ('ActivationMechanismsProbe', 'Contact2DObserver', 'MechanismTrace', 'MechanismDriver')}
    messages = {name: sorted(m for m in re.findall(r'\b(?:void|IEnumerator)\s+(\w+)\s*\(', text or '') if m in UNITY_MESSAGES)
                for name, text in classes.items()}
    want = {'ActivationMechanismsProbe': ['Awake', 'OnDisable', 'OnEnable', 'Start'],
            'Contact2DObserver': ['OnCollisionEnter2D', 'OnCollisionExit2D', 'OnTriggerEnter2D', 'OnTriggerExit2D'],
            'MechanismTrace': [], 'MechanismDriver': ['Start']}
    control('source: message sets per class (recorder = the admitted four; observer = 2D begin/end; driver = Start)',
            want, messages, messages == want)
    declared = re.findall(r'public (?:static )?class (\w+)', code)
    control('source: one file, four classes; the scene-referenced class is the file\'s name',
            ['ActivationMechanismsProbe', 'Contact2DObserver', 'MechanismTrace', 'MechanismDriver'], declared,
            declared == ['ActivationMechanismsProbe', 'Contact2DObserver', 'MechanismTrace', 'MechanismDriver'])
    rec = classes['ActivationMechanismsProbe'] or ''
    for name, counter in (('Awake', 'awakes'), ('OnEnable', 'enables'), ('Start', 'starts'), ('OnDisable', 'disables')):
        m = re.search(r'void '+name+r'\(\) \{ (\w+)\+\+; Note\("'+name+r'"\); \}', rec)
        control(f'source: recorder {name} only counts and notes itself', counter, m and m.group(1), m and m.group(1) == counter)
    writers = re.findall(r'Trace\.(Add|Insert|Sort|Reverse|Remove\w*|Clear)\(', code)
    control('source: the trace is only cleared by Reset and appended by Note (never sorted or reordered)',
            ['Clear', 'Add'], writers, writers == ['Clear', 'Add'])
    forbidden = re.findall(r'OrderBy|ThenBy|\.Sort\(|\.Reverse\(|UnityEditor|EditorApplication|DefaultExecutionOrder|'
                           r'ExecuteAlways|ExecuteInEditMode|Destroy\w*\(|SendMessage|BroadcastMessage|\bInvoke\(|'
                           r'Process\b|Directory\.|unscaledTime|fixedUnscaled|realtimeSinceStartup\b|timeSinceLevelLoad|'
                           r'Time\.time\b|deltaTime|StartCoroutine|Random|Stay2D|AudioSource|DateTime|Stopwatch|'
                           r'Physics2D\.(gravity|autoSyncTransforms|queriesHitTriggers)', code)
    control('source: no sorting, Editor API, execution-order attribute, destruction, messages by name, coroutines, '
            'processes, RNG, Stay callbacks, audio, other clocks or other Physics2D globals in the probe', [], forbidden, not forbidden)
    diag = body(code, r'void Diagnostic\(string what, string extra\)\s*\{')
    outside = code.replace(diag, '') if diag else code
    leaks = re.findall(r'Time\.\w+|graphicsDeviceName|graphicsDeviceVersion', outside)
    control('source: absolute frames, real time and device names only in Diagnostic, except the relative frame base',
            ['Time.frameCount', 'Time.frameCount'], leaks, diag and leaks == ['Time.frameCount', 'Time.frameCount']
            and 'startFrame = Time.frameCount;' in outside and 'Time.frameCount - startFrame' in outside)
    ids = body(code, r'string Ids\(IEnumerable<string> labels\)\s*\{')
    rest = code.replace(ids, '') if ids else code
    control('source: instance ids only in Ids(), whose output goes only to Diagnostic', [],
            [l.strip() for l in rest.splitlines() if 'GetInstanceID' in l],
            ids and 'GetInstanceID' not in rest and all('Diagnostic(' in l for l in code.splitlines() if 'Ids(' in l
                                                         and 'string Ids(' not in l))
    start = body(code, r'IEnumerator Start\(\)\s*\{')
    order = re.findall(r'Stray\("end"\)|MechanismTrace\.closed = true|Value\("capture\.strayCalls"|'
                       r'Value\("capture\.unscopedLogs"|Value\("complete"|File\.Move|yield return null|'
                       r'ActivationMechanismsProbe\.finishedCode = code', start or '')
    want = ['yield return null', 'Stray("end")', 'MechanismTrace.closed = true', 'Value("capture.strayCalls"',
            'Value("capture.unscopedLogs"', 'Value("complete"', 'File.Move', 'yield return null',
            'ActivationMechanismsProbe.finishedCode = code']
    control('source: settle yields, then the trace closes and the record is written once before the end yields '
            'and the finished code', want, order, order == want)
    yields = re.findall(r'yield return', code)
    control('source: the probe yields only in the driver\'s Start', 2, len(yields),
            len(yields) == 2 and len(re.findall(r'yield return', start or '')) == 2)
    op = body(code, r'void Operation\(.*?\)\s*\{')
    seq = re.findall(r'Read\(key \+ "\.before\." \+ label|Stray\(key \+ "\.returned"\)|Read\(key \+ "\.returned"|'
                     r'Take\(key \+ "\.calls"\)|Read\(key \+ "\.after\." \+ label', op or '')
    want = ['Read(key + ".before." + label', 'Stray(key + ".returned")', 'Read(key + ".returned"', 'Take(key + ".calls")',
            'Read(key + ".after." + label']
    control('source: each operation reads every state, closes strays, makes the one call, takes its calls, reads every state',
            want, seq, seq == want)
    contact = body(code, r'void ContactCase\(.*?\)\s*\{') or ''
    final = body(contact, r'finally\s*\{') or ''
    control('source: the 2D cases restore simulationMode and callbacksOnDisable and clear the peer read in finally',
            ['Peer = null', 'simulationMode = mode', 'callbacksOnDisable = callbacks', 'restored'],
            squash(final)[:200], all(x in final for x in ('MechanismTrace.Peer = null', 'Physics2D.simulationMode = mode',
                                                           'Physics2D.callbacksOnDisable = callbacks', '".restored"')))
    particles = body(code, r'void ParticleCase\(.*?\)\s*\{') or ''
    control('source: the particle cases clear the peer read in finally and change no global', True,
            'MechanismTrace.Peer = null' in (body(particles, r'finally\s*\{') or '') and 'Physics2D' not in particles,
            'MechanismTrace.Peer = null' in (body(particles, r'finally\s*\{') or '') and 'Physics2D' not in particles)
    proven = (ADMITTED/'Assets/Editor/ProbeRunner.cs').read_text().replace('ActivationOrderProbe', 'ActivationMechanismsProbe')
    control('source: the runner is the admitted batch GPU Record runner with only the probe names changed', True,
            proven == runner, proven == runner)
    control('source: the runner changes no Editor setting', [], re.findall(r'EditorSettings\.\w+', runner),
            not re.findall(r'EditorSettings\.\w+', runner))
    gate_code = (PROBE/'run.py').read_text()+(PROBE/'guards.py').read_text()
    uses = re.findall(r'import analyze|from analyze|analyze\.\w+\(', gate_code)
    control('source: capture, check and publish never call the analyzer (answers never gate)', [], uses, not uses)
    copies = {}
    for part in ('Packages', 'ProjectSettings'):
        copies[part] = preflight.files(PROBE/part) == preflight.files(SOURCE_FIXTURE/part)
    for rel in ('Assets/Scenes/Probe.unity.meta', 'Assets/Editor/ProbeRunner.cs.meta', 'Assets/Editor.meta',
                'Assets/Scenes.meta', 'Assets/Scripts.meta'):
        copies[rel] = sha(PROBE/rel) == sha(SOURCE_FIXTURE/rel)
    control('source: Packages, ProjectSettings, the folder, runner and scene metas are byte copies of the sprite import '
            'fixture (its Editor-written graph is the preflight graph)', True, copies, all(copies.values()))
    meta = (PROBE/'Assets/Scripts/ActivationMechanismsProbe.cs.meta').read_text()
    guid = re.search(r'guid: (\w+)', meta).group(1)
    others = [str(p) for p in (REPO/'tests/projects').rglob('*.meta') if p != PROBE/'Assets/Scripts/ActivationMechanismsProbe.cs.meta'
              and ('guid: '+guid) in p.read_text(errors='replace')]
    control('source: the probe script meta has a unique guid and no importer settings (default execution order)',
            [], others, not others and meta == f'fileFormatVersion: 2\nguid: {guid}\n')
    scripts = sorted(str(p.relative_to(PROBE)) for p in (PROBE/'Assets').rglob('*') if p.is_file()
                     and p.suffix.lower() in ('.cs', *preflight.COMPILE_INPUTS))
    control('source: the only compile inputs under Assets are the two probe scripts', sorted(run.SOURCES), scripts,
            scripts == sorted(run.SOURCES))
    scene_guards(guid)


def scene_guards(guid):
    text = (PROBE/'Assets/Scenes/Probe.unity').read_text()
    header = (SOURCE_FIXTURE/'Assets/Scenes/Probe.unity').read_text().split('--- !u!1 &')[0]
    control('scene: the settings documents are the Editor-serialized sprite import scene\'s bytes', True,
            text.startswith(header), text.startswith(header))
    docs = re.findall(r'^--- !u!(\d+) &(-?\d+)\n(.*?)(?=^--- |\Z)', text, re.S | re.M)
    ids = [int(f) for _, f, _ in docs]
    control('scene: every document id is unique', len(ids), len(set(ids)), len(ids) == len(set(ids)))
    known = set(ids)
    authored = [b for c, _, b in docs if c in ('1', '4', '114', '1660057539')]
    refs = [int(x) for b in authored for x in re.findall(r'\{fileID: (-?\d+)\}', b) if int(x) != 0]
    local = [r for r in refs if r not in known]
    control('scene: every local fileID reference of the authored objects resolves to a document', [], local, not local)
    guids = set(re.findall(r'm_Script: \{fileID: 11500000, guid: (\w+), type: 3\}', text))
    control('scene: every MonoBehaviour references the probe script\'s meta guid', [guid], sorted(guids), guids == {guid})
    s = analyze.scene_model(text, guid)
    gos = {int(f): b for c, f, b in docs if c == '1'}
    trs = {int(f): b for c, f, b in docs if c == '4'}
    mbs = {int(f): b for c, f, b in docs if c == '114'}
    back = []
    for fid, b in gos.items():
        for comp in re.findall(r'- component: \{fileID: (-?\d+)\}', b):
            owner = re.search(r'm_GameObject: \{fileID: (-?\d+)\}', (trs.get(int(comp)) or mbs.get(int(comp)) or ''))
            if not owner or int(owner.group(1)) != fid:
                back.append((fid, comp))
        first = int(re.findall(r'- component: \{fileID: (-?\d+)\}', b)[0])
        if first not in trs:
            back.append((fid, 'first component is not its Transform'))
    for fid, b in trs.items():
        for child in re.findall(r'\{fileID: (-?\d+)\}', re.search(r'm_Children:(.*?)\n  m_Father', b, re.S).group(1)):
            father = int(re.search(r'm_Father: \{fileID: (-?\d+)\}', trs[int(child)]).group(1))
            if father != fid:
                back.append((fid, child))
    control('scene: components point back to their GameObject, each GameObject starts with its Transform, '
            'children and fathers agree', [], back, not back)
    roots = [f for f, b in trs.items() if re.search(r'm_Father: \{fileID: 0\}', b)]
    listed = [int(x) for x in re.findall(r'\{fileID: (-?\d+)\}', text.split('SceneRoots:')[1])]
    control('scene: SceneRoots lists exactly the root Transforms', sorted(roots), listed, sorted(roots) == sorted(listed))
    control('scene: node names are the probe\'s scene labels', sorted(guards.SCENE_LABELS), sorted(s['children']),
            sorted(s['children']) == sorted(guards.SCENE_LABELS))
    dup = [n for n, c in s['components'].items() if len(c) != len(set(c))]
    control('scene: recorder slots are unique per object and every object has a recorder', [], dup,
            not dup and all(s['components'][n] for n in guards.SCENE_LABELS))
    pre = analyze.expand(analyze.preorder('S', s['children']), s['components'])
    orders = {'document (MonoBehaviour)': s['doc_mb'], 'fileID (MonoBehaviour)': s['fileid_mb'],
              'hierarchy pre-order': [x for x in pre], 'document (GameObject)': analyze.expand(s['doc_go'], s['components'])}
    distinct = len({json.dumps(v) for v in orders.values()}) == len(orders)
    s2 = s['components']['S2']
    doc_s2 = [x for x in s['doc_mb'] if x.startswith('S2/')]
    control('scene: document, fileID, hierarchy and GameObject-document orders all differ; S2\'s component list '
            'differs from its document order', {'distinct': True, 'S2 list != documents': True},
            {'orders': orders, 'S2': [s2, doc_s2]}, distinct and s2 != doc_s2 and s['fileid_go'] != s['doc_go'])
    build = (PROBE/'ProjectSettings/EditorBuildSettings.asset').read_text()
    scene_guid = re.search(r'guid: (\w+)', (PROBE/'Assets/Scenes/Probe.unity.meta').read_text()).group(1)
    control('scene: the build settings list the probe scene under its meta guid', scene_guid, build,
            f'path: Assets/Scenes/Probe.unity\n    guid: {scene_guid}' in build)
    active = {n: a for n, a in s['active'].items()}
    control('scene: every scene object is authored active', True, active, all(active.values()))


# ---------------------------------------------------------------- 2. compile and drive

def dotnet():
    root = Path.home()/'.dotnet'
    csc = sorted((root/'sdk').glob('10.*/Roslyn/bincore/csc.dll'))[-1]
    refs = sorted((root/'packs/Microsoft.NETCore.App.Ref').glob('10.*/ref/net10.0'))[-1]
    version = refs.parent.parent.name
    return root/'dotnet', csc, sorted(refs.glob('*.dll')), version


def compile_all(out):
    exe, csc, refs, version = dotnet()
    build = out/'build'
    build.mkdir()
    record = {'dotnet': str(exe), 'csc': str(csc), 'csc_sha256': sha(csc), 'framework': version, 'steps': [],
              'fake': 'FakeUnity.cs stands in for Unity; this is not the genuine preflight'}
    common = ['/nologo', '/nostdlib+', '/noconfig', *[f'/r:{r}' for r in refs]]
    steps = [
        ('FakeUnity', ['/target:library', '/nowarn:0618,0660,0661', str(HERE/'FakeUnity.cs')], []),
        ('Probe', ['/target:library', '/langversion:9.0', '/nowarn:0618,0649,0169', *map(str, SCRIPTS)], ['FakeUnity']),
        ('Driver', ['/target:exe', str(HERE/'Driver.cs')], ['FakeUnity']),
    ]
    for name, args, deps in steps:
        rsp = build/f'{name}.rsp'
        rsp.write_text('\n'.join('"'+a+'"' for a in [*common, *[f'/r:{build/d}.dll' for d in deps],
                                                    f'/out:{build/name}.dll', *args])+'\n')
        process = subprocess.run([str(exe), str(csc), '@'+str(rsp)], capture_output=True, text=True)
        (build/f'{name}.log').write_text(process.stdout+process.stderr)
        record['steps'].append({'name': name, 'exit': process.returncode, 'output': (process.stdout+process.stderr).strip()})
        if process.returncode:
            control(f'compile {name} at C# {"9" if name == "Probe" else "default"} against the fakes', 0,
                    process.stdout+process.stderr, False)
            return None, record
    (build/'Driver.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {
        'tfm': 'net10.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': version},
        'rollForward': 'LatestPatch'}}))
    control('compile the probe scripts at C# 9 against the fakes', 0, 0, True)
    return build, record


def drive(build, out):
    exe = dotnet()[0]
    spec = out/'scene-spec.json'
    spec.write_text(json.dumps(analyze.scene_model()))
    runs = {}
    for scenario in SCENARIOS:
        target = out/'scenarios'/scenario
        process = subprocess.run([str(exe), str(build/'Driver.dll'), scenario.replace('-repeat', ''),
                                  str(build/'Probe.dll'), str(target), str(spec)], capture_output=True, text=True)
        (out/'scenarios'/f'{scenario}.log').write_text(process.stdout+process.stderr)
        driver = json.loads((target/'driver.json').read_text()) if (target/'driver.json').exists() else None
        runs[scenario] = {'dir': target, 'process': process.returncode, 'driver': driver}
    return runs


def entries(values, key):
    return [f"{e['n']}/{e['r']}.{e['cb']}" for e in values.get(key, [])]


def scenario_controls(runs):
    for name in COMPLETE:
        r = runs[name]
        d = r['driver'] or {}
        directory = r['dir']
        control(f'{name}: the runner quits 0 once', 0, d.get('exit'), r['process'] == 0 and d.get('exit') == 0)
        control(f'{name}: no forbidden call or callback exception', [], d.get('forbidden'), d.get('forbidden') == [])
        want = ['EnterPlaymode from delayCall', 'ExitPlaymode from update', 'Exit 0 from update']
        control(f'{name}: transitions deferred to Editor delayCall/update', want, d.get('transitions'), d.get('transitions') == want)
        problems = guards.record_problems(directory/'values.jsonl', RELEASE, DEVICE)
        control(f'{name}: the record passes the schema/protocol guard (positive)', [], problems, not problems)
        events = guards.runner_event_problems(directory/'runner-events.jsonl')
        control(f'{name}: runner events pass (positive)', [], events, not events)
        diag = guards.diagnostics_problems(directory/'diagnostics.jsonl')
        control(f'{name}: raw diagnostics present (positive)', [], diag, not diag)
        control(f'{name}: teardown callbacks reach the closed probe without changing its record; no handler left; '
                'nothing written at shutdown; Physics2D globals restored', [True, True, 0, 0, 0, 'FixedUpdate', True],
                [d.get('teardownCallbacks', 0) > 0, d.get('recordUnchangedByTeardown'), d.get('handlersAtEnd'),
                 d.get('logHandlersAtEnd'), d.get('eventsWrittenAtShutdown'), d.get('simulationModeAtEnd'),
                 d.get('callbacksOnDisableAtEnd')],
                d.get('teardownCallbacks', 0) > 0 and d.get('recordUnchangedByTeardown') is True
                and d.get('handlersAtEnd') == 0 and d.get('logHandlersAtEnd') == 0 and d.get('eventsWrittenAtShutdown') == 0
                and d.get('simulationModeAtEnd') == 'FixedUpdate' and d.get('callbacksOnDisableAtEnd') is True)
    values = analyze.load(runs['normal']['dir']/'values.jsonl')
    scene = analyze.scene_model()
    want_boot = [f'{x}.{cb}' for x in scene['doc_mb'] for cb in ('Awake', 'OnEnable')]
    boot = entries(values, 'boot.calls')
    starts = [e for e in boot[len(want_boot):-1]]
    control('normal: boot.calls are the fake scene load (document order), the Starts that ran before the driver\'s '
            'Start in raw order, then the driver\'s Start; no stray call', [want_boot, 'scene Starts', 'driver/d.Start', []],
            [boot, values.get('capture.strayCalls')],
            boot[:len(want_boot)] == want_boot and boot[-1:] == ['driver/d.Start'] and all(x.endswith('.Start') for x in starts)
            and sorted(starts) == sorted(f'{x}.Start' for x in scene['doc_mb']) and values.get('capture.strayCalls') == [])
    hier = analyze.load(runs['scenehier']['dir']/'values.jsonl')
    want_hier = [f'{x}.{cb}' for x in analyze.expand(analyze.preorder('S', scene['children']), scene['components'])
                 for cb in ('Awake', 'OnEnable')]
    control('scenehier: the recorded load order follows the (fake) load, not an assumption', want_hier,
            entries(hier, 'boot.calls'), entries(hier, 'boot.calls')[:len(want_hier)] == want_hier)
    setup = entries(values, 'setup.calls')
    want = [f'{n}/r.{cb}' for n in guards.ADD_ORDER if n not in guards.WAKE_ORDER for cb in ('Awake', 'OnEnable')]
    want += [f'{n}/r.{cb}' for n in guards.WAKE_ORDER for cb in ('Awake', 'OnEnable')]
    want += [f'{n}/r.{cb}' for n in guards.TOGGLE_ORDER for cb in ('OnDisable', 'OnEnable')]
    want += [f'{a}/{s}.{cb}' for a, s in guards.SCENE_ADDS for cb in ('Awake', 'OnEnable')]
    control('normal: setup.calls are the adds (inactive nodes asleep), then the wakes, the toggle and the scene add',
            want, setup, setup == want)
    driver_seen = [k for k, v in values.items() if k.endswith('.calls') and k != 'boot.calls'
                   and any(e['n'] == guards.DRIVER for e in v)]
    control('normal: the driver adds no callback to any observed list', [], driver_seen, not driver_seen)
    call = values.get('contact.solidOnScripts.calls', [])
    control('normal: a 2D case keeps OnDisable and exit callbacks in one raw list with the peer state of each entry',
            ['recorder', 'observer'], sorted({e['t'] for e in call}),
            {e['t'] for e in call} == {'recorder', 'observer'} and all(isinstance(e['peer'], dict) for e in call))
    pause = runs['pause']
    names = [json.loads(line)['e'] for line in (pause['dir']/'runner-events.jsonl').read_text().splitlines()]
    control('pause: Error Pause is cleared and recorded', 'unpause event', names.count('unpause'),
            (pause['driver'] or {}).get('paused') and not (pause['driver'] or {}).get('stillPaused') and 'unpause' in names)
    a, b = (runs[n]['dir']/'values.jsonl' for n in ('normal', 'normal-repeat'))
    control('normal: two runs of the fake capture are byte-identical', True, a.read_bytes() == b.read_bytes(),
            a.read_bytes() == b.read_bytes())
    text = a.read_text()
    leaked = [k for k in ('Fake GPU', '"frame"', '"realtime"') if k in text] + re.findall(r'-\d{4,}\b', text)[:3]
    da = runs['normal']['dir']/'diagnostics.jsonl'
    control('normal: instance ids, absolute frames, real times and device names are only in diagnostics.jsonl', [],
            leaked, not leaked and da.exists() and 'Fake GPU' in da.read_text() and re.search(r'-\d{4,}\b', da.read_text()))
    w = runs['watchdog']
    control('watchdog: Editor-side watchdog quits 4', 4, (w['driver'] or {}).get('exit'),
            (w['driver'] or {}).get('exit') == 4 and (w['dir']/'watchdog.txt').exists())
    wp = guards.runner_event_problems(w['dir']/'runner-events.jsonl')
    control('watchdog: refused by the runner-event guard', 'problems', wp, wp)
    f = runs['setupfault']
    fault = guards.record_problems(f['dir']/'values.jsonl', RELEASE, DEVICE)
    control('setupfault: the record is still written and closed; refused because the nodes could not be built',
            ['exit 0', 'could not build its nodes'], fault[:3],
            (f['driver'] or {}).get('exit') == 0 and any('could not build its nodes' in p for p in fault))
    n = runs['nooutput']['driver'] or {}
    control('nooutput: quits 7 from a delayCall, no record', ['Exit 7 from delayCall'], n.get('transitions'),
            n.get('exit') == 7 and n.get('transitions') == ['Exit 7 from delayCall']
            and not (runs['nooutput']['dir']/'values.jsonl').exists())


# ---------------------------------------------------------------- 3. record controls

def write_rows(path, rows):
    path.write_text(''.join(json.dumps(r, separators=(',', ':'))+'\n' for r in rows))


def record_controls(normal, out):
    base = guards.parse(normal)[0]
    work = out/'record-controls'
    work.mkdir()

    def index(key):
        return next(i for i, r in enumerate(base) if r['k'] == key)

    def mutated(name, change, expect):
        rows = copy.deepcopy(base)
        text = change(rows)
        path = work/(re.sub(r'\W+', '-', name)+'.jsonl')
        if isinstance(text, str):
            path.write_text(text)
        else:
            write_rows(path, rows)
        problems = guards.record_problems(path, RELEASE, DEVICE)
        control('record refused: '+name, expect, problems[:4], any(expect in p for p in problems))

    def setv(key, value):
        def change(rows):
            rows[index(key)]['v'] = value
        return change
    full = normal.read_text()
    entry = base[index('op.offAOff.calls')]['v'][0]
    mutated('completion-only', lambda rows: '{"k":"complete","v":true}\n', 'missing context.unityVersion')
    mutated('truncated by lines', lambda rows: ''.join(full.splitlines(True)[:len(base)*2//3]), 'completion marker is not the last line')
    mutated('truncated mid-line', lambda rows: full[:len(full)//2], 'truncated')
    mutated('omitted operation', lambda rows: rows.__setitem__(slice(None), [r for r in rows if not r['k'].startswith('op.on1On.')]), 'missing op.on1On')
    mutated('omitted 2D case', lambda rows: rows.__setitem__(slice(None), [r for r in rows if not r['k'].startswith('contact.solidOnScripts.')]), 'missing contact.solidOnScripts')
    mutated('omitted particle case', lambda rows: rows.__setitem__(slice(None), [r for r in rows if not r['k'].startswith('particles.')]), 'missing particles.psScripts')
    mutated('omitted node state', lambda rows: rows.pop(index('op.on2Off.after.S21')), 'missing op.on2Off.after.S21')
    mutated('omitted calls list', lambda rows: rows.pop(index('op.mirrorOn.calls')), 'missing op.mirrorOn.calls')
    mutated('omitted simulate list', lambda rows: rows.pop(index('contact.triggerOnScripts.sim3.calls')), 'missing contact.triggerOnScripts.sim3.calls')
    mutated('reordered reads', lambda rows: rows.insert(index('op.baseOn.relativeFrame'), rows.pop(index('op.baseOn.returned'))), 'unexpected record')
    mutated('omitted block log', lambda rows: rows.pop(index('settle.log')), 'missing settle.log')
    mutated('omitted stray list', lambda rows: rows.pop(index('capture.strayCalls')), 'missing capture.strayCalls')
    mutated('duplicate key', lambda rows: rows.insert(index('op.offAOff.log'), dict(rows[index('op.offAOff.log')-1])), 'duplicate key')
    mutated('calls entry without its peer read', setv('op.offAOff.calls', [{k: v for k, v in entry.items() if k != 'peer'}]), 'op.offAOff.calls: wrong type')
    mutated('calls entry with an id field (clock/id contamination)', setv('op.offAOff.calls', [dict(entry, id=-1234)]), 'op.offAOff.calls: wrong type')
    mutated('calls entry with an unknown label', setv('op.offAOff.calls', [dict(entry, n='X')]), 'op.offAOff.calls: wrong type')
    mutated('recorder entry with a 2D callback (wrong type)', setv('op.offAOff.calls', [dict(entry, cb='OnCollisionExit2D')]), 'op.offAOff.calls: wrong type')
    mutated('calls entry with reordered fields', setv('op.offAOff.calls', [dict(reversed(list(entry.items())))]), 'op.offAOff.calls: wrong type')
    mutated('a context real-time key (clock contamination)', lambda rows: rows.insert(index('context.log'), {'k': 'context.realtime', 'v': 1.5}), 'unexpected record context.realtime')
    state = base[index('op.baseOff.before.P')]['v']
    mutated('state without recorders', setv('op.baseOff.before.P', {k: v for k, v in state.items() if k != 'recorders'}), 'op.baseOff.before.P: wrong type')
    mutated('state with a negative count', setv('op.baseOff.before.P', dict(state, recorders=[dict(state['recorders'][0], counts=dict(state['recorders'][0]['counts'], Awake=-1))])), 'op.baseOff.before.P: wrong type')
    mutated('tree row without components', setv('setup.tree', [{k: v for k, v in r.items() if k != 'components'} for r in base[index('setup.tree')]['v']]), 'setup.tree: wrong type')
    physics = base[index('contact.solidOnScripts.before')]['v']
    mutated('physics state with a float', setv('contact.solidOnScripts.before', dict(physics, physics=dict(physics['physics'], touching=0.5))), 'contact.solidOnScripts.before: wrong type')
    mutated('authored request changed', setv('op.offAOff.request', {'target': 'A1', 'value': False}), 'op.offAOff.request: not the authored request')
    mutated('2D request of another layout', setv('contact.solidOnScripts.request', dict(base[index('contact.solidOnScripts.request')]['v'], layout='builtinsFirst')), 'not the authored request')
    mutated('Physics2D globals not restored', setv('contact.solidOnBuiltins.restored', {'simulationMode': 'Script', 'callbacksOnDisable': True}), 'globals were not restored')
    mutated('fatal record', lambda rows: rows.insert(len(rows)-1, {'k': 'fatal', 'v': 'boom'}), 'fatal')
    mutated('two markers', lambda rows: rows.insert(5, {'k': 'complete', 'v': True}), '2 completion markers')
    mutated('marker not last', lambda rows: rows.append(rows.pop(-2)), 'completion marker is not the last line')
    mutated('extra key', lambda rows: rows.insert(index('op.on1On.log'), {'k': 'op.on1On.extra', 'v': 1}), 'unexpected record op.on1On.extra')
    mutated('probe fault', lambda rows: rows.insert(index('op.on1On.log'), {'k': 'op.on1On.exception', 'v': 'NullReferenceException: x'}), 'probe fault in block op.on1On')
    mutated('set-up step failed', lambda rows: rows.insert(index('setup.calls'), {'k': 'setup.P.step[recorder].exception', 'v': 'Exception: x'}), 'could not build its nodes')
    mutated('scene not found', lambda rows: rows.insert(index('boot.scene'), {'k': 'boot.step[find].exception', 'v': 'Exception: x'}), 'could not build its nodes')
    mutated('2D case could not build', lambda rows: rows.insert(index('contact.triggerOffBuiltins.setup.calls'), {'k': 'contact.triggerOffBuiltins.step[components].exception', 'v': 'Exception: x'}), 'could not build its nodes')
    mutated('particle case could not emit', lambda rows: rows.insert(index('particles.psBuiltins.before'), {'k': 'particles.psBuiltins.step[emit].exception', 'v': 'Exception: x'}), 'could not build its nodes')
    mutated('wrong release', setv('context.unityVersion', '6000.0.70f1'), 'context.unityVersion')
    mutated('wrong device', setv('context.graphicsDeviceType', 'Null'), 'context.graphicsDeviceType')
    mutated('not batchmode', setv('context.isBatchMode', False), 'context.isBatchMode')
    mutated('unscoped error', setv('capture.unscopedLogs', ['Error: boom']), 'unexpected errors outside the probe blocks')
    mutated('not a k/v record', lambda rows: full.replace('{"k":"op.offAOff.log"', '{"key":"op.offAOff.log"', 1), 'not a {"k","v"} record')
    # Answers are data: any order, extra/missing callback, flag, count, contact or particle state,
    # simulate result, stray call, returned exception or scoped error passes.
    rows = copy.deepcopy(base)
    for r in rows:
        if r['k'].endswith('.calls') and r['k'] != 'capture.strayCalls':
            r['v'] = list(reversed(r['v']))+[dict(entry, n='unlabelled', cb='Start', self=[False, True, False, True])]
        if '.before.' in r['k'] or '.after.' in r['k'] or r['k'].startswith('settle.state.'):
            r['v'] = dict(r['v'], activeSelf=not r['v']['activeSelf'], parent='U', sibling=7, recorders=[])
        if r['k'].endswith(('.before', '.after', '.final')) and isinstance(r['v'], dict) and 'physics' in r['v']:
            r['v'] = dict(r['v'], physics=dict(r['v']['physics'], touching=False, colliderContacts=0))
        if r['k'].endswith('.returned') and isinstance(r['v'], bool):
            r['v'] = not r['v']
        if r['k'] == 'capture.strayCalls':
            r['v'] = [{'at': 'op.on1On.returned', 'calls': [dict(entry, n='driver', cb='OnDisable')]}]
        if r['k'] == 'settle.relativeFrame':
            r['v'] = 9
        if r['k'] == 'op.on2On.log':
            r['v'] = ['Error: an expected API error']
    rows = [{'k': r['k']+'.exception', 'v': 'ArgumentException: x'} if r['k'] in ('op.baseOn.returned', 'contact.solidOnScripts.sim2.returned') else r
            for r in rows]
    path = work/'answers-are-data.jsonl'
    write_rows(path, rows)
    problems = guards.record_problems(path, RELEASE, DEVICE)
    control('record accepted: any callback order/extra entry, state, contact, simulate result, stray call, relative '
            'frame, per-read exception and a scoped error log (answers never gate)', [], problems, not problems)


# ---------------------------------------------------------------- 4. analyzer

def fit(path, work, name, diagnostics=None):
    report = work/f'{name}.report.json'
    with contextlib.redirect_stdout(io.StringIO()):
        analyze.fit(path, report, diagnostics)
    return json.loads(report.read_text())


def analyzer_controls(runs, out):
    work = out/'analyzer-controls'
    work.mkdir()
    with contextlib.redirect_stdout(io.StringIO()):
        analyze.predict(work/'PREDICTIONS.json')
    same = (work/'PREDICTIONS.json').read_bytes() == (PROBE/'PREDICTIONS.json').read_bytes()
    control('analyzer: the committed PREDICTIONS.json is what analyze.py predicts from the committed probe source and scene',
            True, same, same)
    data = analyze.predictions()
    sep = data['separations']
    control('analyzer: every claimed separation holds; retained indistinguishable cells are kept as such',
            True, sep, all(sep['separated'].values()) and all(sep['retained_indistinguishable'].values()))
    reports = {name: fit(runs[name]['dir']/'values.jsonl', work, name) for name in COMPLETE}

    def fits(report, group, key):
        return report[group][key].get('fits', [])

    def has(name, group, key, want, absent=()):
        f = fits(reports[name], group, key)
        ok = all(w in f for w in want) and not any(a in f for a in absent)
        control(f'analyzer: {name} {key} fits {want} and not {list(absent)}', [want, list(absent)], f, ok)
    for key in ('op.baseOff', 'op.offAOff', 'op.mirrorOff', 'op.on1Off', 'op.on3Off', 'op.on2Off'):
        has('normal', 'operations', key, ['POST'], ['PRE', 'REVLEVEL'] if key in ('op.offAOff', 'op.mirrorOff') else ['PRE'])
    has('normal', 'operations', 'op.on1On', ['ADD', 'IDDESC'], ['FIRSTAWAKE', 'AFIRSTAWAKE', 'PRE', 'REVPOST'])
    has('normal', 'operations', 'op.on3On', ['ADD', 'IDDESC'], ['LASTENABLE', 'FIRSTAWAKE'])
    has('normal', 'operations', 'op.on2On', ['IDDESC'], ['DOCMB', 'PRE', 'FIRSTAWAKE'])
    has('revlevel', 'operations', 'op.offAOff', ['REVLEVEL'], ['POST'])
    has('revlevel', 'operations', 'op.mirrorOff', ['REVLEVEL'], ['POST'])
    has('pre', 'operations', 'op.offAOff', ['PRE'], ['POST'])
    has('pre', 'operations', 'op.on1On', ['PRE'], ['ADD'])
    has('firstawake', 'operations', 'op.on1On', ['FIRSTAWAKE', 'AFIRSTAWAKE'], ['ADD', 'IDDESC'])
    has('firstawake', 'operations', 'op.on2On', ['FIRSTAWAKE', 'DOCMB'], ['PRE'])
    has('scenehier', 'operations', 'op.on2Off', ['POST'], [])
    has('lastenable', 'operations', 'op.on3On', ['LASTENABLE', 'ALASTENABLE'], ['FIRSTAWAKE', 'ADD'])
    has('lastenable', 'operations', 'op.on1On', ['LASTENABLE', 'FIRSTAWAKE'], ['ADD'])
    has('normal', 'contacts', 'solidOnScripts', ['F/AT_REMOVAL/REMOVAL'], ['S/AT_REMOVAL/REMOVAL', 'CUR/AT_REMOVAL/REMOVAL'])
    has('normal', 'contacts', 'triggerOnScripts', ['F/AT_REMOVAL/REMOVAL'], ['S/AT_REMOVAL/REMOVAL'])
    has('normal', 'contacts', 'solidOffScripts', ['F/NONE/REMOVAL'], ['F/AT_REMOVAL/REMOVAL'])
    has('normal', 'contacts', 'solidOnBuiltins', ['F/AT_REMOVAL/REMOVAL', 'S/AT_REMOVAL/REMOVAL'], ['CUR/AT_REMOVAL/REMOVAL'])
    has('builtinsfirst', 'contacts', 'solidOnScripts', ['S/AT_REMOVAL/REMOVAL'], ['F/AT_REMOVAL/REMOVAL'])
    has('scriptsfirst', 'contacts', 'solidOnScripts', ['SCRIPTS_FIRST/AT_REMOVAL/REMOVAL'], ['F/AT_REMOVAL/REMOVAL'])
    has('deferred', 'contacts', 'solidOnScripts', ['F/DEFERRED/REMOVAL'], ['F/AT_REMOVAL/REMOVAL', 'F/NONE/REMOVAL'])
    has('noexit', 'contacts', 'triggerOnScripts', ['F/NONE/REMOVAL'], ['F/AT_REMOVAL/REMOVAL'])
    has('flags', 'contacts', 'solidOnScripts', ['F/AT_REMOVAL/FLAGS'], ['F/AT_REMOVAL/REMOVAL'])
    has('normal', 'particles', 'psScripts', ['F/REMOVAL'], ['S/REMOVAL', 'CUR/REMOVAL'])
    has('builtinsfirst', 'particles', 'psScripts', ['S/REMOVAL'], ['F/REMOVAL'])
    nocontact = reports['nocontact']['contacts']
    control('analyzer: no contact before the call makes every 2D case NOT_EVALUABLE (not a gate)', 'NOT_EVALUABLE',
            {k: v['outcome'] for k, v in nocontact.items()},
            all(v['outcome'] == 'NOT_EVALUABLE' and any('no contact' in r for r in v['reasons']) for v in nocontact.values())
            and not guards.record_problems(runs['nocontact']['dir']/'values.jsonl', RELEASE, DEVICE))
    nops = reports['noparticle']['particles']
    control('analyzer: an unchanged ParticleSystem state is the partial D-1 negative (NOT_EVALUABLE, missing observable named)',
            'NOT_EVALUABLE', {k: v.get('reasons') for k, v in nops.items()},
            all(v['outcome'] == 'NOT_EVALUABLE' and 'missing observable' in v['reasons'][0] for v in nops.values()))
    summary = reports['normal']['summary']
    control('analyzer: normal summaries (OFF POST family; built-in interleaving F)', ['POST', 'F'],
            [summary['off_traversal'].get('consistent'), summary['builtin_interleaving'].get('consistent')],
            'POST' in summary['off_traversal'].get('consistent', []) and summary['builtin_interleaving'].get('consistent') == ['F'])
    # The id hypotheses use the capture's own diagnostics: swapping two recorder ids changes IDDESC.
    normal = runs['normal']['dir']
    rows = [json.loads(line) for line in (normal/'diagnostics.jsonl').read_text().splitlines()]
    for row in rows:
        if row['what'] == 'ids:setup':
            comps = row['extra']['RX']['components'], row['extra']['R']['components']
            a = next(c for c in comps[0] if c[0].startswith('ActivationMechanismsProbe'))
            b = next(c for c in comps[1] if c[0].startswith('ActivationMechanismsProbe'))
            a[1], b[1] = b[1], a[1]
    swapped = work/'swapped-diagnostics.jsonl'
    swapped.write_text(''.join(json.dumps(r)+'\n' for r in rows))
    report = fit(normal/'values.jsonl', work, 'swapped', swapped)
    control('analyzer: IDDESC follows the capture\'s recorded ids (swapped ids no longer fit)', 'IDDESC absent',
            fits(report, 'operations', 'op.on1On'), 'IDDESC' not in fits(report, 'operations', 'op.on1On')
            and 'ADD' in fits(report, 'operations', 'op.on1On'))
    report = fit(normal/'values.jsonl', work, 'no-diagnostics', work/'missing-diagnostics.jsonl')
    miss = report['operations']['op.on2On']['not_evaluable_candidates']
    control('analyzer: without recorded ids the id hypotheses are not evaluable (never inferred)', ['IDDESC', 'GOIDDESC', 'TRIDDESC'],
            sorted(miss), all(k in miss for k in ('IDDESC', 'GOIDDESC', 'TRIDDESC')) and 'IDDESC' not in fits(report, 'operations', 'op.on2On'))
    base = guards.parse(normal/'values.jsonl')[0]

    def synthetic(name, change):
        rows = copy.deepcopy(base)
        change(rows)
        path = work/f'{name}.jsonl'
        write_rows(path, rows)
        shutil.copyfile(normal/'diagnostics.jsonl', work/'diagnostics.jsonl')
        return fit(path, work, name, normal/'diagnostics.jsonl')

    def at(rows, key):
        return next(r for r in rows if r['k'] == key)
    report = synthetic('unpredicted', lambda rows: at(rows, 'op.offAOff.calls').__setitem__('v', at(rows, 'op.offAOff.calls')['v'][1:]+at(rows, 'op.offAOff.calls')['v'][:1]))
    control('analyzer: an unpredicted order is NONE (valid)', 'NONE', report['operations']['op.offAOff']['outcome'],
            report['operations']['op.offAOff']['outcome'] == 'NONE')

    def twice(rows):
        st = at(rows, 'op.offAOff.before.A1')['v']
        st['recorders'][0]['counts']['Awake'] = 2
    report = synthetic('awake-twice', twice)
    control('analyzer: a broken precondition is NOT_EVALUABLE, never refitted', 'NOT_EVALUABLE',
            report['operations']['op.offAOff']['outcome'], report['operations']['op.offAOff']['outcome'] == 'NOT_EVALUABLE')

    def same_awake(rows):
        calls = at(rows, 'setup.calls')['v']
        on1 = [e for e in calls if e['n'] in ('R', 'RX', 'RY', 'RZ')]
        rest = [e for e in calls if e['n'] not in ('R', 'RX', 'RY', 'RZ')]
        order = {'RX': 0, 'R': 1, 'RZ': 2, 'RY': 3}
        at(rows, 'setup.calls')['v'] = rest + sorted(on1, key=lambda e: order[e['n']])
    report = synthetic('add-equals-awake', same_awake)
    control('analyzer: ON-1 is NOT_EVALUABLE when the actual first Awake equals the add order', 'NOT_EVALUABLE',
            report['operations']['op.on1On'].get('reasons'), report['operations']['op.on1On']['outcome'] == 'NOT_EVALUABLE')
    report = synthetic('scene-tree-changed', lambda rows: at(rows, 'boot.scene')['v'].reverse())
    control('analyzer: a read-back scene tree other than the authored one makes the scene cases NOT_EVALUABLE',
            'NOT_EVALUABLE', report['operations']['op.on2On']['outcome'],
            report['operations']['op.on2On']['outcome'] == 'NOT_EVALUABLE' and report['operations']['op.offAOn']['outcome'] != 'NOT_EVALUABLE')

    def layout(rows):
        c = at(rows, 'contact.solidOnScripts.components')['v']
        c['B0C'] = ['Transform', 'CircleCollider2D', 'ActivationMechanismsProbe:r', 'Contact2DObserver']
    report = synthetic('layout-changed', layout)
    control('analyzer: a 2D case whose component order is not the authored layout is NOT_EVALUABLE', 'NOT_EVALUABLE',
            report['contacts']['solidOnScripts']['outcome'], report['contacts']['solidOnScripts']['outcome'] == 'NOT_EVALUABLE')

    def split_exits(rows):
        calls = at(rows, 'contact.solidOnScripts.calls')['v']
        obs = [e for e in calls if e['t'] == 'observer']
        rec = [e for e in calls if e['t'] == 'recorder']
        at(rows, 'contact.solidOnScripts.calls')['v'] = [obs[0], rec[0], *obs[1:], *rec[1:]]
    report = synthetic('split-exits', split_exits)
    control('analyzer: exits split around an OnDisable fit no precommitted model (NONE)', 'NONE',
            report['contacts']['solidOnScripts']['outcome'], report['contacts']['solidOnScripts']['outcome'] == 'NONE')
    report = synthetic('multiple', lambda rows: None)
    control('analyzer: indistinguishable cells are MULTIPLE (builtins-first layout F = S)', 'MULTIPLE',
            report['contacts']['solidOnBuiltins']['outcome'], report['contacts']['solidOnBuiltins']['outcome'] == 'MULTIPLE')


# ---------------------------------------------------------------- 5. run controls

def synthetic_capture(directory, normal_dir, project):
    capture = directory/'capture'
    capture.mkdir(parents=True)
    for name in ('values.jsonl', 'runner-events.jsonl', 'diagnostics.jsonl'):
        shutil.copyfile(normal_dir/name, capture/name)
    j = run.job(directory.parent, directory.name, RELEASE)
    files = run.prepared_files(project)
    (directory/'inputs.json').write_text(json.dumps({'release': RELEASE, 'files': files}))
    (directory/'prelaunch-inputs.json').write_text(json.dumps({
        'selected_release': RELEASE, 'files': {k: {'sha256': v} for k, v in files.items()},
        'environment': {guards.OUTPUT_VARIABLE: str(capture.resolve()), 'UNBOUND_PROBE_UNITY_VERSION': RELEASE}}))
    (directory/'editor-run.json').write_text(json.dumps({
        'kind': 'editor-gpu', 'require_gpu': True, 'command': j.command(), 'exit_code': 0, 'signal': None,
        'forced_exit': None, 'forced_detail': None, 'renderer': 'NVIDIA GeForce RTX 4060/PCIe/SSE2',
        'renderer_ok': True, 'shutdown_logged': True, 'launcher': {'exit': 0},
        'late_sweeps': {'log': str(directory/'hub-sweeps.jsonl'), 'delays': [60, 150, 300]},
        'fake': 'control; no Unity Editor ran'}))
    (directory/'editor.log').write_text('Renderer: NVIDIA GeForce RTX 4060/PCIe/SSE2\nCleanup mono\n')
    return j


def run_controls(normal_dir, out):
    work = out/'run-controls'
    project = PROBE
    positive = work/'positive'
    j = synthetic_capture(positive, normal_dir, project)
    watched = [str((positive/'capture'/name).resolve()) for name in ('runner-events.jsonl', 'values.jsonl')]
    control('launch: only runner transitions and the completed record count as progress', watched, j.progress_files,
            j.progress_files == watched)
    control('launch: 300 s external progress inside the 1200 s hard deadline, shutdown 30 / kill 10',
            [300, 1200, 30, 10], [j.progress_timeout, j.timeout, j.shutdown_grace, j.kill_grace],
            [j.progress_timeout, j.timeout, j.shutdown_grace, j.kill_grace] == [300, 1200, 30, 10])
    control('launch: vglrun EGL batchmode GPU Record command without -quit/-nographics', [], guards.command_problems(j.command(), RELEASE),
            not guards.command_problems(j.command(), RELEASE) and j.batch and j.graphics and j.kind == 'editor-gpu')
    problems = guards.capture_problems(positive, project, RELEASE, DEVICE)
    control('capture: the synthetic complete capture passes every run guard (positive)', [], problems, not problems)

    def case(name, change, expect):
        directory = work/re.sub(r'\W+', '-', name)
        synthetic_capture(directory, normal_dir, project)
        change(directory)
        problems = guards.capture_problems(directory, project, RELEASE, DEVICE)
        control('capture refused: '+name, expect, problems[:4], any(expect in p for p in problems))

    def edit_json(name, change):
        def apply(directory):
            data = json.loads((directory/name).read_text())
            change(data, directory)
            (directory/name).write_text(json.dumps(data))
        return apply

    def command(change):
        return edit_json('editor-run.json', lambda d, _: d.__setitem__('command', change(list(d['command']))))
    case('forced shutdown-guard exit after a complete marker', edit_json('editor-run.json', lambda d, _: d.update(exit_code=None, forced_exit='shutdown-guard', forced_detail='still alive')), 'forced exit shutdown-guard')
    case('watchdog forced exit', edit_json('editor-run.json', lambda d, _: d.update(exit_code=137, forced_exit='watchdog')), 'forced exit watchdog')
    case('no-progress forced exit after completion', edit_json('editor-run.json', lambda d, _: d.update(exit_code=137, forced_exit='no-progress')), 'forced exit no-progress')
    case('cancelled', edit_json('editor-run.json', lambda d, _: d.update(exit_code=143, forced_exit='cancelled')), 'forced exit cancelled')
    case('Editor exit 255 with a complete marker', edit_json('editor-run.json', lambda d, _: d.update(exit_code=255)), 'Editor exit 255')
    case('software renderer', edit_json('editor-run.json', lambda d, _: d.update(renderer='llvmpipe (LLVM 19.1.1, 256 bits)', renderer_ok=False)), 'not the NVIDIA GPU')
    case('no shutdown line', edit_json('editor-run.json', lambda d, _: d.update(shutdown_logged=False)), 'no shutdown line')
    case('launcher exit 1', edit_json('editor-run.json', lambda d, _: d.update(launcher={'exit': 1})), 'launcher exit 1')
    case('-quit passed', command(lambda c: c+['-quit']), '-quit must not be passed')
    case('-nographics passed', command(lambda c: c[:5]+['-nographics']+c[5:]), '-nographics must not be passed')
    case('no vglrun', command(lambda c: c[3:]), 'not a vglrun EGL command')
    case('no -batchmode', command(lambda c: [a for a in c if a != '-batchmode']), '-batchmode is missing')
    case('no Record entry point', command(lambda c: [a for a in c if a not in ('-executeMethod', 'ProbeRunner.Record')]), 'ProbeRunner.Record is missing')
    case('Editor of another release', command(lambda c: [a.replace(RELEASE, '6000.0.70f1') for a in c]), 'is not release')
    case('no Editor result', lambda d: (d/'editor-run.json').unlink(), 'no Editor result')
    case('prelaunch release', edit_json('prelaunch-inputs.json', lambda d, _: d.update(selected_release='6000.0.70f1')), 'selected release')
    case('stale launched probe script', edit_json('prelaunch-inputs.json', lambda d, _: d['files']['assets/Scripts/ActivationMechanismsProbe.cs'].update(sha256='0'*64)), 'differs from the prepared fixture')
    case('stale launched scene', edit_json('prelaunch-inputs.json', lambda d, _: d['files']['assets/Scenes/Probe.unity'].update(sha256='0'*64)), 'assets/Scenes/Probe.unity differs')
    case('stale launched package manifest (module drift)', edit_json('prelaunch-inputs.json', lambda d, _: d['files']['packages/manifest.json'].update(sha256='0'*64)), 'packages/manifest.json differs')
    case('launched extra project input', edit_json('prelaunch-inputs.json', lambda d, _: d['files'].update({'assets/Scripts/Other.cs': {'sha256': '1'*64}})), 'not in the prepared fixture')
    case('capture variable elsewhere', edit_json('prelaunch-inputs.json', lambda d, _: d['environment'].update({guards.OUTPUT_VARIABLE: '/elsewhere'})), 'capture variable')

    def events(change):
        def apply(directory):
            path = directory/'capture/runner-events.jsonl'
            rows = [json.loads(line) for line in path.read_text().splitlines()]
            rows = change(rows)
            path.write_text(''.join(json.dumps(r)+'\n' for r in rows))
        return apply

    def renumber(rows):
        for i, r in enumerate(rows):
            r['seq'] = i
        return rows
    case('runner never quit', events(lambda rows: [r for r in rows if r['e'] != 'exit']), 'runner transitions')

    def swap(rows):
        a = next(i for i, r in enumerate(rows) if r['e'] == 'finished')
        b = next(i for i, r in enumerate(rows) if r['e'] == 'leave')
        rows[a], rows[b] = rows[b], rows[a]
        return renumber(rows)
    case('runner left Play before finishing', events(swap), 'runner transitions')
    case('runner sequence gap', events(lambda rows: [r for i, r in enumerate(rows) if i != 1]), 'not contiguous')
    case('runner probe finished 3', events(lambda rows: [dict(r, detail='3') if r['e'] == 'finished' else r for r in rows]), "finished with '3'")
    case('runner event after exit (last marker)', events(lambda rows: renumber(rows+[{'seq': 0, 't': 9, 'e': 'state', 'detail': 'x'}])), 'not exit')
    case('runner not batchmode', events(lambda rows: [dict(r, detail=r['detail'].replace('batchmode=true', 'batchmode=false')) if r['e'] == 'record' else r for r in rows]), 'not record in batchmode')

    def log(text):
        return lambda d: (d/'editor.log').write_text((d/'editor.log').read_text()+text+'\n')
    case('native fault in the Editor log', log('Caught fatal signal - signo:11 code:1 errno:0 addr:0x40'), 'Caught fatal signal')
    case('compile error in the Editor log', log('Assets/Scripts/ActivationMechanismsProbe.cs(3,1): error CS0246: x'), 'error CS0246')
    case('no renderer in the Editor log', lambda d: (d/'editor.log').write_text('Cleanup mono\n'), 'names no renderer')
    case('Editor-side watchdog file', lambda d: (d/'capture/watchdog.txt').write_text('x\n'), 'watchdog fired')
    case('partial record left', lambda d: (d/'capture/values.jsonl.partial').write_text(''), 'partial record')
    case('record refused inside the capture', lambda d: (d/'capture/values.jsonl').write_text('{"k":"complete","v":true}\n'), 'record: missing')
    case('raw diagnostics missing', lambda d: (d/'capture/diagnostics.jsonl').unlink(), 'diagnostics unreadable')
    case('raw diagnostics phase missing', lambda d: (d/'capture/diagnostics.jsonl').write_text(
        ''.join(l+'\n' for l in (d/'capture/diagnostics.jsonl').read_text().splitlines() if '"ids:setup"' not in l)), 'diagnostics phases')
    changed = work/'changed-project'
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, changed/part)
    (changed/'Assets/Scenes/Probe.unity').write_text('changed\n')
    (changed/'ProjectSettings/TimeManager.asset').write_text('rewritten\n')
    (changed/'Assets/Scenes/Probe').mkdir()
    (changed/'Assets/Scenes/Probe/LightingData.asset').write_text('written\n')
    directory = work/'fixture-changed'
    synthetic_capture(directory, normal_dir, project)
    problems = guards.capture_problems(directory, changed, RELEASE, DEVICE)
    control('capture refused: the hand-authored scene changed by the Editor', 'changed or was removed', problems[:4],
            any('Probe.unity changed or was removed' in p for p in problems))
    delta = guards.authored_delta(directory, changed)
    want = {'changed': ['assets/Scenes/Probe.unity', 'settings/TimeManager.asset'], 'removed': [],
            'added': ['assets/Scenes/Probe/LightingData.asset']}
    control('capture: authored settings deltas and Editor-written files are recorded, not refused', want, delta,
            delta == want and not any('TimeManager' in p or 'LightingData' in p for p in problems))
    return j


# ---------------------------------------------------------------- 6. pair, admission, seals, graph, preflight

def fake_preflight(path, passed=True, sources=None, identity_only=False):
    record = {'passed': passed, 'release': RELEASE, 'identity_only': identity_only,
              'sources': sources or {s: sha(PROBE/s) for s in run.SOURCES},
              'results': {a: {'rsp': {'rebound_text': f'-target:library\n-out:"Library/Bee/artifacts/2400b0aE.dag/{a}.dll"\n'
                                                       f'-define:UNITY_6000\n-r:"/x/UnityEngine.Physics2DModule.dll"\n'
                                                       f'-r:"/x/UnityEngine.ParticleSystemModule.dll"\n"{preflight.SOURCES[a][0]}"\n'},
                              'rsp2': {'rebound_text': ''}} for a in preflight.SOURCES}}
    path.write_text(json.dumps(record))
    return record


def fake_graph(project, record, dag='2400b0aE.dag', change=None):
    root = Path(project)/'Library/Bee/artifacts'/dag
    root.mkdir(parents=True, exist_ok=True)
    for a, r in record['results'].items():
        text = r['rsp']['rebound_text'].replace('2400b0aE.dag', dag)
        (root/f'{a}.rsp').write_text(change(a, text) if change else text)
        (root/f'{a}.rsp2').write_text(r['rsp2']['rebound_text'])
        (root/f'{a}.UnityAdditionalFile.txt').write_text(str(Path(project).resolve()))


def fake_editor(normal_dir, record, behaviour=None):
    """A fake editor_runner.run: writes a complete synthetic launch for the job (no Editor starts)."""
    behaviour = behaviour or {}
    calls = []

    def launch(job):
        calls.append(job.label)
        out = Path(job.out)
        capture = out/'capture'
        for name in ('values.jsonl', 'runner-events.jsonl', 'diagnostics.jsonl'):
            shutil.copyfile(normal_dir/name, capture/name)
        files = run.prepared_files(job.project)
        (out/'prelaunch-inputs.json').write_text(json.dumps({
            'selected_release': RELEASE, 'files': {k: {'sha256': v} for k, v in files.items()},
            'environment': {guards.OUTPUT_VARIABLE: str(capture.resolve()), 'UNBOUND_PROBE_UNITY_VERSION': RELEASE}}))
        result = {'kind': 'editor-gpu', 'require_gpu': True, 'command': job.command(), 'exit_code': behaviour.get('exit', 0),
                  'signal': None, 'forced_exit': behaviour.get('forced'), 'forced_detail': None,
                  'renderer': 'NVIDIA GeForce RTX 4060/PCIe/SSE2', 'renderer_ok': True, 'shutdown_logged': True,
                  'launcher': {'exit': 0}, 'late_sweeps': {'log': str(out/'hub-sweeps.jsonl'), 'delays': list(job.late_sweeps)},
                  'fake': 'control; no Unity Editor ran'}
        (out/'editor-run.json').write_text(json.dumps(result))
        (out/'editor.log').write_text('Renderer: NVIDIA GeForce RTX 4060/PCIe/SSE2\nCleanup mono\n')
        fake_graph(job.project, record, change=behaviour.get('graph'))
        return result
    return launch, calls


def pair_controls(normal_dir, out):
    work = out/'pair-controls'
    work.mkdir(parents=True)
    good = work/'preflight-good.json'
    record = fake_preflight(good)
    clean = lambda: {'commit': 'f'*40, 'clean': True, 'status': []}  # noqa: E731

    def editor(root, repeat, preflight_path, behaviour=None, git=clean):
        launch, calls = fake_editor(normal_dir, record, behaviour)
        with mock.patch.object(run.editor_runner, 'run', launch), mock.patch.object(run, 'git_state', git), \
                mock.patch.object(run, 'reference_file', lambda probe, suffix: root/'Reference/unity-6000.0.71f1.jsonl'), \
                contextlib.redirect_stdout(io.StringIO()):
            try:
                code = run.editor(root, repeat, RELEASE, preflight_path)
                return ('passes' if code == 0 else f'problems {code}'), calls
            except SystemExit as error:
                return str(error), calls

    def check(root, repeat):
        with mock.patch.object(run, 'reference_file', lambda probe, suffix: root/'Reference/unity-6000.0.71f1.jsonl'), \
                contextlib.redirect_stdout(io.StringIO()) as text:
            try:
                return run.check(root, repeat, RELEASE), text.getvalue()
            except SystemExit as error:
                return str(error), text.getvalue()

    def publish(root):
        with mock.patch.object(run, 'reference_file', lambda probe, suffix: root/'Reference/unity-6000.0.71f1.jsonl'), \
                contextlib.redirect_stdout(io.StringIO()) as text:
            try:
                return run.publish(root, RELEASE), text.getvalue()
            except SystemExit as error:
                return str(error), text.getvalue()

    def full(name, behaviour2=None):
        root = work/name
        steps = [editor(root, 'native1', good)[0], check(root, 'native1')[0], editor(root, 'native2', good, behaviour2)[0],
                 check(root, 'native2')[0]]
        return root, steps
    root, steps = full('agree')
    code, text = publish(root)
    target = root/'Reference/unity-6000.0.71f1.jsonl'
    control('pair: fake-launched native1 -> check (seal) -> native2 -> check -> publish (positive, private target)',
            ['passes', 0, 'passes', 0, 0], steps+[code],
            steps == ['passes', 0, 'passes', 0] and code == 0 and target.read_bytes() == (root/'native1/capture/values.jsonl').read_bytes())
    before = target.stat().st_mtime_ns, target.read_bytes()
    code, text = publish(root)
    control('pair refused: publishing the label again (its bound reference changed) never rewrites the reference',
            'the reference changed after the label was made', code,
            isinstance(code, str) and 'reference changed after the label' in code
            and (target.stat().st_mtime_ns, target.read_bytes()) == before)
    for name, existing, want in (('equal', None, (0, 'equals the pair')), ('different', b'{"k":"complete","v":true}\n', (1, 'DIFFERS'))):
        root = work/f'existing-{name}-reference'
        target = root/'Reference/unity-6000.0.71f1.jsonl'
        target.parent.mkdir(parents=True)
        target.write_bytes(existing if existing is not None else (normal_dir/'values.jsonl').read_bytes())
        written = target.read_bytes(), target.stat().st_mtime_ns
        steps = [editor(root, 'native1', good)[0], check(root, 'native1')[0], editor(root, 'native2', good)[0],
                 check(root, 'native2')[0]]
        code, text = publish(root)
        control(f'pair: a reference that existed before the label ({name}) is compared, never rewritten',
                ['passes', 0, 'passes', 0, *want], steps+[code, text.strip()],
                steps == ['passes', 0, 'passes', 0] and code == want[0] and want[1] in text
                and (target.read_bytes(), target.stat().st_mtime_ns) == written)
    a1 = json.loads((root/'native1/admission.json').read_text())
    control('seal: the admission hashes every launch artifact except the cleanup journal', 'no hub-sweeps.jsonl',
            sorted(a1['artifacts']), 'hub-sweeps.jsonl' not in a1['artifacts'] and 'capture/values.jsonl' in a1['artifacts']
            and 'editor-run.json' in a1['artifacts'])

    def journal(root, rows, raw=None):
        path = root/'native1/hub-sweeps.jsonl'
        if raw is not None:
            path.write_bytes(raw)
        else:
            with path.open('a') as stream:
                for r in rows:
                    stream.write(json.dumps(r)+'\n')
    for name, prepare, after, admitted in (
            ('late cleanup appended after admission', None, lambda r: journal(r, [{'after_s': 60, 'time': 1e9, 'swept': []}]), True),
            ('late cleanup prefix then append', lambda r: journal(r, [{'after_s': 60, 'time': 1e9, 'swept': []}]),
             lambda r: journal(r, [{'after_s': 150, 'time': 1e9+90, 'swept': []}]), True),
            ('late cleanup write in progress', None, lambda r: journal(r, [], raw=b'{"after_s": 60, "ti'), True),
            ('late cleanup prefix rewritten', lambda r: journal(r, [{'after_s': 60, 'time': 1e9, 'swept': []}]),
             lambda r: journal(r, [], raw=b'{"after_s": 60, "time": 2e9, "swept": []}\n'), False),
            ('late cleanup prefix truncated', lambda r: journal(r, [{'after_s': 60, 'time': 1e9, 'swept': []}]),
             lambda r: journal(r, [], raw=b''), False),
            ('late cleanup malformed completed row', None, lambda r: journal(r, [{'after_s': 61, 'time': 1e9, 'swept': []}]), False),
            ('late cleanup reordered schedule', lambda r: journal(r, [{'after_s': 150, 'time': 1e9, 'swept': []}]),
             lambda r: journal(r, [{'after_s': 60, 'time': 1e9+1, 'swept': []}]), False)):
        root = work/re.sub(r'\W+', '-', name)
        e1 = editor(root, 'native1', good)[0]
        if prepare:
            prepare(root)
        c1 = check(root, 'native1')[0]
        after(root)
        e2 = editor(root, 'native2', good)[0]
        if admitted:
            check(root, 'native2')
            code, _ = publish(root)
            control('seal: '+name+' keeps the admission (native2 starts, publish passes)', ['passes', 0, 'passes', 0],
                    [e1, c1, e2, code], [e1, c1, e2, code] == ['passes', 0, 'passes', 0])
        else:
            control('seal refused: '+name+' revokes the admission (native2 does not start)', 'native1 is not admitted',
                    [e1, c1, e2], e1 == 'passes' and c1 == 0 and 'native1 is not admitted' in e2)
    root = work/'unregistered-journal'
    editor(root, 'native1', good)
    data = json.loads((root/'native1/editor-run.json').read_text())
    data.pop('late_sweeps')
    (root/'native1/editor-run.json').write_text(json.dumps(data))
    (root/'native1/hub-sweeps.jsonl').write_text('{"after_s": 60, "time": 1, "swept": []}\n')
    code, text = check(root, 'native1')
    control('seal refused: an unregistered cleanup journal', 'unregistered', text.strip()[:200],
            code == 1 and 'unregistered cleanup journal' in text)
    root = work/'changed-after-admission'
    editor(root, 'native1', good)
    check(root, 'native1')
    (root/'native1/capture/diagnostics.jsonl').write_text('{}\n')
    out2 = editor(root, 'native2', good)[0]
    control('seal refused: a sealed artifact changed after admission', 'changed after its admission', out2,
            'changed after its admission' in out2)
    root, steps = full('disagree', None)

    def disagree(root):
        path = root/'native2/capture/values.jsonl'
        rows = guards.parse(path)[0]
        for r in rows:
            if r['k'] == 'op.offAOff.calls':
                r['v'] = list(reversed(r['v']))
        write_rows(path, rows)
    root = work/'disagree2'
    editor(root, 'native1', good)
    check(root, 'native1')
    editor(root, 'native2', good)
    disagree(root)
    check(root, 'native2')
    code, text = publish(root)
    control('pair refused: repeats whose raw callback order differs', 'the two admitted repeats differ', code,
            isinstance(code, str) and 'differ' in code and not (root/'Reference/unity-6000.0.71f1.jsonl').exists())
    for name, behaviour, expect in (('forced shutdown-guard exit', {'forced': 'shutdown-guard'}, 'forced exit'),
                                    ('nonzero exit with a complete marker', {'exit': 1}, 'Editor exit 1'),
                                    ('a graph other than the preflight (module drift)',
                                     {'graph': lambda a, t: t.replace('-r:"/x/UnityEngine.Physics2DModule.dll"\n', '')},
                                     'differs from the preflight-compiled graph')):
        root = work/re.sub(r'\W+', '-', name)
        e1, _ = editor(root, 'native1', good, behaviour)
        code, text = check(root, 'native1')
        e2, calls = editor(root, 'native2', good)
        control('admission refused: native1 with '+name, [expect, 'not admitted'], [text.strip()[:160], e2],
                code == 1 and expect in text and 'native1 is not admitted' in e2 and calls == [])
    bad = work/'preflight-bad.json'
    fake_preflight(bad, passed=False)
    stale = work/'preflight-stale.json'
    fake_preflight(stale, sources={s: '0'*64 for s in run.SOURCES})
    ident = work/'preflight-identity-only.json'
    fake_preflight(ident, identity_only=True)
    for name, root, repeat, path, expect, git in (
            ('failed preflight', 'admit-a', 'native1', bad, 'the preflight did not pass', clean),
            ('stale preflight sources', 'admit-b', 'native1', stale, 'differs from the preflight-compiled bytes', clean),
            ('identity-only preflight', 'admit-c', 'native1', ident, 'identity-only', clean),
            ('no preflight record', 'admit-d', 'native1', work/'missing.json', 'no preflight record', clean),
            ('native2 before an admitted native1', 'admit-e', 'native2', good, 'native1 is not admitted', clean),
            ('uncommitted probe changes', 'admit-f', 'native1', good, 'uncommitted changes',
             lambda: {'commit': 'f'*40, 'clean': False, 'status': ['?? x']})):
        outcome, calls = editor(work/root, repeat, path, git=git)
        control('admission refused: '+name, expect, outcome, expect in outcome and calls == [])
    root = work/'admit-reuse'
    editor(root, 'native1', good)
    outcome, calls = editor(root, 'native1', good)
    control('admission refused: label reuse (a launch is never repeated)', 'write-once', outcome, 'write-once' in outcome and calls == [])
    root = work/'admit-manifest'
    editor(root, 'native1', good)
    check(root, 'native1')
    other = work/'preflight-other.json'
    fake_preflight(other)
    other.write_text(other.read_text()+' ')
    outcome, calls = editor(root, 'native2', other)
    control('admission refused: native2 under another preflight than native1\'s label manifest', 'differ from native1', outcome,
            'differ from native1' in outcome and calls == [])
    project = work/'graph/project'
    fake_graph(project, record, dag='99999999.dag')
    problems = guards.graph_problems(project, record)
    control('graph: the same response files under another graph directory name pass', [], problems, not problems)
    for name, mutate, expect in (
            ('another source', lambda a, t: t.replace('ActivationMechanismsProbe.cs', 'Other.cs'), 'differs from the preflight'),
            ('another reference', lambda a, t: t+'-r:"/x/UnityEngine.Extra.dll"\n', 'differs from the preflight'),
            ('a missing module', lambda a, t: t.replace('-r:"/x/UnityEngine.ParticleSystemModule.dll"\n', ''), 'differs from the preflight'),
            ('another analyzer', lambda a, t: t+'-analyzer:"/x/gen.dll"\n', 'differs from the preflight')):
        p = work/'graph'/re.sub(r'\W+', '-', name)
        fake_graph(p, record, change=mutate)
        problems = guards.graph_problems(p, record)
        control('graph refused: '+name, expect, problems[:3], any(expect in x for x in problems))
    p = work/'graph/elsewhere'
    fake_graph(p, record)
    (p/'Library/Bee/artifacts/2400b0aE.dag/Assembly-CSharp.UnityAdditionalFile.txt').write_text('/elsewhere')
    problems = guards.graph_problems(p, record)
    control('graph refused: additional file names another project', 'does not name the launched copy', problems[:3],
            any('does not name the launched copy' in x for x in problems))
    p = work/'graph/none'
    p.mkdir(parents=True)
    problems = guards.graph_problems(p, record)
    control('graph refused: no Editor-written graph', '0 Bee graph directories', problems[:3], any('0 Bee graph' in x for x in problems))
    judge_controls()


def judge_controls():
    ok = {'sources': {'Assets/Scripts/ActivationMechanismsProbe.cs': 'x'}, 'references': {'a.dll': 'h'},
          'analyzers': {'g.dll': 'h'}, 'exit': 0, 'errors': [], 'dll_sha256': 'd'}
    ok_editor = dict(ok, sources={'Assets/Editor/ProbeRunner.cs': 'x'})
    cases = {
        'passes': ({'Assembly-CSharp': ok, 'Assembly-CSharp-Editor': ok_editor}, None),
        'missing reference': ({'Assembly-CSharp': dict(ok, references={'a.dll': None}), 'Assembly-CSharp-Editor': ok_editor}, 'missing references'),
        'missing analyzer': ({'Assembly-CSharp': dict(ok, analyzers={'g.dll': None}), 'Assembly-CSharp-Editor': ok_editor}, 'missing analyzers'),
        'compile error': ({'Assembly-CSharp': dict(ok, exit=1, errors=['x.cs(1,1): error CS1002: ;'], dll_sha256=None)}, 'errors'),
        'other sources': ({'Assembly-CSharp': dict(ok, sources={'Assets/Other.cs': 'x'}), 'Assembly-CSharp-Editor': ok_editor}, 'sources'),
        'editor assembly not compiled': ({'Assembly-CSharp': ok}, 'not compiled'),
    }
    for name, (res, expect) in cases.items():
        problems = preflight.judge(res)
        control('preflight judge: '+name, expect or [], problems, (not problems) if expect is None else any(expect in p for p in problems))
    failed = {'passed': False, 'identity_problems': [], 'corrects': None, 'problems': ['Assembly-CSharp: exit 1, errors [...]'],
              'results': {'Assembly-CSharp': {'exit': 1, 'errors': ['x.cs(1,1): error CS1002: ;']}}}
    control('preflight correction: allowed after compile errors only', True, preflight.compile_errors_only(failed),
            preflight.compile_errors_only(failed))
    for name, change in (('a correction of a correction', {'corrects': {'path': 'x'}}),
                         ('an identity failure', {'identity_problems': ['Packages differ']}),
                         ('a missing reference', {'problems': ['Assembly-CSharp: missing references [x]']}),
                         ('an identity-only record', {'identity_only': True}),
                         ('a passing preflight', {'passed': True})):
        control('preflight correction refused: '+name, False, preflight.compile_errors_only(dict(failed, **change)),
                not preflight.compile_errors_only(dict(failed, **change)))
    rsp = b'-target:library\n-define:A\n"Assets/Scripts/SpriteImportProbe.cs"\n-langversion:9.0\n'
    text, sub = preflight.rebind_sources(rsp, 'Assembly-CSharp')
    want = b'-target:library\n-define:A\n"Assets/Scripts/ActivationMechanismsProbe.cs"\n-langversion:9.0\n'
    control('preflight: only the one source path is rebound', want.decode(), text.decode(),
            text == want and sub['from'] == 'Assets/Scripts/SpriteImportProbe.cs')
    for name, data in (('two sources', rsp+b'"Assets/Scripts/B.cs"\n'), ('a source in another folder', rsp.replace(b'Assets/Scripts/', b'Assets/Other/'))):
        try:
            preflight.rebind_sources(data, 'Assembly-CSharp')
            outcome = 'rebound'
        except ValueError as error:
            outcome = str(error)
        control('preflight refused: '+name, 'ValueError', outcome, outcome != 'rebound')


def preflight_identity_controls(out):
    """The identity proof on a mocked capture label built from the committed sprite import fixture."""
    work = out/'preflight-identity'
    modules = work/'modules'
    modules.mkdir(parents=True)
    for m in preflight.REQUIRED_MODULES:
        (modules/m).write_bytes(b'mock module')
    editor = f'/x/Unity/Hub/Editor/{RELEASE}/Editor/Unity'

    def mock_label(name, change=None):
        root = work/name
        for repeat in ('native1', 'native2'):
            copy_dir = root/'projects'/repeat
            for part in ('Assets', 'Packages', 'ProjectSettings'):
                shutil.copytree(SOURCE_FIXTURE/part, copy_dir/part)
            (copy_dir/'Library/ScriptAssemblies').mkdir(parents=True)
            dag = copy_dir/'Library/Bee/artifacts/2400b0aE.dag'
            dag.mkdir(parents=True)
            for a in preflight.SOURCES:
                (copy_dir/f'Library/ScriptAssemblies/{a}.dll').write_bytes(b'mock')
                refs = ''.join(f'-r:"{modules/m}"\n' for m in preflight.REQUIRED_MODULES)
                (dag/f'{a}.rsp').write_text(f'-target:library\n{refs}-define:UNITY_6000\n"{preflight.GRAPH_SOURCES[a]}"\n')
                (dag/f'{a}.rsp2').write_text('')
                (dag/f'{a}.UnityAdditionalFile.txt').write_text(str(copy_dir.resolve()))
            (root/repeat).mkdir(parents=True)
            shutil.copyfile(SOURCE_FIXTURE/f'Reference/unity-{RELEASE}.jsonl', root/repeat/'sprites.jsonl')
        copy1 = root/'projects/native1'
        (root/'native1/editor.log').write_text(
            f'Unity Editor version:    {RELEASE} (mock)\nCOMMAND LINE ARGUMENTS:\n{editor}\n-batchmode\n-nographics\n'
            f'-projectPath\n{copy1}\n-executeMethod\nProbeRunner.Record\n-logFile\n/x/editor.log\n'
            f'Successfully changed project path to: {copy1}\n    Renderer: Null Device\n'
            '[142/148    1s] Csc Library/Bee/artifacts/2400b0aE.dag/Assembly-CSharp.dll (+2 others)\n'
            '[143/148    0s] CopyFiles Library/ScriptAssemblies/Assembly-CSharp.dll\n'
            '[145/148    2s] Csc Library/Bee/artifacts/2400b0aE.dag/Assembly-CSharp-Editor.dll (+2 others)\n'
            '[147/148    0s] CopyFiles Library/ScriptAssemblies/Assembly-CSharp-Editor.dll\nCleanup mono\n')
        if change:
            change(root)
        written, proof = {}, {}
        return preflight.identity(root, RELEASE, written, proof), proof

    problems, proof = mock_label('good')
    control('preflight identity: a mocked two-copy capture of the committed sprite import fixture with both modules passes',
            [], problems, not problems and proof['log']['nographics'] is True and all(proof['required_modules'].values()))

    def edit(rel, old, new):
        def apply(root):
            p = root/rel
            p.write_text(p.read_text().replace(old, new))
        return apply

    def remove(rel):
        return lambda root: (root/rel).unlink()

    def write(rel, text):
        def apply(root):
            (root/rel).parent.mkdir(parents=True, exist_ok=True)
            (root/rel).write_text(text)
        return apply
    rsp = 'projects/native1/Library/Bee/artifacts/2400b0aE.dag/Assembly-CSharp.rsp'
    for name, change, expect in (
            ('a package changed in the copy', edit('projects/native1/Packages/manifest.json', 'physics2d', 'physics'), 'Packages differ'),
            ('a setting changed in the repeat copy', edit('projects/native2/ProjectSettings/ProjectSettings.asset', 'productName', 'productNames'), 'ProjectSettings differ'),
            ('another Assets in the copy', write('projects/native1/Assets/Sprites/extra.png', 'x'), 'Assets are not the committed'),
            ('an assembly definition in the copy', write('projects/native1/Assets/Scripts/Probe.asmdef', '{}'), 'compile inputs under Assets'),
            ('a second Assembly-CSharp script', write('projects/native2/Assets/Scripts/Other.cs', '//\n'), 'not exactly one Assembly-CSharp script'),
            ('not compiled by the Editor', remove('projects/native1/Library/ScriptAssemblies/Assembly-CSharp.dll'), 'did not compile Assembly-CSharp'),
            ('the repeat wrote another response file', edit('projects/native2/Library/Bee/artifacts/2400b0aE.dag/Assembly-CSharp.rsp', '-define:UNITY_6000', '-define:OTHER'), 'different Assembly-CSharp.rsp'),
            ('no 2D physics module in the runtime graph', lambda root: [edit(f'projects/{r}/Library/Bee/artifacts/2400b0aE.dag/Assembly-CSharp.rsp', 'UnityEngine.Physics2DModule.dll', 'UnityEngine.Other.dll')(root) for r in ('native1', 'native2')], 'no UnityEngine.Physics2DModule.dll'),
            ('no particle module in the runtime graph', lambda root: [edit(f'projects/{r}/Library/Bee/artifacts/2400b0aE.dag/Assembly-CSharp.rsp', 'UnityEngine.ParticleSystemModule.dll', 'UnityEngine.Other.dll')(root) for r in ('native1', 'native2')], 'no UnityEngine.ParticleSystemModule.dll'),
            ('another graph source', lambda root: [edit(f'projects/{r}/Library/Bee/artifacts/2400b0aE.dag/Assembly-CSharp.rsp', 'SpriteImportProbe.cs', 'Other.cs')(root) for r in ('native1', 'native2')], 'does not compile'),
            ('additional file naming another copy', write('projects/native2/Library/Bee/artifacts/2400b0aE.dag/Assembly-CSharp.UnityAdditionalFile.txt', '/elsewhere'), 'does not name its own copy'),
            ('the log names another project', edit('native1/editor.log', '-projectPath\n', '-projectPath\n/elsewhere\n-x\n'), 'not the graph copy'),
            ('the log names another release', edit('native1/editor.log', f'/{RELEASE}/Editor/Unity', '/6000.0.70f1/Editor/Unity'), 'not release'),
            ('a compile error in the log', lambda root: (root/'native1/editor.log').write_text((root/'native1/editor.log').read_text()+'x.cs(1,1): error CS1002: ;\n'), 'compile errors or faults'),
            ('no shutdown in the log', edit('native1/editor.log', 'Cleanup mono', ''), 'no normal shutdown'),
            ('no compile in the log', edit('native1/editor.log', 'CopyFiles Library/ScriptAssemblies/Assembly-CSharp-Editor.dll', ''), 'no compile and copy of Assembly-CSharp-Editor'),
            ('a capture that is not the published reference', lambda root: (root/'native2/sprites.jsonl').write_text('{}\n'), 'not the published sprite import reference'),
            ('no repeat graph', lambda root: shutil.rmtree(root/'projects/native2/Library/Bee'), 'not exactly one Bee graph')):
        problems, _ = mock_label(re.sub(r'\W+', '-', name), change)
        control('preflight identity refused: '+name, expect, problems[:3], any(expect in p for p in problems))


# ---------------------------------------------------------------- 7. comparator

def compare_controls(normal, repeat, out):
    work = out/'compare-controls'
    work.mkdir()

    def main(argv):
        with mock.patch.object(sys, 'argv', ['compare.py', *argv]), contextlib.redirect_stdout(io.StringIO()):
            return compare.main()
    code = main([str(normal), str(repeat), '--report', str(work/'same.json')])
    same = json.loads((work/'same.json').read_text())
    control('compare: identical records have no failing key and no order change', [0, []], [same['failing'], same['order']],
            code == 0 and same['failing'] == 0 and same['order'] == [])
    rows = guards.parse(normal)[0]
    rows = [r for r in rows if r['k'] != 'particles.psBuiltins.after']
    for r in rows:
        if r['k'] == 'contact.solidOnScripts.calls':
            r['v'] = list(reversed(r['v']))
    rows.insert(3, {'k': 'op.extra', 'v': 1})
    i = next(j for j, r in enumerate(rows) if r['k'] == 'settle.relativeFrame')
    rows[i], rows[i+1] = rows[i+1], rows[i]
    other = work/'other.jsonl'
    write_rows(other, rows)
    code = main([str(normal), str(other), '--report', str(work/'other.json')])
    r = json.loads((work/'other.json').read_text())
    got = {'differ': sorted(r['differ']), 'extra': sorted(r['extra']), 'missing': sorted(r['missing']),
           'groups': r['groups'], 'tolerance': r['tolerance'], 'order': sorted(m['key'] for m in r['order'])}
    want = {'differ': ['contact.solidOnScripts.calls'], 'extra': ['op.extra'], 'missing': ['particles.psBuiltins.after'],
            'groups': {'contact.solidOnScripts': 1, 'op.extra': 1, 'particles.psBuiltins': 1}, 'tolerance': 'exact',
            'order': ['settle.relativeFrame', 'settle.state.P']}
    control('compare: missing, extra, differing (reordered raw callback list) keys and moved keys all kept with values '
            'and groups', want, got, code == 1 and got == want
            and r['differ']['contact.solidOnScripts.calls']['other'] == list(reversed(r['differ']['contact.solidOnScripts.calls']['unity'])))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    out = cache_artifact_path(args.out).resolve()
    if out.exists():
        sys.exit(f'refusing to reuse {out}')
    out.mkdir(parents=True)
    source_guards()
    build, compiled = compile_all(out)
    plan = None
    if build:
        runs = drive(build, out)
        scenario_controls(runs)
        normal = runs['normal']['dir']
        if (normal/'values.jsonl').exists():
            record_controls(normal/'values.jsonl', out)
            analyzer_controls(runs, out)
            plan = run.describe(run_controls(normal, out))
            pair_controls(normal, out)
            preflight_identity_controls(out)
            compare_controls(normal/'values.jsonl', runs['normal-repeat']['dir']/'values.jsonl', out)
    failed = [r for r in results if not r['passed']]
    report = {'scope': 'offline controls with FAKE Unity answers: emission, schema, protocol, gates, seals, preflight '
                       'identity, analyzer and comparator only; no Unity behaviour, no Editor/engine/Host/Unity-compiler run',
              'sources': {str(p.relative_to(REPO)): sha(p) for p in
                          [*SCRIPTS, *(PROBE/n for n in run.TOOLS), HERE/'FakeUnity.cs', HERE/'Driver.cs', HERE/'controls.py',
                           PROBE/'Assets/Scenes/Probe.unity', REPO/'tests/editor_runner.py', REPO/'tests/probe_paths.py']},
              'compile': compiled, 'planned_job_shape': plan, 'controls': len(results), 'failed': len(failed),
              'results': results}
    (out/'controls.json').write_text(json.dumps(report, indent=1, default=str)+'\n')
    for r in results:
        print(('PASS ' if r['passed'] else 'FAIL ')+r['control'] + ('' if r['passed'] else f"\n     expect {r['expect']!r}\n     actual {r['actual']!r}"))
    print(f'{len(results)-len(failed)} of {len(results)} controls pass; report {out/"controls.json"}')
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main())
