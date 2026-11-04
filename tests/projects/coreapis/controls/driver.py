#!/usr/bin/env python3
"""Mock controls of the core API probe's Native driver (run.py editor/check/publish).

    controls/driver.py --out NEW_DIR --preflight PREFLIGHT.json [--release R]

No Editor, display, lease or engine is started. editor_runner.run is replaced by a fake Editor
that writes what a supervised run leaves behind (editor-spec.json, the real prelaunch-inputs.json
receipt, editor-run.json, a fake editor.log, the record, the compile graph) into the case's own
label under NEW_DIR, with one defect per case. Each case runs the real run.py steps on it and
compares every step's outcome (passes, refused, stop) with the expected one. Every case, its files
and its step outcomes are kept in NEW_DIR/results.json, failures included. These fake runs show
what the driver accepts and refuses; they are never Native evidence.

The fake Editor also checks the shape of every job the driver asks for: a windowed GPU Editor
(vglrun -d egl, no -batchmode/-nographics/-quit/-executeMethod), 1200 s hard and 300 s
record-progress deadlines on the repeat's values.jsonl, CORE_APIS_OUT naming that file, and the
launch inputs (project folders, profile, runner, guards, preflight, editor_runner, probe_paths).
Publishing targets a copy of the reference inside NEW_DIR; the real reference's hash is
checked before and after.
"""
import argparse
import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time

CONTROLS = Path(__file__).resolve().parent
PROBE = CONTROLS.parent
sys.path.insert(0, str(PROBE))
import run as runner  # noqa: E402
import guards  # noqa: E402
editor_runner = runner.editor_runner

RENDERER = 'NVIDIA GeForce RTX 4060/PCIe/SSE2'
LOG = '''Unity Editor version:    {release} (fake)
Batch mode:              NO
[fake Editor log written by the driver controls; no Unity Editor ran]
kGfxThreadingModeSplitJobs is not supported on OpenGL Core. Reverting to kGfxThreadingModeDirect instead.
Renderer: {renderer}
Vendor:   NVIDIA Corporation
Version:  4.5.0 NVIDIA 580.178.04
GLES:     0
Cleanup mono
'''
JOBS = []


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def lines(data):
    return data.decode().splitlines()


def join(rows):
    return ('\n'.join(rows)+'\n').encode()


def edit_rows(data, function):
    return join(function(lines(data)))


def job_problems(job):
    problems = []
    command = job.command()
    if command[:3] != ['vglrun', '-d', 'egl'] or not job.graphics or job.batch or job.kind != 'editor-gpu':
        problems.append('not a windowed GPU Editor')
    for flag in ('-batchmode', '-nographics', '-quit', '-executeMethod'):
        if flag in command:
            problems.append(f'command has {flag}')
    values = str((job.out/'values.jsonl').resolve())
    if job.timeout != 1200 or job.progress_timeout != 300 or job.progress_files != [values]:
        problems.append(f'deadlines {job.timeout}/{job.progress_timeout} on {job.progress_files}')
    if job.env.get('CORE_APIS_OUT') != values:
        problems.append('CORE_APIS_OUT does not name the progress file')
    want = {'assets', 'packages', 'settings', 'profile', 'run', 'guards', 'compare', 'preflight', 'editor_runner', 'probe_paths'}
    if set(job.launch_inputs['paths']) != want:
        problems.append(f"launch inputs {sorted(job.launch_inputs['paths'])}")
    return problems


def fake_editor(behaviour, preflight):
    """A stand-in for editor_runner.run: writes a supervised run's files with `behaviour`'s defect."""
    def run(job):
        JOBS.append({'label': job.label, 'problems': job_problems(job)})
        job.out.mkdir(parents=True, exist_ok=True)
        if (job.out/editor_runner.RESULT).exists():
            raise SystemExit('refusing to overwrite an Editor result')
        admitted = time.time()
        owner = editor_runner.read_proc(os.getpid())
        spec = {'label': job.label, 'kind': job.kind, 'command': job.command(), 'project': str(job.project),
                'log': str(job.log), 'out': str(job.out), 'cwd': str(job.cwd), 'timeout': job.timeout,
                'shutdown_grace': job.shutdown_grace, 'kill_grace': job.kill_grace, 'follow': job.follow,
                'require_gpu': job.graphics, 'private_display': job.graphics, 'screen': job.screen,
                'owner_pid': owner.pid, 'owner_start_ticks': owner.start_ticks,
                'progress_files': job.progress_files, 'progress_timeout': job.progress_timeout,
                'launch_inputs': job.launch_inputs}
        editor_runner.write_json(job.out/'editor-spec.json', spec)
        env = dict(os.environ, PWD=str(job.project), DISPLAY=':fake', **job.env)
        editor_runner.write_launch_inputs(dict(spec, display=':fake'), env)
        # The fake Editor: its compile graph, the record, its log, the settings file it creates.
        dag = Path(job.project)/'Library/Bee/artifacts/2400b0aE.dag'
        dag.mkdir(parents=True)
        for assembly, result in preflight['results'].items():
            (dag/f'{assembly}.rsp').write_text(behaviour.get('rsp', lambda a, t: t)(assembly, result['rsp']['compiled_text']))
            (dag/f'{assembly}.rsp2').write_text(result['rsp2']['compiled_text'])
            (dag/f'{assembly}.UnityAdditionalFile.txt').write_text(behaviour.get('additional', str(job.project)))
        if behaviour.get('no_graph'):
            shutil.rmtree(Path(job.project)/'Library')
        values = Path(job.env['CORE_APIS_OUT'])
        values.write_bytes(behaviour.get('values', REFERENCE_BYTES))
        if behaviour.get('watchdog'):
            Path(str(values)+'.watchdog').write_text('Play mode did not finish\n')
        log = LOG.format(release=os.environ['UNBOUND_PROBE_UNITY_VERSION'], renderer=behaviour.get('renderer', RENDERER))
        for old, new in behaviour.get('log', []):
            log = log.replace(old, new)
        job.log.write_text(log)
        (Path(job.project)/'ProjectSettings/PackageManagerSettings.asset').write_text('fake Editor-written settings\n')
        if not behaviour.get('keep_version'):    # the Editor writes the release's full version file back
            shutil.copyfile(PROBE/'ProjectSettings/ProjectVersion.txt', Path(job.project)/'ProjectSettings/ProjectVersion.txt')
        if behaviour.get('mutate'):
            behaviour['mutate'](Path(job.project))
        exit_s = behaviour.get('exit_s', 60.0)
        if behaviour.get('late_values'):
            os.utime(values, (admitted+exit_s+100, admitted+exit_s+100))
        else:
            os.utime(values, (admitted+1, admitted+1))
        renderer = behaviour.get('renderer', RENDERER)
        result = {'format': 1, 'label': job.label, 'kind': job.kind,
                  'command': job.command()+behaviour.get('flags', []), 'project': str(job.project),
                  'log': str(job.log), 'display': ':fake', 'require_gpu': True,
                  'exit_code': behaviour.get('exit_code', 0), 'signal': behaviour.get('signal'),
                  'forced_exit': behaviour.get('forced'), 'forced_detail': behaviour.get('forced') and 'fake',
                  'renderer': renderer, 'renderer_ok': None if renderer is None else 'NVIDIA' in renderer,
                  'shutdown_logged': behaviour.get('shutdown_logged', True), 'hub_launched': False,
                  'followed': behaviour.get('followed', []), 'stopped': [], 'leftovers': [], 'swept': [],
                  'times': {'admitted': round(admitted, 3), 'exit_s': exit_s, 'finished_s': exit_s+1},
                  'launcher': {'command': ['fake'], 'exit': behaviour.get('launcher_exit', 0), 'scope': 'fake'},
                  'fake': 'driver control; no Unity Editor ran'}
        editor_runner.write_json(job.out/editor_runner.RESULT, result)
        return result
    return run


def step(kind, work, release, preflight, repeat=None, target=None, dry_run=False):
    """(outcome, code, output) of one real run.py step."""
    buffer = io.StringIO()
    try:
        with contextlib.redirect_stdout(buffer):
            if kind == 'editor':
                code = runner.editor(work, repeat, release, preflight, dry_run)
            elif kind == 'check':
                code = runner.check(work, repeat, release)
            else:
                code = runner.publish(work, release, dry_run, target)
    except SystemExit as stop:
        return 'refused', stop.code if isinstance(stop.code, int) else 1, buffer.getvalue()+str(stop.code)
    outcome = {0: 'passes', runner.EXIT_STOP: 'stop'}.get(code, 'fails')
    return outcome, code, buffer.getvalue()


def change_window(rows, w, field, value):
    out, done = [], False
    for line in rows:
        row = json.loads(line)
        if not done and row.get('w') == w and row.get('who') == 'RtCam':
            row[field] = value
            line, done = guards.written(row), True
        out.append(line)
    return out


def replace_first(rows, old, new):
    i = next(i for i, line in enumerate(rows) if old in line)
    return rows[:i]+[rows[i].replace(old, new)]+rows[i+1:]


def insert_event(rows, e, who='probe', s='lazy', f=0):
    row = {'f': f, 's': s, 'who': who, 'e': e}
    return rows[:5]+[guards.written(row)]+rows[5:]


def cases(release):
    full = [('editor', 'native1', 'passes'), ('check', 'native1', 'passes'), ('editor', 'native2', 'passes'),
            ('check', 'native2', 'passes')]
    one = [('editor', 'native1', 'fails'), ('check', 'native1', 'fails'), ('editor', 'native2', 'refused')]

    def bad(name, behaviour):
        return {'name': name, 'native1': behaviour, 'steps': one}
    values = REFERENCE_BYTES
    pose_changed = edit_rows(values, lambda r: replace_first(r, '(3fffffff,40800000,40c00000)', '(3fffffff,40800000,40c00001)'))
    result = [
        {'name': 'valid-pair-equals-reference', 'steps': full+[('publish', None, 'passes'), ('publish-again', None, 'refused')]},
        {'name': 'native2-before-admission', 'steps': [('editor', 'native1', 'passes'), ('editor', 'native2', 'refused')]},
        {'name': 'label-reuse', 'steps': [('editor', 'native1', 'passes'), ('editor', 'native1', 'refused')]},
        {'name': 'no-preflight', 'preflight': 'missing', 'steps': [('editor', 'native1', 'refused')]},
        {'name': 'failed-preflight', 'preflight': 'failed', 'steps': [('editor', 'native1', 'refused')]},
        {'name': 'preflight-other-sources', 'preflight': 'other-sources', 'steps': [('editor', 'native1', 'refused')]},
        {'name': 'dirty-checkout', 'dirty': True, 'steps': [('editor', 'native1', 'refused')]},
        {'name': 'native2-other-sources', 'native2_manifest_change': True,
         'steps': [('editor', 'native1', 'passes'), ('check', 'native1', 'passes'), ('editor', 'native2', 'refused')]},
        bad('exit-nonzero', {'exit_code': 1}),
        bad('killed-by-signal', {'exit_code': 137, 'signal': 'SIGKILL'}),
        bad('renderer-llvmpipe', {'renderer': 'llvmpipe (LLVM 19.1.1, 256 bits)'}),
        bad('renderer-missing', {'renderer': None, 'log': [('Renderer: '+RENDERER+'\n', '')]}),
        bad('not-desktop-gl', {'log': [('GLES:     0', 'GLES:     1')]}),
        bad('not-glcore', {'log': [('kGfxThreadingModeSplitJobs is not supported on OpenGL Core. Reverting to kGfxThreadingModeDirect instead.\n', '')]}),
        bad('batch-editor-log', {'log': [('Batch mode:              NO', 'Batch mode:              YES')]}),
        bad('forced-shutdown-guard-after-finish', {'forced': 'shutdown-guard'}),
        bad('forced-watchdog', {'forced': 'watchdog', 'exit_code': 137, 'signal': 'SIGKILL'}),
        bad('forced-no-progress', {'forced': 'no-progress', 'exit_code': 143, 'signal': 'SIGTERM'}),
        bad('forced-cancelled', {'forced': 'cancelled', 'exit_code': 143, 'signal': 'SIGTERM'}),
        bad('forced-owner-exit', {'forced': 'owner-exit', 'exit_code': 143, 'signal': 'SIGTERM'}),
        bad('no-shutdown-logged', {'shutdown_logged': False, 'log': [('Cleanup mono\n', '')]}),
        bad('batch-flags', {'flags': ['-batchmode', '-nographics', '-quit']}),
        bad('probe-watchdog-file', {'watchdog': True}),
        bad('marker-missing', {'values': edit_rows(values, lambda r: r[:-1])}),
        bad('marker-twice', {'values': edit_rows(values, lambda r: r+[r[-1]])}),
        bad('marker-not-last', {'values': edit_rows(values, lambda r: r[:-2]+[r[-1], r[-2]])}),
        bad('record-not-closed', {'values': values[:-1]}),
        bad('scenario-missing', {'values': edit_rows(values, lambda r: [x for x in r if '"s":"instantiate.errors"' not in x])}),
        bad('window-frame-missing', {'values': edit_rows(values, lambda r: [x for x in r if not x.startswith('{"w":5,')])}),
        bad('gui-and-eof-missing', {'values': edit_rows(values, lambda r: [x for x in r if 'OnGUI' not in x and 'WaitForEndOfFrame' not in x])}),
        bad('scene-view-camera', {'values': edit_rows(values, lambda r: replace_first(r, 'cam=ScreenCam"', 'cam=SceneCamera"'))}),
        bad('key-order', {'values': edit_rows(values, lambda r: [r[0].replace('{"f":-1,"s":"-",', '{"s":"-","f":-1,')]+r[1:])}),
        bad('wrong-type', {'values': edit_rows(values, lambda r: [r[0].replace('{"f":-1,', '{"f":"-1",')]+r[1:])}),
        bad('step-threw', {'values': edit_rows(values, lambda r: insert_event(r, 'step threw System.NullReferenceException: x'))}),
        bad('log-exception', {'values': edit_rows(values, lambda r: insert_event(r, 'Exception: boom', who='log'))}),
        {'name': 'log-warning-is-a-note', 'native1': {'values': edit_rows(values, lambda r: insert_event(r, 'Warning: note', who='log'))},
         'steps': [('editor', 'native1', 'passes'), ('check', 'native1', 'passes')]},
        bad('absolute-path', {'values': edit_rows(values, lambda r: insert_event(r, 'path /home/user/project/Library/x'))}),
        bad('device-name', {'values': edit_rows(values, lambda r: insert_event(r, 'gpu NVIDIA GeForce'))}),
        bad('raw-instance-id', {'values': edit_rows(values, lambda r: replace_first(r, 'id=#LazyMat ', 'id=12345 '))}),
        bad('raw-json-instance-id', {'values': edit_rows(values, lambda r: replace_first(r, '\\"instanceID\\":\\"#LazyMat\\"', '\\"instanceID\\":12345'))}),
        bad('authored-change', {'mutate': lambda p: (p/'Assets/Scripts/Msg.cs.meta').write_text('changed\n')}),
        bad('authored-add', {'mutate': lambda p: (p/'Assets/New.asset').write_text('new\n')}),
        {'name': 'version-file-left-as-prepared', 'native1': {'keep_version': True},
         'steps': [('editor', 'native1', 'passes'), ('check', 'native1', 'passes')]},
        bad('version-file-rewritten-otherwise', {'mutate': lambda p: (p/'ProjectSettings/ProjectVersion.txt').write_text(
            'm_EditorVersion: 6000.0.99f1\n')}),
        bad('graph-other-define', {'rsp': lambda a, t: t.replace('-define:UNITY_64\n', '-define:UNITY_64\n-define:EXTRA\n')}),
        bad('graph-additional-file', {'additional': '/elsewhere'}),
        bad('graph-missing', {'no_graph': True}),
        bad('record-after-exit', {'late_values': True}),
        bad('over-hard-deadline', {'exit_s': 1300.0}),
        bad('handed-to-another-editor', {'followed': [{'pid': 1, 'cmd': 'Unity'}]}),
        bad('launcher-failed', {'launcher_exit': 1}),
        {'name': 'admission-tampered', 'tamper_native1_after_check': True,
         'steps': [('editor', 'native1', 'passes'), ('check', 'native1', 'passes'), ('editor', 'native2', 'refused'),
                   ('publish', None, 'refused')]},
        {'name': 'pair-disagreement', 'native2': {'values': edit_rows(values, lambda r: change_window(r, 3, 'rendered', 99))},
         'steps': full+[('publish', None, 'refused')]},
        {'name': 'pair-differs-from-existing-reference', 'native1': {'values': pose_changed}, 'native2': {'values': pose_changed},
         'steps': full+[('publish', None, 'stop'), ('publish-again', None, 'refused')]},
        {'name': 'new-release-reference-written-once', 'target': 'absent',
         'steps': full+[('publish', None, 'passes'), ('publish-again', None, 'passes')]},
        {'name': 'dry-runs-write-nothing', 'steps': [('editor-dry', 'native1', 'passes'), ('publish-dry', None, 'passes')]},
    ]
    return result


def run_case(case, root, release, preflight_path, preflight):
    work = root/'cases'/case['name']/'label'
    target = root/'cases'/case['name']/'reference.jsonl'
    target.parent.mkdir(parents=True)
    if case.get('target') != 'absent':
        target.write_bytes(REFERENCE_BYTES)
    path = preflight_path
    if case.get('preflight') == 'missing':
        path = root/'cases'/case['name']/'no-preflight.json'
    elif case.get('preflight') in ('failed', 'other-sources'):
        record = json.loads(preflight_path.read_text())
        if case['preflight'] == 'failed':
            record['passed'] = False
        else:
            record['sources']['Assets/Scripts/CoreProbe.cs'] = '0'*64
        path = root/'cases'/case['name']/'preflight.json'
        path.write_text(json.dumps(record))
    real_git, real_manifest = runner.git_state, runner.source_manifest
    if case.get('dirty'):
        runner.git_state = lambda: dict(real_git(), clean=False, status=['?? fake-untracked-file'])
    steps = []
    try:
        for kind, repeat, expected in case['steps']:
            behaviour = case.get(repeat, {}) if repeat else {}
            editor_runner.run = fake_editor(behaviour, preflight)
            if kind == 'editor' and repeat == 'native2' and case.get('native2_manifest_change'):
                runner.source_manifest = lambda *a: dict(real_manifest(*a), tools={'run.py': '0'*64})
            before = target.read_bytes() if target.exists() else None
            if kind == 'editor-dry':
                got = step('editor', work, release, path, repeat, dry_run=True)
                written = work.exists()
            elif kind == 'publish-dry':
                got = step('publish', work, release, path, target=target, dry_run=True)
                written = (work/'publish-comparison.json').exists()
            else:
                got = step(kind.replace('-again', ''), work, release, path, repeat, target)
                written = None
            runner.source_manifest = real_manifest
            after = target.read_bytes() if target.exists() else None
            entry = {'step': kind, 'repeat': repeat, 'expected': expected, 'outcome': got[0], 'code': got[1],
                     'output': got[2][-4000:], 'target_changed': before != after}
            ok = got[0] == expected
            if kind in ('editor-dry', 'publish-dry'):
                ok = ok and not written
            if kind == 'publish' and case.get('target') != 'absent':
                ok = ok and before == after == REFERENCE_BYTES
            if kind == 'publish' and case.get('target') == 'absent' and expected == 'passes':
                ok = ok and after == (work/'native1/values.jsonl').read_bytes()
            if kind == 'publish-again':    # never a rewrite: the target keeps its bytes
                ok = ok and before == after
            if kind == 'publish' and expected == 'stop':
                path = work/'publish-comparison.json'
                report = json.loads(path.read_text()) if path.exists() else {'identical': None, 'summary': {}}
                entry['report_summary'] = report['summary']
                ok = ok and report['identical'] is False and report['summary'].get('missing') == report['summary'].get('extra') == 1
            entry['pass'] = ok
            steps.append(entry)
            if kind == 'check' and repeat == 'native1' and case.get('tamper_native1_after_check'):
                with (work/'native1/editor.log').open('a') as log:
                    log.write('tampered after admission\n')
    finally:
        runner.git_state, runner.source_manifest = real_git, real_manifest
    return {'name': case['name'], 'steps': steps, 'pass': all(s['pass'] for s in steps)}


def main():
    global REFERENCE_BYTES
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--preflight', type=Path, required=True)
    parser.add_argument('--release', default='6000.0.71f1')
    args = parser.parse_args()
    if args.out.exists():
        sys.exit(f'refusing to reuse {args.out}')
    os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
    reference = runner.reference_file(PROBE, 'jsonl')
    REFERENCE_BYTES = reference.read_bytes()
    reference_before = sha(reference)
    preflight_path = args.preflight.resolve(strict=True)
    preflight = json.loads(preflight_path.read_text())
    args.out.mkdir(parents=True)
    real_run = editor_runner.run
    results = []
    try:
        for case in cases(args.release):
            results.append(run_case(case, args.out, args.release, preflight_path, preflight))
    finally:
        editor_runner.run = real_run
    batch = subprocess.run([sys.executable, '-I', str(PROBE/'run.py'), 'batch', '--label', 'never-made',
                            '--release', args.release], capture_output=True, text=True)
    results.append({'name': 'batch-mode-refused', 'pass': batch.returncode == 2 and 'no batch capture' in batch.stderr,
                    'steps': [{'step': 'run.py batch', 'code': batch.returncode, 'output': batch.stderr[-1000:]}]})
    job_failures = [j for j in JOBS if j['problems']]
    results.append({'name': 'every-launch-is-a-strict-windowed-gpu-job', 'pass': bool(JOBS) and not job_failures,
                    'steps': [{'jobs': len(JOBS), 'failures': job_failures}]})
    reference_after = sha(reference)
    results.append({'name': 'real-reference-untouched', 'pass': reference_before == reference_after,
                    'steps': [{'before': reference_before, 'after': reference_after}]})
    record = {'release': args.release, 'preflight': str(preflight_path), 'preflight_sha256': sha(preflight_path),
              'reference_sha256': reference_before, 'cases': results, 'passed': all(r['pass'] for r in results),
              'fake': 'Fake Editor runs; no Unity Editor, display, lease or engine was started.'}
    (args.out/'results.json').write_text(json.dumps(record, indent=1)+'\n')
    for r in results:
        print(('pass ' if r['pass'] else 'FAIL ')+r['name']+('' if r['pass'] else ': '+json.dumps(
            [s for s in r['steps'] if not s.get('pass', True)])[:600]))
    print(f"{sum(r['pass'] for r in results)}/{len(results)} cases pass")
    return 0 if record['passed'] else 1


REFERENCE_BYTES = b''

if __name__ == '__main__':
    sys.exit(main())
