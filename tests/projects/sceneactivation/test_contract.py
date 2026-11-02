#!/usr/bin/env python3
"""Synthetic controls of the activation witness gates, canonical form and publication.

    test_contract.py OUT_DIR      (a new directory under $XDG_CACHE_HOME/unbound)

Builds complete synthetic repeat directories (Editor result, prelaunch receipt, post-run fixture
hashes, log, raw capture) from fixture.json and checks that run.capture_problem accepts the valid
ones in ANY callback/root order and refuses every incomplete, duplicated, malformed, wrong-input,
wrong-renderer, forced or rewritten one; that analysis reports match/none/multiple; and that
publication refuses differing orders/ranks, damaged repeats and an existing target. No Editor runs.
"""
import copy
import json
from pathlib import Path
import random
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE))
sys.path.insert(0, str(PROBE.parents[1]))
import contract  # noqa: E402
import run  # noqa: E402
from probe_paths import cache_artifact_path  # noqa: E402

FIXTURE = contract.load_fixture()
PREDS = contract.load_predictions()
NAMES = [o['n'] for o in FIXTURE['objects']]
CLASS = {o['n']: o['c'] for o in FIXTURE['objects']}


def capture(order=None, copy_order=None, roots=None, id_base=-2000, id_step=-6, edit_shift=0):
    """A complete raw capture. order/copy_order: wake order of the two phases (default document)."""
    order = order or PREDS['document (outer-first)']
    copy_order = copy_order or order
    roots = roots or FIXTURE['roots']
    rows = [{'k': 'roots', 'at': 'edit', 'scene': 'Wake', 'roots': list(roots)},
            {'k': 'tree', 'at': 'edit', 'scene': 'Wake', 'nodes': list(FIXTURE['tree'])}]

    def ids(phase, name):
        i = NAMES.index(name)+{'edit': edit_shift, 'Wake': 0, 'WakeCopy': 100}[phase]
        base = id_base+i*id_step
        return {'cid': base, 'gid': base-1, 'tid': base-2}
    for o in FIXTURE['objects']:
        rows.append({'k': 'bind', 'scene': 'Wake', 'obj': o['n'], 'type': o['c'], **ids('edit', o['n']),
                     **o['globalobjectid']})

    def phase(scene, seq, frame, mode):
        for name in seq:
            for ev in ('Awake', 'OnEnable'):
                rows.append({'k': 'cb', 'ev': ev, 'scene': scene, 'obj': name, 'type': CLASS[name],
                             'frame': frame, **ids(scene, name)})
        rows.append({'k': 'loaded', 'scene': scene, 'mode': mode, 'frame': frame})
        rows.append({'k': 'roots', 'at': 'loaded', 'scene': scene, 'frame': frame, 'roots': list(roots)})
        rows.append({'k': 'tree', 'at': 'loaded', 'scene': scene, 'frame': frame, 'nodes': list(FIXTURE['tree'])})
        for name in seq:
            rows.append({'k': 'cb', 'ev': 'Start', 'scene': scene, 'obj': name, 'type': CLASS[name],
                         'frame': frame+1, **ids(scene, name)})
        rows.append({'k': 'roots', 'at': 'settled', 'scene': scene, 'frame': frame+1, 'roots': list(roots)})
        rows.append({'k': 'tree', 'at': 'settled', 'scene': scene, 'frame': frame+1, 'nodes': list(FIXTURE['tree'])})
    phase('Wake', order, 0, '4')
    rows.append({'k': 'request', 'scene': 'WakeCopy', 'frame': 2})
    rows.append({'k': 'unloaded', 'scene': 'Wake', 'frame': 3})
    phase('WakeCopy', copy_order, 3, 'Single')
    rows.append(dict(contract.COMPLETE))
    return rows


def text(rows):
    return ''.join(json.dumps(r, separators=(',', ':'))+'\n' for r in rows)


def repeat_dir(path, rows=None, raw=None, result=None, log='Renderer: NVIDIA\nCleanup mono\n',
               inputs=None, after=None, release=run.RELEASE, runner_override=None, watchdog=False):
    path.mkdir(parents=True)
    (path/'raw.jsonl').write_text(raw if raw is not None else text(rows if rows is not None else capture()))
    (path/'editor-run.json').write_text(json.dumps(result or {
        'exit_code': 0, 'forced_exit': None, 'renderer': 'NVIDIA GeForce RTX 4060/PCIe/SSE2',
        'renderer_ok': True, 'shutdown_logged': True, 'require_gpu': True}))
    (path/'editor.log').write_text(log)
    files = {}
    back = {'Assets': 'assets', 'Packages': 'packages', 'ProjectSettings': 'settings'}
    for key, value in (inputs or FIXTURE['inputs']).items():
        part, _, rel = key.partition('/')
        files[back[part]+'/'+rel] = {'sha256': value}
    for name in run.RUNNER_SOURCES:
        files[name] = {'sha256': run.sha(PROBE/name)}
    if runner_override:
        files[runner_override] = {'sha256': '0'*64}
    (path/'prelaunch-inputs.json').write_text(json.dumps({'selected_release': release, 'files': files}))
    (path/'fixture-after.json').write_text(json.dumps(after or FIXTURE['inputs']))
    if watchdog:
        (path/'raw.jsonl.watchdog').write_text('Play mode did not finish\n')
    return path


def main():
    out = cache_artifact_path(Path(sys.argv[1]))
    if out.exists():
        sys.exit(f'refusing to overwrite {out}')
    out.mkdir(parents=True)
    results = []

    def expect(name, directory, ok):
        problem = run.capture_problem(directory)
        good = (problem is None) == ok
        results.append({'case': name, 'expect': 'pass' if ok else 'refuse', 'problem': problem, 'ok': good})
        print(('ok  ' if good else 'FAIL'), name, '->', problem or 'passes')

    n = [0]

    def d():
        n[0] += 1
        return out/'gate'/f'case{n[0]:02d}'

    valid = capture()
    expect('valid (document order)', repeat_dir(d(), valid), True)
    rng = random.Random(7)
    shuffled = NAMES[:]
    rng.shuffle(shuffled)
    expect('changed callback order (shuffled, both phases)', repeat_dir(d(), capture(shuffled, shuffled[::-1])), True)
    expect('changed root order (reversed)', repeat_dir(d(), capture(roots=FIXTURE['roots'][::-1])), True)
    expect('instance object GlobalObjectId object id differs (reported only)',
           repeat_dir(d(), [dict(r, go=r['go'].rsplit('-', 2)[0]+'-1-9000') if r.get('obj') == 'P1' and r['k'] == 'bind'
                            else r for r in valid]), True)

    def drop(pred):
        rows = copy.deepcopy(valid)
        i = next(i for i, r in enumerate(rows) if pred(r))
        del rows[i]
        return rows
    expect('incomplete: a Start missing', repeat_dir(d(), drop(lambda r: r['k'] == 'cb' and r['ev'] == 'Start')), False)
    expect('incomplete: a WakeCopy Awake missing',
           repeat_dir(d(), drop(lambda r: r['k'] == 'cb' and r['scene'] == 'WakeCopy' and r['ev'] == 'Awake')), False)
    expect('incomplete: a bind missing', repeat_dir(d(), drop(lambda r: r['k'] == 'bind')), False)
    expect('incomplete: settled roots missing', repeat_dir(d(), drop(lambda r: r['k'] == 'roots' and r['at'] == 'settled')), False)
    expect('incomplete: no completion marker', repeat_dir(d(), valid[:-1]), False)
    expect('truncated last line', repeat_dir(d(), raw=text(valid)[:-1]), False)
    expect('two completion markers', repeat_dir(d(), valid+[dict(contract.COMPLETE)]), False)
    expect('record after the completion marker', repeat_dir(d(), valid+[{'k': 'unloaded', 'scene': 'WakeCopy', 'frame': 9}]), False)
    expect('unloaded WakeCopy before close', repeat_dir(d(), valid[:-1]+[{'k': 'unloaded', 'scene': 'WakeCopy', 'frame': 9}, valid[-1]]), False)
    dup = copy.deepcopy(valid)
    k = next(i for i, r in enumerate(dup) if r['k'] == 'cb')
    dup.insert(k, dict(dup[k]))
    expect('duplicate Awake', repeat_dir(d(), dup), False)
    expect('duplicate root', repeat_dir(d(), capture(roots=FIXTURE['roots'][:-1]+[FIXTURE['roots'][0]])), False)
    expect('missing root', repeat_dir(d(), capture(roots=FIXTURE['roots'][:-1])), False)
    wrong_parent = [dict(r, nodes=[x.replace('B/B1', 'A/B1') for x in r['nodes']]) if r['k'] == 'tree' else r for r in valid]
    expect('wrong parent in the tree', repeat_dir(d(), wrong_parent), False)
    expect('unknown object callback', repeat_dir(d(), [dict(r, obj='Z') if r['k'] == 'cb' and r['obj'] == 'Q' and r['ev'] == 'Start' else r for r in valid]), False)
    expect('wrong marker class', repeat_dir(d(), [dict(r, type='W') if r.get('obj') == 'E' else r for r in valid]), False)
    expect('schema: extra key', repeat_dir(d(), [dict(r, extra=1) if r['k'] == 'request' else r for r in valid]), False)
    expect('schema: missing key', repeat_dir(d(), [{k: v for k, v in r.items() if k != 'frame'} if r['k'] == 'request' else r for r in valid]), False)
    expect('schema: string frame', repeat_dir(d(), [dict(r, frame='0') if r['k'] == 'loaded' else r for r in valid]), False)
    late = copy.deepcopy(valid)
    late.insert(len(late)-1, late.pop(2))
    expect('Edit record after Play began', repeat_dir(d(), late), False)
    expect('a marker changes its id', repeat_dir(d(), [dict(r, cid=r['cid']-1) if r['k'] == 'cb' and r['ev'] == 'Start' and r['obj'] == 'A' else r for r in valid]), False)
    expect('two markers share an id', repeat_dir(d(), [dict(r, gid=-1) if r['k'] == 'cb' and r['scene'] == 'Wake' and r['obj'] in ('A', 'B') else r for r in valid]), False)
    expect('plain GlobalObjectId not the fixture fileID',
           repeat_dir(d(), [dict(r, tr=r['tr'].replace('-5001-', '-5002-')) if r.get('obj') == 'A' and r['k'] == 'bind' else r for r in valid]), False)
    expect('instance GlobalObjectId names another PrefabInstance (reported only)',
           repeat_dir(d(), [dict(r, mb=r['mb'][:-4]+'6000') if r.get('obj') == 'P1/Xc' and r['k'] == 'bind' else r for r in valid]), True)
    expect('instance GlobalObjectId of another scene GUID',
           repeat_dir(d(), [dict(r, mb=r['mb'].replace(FIXTURE['scene_guid'], FIXTURE['copy_guid'])) if r.get('obj') == 'P1/Xc' and r['k'] == 'bind' else r for r in valid]), False)
    expect('plain GlobalObjectId of an asset (type 1)',
           repeat_dir(d(), [dict(r, go=r['go'].replace('V1-2-', 'V1-1-')) if r.get('obj') == 'Q' and r['k'] == 'bind' else r for r in valid]), False)
    good = {'exit_code': 0, 'forced_exit': None, 'renderer': 'NVIDIA GeForce RTX 4060/PCIe/SSE2', 'renderer_ok': True,
            'shutdown_logged': True, 'require_gpu': True}
    expect('wrong renderer (llvmpipe)', repeat_dir(d(), result=dict(good, renderer='llvmpipe (LLVM 17.0.6, 256 bits)', renderer_ok=False)), False)
    expect('forced shutdown-guard stop (strict exit)', repeat_dir(d(), result=dict(good, forced_exit='shutdown-guard')), False)
    expect('forced progress stop', repeat_dir(d(), result=dict(good, forced_exit='no-progress', exit_code=None)), False)
    expect('exit 4', repeat_dir(d(), result=dict(good, exit_code=4)), False)
    expect('shutdown not logged', repeat_dir(d(), result=dict(good, shutdown_logged=False)), False)
    expect('probe watchdog fired', repeat_dir(d(), watchdog=True), False)
    expect('compile error in editor.log', repeat_dir(d(), log='Assets/Scripts/W.cs(3,1): error CS1002: ; expected\n'), False)
    bad_inputs = dict(FIXTURE['inputs'], **{'Assets/Scenes/Wake.unity': '0'*64})
    expect('wrong input: launched scene bytes differ', repeat_dir(d(), inputs=bad_inputs), False)
    expect('wrong input: other release', repeat_dir(d(), release='6000.1.0f1'), False)
    expect('wrong input: other runner source', repeat_dir(d(), runner_override='contract.py'), False)
    rewritten = dict(FIXTURE['inputs'], **{'Assets/Scenes/Wake.unity': '1'*64, 'Assets/Scenes/WakeCopy.unity': '1'*64})
    expect('Editor rewrote both scenes', repeat_dir(d(), after=rewritten), False)
    expect('Editor added an Assets file', repeat_dir(d(), after=dict(FIXTURE['inputs'], **{'Assets/New.asset': '2'*64})), False)
    expect('ProjectSettings rewritten (reported, not refused)',
           repeat_dir(d(), after=dict(FIXTURE['inputs'], **{'ProjectSettings/PackageManagerSettings.asset': '3'*64})), True)

    # Analysis: match / none / multiple.
    an = contract.analyze(valid)
    results.append({'case': 'analysis: document order matches exactly one candidate',
                    'ok': an['phases']['Wake']['events']['Awake']['verdict'] == 'match'
                    and an['phases']['Wake']['events']['Awake']['matches'] == ['document (outer-first)']})
    an = contract.analyze(capture(shuffled))
    results.append({'case': 'analysis: a shuffled order matches none', 'ok': an['phases']['Wake']['events']['Awake']['verdict'] == 'none'})
    fake = dict(PREDS, duplicate=PREDS['document (outer-first)'])
    an = contract.analyze(valid, preds=fake)
    results.append({'case': 'analysis: two equal candidates give multiple', 'ok': an['phases']['Wake']['events']['Awake']['verdict'] == 'multiple'})
    an = contract.analyze(valid)
    results.append({'case': 'analysis: id order equals the synthetic wake order only where built so',
                    'ok': an['phases']['Wake']['instance_id_order']['cid']['awake_equals_ascending'] is False})
    for r in results[-4:]:
        print(('ok  ' if r['ok'] else 'FAIL'), r['case'])

    # Canonical form and publication.
    def pair(name, first, second, target_exists=False, ok=False):
        work = out/'publish'/name
        repeat_dir(work/'native1', first)
        repeat_dir(work/'native2', second)
        target = work/'Reference'/'unity-6000.0.71f1.jsonl'
        if target_exists:
            target.parent.mkdir(parents=True)
            target.write_text('existing\n')
        try:
            code = run.publish(work, target)
            outcome = 'published' if code == 0 and target.exists() and not target_exists else 'refused'
        except SystemExit as e:
            outcome = 'refused: '+str(e)
        published = outcome == 'published'
        same = published and target.read_text() == (work/'native1'/'canonical.jsonl').read_text()
        good = published == ok and (same or not ok)
        results.append({'case': 'publish: '+name, 'expect': 'publish' if ok else 'refuse', 'outcome': outcome, 'ok': good})
        print(('ok  ' if good else 'FAIL'), 'publish:', name, '->', outcome)
    pair('identical orders, other absolute ids (same ranks)', capture(), capture(id_base=-90000, id_step=-9), ok=True)
    pair('changed callback order', capture(), capture(shuffled))
    pair('changed root order', capture(), capture(roots=FIXTURE['roots'][::-1]))
    pair('changed id ranks', capture(), capture(id_step=6))
    pair('changed frame', capture(), [dict(r, frame=5) if r['k'] == 'request' else r for r in capture()])
    pair('one damaged repeat', capture(), capture()[:-1])
    pair('existing target', capture(), capture(), target_exists=True)

    failed = [r for r in results if not r['ok']]
    (out/'guards.json').write_text(json.dumps({'cases': results, 'failed': len(failed)}, indent=1)+'\n')
    print(f'{len(results)-len(failed)}/{len(results)} ok', 'GUARDS OK' if not failed else 'GUARDS FAILED')
    return int(bool(failed))


if __name__ == '__main__':
    sys.exit(main())
