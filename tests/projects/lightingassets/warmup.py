"""Cold shader warm-up captures and actual GPU preparation checks."""
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import editor_runner

MODES = ('collection', 'progressive', 'startup', 'fallback', 'all')


def project(run, target, mode):
    run.copy_project(target)
    settings = target/'ProjectSettings/GraphicsSettings.asset'
    if mode == 'state': return target
    settings.write_text(re.sub(r'  m_PreloadedShaders:\n(?:  -[^\n]*\n)*',
                              '  m_PreloadedShaders: []\n', settings.read_text()))
    return target


def record(run, label, mode):
    out = run.work(label)
    p = project(run, out/'project', mode)
    job = editor_runner.EditorJob(p, out/'editor.log', out, ['-executeMethod', 'LightingAssetsRunner.Record'],
        batch=True, graphics=True, env={'LIGHTING_ASSETS_OUT': str(out), 'SHADER_WARMUP_MODE': mode},
        timeout=900, label='lightingassets-'+label)
    print(editor_runner.summary(editor_runner.run(job)))
    return int(bool(editor_runner.result_problem(out, gpu=True) or editor_runner.jsonl_complete(run.OUT, run.END)(out)))


def publish(run, labels, mode):
    def write(path, first):
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(first/run.OUT, path)
    if mode == 'state':
        return publish_state(run, labels)
    target = run.reference_root(run.PROBE)/f'warmup-{mode}.jsonl'
    appending = mode == 'all' and target.exists()
    candidate = run.work('publish-'+labels[0])/run.OUT if appending else target
    code = editor_runner.publish([run.work(label) for label in labels], candidate,
        lambda d: [run.OUT], write, complete=[editor_runner.jsonl_complete(run.OUT, run.END)], gpu=True)
    if code or not appending: return code
    old, new = target.read_text().splitlines(), candidate.read_text().splitlines()
    if json.loads(old[-1]) != run.END or new[:len(old)-1] != old[:-1]:
        raise SystemExit('global warmup capture changed an existing reference row')
    extra = [json.loads(line) for line in new[len(old)-1:-1]]
    if {row.get('step') for row in extra} != {
            'draw unlisted instancing combination', 'draw lower LOD', 'draw global fallback 0', 'draw global fallback 1', 'all repeat call', 'global repeat'} or len(extra) != 6:
        raise SystemExit('global warmup append must contain exactly the six new controls')
    target.write_text('\n'.join(new)+'\n')
    print('appended six global warmup controls; every old row preserved')
    return 0


def publish_state(run, labels):
    candidate = run.work('publish-'+labels[0])/run.OUT
    def write(path, first):
        path.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(first/run.OUT, path)
    code = editor_runner.publish([run.work(label) for label in labels], candidate, lambda d: [run.OUT], write,
        complete=[editor_runner.jsonl_complete(run.OUT, run.END)], gpu=True)
    if code: return code
    target = run.reference_root(run.PROBE)/run.OUT
    old = target.read_text().splitlines()
    new = candidate.read_text().splitlines()
    if any(json.loads(line)['case'] == 'shader-warmup' for line in old):
        raise SystemExit('warmup state reference is already published')
    if any(json.loads(line)['case'] not in ('shader-warmup', 'end') for line in new):
        raise SystemExit('state capture contains another section')
    if json.loads(old[-1]) != run.END: raise SystemExit('old reference is incomplete')
    target.write_text('\n'.join(old[:-1] + new[:-1] + old[-1:])+'\n')
    print('appended state records; every original value and image reference preserved')
    return 0


def compare(reference, candidate, log, mode):
    expected = [json.loads(line) for line in Path(reference).read_text().splitlines() if line.strip()]
    actual = [json.loads(line) for line in Path(candidate).read_text().splitlines() if line.strip()]
    failures = []
    for i in range(max(len(expected), len(actual))):
        a = expected[i] if i < len(expected) else None
        b = actual[i] if i < len(actual) else None
        if a != b:
            print('warmup difference', json.dumps({'row': i, 'expected': a, 'actual': b}, sort_keys=True))
            failures.append((a, b))
    if failures:
        return 1
    proofs = re.findall(r'WARMUP_GPU_PROOF ' + mode + r' (\d+) (\d+) (\d+) (\d+) (\d+) (\d+)', log)
    valid = len(proofs) == (2 if mode == 'fallback' else 8 if mode == 'all' else 6)
    if mode == 'all':
        repeated = re.findall(r'WARMUP_GLOBAL_REPEAT (\d+) (\d+) (\d+) (\d+)', log)
        valid &= len(repeated) == 1
        for first_programs, first_pipelines, last_programs, last_pipelines in repeated:
            valid &= (first_programs == last_programs and first_pipelines == last_pipelines)
    for proof in proofs:
        before_programs, before_pipelines, warm_programs, warm_pipelines, after_programs, after_pipelines = map(int, proof)
        if mode == 'startup':
            valid &= (warm_programs > 0 and warm_pipelines > 0
                      and warm_programs == before_programs and warm_pipelines == before_pipelines)
        else:
            valid &= warm_programs > before_programs and warm_pipelines > before_pipelines
        valid &= warm_programs == after_programs and warm_pipelines == after_pipelines
    if mode == 'all':
        inherited = re.findall(r'WARMUP_GLOBAL_FALLBACK (\d+) (\d+) (\d+) (\d+) (\d+) (\d+)', log)
        valid &= len(inherited) == 2
        for proof in inherited:
            first_p, first_l, warm_p, warm_l, last_p, last_l = map(int, proof)
            valid &= warm_p > first_p and warm_l > first_l and (warm_p, warm_l) == (last_p, last_l)
    if mode == 'fallback':
        own = re.findall(r'WARMUP_FALLBACK_OWN (\d+) (\d+) (\d+) (\d+)', log)
        valid &= len(own) == 1
        if len(own) == 1:
            programs, pipelines, before_base, after_base = map(int, own[0])
            valid &= programs > 0 and pipelines > 0 and before_base == 0 and after_base == 0
    if mode == 'progressive':
        partials = re.findall(r'WARMUP_PARTIAL_GPU_PROOF (\d+) (\d+) (\d+) (\d+)', log)
        valid &= len(partials) == 1
        if len(partials) == 1 and proofs:
            before_programs, before_pipelines, partial_programs, partial_pipelines = map(int, partials[0])
            full = list(map(int, proofs[0]))
            valid &= (before_programs == full[0] and before_pipelines == full[1]
                      and before_programs < partial_programs < full[2]
                      and before_pipelines < partial_pipelines < full[3])
    print('warmup GPU preparation:', 'PASS' if valid else 'FAIL', mode, proofs)
    return 0 if valid else 1


def gate(run, label, mode):
    out = run.work(label)
    p = project(run, out/'project', mode)
    cli = os.environ.get('UNBOUND_CLI') or os.environ.get('UNBOUND') or str(run.REPO/'target/release/unbound')
    env = {**os.environ, 'LIGHTING_ASSETS_OUT': str(out/'engine'), 'SHADER_WARMUP_MODE': mode}
    command = [str(run.HEAVY), '--kind', 'engine-gpu', 'timeout', '300', cli, 'run', '-p', str(p),
               '--scene', 'Assets/Scenes/WarmupOnly.unity', '--no-watch', '--frames', '60', '--size', '32x32']
    with (out/'engine.log').open('w') as log:
        code = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT).returncode
    candidate = out/'engine'/run.OUT
    if code or not candidate.exists():
        print('warmup engine failure', code, out/'engine.log')
        return 1
    return compare(run.reference_root(run.PROBE)/f'warmup-{mode}.jsonl', candidate,
                   (out/'engine.log').read_text(), mode)
