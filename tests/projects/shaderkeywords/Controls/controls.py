#!/usr/bin/env python3
"""Offline controls of the probe's guards, runner protocol and publish gate. No Editor, engine,
Host or Unity compiler runs here.

    ~/.cache/compat/with-heavy-job --kind tool python3 Controls/controls.py --out NEW_CACHE_DIR

1. Source guards on the two probe scripts (no type/flag read outside the valid-keyword helper,
   no default keyword passed to a material, no CommandBuffer, no transition in the probe script).
2. Compiles the probe's two real scripts at C# 9 (Unity's language version) against
   FakeUnity.cs with the .NET SDK's Roslyn, and runs them through Driver.cs in six scenarios.
   The fake answers are plausible, not Unity's: this checks emission and control flow only.
3. The guards must pass the normal captures (positive controls) and refuse every mocked
   negative control: record mutations, run results, inputs, runner events, Editor logs, repeat
   disagreement, launch admission and the preflight judgement.

Writes NEW_CACHE_DIR/controls.json with every control's expected and actual result; exit 0 only
when all hold.
"""
import argparse
import copy
import contextlib
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
sys.path.insert(0, str(PROBE))
sys.path.insert(0, str(REPO/'tests'))
os.environ['UNBOUND_PROBE_UNITY_VERSION'] = '6000.0.71f1'
import guards  # noqa: E402
import preflight  # noqa: E402
import run  # noqa: E402
import compare  # noqa: E402
import editor_runner  # noqa: E402
from probe_paths import cache_artifact_path  # noqa: E402

RELEASE = '6000.0.71f1'
DEVICE = 'OpenGLCore'
SCRIPTS = [PROBE/'Assets/Scripts/ShaderKeywordsProbe.cs', PROBE/'Assets/Editor/ProbeRunner.cs']
SCENARIOS = ('normal', 'normal-repeat', 'noreload', 'pause', 'watchdog', 'fatal', 'nooutput')
results = []


def control(name, expect, actual, passed):
    results.append({'control': name, 'expect': expect, 'actual': actual, 'passed': bool(passed)})


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


# ---------------------------------------------------------------- 1. source guards

def source_guards():
    probe = SCRIPTS[0].read_text()
    runner = SCRIPTS[1].read_text()
    helper = re.search(r'static string Keyword\(LocalKeyword k\) => !k\.isValid \?.*?;\n', probe, re.S)
    control('source: one valid-keyword helper reads type/flags after isValid', 'helper found', bool(helper), helper)
    rest = probe.replace(helper.group(0), '') if helper else probe
    reads = re.findall(r'\.(type|isDynamic|isOverridable)\b', rest)
    control('source: no type/isDynamic/isOverridable read outside the helper', [], reads, not reads)
    defaults = re.findall(r'default\(LocalKeyword\)', probe)
    control('source: default(LocalKeyword) only in the two local.default reads', 2, len(defaults), len(defaults) == 2)
    set_default = re.findall(r'(SetKeyword|EnableKeyword|DisableKeyword|IsKeywordEnabled)\(default', probe)
    control('source: no default LocalKeyword passed to a keyword API', [], set_default, not set_default)
    code = re.sub(r'//[^\n]*', '', probe+runner)
    control('source: no CommandBuffer in code (comments aside)', False, 'CommandBuffer' in code, 'CommandBuffer' not in code)
    transitions = re.findall(r'EditorApplication\.(Exit|ExitPlaymode|EnterPlaymode)|UnityEditor', probe)
    control('source: the probe script makes no Editor transition', [], transitions, not transitions)
    exits = re.findall(r'EditorApplication\.Exit\(', runner)
    control('source: ProbeRunner quits only in Quit() and the no-output path', 2, len(exits), len(exits) == 2)
    state = re.search(r'playModeStateChanged \+= state => Event\("state", state\.ToString\(\)\);', runner)
    control('source: the play mode state callback only records', 'record-only handler', bool(state), state)


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
    record = {'dotnet': str(exe), 'csc': str(csc), 'csc_sha256': sha(csc), 'framework': version, 'steps': []}
    common = ['/nologo', '/nostdlib+', '/noconfig', *[f'/r:{r}' for r in refs]]
    steps = [
        ('FakeUnity', ['/target:library', '/nowarn:0618', str(HERE/'FakeUnity.cs')], []),
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
            control(f'compile {name} at C# {"9" if name == "Probe" else "default"}', 0, process.returncode, False)
            return None, record
    (build/'Driver.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {
        'tfm': 'net10.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': version},
        'rollForward': 'LatestPatch'}}))
    control('compile the probe scripts at C# 9 against the fakes', 0, 0, True)
    return build, record


def drive(build, out):
    exe = dotnet()[0]
    runs = {}
    for scenario in SCENARIOS:
        target = out/'scenarios'/scenario
        process = subprocess.run([str(exe), str(build/'Driver.dll'), scenario.replace('-repeat', ''),
                                  str(build/'Probe.dll'), str(target)], capture_output=True, text=True)
        (out/'scenarios'/f'{scenario}.log').write_text(process.stdout+process.stderr)
        driver = json.loads((target/'driver.json').read_text()) if (target/'driver.json').exists() else None
        runs[scenario] = {'dir': target, 'process': process.returncode, 'driver': driver}
    return runs


def scenario_controls(runs):
    for name in ('normal', 'noreload', 'pause'):
        r = runs[name]
        d = r['driver'] or {}
        directory = r['dir']
        control(f'{name}: the runner quits 0 once', 0, d.get('exit'), r['process'] == 0 and d.get('exit') == 0)
        control(f'{name}: no forbidden call', [], d.get('forbidden'), d.get('forbidden') == [])
        want = ['EnterPlaymode from delayCall', 'ExitPlaymode from update', 'Exit 0 from update']
        control(f'{name}: transitions deferred to Editor delayCall/update', want, d.get('transitions'),
                d.get('transitions') == want)
        problems = guards.record_problems(directory/'values.jsonl', RELEASE, DEVICE)
        control(f'{name}: the record passes the schema guard (positive)', [], problems, not problems)
        events = guards.runner_event_problems(directory/'runner-events.jsonl')
        control(f'{name}: runner events pass (positive)', [], events, not events)
        control(f'{name}: ProbeRunner turned asynchronous shader compilation off', False, d.get('asyncShaderCompilation'), d.get('asyncShaderCompilation') is False)
    d = runs['pause']['driver'] or {}
    names = [json.loads(line)['e'] for line in (runs['pause']['dir']/'runner-events.jsonl').read_text().splitlines()]
    control('pause: Error Pause is cleared and recorded', 'unpause event', names.count('unpause'),
            d.get('paused') and not d.get('stillPaused') and 'unpause' in names)
    values = guards.parse(runs['normal']['dir']/'values.jsonl')[0]
    keys = {r['k'] for r in values}
    exercised = ['probe.Probe/KwForms.subshaderTag[count].exception', 'probe.Probe/KwForms.subshaderTag[-1].exception',
                 'local.ctor(null shader).exception', 'variant.revert.step[RevertPropertyOverride(_Missing)].exception']
    control('normal: per-case exceptions recorded without ending their block', exercised,
            [k for k in exercised if k in keys], all(k in keys for k in exercised))
    control('normal: a not-found built-in ends its shader (found false)', False,
            next((r['v'] for r in values if r['k'] == 'builtin.Hidden/BlitCopy.found'), None),
            'builtin.Hidden/BlitCopy.found' in keys and 'builtin.Hidden/BlitCopy.meta.log' not in keys)
    default = next((r['v'] for r in values if r['k'] == 'local.default.keyword'), None)
    control('normal: the default keyword records name and isValid only', {'name': None, 'isValid': False}, default,
            default == {'name': None, 'isValid': False})
    a, b = (runs[n]['dir']/'values.jsonl' for n in ('normal', 'normal-repeat'))
    control('normal: two runs of the fake capture are byte-identical', True, a.read_bytes() == b.read_bytes(),
            a.read_bytes() == b.read_bytes())
    w = runs['watchdog']
    control('watchdog: Editor-side watchdog quits 4', 4, (w['driver'] or {}).get('exit'),
            (w['driver'] or {}).get('exit') == 4 and (w['dir']/'watchdog.txt').exists())
    control('watchdog: refused by the runner-event guard', 'problems',
            guards.runner_event_problems(w['dir']/'runner-events.jsonl'), guards.runner_event_problems(w['dir']/'runner-events.jsonl'))
    f = runs['fatal']
    fatal = guards.record_problems(f['dir']/'values.jsonl', RELEASE, DEVICE)
    control('fatal: the runner quits 3', 3, (f['driver'] or {}).get('exit'), (f['driver'] or {}).get('exit') == 3)
    control('fatal: refused by the record guard', 'fatal problem', fatal[:3], any('fatal' in p for p in fatal))
    events = guards.runner_event_problems(f['dir']/'runner-events.jsonl')
    control('fatal: refused by the runner-event guard', 'finished 3', events, any("finished with '3'" in p for p in events))
    n = runs['nooutput']['driver'] or {}
    control('nooutput: quits 7 from a delayCall, no record', ['Exit 7 from delayCall'], n.get('transitions'),
            n.get('exit') == 7 and n.get('transitions') == ['Exit 7 from delayCall']
            and not (runs['nooutput']['dir']/'values.jsonl').exists())


# ---------------------------------------------------------------- 3. negative record controls

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
    mutated('completion-only', lambda rows: '{"k":"complete","v":true}\n', 'missing context.unityVersion')
    mutated('truncated by lines', lambda rows: ''.join(full.splitlines(True)[:len(base)*2//3]), 'completion marker is not the last line')
    mutated('truncated mid-line', lambda rows: full[:len(full)//2], 'truncated')
    mutated('omitted group', lambda rows: rows.__setitem__(slice(None), [r for r in rows if not r['k'].startswith('variant.')]), 'group variant is missing')
    mutated('omitted negative case', lambda rows: rows.pop(index('probe.Probe/KwForms.subshaderTag[-1].exception')), 'missing probe.Probe/KwForms.subshaderTag[-1]')
    mutated('omitted block log', lambda rows: rows.pop(index('global.ctor.log')), 'missing global.ctor.log')
    mutated('duplicate key', lambda rows: rows.insert(index('mat.new.log'), dict(rows[index('mat.new.log')-1])), 'duplicate key')
    mutated('wrong type', setv('mat.toggle.keyword', 'false'), 'mat.toggle.keyword: wrong type')
    mutated('invalid keyword with flags', setv('local.ctor(NOT_DECLARED)', {'name': 'NOT_DECLARED', 'type': 'UserDefined', 'isDynamic': False, 'isOverridable': False, 'isValid': False}), 'wrong type')
    mutated('fatal record', lambda rows: rows.insert(len(rows)-1, {'k': 'fatal', 'v': 'boom'}), 'fatal')
    mutated('two markers', lambda rows: rows.insert(5, {'k': 'complete', 'v': True}), '2 completion markers')
    mutated('marker not last', lambda rows: rows.append(rows.pop(-2)), 'completion marker is not the last line')
    mutated('extra key', lambda rows: rows.insert(index('mat.toggle.log'), {'k': 'mat.extra', 'v': 1}), 'unexpected record mat.extra')
    mutated('probe fault', lambda rows: rows.insert(index('mat.new.log'), {'k': 'mat.new.exception', 'v': 'NullReferenceException: x'}), 'probe fault in block mat.new')
    mutated('probe shader not found', setv('probe.Probe/KwHlsl.found', False), 'probe shader Probe/KwHlsl was not found')
    mutated('wrong release', setv('context.unityVersion', '6000.0.70f1'), 'context.unityVersion')
    mutated('wrong device', setv('context.graphicsDeviceType', 'Direct3D11'), 'context.graphicsDeviceType')
    mutated('not batchmode', setv('context.isBatchMode', False), 'context.isBatchMode')
    mutated('render pipeline', setv('context.renderPipeline', 'URP'), 'context.renderPipeline')
    mutated('unscoped error', setv('capture.unscopedLogs', ['Error: boom']), 'unexpected errors outside the probe cases')
    mutated('not a k/v record', lambda rows: full.replace('{"k":"mat.toggle.log"', '{"key":"mat.toggle.log"', 1), 'not a {"k","v"} record')


# ---------------------------------------------------------------- 4. negative run controls

def synthetic_capture(directory, normal_dir, project):
    """A capture directory that should pass every run guard (built from the fake capture)."""
    capture = directory/'capture'
    capture.mkdir(parents=True)
    for name in ('values.jsonl', 'runner-events.jsonl'):
        shutil.copyfile(normal_dir/name, capture/name)
    j = run.job(directory.parent, directory.name, RELEASE)
    files = run.prepared_files(project)
    (directory/'inputs.json').write_text(json.dumps({'release': RELEASE, 'files': files}))
    (directory/'prelaunch-inputs.json').write_text(json.dumps({
        'selected_release': RELEASE, 'files': {k: {'sha256': v} for k, v in files.items()},
        'environment': {'UNBOUND_PROBE_SHADERKEYWORDS_OUT': str(capture.resolve()), 'UNBOUND_PROBE_UNITY_VERSION': RELEASE}}))
    (directory/'editor-run.json').write_text(json.dumps({
        'kind': 'editor-gpu', 'require_gpu': True, 'command': j.command(), 'exit_code': 0, 'signal': None,
        'forced_exit': None, 'forced_detail': None, 'renderer': 'NVIDIA GeForce RTX 4060/PCIe/SSE2',
        'renderer_ok': True, 'shutdown_logged': True, 'launcher': {'exit': 0}}))
    (directory/'editor.log').write_text('Renderer: NVIDIA GeForce RTX 4060/PCIe/SSE2\nCleanup mono\n')
    return j


def run_controls(normal_dir, out):
    work = out/'run-controls'
    project = PROBE
    positive = work/'positive'
    j = synthetic_capture(positive, normal_dir, project)
    watched = [str((positive/'capture'/name).resolve())
               for name in ('runner-events.jsonl', 'values.jsonl')]
    control('launch: only runner transitions and the completed record count as progress',
            watched, j.progress_files, j.progress_files == watched)
    control('launch: external progress deadline is 300 s inside the 1200 s hard deadline',
            [300, 1200], [j.progress_timeout, j.timeout],
            j.progress_timeout == 300 and j.timeout == 1200)
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
    case('forced shutdown-guard exit', edit_json('editor-run.json', lambda d, _: d.update(exit_code=None, forced_exit='shutdown-guard', forced_detail='still alive')), 'forced exit shutdown-guard')
    case('watchdog forced exit', edit_json('editor-run.json', lambda d, _: d.update(exit_code=137, forced_exit='watchdog')), 'forced exit watchdog')
    case('no-progress forced exit after completion', edit_json('editor-run.json', lambda d, _: d.update(exit_code=137, forced_exit='no-progress')), 'forced exit no-progress')
    case('Editor exit 255', edit_json('editor-run.json', lambda d, _: d.update(exit_code=255)), 'Editor exit 255')
    case('software renderer', edit_json('editor-run.json', lambda d, _: d.update(renderer='llvmpipe (LLVM 19.1.1, 256 bits)', renderer_ok=False)), 'not the NVIDIA GPU')
    case('no shutdown line', edit_json('editor-run.json', lambda d, _: d.update(shutdown_logged=False)), 'no shutdown line')
    case('launcher exit 1', edit_json('editor-run.json', lambda d, _: d.update(launcher={'exit': 1})), 'launcher exit 1')
    case('-quit passed', command(lambda c: c+['-quit']), '-quit must not be passed')
    case('-nographics passed', command(lambda c: c[:5]+['-nographics']+c[5:]), '-nographics must not be passed')
    case('no -batchmode', command(lambda c: [a for a in c if a != '-batchmode']), '-batchmode is missing')
    case('no Record entry point', command(lambda c: [a for a in c if a not in ('-executeMethod', 'ProbeRunner.Record')]), 'ProbeRunner.Record is missing')
    case('Editor of another release', command(lambda c: [a.replace(RELEASE, '6000.0.70f1') for a in c]), 'is not release')
    case('no Editor result', lambda d: (d/'editor-run.json').unlink(), 'no Editor result')
    case('prelaunch release', edit_json('prelaunch-inputs.json', lambda d, _: d.update(selected_release='6000.0.70f1')), 'selected release')
    case('launched input differs', edit_json('prelaunch-inputs.json', lambda d, _: d['files']['assets/Shaders/KwForms.shader'].update(sha256='0'*64)), 'differs from the prepared fixture')
    case('capture variable elsewhere', edit_json('prelaunch-inputs.json', lambda d, _: d['environment'].update(UNBOUND_PROBE_SHADERKEYWORDS_OUT='/elsewhere')), 'capture variable')

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
    case('runner sequence gap', events(lambda rows: [r for i, r in enumerate(rows) if i != 1] if len(rows) > 2 else rows), 'not contiguous')
    case('runner probe finished 3', events(lambda rows: [dict(r, detail='3') if r['e'] == 'finished' else r for r in rows]), "finished with '3'")
    case('runner not batchmode', events(lambda rows: [dict(r, detail=r['detail'].replace('batchmode=true', 'batchmode=false')) if r['e'] == 'record' else r for r in rows]), 'not record in batchmode')

    def log(text):
        return lambda d: (d/'editor.log').write_text((d/'editor.log').read_text()+text+'\n')
    case('native fault in the Editor log', log('Caught fatal signal - signo:11 code:1 errno:0 addr:0x40'), 'Caught fatal signal')
    case('compile error in the Editor log', log("Assets/Scripts/ShaderKeywordsProbe.cs(3,1): error CS0246: x"), 'error CS0246')
    case('shader error in the Editor log', log("Shader error in 'Probe/KwForms': syntax error"), "Shader error in")
    case('Editor-side watchdog file', lambda d: (d/'capture/watchdog.txt').write_text('x\n'), 'watchdog fired')
    case('partial record left', lambda d: (d/'capture/values.jsonl.partial').write_text(''), 'partial record')
    case('record refused inside the capture', lambda d: (d/'capture/values.jsonl').write_text('{"k":"complete","v":true}\n'), 'record: missing')

    changed = work/'changed-project'
    shutil.copytree(PROBE/'Assets', changed/'Assets')
    (changed/'Assets/Shaders/KwFixed.shader').write_text('changed\n')
    directory = work/'fixture-changed'
    synthetic_capture(directory, normal_dir, project)
    problems = guards.capture_problems(directory, changed, RELEASE, DEVICE)
    control('capture refused: fixture changed by the Editor', 'changed or was removed', problems[:4],
            any('KwFixed.shader changed or was removed' in p for p in problems))
    return j


# ---------------------------------------------------------------- 5. pair, admission, preflight

def pair_controls(normal_dir, out):
    work = out/'pair-controls'

    def pair(name, change=None):
        root = work/name
        for repeat in run.REPEATS:
            synthetic_capture(root/repeat, normal_dir, PROBE)
        (root/'projects').mkdir()
        for repeat in run.REPEATS:
            os.symlink(PROBE, root/'projects'/repeat)
        if change:
            change(root)
        return root

    def publish(root, target):
        with mock.patch.object(run, 'reference_file', lambda probe, suffix: target), \
                contextlib.redirect_stdout(io.StringIO()):
            try:
                return run.publish(root, RELEASE), None
            except SystemExit as error:
                return None, str(error)
    root = pair('agree')
    target = work/'agree-reference/unity-6000.0.71f1.jsonl'
    code, refusal = publish(root, target)
    control('pair: two complete agreeing repeats publish (positive, private target)', 'published',
            refusal or 'published', code == 0 and target.read_bytes() == (root/'native1/capture/values.jsonl').read_bytes())
    code, refusal = publish(root, target)
    control('pair refused: existing reference is never overwritten', 'refusing to overwrite', refusal,
            refusal and 'refusing to overwrite' in refusal)

    def disagree(root):
        path = root/'native2/capture/values.jsonl'
        path.write_text(path.read_text().replace('"mat.toggle.keyword","v":false', '"mat.toggle.keyword","v":true', 1))
    root = pair('disagree', disagree)
    code, refusal = publish(root, work/'disagree-reference.jsonl')
    control('pair refused: disagreeing repeats', 'the repeats differ', refusal,
            refusal and 'the repeats differ' in refusal and not (work/'disagree-reference.jsonl').exists())
    root = pair('one-forced', lambda root: (root/'native2/editor-run.json').write_text(
        (root/'native2/editor-run.json').read_text().replace('"forced_exit": null', '"forced_exit": "shutdown-guard"')))
    code, refusal = publish(root, work/'forced-reference.jsonl')
    control('pair refused: one repeat with a forced (shutdown-guard) exit', 'native2: ... forced exit', refusal,
            refusal and refusal.startswith('native2') and 'forced exit' in refusal)

    # Launch admission: no Editor may start in these cases.
    def no_launch(job):
        raise AssertionError('an Editor launch was attempted')
    good = work/'preflight-good.json'
    good.write_text(json.dumps({'passed': True, 'sources': {s: sha(PROBE/s) for s in run.SOURCES}}))
    bad = work/'preflight-bad.json'
    bad.write_text(json.dumps({'passed': False, 'sources': {s: sha(PROBE/s) for s in run.SOURCES}}))
    stale = work/'preflight-stale.json'
    stale.write_text(json.dumps({'passed': True, 'sources': {s: '0'*64 for s in run.SOURCES}}))

    def admit(name, root, repeat, preflight_path, expect):
        with mock.patch.object(run, 'run', no_launch), contextlib.redirect_stdout(io.StringIO()):
            try:
                run.editor(root, repeat, RELEASE, preflight_path)
                outcome = 'started'
            except SystemExit as error:
                outcome = str(error)
            except AssertionError as error:
                outcome = 'LAUNCHED: '+str(error)
        control('admission refused: '+name, expect, outcome, expect in outcome)
    admit('failed preflight', work/'admit-a', 'native1', bad, 'the preflight did not pass')
    admit('stale preflight sources', work/'admit-b', 'native1', stale, 'differs from the preflight-compiled bytes')
    admit('native2 before a passing native1', work/'admit-c', 'native2', good, 'native1 did not pass')
    reuse = work/'admit-d'
    (reuse/'native1').mkdir(parents=True)
    admit('reused repeat directory', reuse, 'native1', good, 'never repeated')
    failed_first = pair('admit-e', lambda root: (root/'native1/capture/watchdog.txt').write_text('x\n'))
    admit('native2 after a refused native1', failed_first, 'native2', good, 'native1 did not pass')

    # Preflight judgement on mocked compiler results.
    ok = {'sources': {'Assets/Scripts/ShaderKeywordsProbe.cs': 'x'}, 'references': {'a.dll': 'h'}, 'exit': 0, 'errors': [], 'dll_sha256': 'd'}
    ok_editor = dict(ok, sources={'Assets/Editor/ProbeRunner.cs': 'x'})
    cases = {
        'passes': ({'Assembly-CSharp': ok, 'Assembly-CSharp-Editor': ok_editor}, None),
        'missing reference': ({'Assembly-CSharp': dict(ok, references={'a.dll': None}), 'Assembly-CSharp-Editor': ok_editor}, 'missing references'),
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
                         ('a passing preflight', {'passed': True})):
        control('preflight correction refused: '+name, False, preflight.compile_errors_only(dict(failed, **change)),
                not preflight.compile_errors_only(dict(failed, **change)))


def compare_controls(normal, repeat, out):
    """The comparator keeps the full union: identical records have no failing key; a changed,
    a removed and an added key are each reported with both values and their group."""
    work = out/'compare-controls'
    work.mkdir()
    report = work/'same.json'
    with contextlib.redirect_stdout(io.StringIO()):
        code = compare_main([str(normal), str(repeat), '--report', str(report)])
    same = json.loads(report.read_text())
    control('compare: identical records have no failing key', 0, same['failing'], code == 0 and same['failing'] == 0)
    rows = guards.parse(normal)[0]
    rows = [r for r in rows if r['k'] != 'variant.child.value']
    for r in rows:
        if r['k'] == 'context.qualityName':
            r['v'] = 'Low'
    rows.insert(3, {'k': 'mat.extra', 'v': 1})
    other = work/'other.jsonl'
    write_rows(other, rows)
    with contextlib.redirect_stdout(io.StringIO()):
        code = compare_main([str(normal), str(other), '--report', str(work/'other.json')])
    r = json.loads((work/'other.json').read_text())
    want = {'missing': ['variant.child.value'], 'extra': ['mat.extra'], 'differ': ['context.qualityName']}
    got = {k: sorted(r[k]) for k in want}
    control('compare: missing, extra and differing keys all kept with values and groups', want, got,
            code == 1 and got == want and r['failing'] == 3 and r['groups'] == {'context': 1, 'mat': 1, 'variant': 1}
            and r['differ']['context.qualityName'] == {'unity': 'Ultra', 'unbound': 'Low'})


def compare_main(argv):
    with mock.patch.object(sys, 'argv', ['compare.py', *argv]):
        return compare.main()


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
            plan = run.describe(run_controls(normal, out))
            pair_controls(normal, out)
            compare_controls(normal/'values.jsonl', runs['normal-repeat']['dir']/'values.jsonl', out)
    failed = [r for r in results if not r['passed']]
    report = {'scope': 'offline controls with fake Unity answers: emission, schema, control flow and gates only; '
                       'no Unity behaviour, no Editor/engine/Host/Unity-compiler run',
              'sources': {str(p.relative_to(REPO)): sha(p) for p in
                          [*SCRIPTS, PROBE/'guards.py', PROBE/'run.py', PROBE/'compare.py', PROBE/'preflight.py',
                           HERE/'FakeUnity.cs', HERE/'Driver.cs', HERE/'controls.py', REPO/'tests/editor_runner.py']},
              'compile': compiled, 'planned_job_shape': plan, 'controls': len(results), 'failed': len(failed),
              'results': results}
    (out/'controls.json').write_text(json.dumps(report, indent=1, default=str)+'\n')
    for r in results:
        print(('PASS ' if r['passed'] else 'FAIL ')+r['control'] + ('' if r['passed'] else f"\n     expect {r['expect']!r}\n     actual {r['actual']!r}"))
    print(f'{len(results)-len(failed)} of {len(results)} controls pass; report {out/"controls.json"}')
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main())
