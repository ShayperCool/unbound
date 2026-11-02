#!/usr/bin/env python3
"""Guards and the per-control reading of one OnRenderObject order capture.

    analyze.py VALUES.jsonl [--context CORE_APIS_REFERENCE.jsonl] [--json OUT.json]

Reads the record as it is: the camera render sequence of each frame (Camera.onPreCull), the
OnRenderObject calls of each camera render in record order, the state line and every
mutation. It never sorts, filters or rewrites the record; the predictions below were fixed
before any capture (README.md) and are only compared with what was recorded.
"""
import argparse
import json
from pathlib import Path
import sys

HANDLERS = ('RtCam', 'ScreenCam', 'Cube')
CAMERAS = ('RtCam', 'ScreenCam')
WINDOW_FRAMES = 16          # CoreProbe.WindowFrames: the OnDemandRendering window is w0..w16
ORO_FIRST = WINDOW_FRAMES + 1   # order window frame o = w - ORO_FIRST
ORO_LAST = 16               # o0..o16 (CoreProbe.OroFrames = 17)
MARKER = {'k': 'complete', 'v': True}
PHASES = ('P0-baseline', 'P1-mixed-depth', 'P2-offscreen-depth', 'P2b-offscreen-restore',
          'P3-restore', 'P4-reenable', 'P5-combined')
# The deliberate mutations, by frame, with their read-back (exactly as CoreProbe records them).
MUTATIONS = {
    0: ['renderFrameInterval=1 -> 1'],
    4: ['ScreenCam.depth=-10 -> -10'],
    6: ['ScreenCam.targetTexture=texture -> 1'],
    8: ['ScreenCam.depth=0 -> 0'],
    10: ['ScreenCam.targetTexture=null -> 0'],
    12: ['RtCam.RenderWatch.enabled=false -> 0 isActiveAndEnabled=0',
         'RtCam.RenderWatch.enabled=true -> 1 isActiveAndEnabled=1'],
    14: ['ScreenCam.targetTexture=texture -> 1', 'ScreenCam.depth=-10 -> -10'],
}
# The re-enabled handler (frame 12) and the frame from which it counts as re-registered.
REENABLED, REENABLE_FROM = 'RtCam', 12


def rows(path):
    return [json.loads(line) for line in Path(path).read_text().splitlines() if line.strip()]


# ---------------------------------------------------------------- reading

def frames(records):
    """Order-window frames: o -> {phase, state, mutations, cameras, renders}. A render is one
    camera's OnRenderObject calls (handler names in record order)."""
    out = {}
    for r in records:
        w = r.get('w')
        if w is None or w < ORO_FIRST:
            continue
        f = out.setdefault(w - ORO_FIRST, {'phase': None, 'state': None, 'mutations': [], 'cameras': [],
                                           'renders': [], 'oro_cams': []})
        who, e = r['who'], r['e']
        if who == 'oro' and e.startswith('frame '):
            f['phase'] = e.split('phase=', 1)[1]
        elif who == 'oro' and e.startswith('state '):
            f['state'] = e
        elif who == 'mutation':
            f['mutations'].append(e)
        elif e == 'Camera.onPreCull':
            f['cameras'].append(who)
        elif e.startswith('OnRenderObject cam='):
            cam = e.split('=', 1)[1]
            if not f['renders'] or f['oro_cams'][-1] != cam or who in f['renders'][-1]:
                f['renders'].append([])
                f['oro_cams'].append(cam)
            f['renders'][-1].append(who)
    return out


def id_orders(records):
    return {r['e'].split(':', 1)[0]: r['e'].split(':', 1)[1].split()
            for r in records if r.get('who') == 'oro' and r['e'].startswith('instance id order')}


def depths(state):
    """Camera depth/target from a state line."""
    parts = state.split()
    out = {}
    for i, p in enumerate(parts):
        if p in CAMERAS and i + 2 < len(parts) and parts[i + 2].startswith('depth='):
            out[p] = (float(parts[i + 2][6:]), parts[i + 3][7:])
    return out


# ---------------------------------------------------------------- predictions (fixed in advance)

def baseline(fr):
    """Observed P0 order (the order the registration hypotheses take as the scene-load
    registration order; that wake order itself is not witnessed here)."""
    seen = [tuple(r) for o in (2, 3) for r in fr.get(o, {}).get('renders', [])]
    return list(seen[0]) if seen and all(s == seen[0] for s in seen) else None


def predict(name, o, f, cam, base, ids):
    reenabled = o >= REENABLE_FROM
    if name == 'H1-registration-append':
        if base is None:
            return None
        return [h for h in base if h != REENABLED] + [REENABLED] if reenabled else list(base)
    if name == 'H1s-registration-swap-remove':
        if base is None:
            return None
        if not reenabled:
            return list(base)
        rest = list(base)
        i = rest.index(REENABLED)
        last = rest.pop()
        if i < len(rest):
            rest[i] = last
        return rest + [REENABLED]
    if name == 'H2-instance-id':
        order = ids.get('instance id order .RenderWatch')
        return [n.split('.', 1)[0] for n in order] if order else None
    if name == 'H2f-file-order':
        return ['RtCam', 'ScreenCam', 'Cube']  # Probe.unity fileID/document order
    if name == 'H3a-camera-render-order-first':
        cams = [c for c in f['cameras'] if c in CAMERAS]
        return cams + ['Cube'] if sorted(cams) == sorted(CAMERAS) else None
    if name == 'H3b-camera-depth-order-first':
        d = depths(f['state'] or '')
        if len(d) != 2 or d['RtCam'][0] == d['ScreenCam'][0]:
            return None
        return sorted(CAMERAS, key=lambda c: d[c][0]) + ['Cube']
    if name == 'H0-current-camera-first':
        return [cam] + [h for h in HANDLERS if h != cam]
    raise KeyError(name)


HYPOTHESES = ('H1-registration-append', 'H1s-registration-swap-remove', 'H2-instance-id', 'H2f-file-order',
              'H3a-camera-render-order-first', 'H3b-camera-depth-order-first', 'H0-current-camera-first')
# H4 (type-grouped registration) predicts what H1/H1s predict: the probe has one handler class.


# ---------------------------------------------------------------- guards

def guard_problem(path, context=None):
    """Why this capture cannot back the reference (None if it can). Structural only: the
    observed order never enters a guard."""
    try:
        records = rows(path)
    except (OSError, json.JSONDecodeError) as error:
        return f'unreadable record: {error}'
    if not records or records[-1] != MARKER or records.count(MARKER) != 1:
        return 'the completion marker is not the single last line'
    if not any(r.get('e') == 'finish' and r.get('who') == 'probe' for r in records[-2:]):
        return 'the finish record does not precede the marker'
    fr = frames(records)
    missing = [o for o in range(ORO_LAST + 1) if o not in fr or fr[o]['phase'] is None or fr[o]['state'] is None]
    if missing:
        return f'order-window frames without their frame/state lines: {missing}'
    for o in range(ORO_LAST + 1):
        if fr[o]['mutations'] != MUTATIONS.get(o, []):
            return f'frame o{o}: mutations {fr[o]["mutations"]} instead of {MUTATIONS.get(o, [])}'
    ids = id_orders(records)
    if sorted(ids) != ['instance id order .Camera', 'instance id order .RenderWatch', 'instance id order GameObject']:
        return f'instance id order lines: {sorted(ids)}'
    start = max(i for i, r in enumerate(records) if r.get('w') is not None and r['w'] <= WINDOW_FRAMES) + 1
    bad = [r for r in records[start:-2] if 'w' not in r and (r.get('e', '').startswith('step threw') or
           (r.get('who') == 'log' and r.get('e', '').split(':', 1)[0] in ('Exception', 'Error', 'Assert')))]
    if bad:
        return f'exceptions/errors after the OnDemandRendering window: {bad[:3]}'
    return None


def context_check(records, reference):
    """Non-blocking: does the record reproduce the core API probe's w4 reference up to the end
    of its OnDemandRendering window (all of it but its finish line)?"""
    ref = rows(reference)
    prefix = ref[:-1]
    same = records[:len(prefix)] == prefix
    first = next((i for i, (a, b) in enumerate(zip(records, prefix)) if a != b), None)
    return {'reference': str(reference), 'reference_lines': len(ref), 'prefix_lines': len(prefix),
            'prefix_identical': same, 'first_difference_line': None if first is None else first + 1}


# ---------------------------------------------------------------- report

def report(path, context=None):
    records = rows(path)
    fr = frames(records)
    ids = id_orders(records)
    base = baseline(fr)
    phases = {}
    for o in sorted(fr):
        f = fr[o]
        if f['phase'] not in PHASES:
            continue
        p = phases.setdefault(f['phase'], {'frames': [], 'renders': [], 'verdict': {}})
        p['frames'].append({'o': o, 'state': f['state'], 'mutations': f['mutations'], 'cameras': f['cameras'],
                            'renders': [{'cam': c, 'handlers': r} for c, r in zip(f['oro_cams'], f['renders'])]})
        for cam, handlers in zip(f['oro_cams'], f['renders']):
            row = {'o': o, 'cam': cam, 'observed': handlers}
            for h in HYPOTHESES:
                row[h] = predict(h, o, f, cam, base, ids)
            p['renders'].append(row)
    for p in phases.values():
        for h in HYPOTHESES:
            preds = [r[h] for r in p['renders']]
            if not p['renders'] or any(x is None for x in preds):
                p['verdict'][h] = 'no prediction'
            else:
                p['verdict'][h] = 'match' if all(r[h] == r['observed'] for r in p['renders']) else 'mismatch'
        p['observed_orders'] = sorted({','.join(r['observed']) for r in p['renders']})
        p['camera_orders'] = sorted({','.join(f['cameras']) for f in p['frames']})
    out = {'record': str(path), 'guard': guard_problem(path), 'instance_id_orders': ids, 'baseline': base,
           'phases': phases,
           'transition_frames': {o: {'phase': fr[o]['phase'], 'cameras': fr[o]['cameras'], 'renders': fr[o]['renders'],
                                     'state': fr[o]['state'], 'mutations': fr[o]['mutations']}
                                 for o in sorted(fr) if fr[o]['phase'] not in PHASES}}
    if context:
        out['context'] = context_check(records, context)
    return out


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('values')
    parser.add_argument('--context')
    parser.add_argument('--json')
    args = parser.parse_args()
    result = report(args.values, args.context)
    text = json.dumps(result, indent=1)
    if args.json:
        Path(args.json).write_text(text + '\n')
    print('guard:', result['guard'] or 'passes')
    print('baseline:', result['baseline'], ' ids:', result['instance_id_orders'])
    for name, p in result['phases'].items():
        print(f"{name}: cameras {p['camera_orders']} handlers {p['observed_orders']}")
        print('   ', ', '.join(f'{h}={v}' for h, v in p['verdict'].items()))
    if 'context' in result:
        print('context:', result['context'])
    return 0


if __name__ == '__main__':
    sys.exit(main())
