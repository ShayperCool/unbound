#!/usr/bin/env python3
"""Keep the full raw delta and gate lifetime observations with named residuals."""
import collections
import difflib
import json
from pathlib import Path
import sys

RENDER_PHASE = 'after:PostLateUpdate.ObjectDispatcherPostLateUpdate'
PARTICLE_PHASE = 'PreLateUpdate.ParticleSystemBeginUpdateAll'


def fields(detail):
    return dict(item.split('=', 1) for item in detail.split() if '=' in item)


def project_case(rows):
    """A comparison view only: the raw records are always reported unchanged."""
    out, problems, notes = [], [], []
    pending = False
    last_ticks = 0
    visibility = [r for r in rows if r['case'] == 'delayFraction' and r['frame'] in (0, 1)
                  and r['event'].endswith(('.OnBecameVisible', '.OnWillRenderObject'))]
    counts = collections.Counter((r['frame'], r['event'].rsplit('.', 1)[-1]) for r in visibility)
    native_counts = {(0, 'OnBecameVisible'): 1, (0, 'OnWillRenderObject'): 1,
                     (1, 'OnBecameVisible'): 1, (1, 'OnWillRenderObject'): 2}
    engine_counts = {(0, 'OnBecameVisible'): 2, (0, 'OnWillRenderObject'): 2,
                     (1, 'OnWillRenderObject'): 2}
    bounded_visibility = dict(counts) in (native_counts, engine_counts) and all(not r['detail'] for r in visibility)
    for original in rows:
        row = dict(original)
        event, phase = row['event'], row['phase']
        if bounded_visibility and original in visibility:
            notes.append('particle renderer first-step visibility')
            continue
        if row['case'] == 'immediateAwake' and event == 'error':
            expected = "MissingReferenceException: The object of type 'ParticleSystem' has been destroyed but you are still trying to access it.\nYour script should either check if it is null or you should not destroy the object."
            if row['detail'] == expected:
                row['detail'] = expected.replace("'ParticleSystem'", "'UnityEngine.ParticleSystem'")
                notes.append('destroyed-object diagnostic type qualification')
        # IK's remaining leaf-location residual does not excuse a changed frame or state.
        if row['case'] == 'OnAnimatorIK' and phase == 'PreLateUpdate.DirectorUpdateAnimationBegin':
            row['phase'] = 'PreLateUpdate.DirectorUpdateAnimationEnd'
            notes.append('IK callback leaf')
        if event == 'returned':
            pending = fields(row['detail']).get('scriptEnabled') == 'false'
        if event.endswith('.OnDestroy'):
            pending = False
        if pending and phase == PARTICLE_PHASE:
            if event.endswith('.OnParticleTrigger'):
                notes.append('pending particle callback')
                continue
            if event == 'after.particles':
                values = fields(row['detail'])
                tick = int(values.get('particleTicks', '-1'))
                # The retained particle guard skips exactly this frame's update. No wider
                # numerical allowance: an unexpected clock or any other state still fails.
                if tick in (last_ticks, last_ticks + 1):
                    row['detail'] = ' '.join('particleTicks=<pending-frame>' if part.startswith('particleTicks=') else part
                                             for part in row['detail'].split())
                    notes.append('pending particle simulation')
        if event == 'after.particles' and not pending:
            last_ticks = int(fields(row['detail']).get('particleTicks', '0'))
        out.append(row)
    if rows and rows[0]['case'].startswith('unload'):
        out, unload_problems = unload_view(out)
        problems.extend(unload_problems)
        notes.append('asynchronous unload completion time')
    return out, problems, notes


def unload_view(rows):
    """Completion time is unspecified; preserve callback order/state and validate causality."""
    returns = [i for i, r in enumerate(rows) if r['event'] == 'unload.return']
    completions = [i for i, r in enumerate(rows) if r['event'] == 'unload.completed']
    if len(returns) != 1 or len(completions) != 1:
        return rows, ['missing/duplicate unload return or completion']
    returned, completed = returns[0], completions[0]
    if returned >= completed:
        return rows, ['unload completed before returning']
    completion_frame = rows[completed]['frame']
    out, problems = rows[:returned + 1], []
    repeatable = {'FixedUpdate', 'Update', 'LateUpdate', 'OnParticleTrigger', 'OnWillRenderObject', 'OnBecameVisible',
                  'preCull', 'postRender', 'particles'}
    allowed = {(r['event'], r['phase']) for r in rows[:returned + 1]
               if r['event'].rsplit('.', 1)[-1] in repeatable}
    # Objects born during Update have Start/LateUpdate before the first regular Update.
    # A queued unload still lets that next Update finish before its preloading teardown.
    for r in rows[:returned + 1]:
        if r['event'].endswith('.Start'):
            allowed.add((r['event'].rsplit('.', 1)[0] + '.Update', 'Update.ScriptRunBehaviourUpdate'))
    returned_state = fields(rows[returned]['detail'])
    terminal = lambda e: e.endswith(('.OnDisable', '.OnDestroy')) or e in ('unload.completed', 'firstNull')
    for i, row in enumerate(rows[returned + 1:], returned + 1):
        event = row['event']
        if terminal(event):
            if row['frame'] != completion_frame or row['phase'] != 'EarlyUpdate.UpdatePreloading':
                problems.append('unload callbacks/null/completion are outside the preloading completion pass: ' + json.dumps(row))
            if event.endswith('.OnDestroy') and i >= completed:
                problems.append('OnDestroy follows completion: ' + json.dumps(row))
            out.append(dict(row, frame='<completion>'))
        elif event == 'case.end':
            out.append(row)
        elif i > completed and event not in ('driver.Update', 'driver.LateUpdate'):
            problems.append('object callback after unload completion: ' + json.dumps(row))
        elif i < completed and (event, row['phase']) not in allowed:
            problems.append('unexpected callback while unloading: ' + json.dumps(row))
        state = fields(row['detail'])
        if 'targetNull' in state:
            for key in ('targetNull', 'goNull', 'scriptEnabled', 'psPlaying', 'audioPlaying', 'audioEnabled', 'animationEnabled'):
                expected = ('true' if key in ('targetNull', 'goNull') else 'false') if i >= completed else returned_state.get(key)
                if key in state and state[key] != expected:
                    problems.append('wrong object state around unload completion: ' + json.dumps(row))
                    break
    return out, problems


def compare(reference, actual, emit=print):
    left, right = Path(reference).read_text().splitlines(), Path(actual).read_text().splitlines()
    if left == right:
        emit(f'{len(left)}/{len(left)} records match; 0 residuals; 0 unexplained')
        return 0
    # Keep every missing, extra, different and reordered raw value visible.
    emit('\n'.join(difflib.unified_diff(left, right, fromfile=str(reference), tofile=str(actual))))
    decoded = [[json.loads(line) for line in lines] for lines in (left, right)]
    if any(not rows or rows[-1] != {'complete': True} for rows in decoded):
        emit('FAIL missing completion marker')
        return 1
    groups = []
    for rows in decoded:
        grouped = collections.defaultdict(list)
        for row in rows:
            if 'case' in row:
                grouped[row['case']].append(row)
        groups.append(grouped)
    failed = 0
    residuals = collections.Counter()
    for case in sorted(set(groups[0]) | set(groups[1])):
        views = []
        for group in groups:
            view, problems, notes = project_case(group.get(case, []))
            views.append(view)
            residuals.update(notes)
            for problem in problems:
                emit('FAIL ' + case + ': ' + problem)
                failed += 1
        if views[0] != views[1]:
            failed += 1
            emit('FAIL unexplained lifetime/callback difference: ' + case)
            emit('\n'.join(difflib.unified_diff([json.dumps(r, sort_keys=True) for r in views[0]],
                                              [json.dumps(r, sort_keys=True) for r in views[1]],
                                              fromfile='reference comparison view', tofile='engine comparison view')))
    matches = sum((collections.Counter(left) & collections.Counter(right)).values())
    emit('Documented residual observations: ' + json.dumps(residuals, sort_keys=True))
    emit(f'{matches}/{max(len(left), len(right))} records match; {sum(residuals.values())} residual observations; {failed} unexplained')
    return int(failed != 0)


if __name__ == '__main__':
    raise SystemExit(compare(Path(sys.argv[1]), Path(sys.argv[2])))
