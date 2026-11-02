#!/usr/bin/env python3
"""Predeclared first-age candidates for sub-emitter particles and their exact evaluation.

    analyze.py predict [--out PREDICTIONS.json]          the committed predictions at nominal clocks
    analyze.py fit VALUES.jsonl [--report REPORT.json]   every candidate against one capture, with the
                                                         capture's ACTUAL clocks
    analyze.py history DEATHTIMING_RUN [--report R]      the same engine on the cached particledeathtiming
                                                         Native pairs (default, fixed-0.01, delta-0.015)

All candidates, clocks and the fit criterion are fixed here BEFORE any capture. Nothing is fitted:
a clock candidate is a value the capture records, never a constant chosen to match. The evaluation
never gates publication; none or several fitting candidates is a valid negative result, and other
explanations remain possible.

Model (float32, every operation rounded to float32; the particle-death-source checkpoint's
age_model.py, sha256 fe154f28..., which reproduced 26/26 admitted values and, with the Td rules,
38/38 Native values): a particle of start lifetime S keeps inv = 1/S and a percent age; one
integration of dt does age += inv*(dt*100) (capped at 100.00000762939453), life = (1 + age*-.01)*(1/inv),
pos += vel*(1*dt). A sub-emitted particle starts at the event origin with age 0 and receives its
FIRST AGE as the integrations a candidate lists; every later Simulate call integrates each of that
call's internal steps.

Internal steps of a call of length T: n = ceil(T / maximumParticleDeltaTime) equal steps of T/n
(the previously measured split; .035 -> .0175 + .0175, .05 -> 2 x .025). A natural Death event is
in step e = the first k with k*T/n >= the parent's remaining life at the call's start. A Birth event
of the broad history is taken at the call's end (e = n): an inference, not a measurement.

Candidate first ages for an event in step e of n steps of length L in a call of length T, clock C:
  R1(C)          one integration of floor(T/C)*C                       (call-level quantum)
  R2(C)          one integration of C for each step k in e..n with L >= C; none: one integration of 0
  PERSTEP(C)     one integration of C for each step k in e..n           (contradicted at C=Td by delta-0.015)
  CONT_REMAINING one integration of 0, then L for each step k in e..n  (the current engine rule;
                 contradicted by every recorded .025 history)
  CONT_TAIL      one integration of n*L - life0 (from the death instant; contradicted by .025 histories)
  LITERAL_002    one integration of .02 (contradicted at the bit level by the later lifetime record)
  STEP_MINUS_005 one integration of L - .005; STEP_X08 one integration of .8*L (both contradicted)
Clocks C, read by the probe in the case's own frame (case.*.clock.before) or at startup:
  frame.deltaTime, frame.unscaledDeltaTime, frame.fixedDeltaTime, frame.fixedUnscaledDeltaTime,
  frame.captureDeltaTime, startup.deltaTime, startup.unscaledDeltaTime, startup.fixedDeltaTime.
  A clock <= 0 leaves the candidate UNDEFINED in that case (it cannot fit there).

Fit criterion (Death cases): the Death child's one particle (same seed after both calls) must
equal the prediction bit for bit in life and pos.z after call 1 (FIRST) and after call 2 (FIT).
Birth control: the first child's life bits at its first record and at the next record (same seed);
its position is reported, not modelled. Classes: candidates whose predictions are identical in
every case are one indistinguishable class. Outcome: NONE, ONE class, or MULTIPLE classes.
"""
import argparse
import json
import math
from pathlib import Path
import struct
import sys

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import guards  # noqa: E402

TD = 0.0199999921            # the recorded startup deltaTime = fixedDeltaTime of every cached history
DEATH_START = 2.013          # Death child start lifetime (speed 1, +z)
DEATH_ORIGIN = 0.46          # stationary parent
BIRTH_START = 0.413
BIRTH_SPEED = 0.5
CLOCKS = ('frame.deltaTime', 'frame.unscaledDeltaTime', 'frame.fixedDeltaTime', 'frame.fixedUnscaledDeltaTime',
          'frame.captureDeltaTime', 'startup.deltaTime', 'startup.unscaledDeltaTime', 'startup.fixedDeltaTime')
CLOCK_RULES = ('R1', 'R2', 'PERSTEP')
FIXED_RULES = ('CONT_REMAINING', 'CONT_TAIL', 'LITERAL_002', 'STEP_MINUS_005', 'STEP_X08')
CONTRADICTED = {
    'PERSTEP(startup.fixedDeltaTime)': 'delta-0.015 cached pair: first age 0, not Td',
    'CONT_REMAINING': 'every .025 history: first age Td, not .025 (lifetime27, deathtiming default)',
    'CONT_TAIL': 'deathtiming default: Td, not .012',
    'LITERAL_002': 'lifetime27 natural_death_child after2 3ffbe76d (Td) vs 3ffbe76c (.02)',
    'STEP_MINUS_005': 'broad sub_emitters_child call 16 life 3e645a1d needs 2Td, not .04',
    'STEP_X08': 'broad sub_emitters_child call 16 life 3e645a1d needs 2Td, not .04',
    'R(frame.fixedDeltaTime) at C=.01': 'deathtiming fixed-0.01: unchanged Td with the current fixed delta .01',
}


def f(x):
    return struct.unpack('<f', struct.pack('<f', float(x)))[0]


def bits(x):
    return struct.pack('>f', f(x)).hex()


class Particle:
    """age_model.P: our Integrate/ExportLifetime in float32."""

    def __init__(self, start, pos, vel):
        self.inv = f(f(1)/f(start))
        self.age = f(0)
        self.start = f(f(1)/self.inv)
        self.life = f(start)
        self.pos = f(pos)
        self.vel = f(vel)

    def integ(self, dt):
        dt = f(dt)
        if self.age <= f(100):
            self.age = min(f(self.age+f(self.inv*f(dt*f(100)))), f(100.00000762939453))
        self.start = f(f(1)/self.inv)
        self.life = f(f(f(1)+f(self.age*f(-.01)))*self.start)
        self.pos = f(self.pos+f(self.vel*f(f(1)*dt)))
        return self


def split(call, max_dt):
    n = max(1, math.ceil(f(call)/f(max_dt) - 1e-12))
    return n, f(f(call)/f(n))


def event_step(n, length, life0):
    for k in range(1, n+1):
        if f(k*length) >= f(life0):
            return k
    return None


def first_age(rule, clock, call, n, e, length, life0):
    """The integrations of the first age, or None (UNDEFINED)."""
    steps = n-e+1
    if rule in CLOCK_RULES:
        if clock is None or not clock > 0:
            return None
        c = f(clock)
        if rule == 'R1':
            return [f(math.floor(f(call)/c)*c)]
        if rule == 'R2':
            ages = [c for _ in range(steps) if length >= c]
            return ages or [f(0)]
        return [c]*steps
    if rule == 'CONT_REMAINING':
        return [f(0)]+[length]*steps
    if rule == 'CONT_TAIL':
        return [f(f(n*length)-f(life0))]
    if rule == 'LITERAL_002':
        return [f(.02)]
    if rule == 'STEP_MINUS_005':
        return [f(length-f(.005))]
    if rule == 'STEP_X08':
        return [f(length*f(.8))]
    raise ValueError(rule)


def candidates():
    for rule in CLOCK_RULES:
        for clock in CLOCKS:
            yield f'{rule}({clock})', rule, clock
    for rule in FIXED_RULES:
        yield rule, rule, None


def clock_value(name, frame, startup):
    scope, key = name.split('.', 1)
    return (frame if scope == 'frame' else startup).get(key)


def predict_death(call, life0, frame, startup, later_calls=1):
    """Every candidate's integrations and (life, pos.z) after call 1 and call 2."""
    max_dt = frame['maximumParticleDeltaTime']
    n, length = split(call, max_dt)
    e = event_step(n, length, life0)
    out = {}
    for name, rule, clock in candidates():
        ages = first_age(rule, clock_value(clock, frame, startup) if clock else None, call, n, e, length, life0)
        if ages is None:
            out[name] = None
            continue
        p = Particle(DEATH_START, DEATH_ORIGIN, 1)
        for a in ages:
            p.integ(a)
        after1 = (p.life, p.pos)
        for _ in range(later_calls):
            for _ in range(n):
                p.integ(length)
        out[name] = {'ages': [bits(a) for a in ages], 'age_sum': sum(ages),
                     'after1': {'life': bits(after1[0]), 'pos': bits(after1[1]), 'life_value': after1[0], 'pos_value': after1[1]},
                     'after2': {'life': bits(p.life), 'pos': bits(p.pos), 'life_value': p.life, 'pos_value': p.pos}}
    return {'call': call, 'parentLife': life0, 'steps': n, 'stepLength': length, 'eventStep': e}, out


def predict_birth(call, frame, startup):
    """Birth: event at the call's end (e = n); life only (the origin is not modelled)."""
    n, length = split(call, frame['maximumParticleDeltaTime'])
    out = {}
    for name, rule, clock in candidates():
        if rule in ('CONT_TAIL',):
            out[name] = None
            continue
        ages = first_age(rule, clock_value(clock, frame, startup) if clock else None, call, n, n, length, 0)
        if ages is None:
            out[name] = None
            continue
        p = Particle(BIRTH_START, 0, BIRTH_SPEED)
        for a in ages:
            p.integ(a)
        first = p.life
        for _ in range(n):
            p.integ(length)
        out[name] = {'ages': [bits(a) for a in ages], 'age_sum': sum(ages), 'first': {'life': bits(first), 'life_value': first},
                     'next': {'life': bits(p.life), 'life_value': p.life}}
    return {'call': call, 'steps': n, 'stepLength': length, 'eventStep': n, 'assumption': 'Birth event at the call end'}, out


# ---------------------------------------------------------------- nominal predictions

def nominal():
    """Nominal clocks for the predictions table. The fit always uses the recorded ones."""
    startup = {'deltaTime': TD, 'unscaledDeltaTime': TD, 'fixedDeltaTime': TD, 'fixedUnscaledDeltaTime': TD,
               'captureDeltaTime': 0.0, 'timeScale': 1.0, 'maximumParticleDeltaTime': 0.03}
    capture = f(1/30)
    d3 = dict(startup, deltaTime=f(capture*f(.5)), unscaledDeltaTime=capture, captureDeltaTime=capture, timeScale=.5,
              fixedUnscaledDeltaTime=f(TD/.5))
    fixed = dict(d3, fixedDeltaTime=f(.0075), fixedUnscaledDeltaTime=f(f(.0075)/.5))
    return startup, {'case.baseline': startup, 'case.d1': startup, 'case.d2': startup, 'case.d3': d3, 'case.fixed': fixed}


def classes(table):
    """Candidates with identical predictions in every case form one indistinguishable class."""
    signature = {}
    for name, _, _ in candidates():
        sig = json.dumps([None if table[c][name] is None else [table[c][name][k] for k in sorted(table[c][name]) if k in ('after1', 'after2', 'first', 'next')]
                          for c in sorted(table)], sort_keys=True)
        signature.setdefault(sig, []).append(name)
    return sorted(signature.values())


def predict(out):
    startup, frames = nominal()
    table, cases = {}, {}
    for key, (call, life, _) in guards.DEATH_CASES.items():
        cases[key], table[key] = predict_death(call, life, frames[key], startup)
    cases['case.birth'], table['case.birth'] = predict_birth(0.05, startup, startup)
    summary = {}
    for key in table:
        summary[key] = {}
        for name in ('R1(startup.fixedDeltaTime)', 'R2(startup.fixedDeltaTime)', 'R1(frame.deltaTime)', 'R2(frame.deltaTime)',
                     'R1(frame.unscaledDeltaTime)', 'R2(frame.unscaledDeltaTime)', 'R1(frame.fixedDeltaTime)',
                     'R2(frame.fixedDeltaTime)', 'CONT_REMAINING'):
            t = table[key][name]
            summary[key][name] = None if t is None else round(t['age_sum'], 9)
    report = {'format': 1, 'scope': 'precommitted before any capture; nominal clocks (the fit uses the recorded ones)',
              'nominal_clocks': {'startup': startup, **{k: v for k, v in frames.items() if k in ('case.d3', 'case.fixed')}},
              'nominal_assumptions': ['startup: every clock is Td = 0.0199999921 (all cached histories), captureDeltaTime 0',
                                      'd3: deltaTime = captureDeltaTime * timeScale, unscaledDeltaTime = captureDeltaTime, '
                                      'fixedUnscaledDeltaTime = fixedDeltaTime / timeScale (unverified; recorded)',
                                      'fixed: d3 with fixedDeltaTime .0075 (fixedUnscaled = .0075 / .5)'],
              'cases': cases, 'first_age_seconds': summary, 'contradicted_before_capture': CONTRADICTED,
              'indistinguishable_classes_at_nominal_clocks': [c for c in classes(table) if len(c) > 1],
              'class_count': len(classes(table)), 'predictions': table}
    text = json.dumps(report, indent=1, sort_keys=False)+'\n'
    if out:
        Path(out).write_text(text)
    for key in summary:
        print(key, json.dumps(summary[key]))
    print('classes:', len(classes(table)))
    return 0


# ---------------------------------------------------------------- fit

def load(path):
    values = {}
    for line in Path(path).read_text().splitlines():
        row = json.loads(line)
        values.setdefault(row['k'], row['v'])
    return values


def native_particle(state):
    if not isinstance(state, dict) or state.get('count') != 1 or len(state.get('particles', [])) != 1:
        return None
    return state['particles'][0]


def judge(pred, n1, n2):
    if pred is None:
        return 'UNDEFINED'
    first = bits(n1['life']) == pred['after1']['life'] and bits(n1['pos'][2]) == pred['after1']['pos']
    second = bits(n2['life']) == pred['after2']['life'] and bits(n2['pos'][2]) == pred['after2']['pos']
    return 'FIT' if first and second else 'FIRST' if first else 'NO'


def clocks_of(values, key):
    return values.get(key+'.clock.before') or {}


def fit_death(values, key, startup):
    call, life, _ = guards.DEATH_CASES[key]
    frame = clocks_of(values, key)
    n1, n2 = (native_particle(values.get(f'{key}.death.after{i}')) for i in (1, 2))
    native = {'after1': values.get(key+'.death.after1'), 'after2': values.get(key+'.death.after2')}
    if not frame or n1 is None or n2 is None or n1['seed'] != n2['seed']:
        return {'evaluable': False, 'why': 'not one Death child particle with the same seed after both calls',
                'native': native}, {}
    shape, table = predict_death(call, life, frame, startup)
    verdicts = {name: judge(table[name], n1, n2) for name in table}
    derived = {'after1_age_from_life': f(f(DEATH_START)-f(n1['life'])), 'after1_age_from_pos': f(f(n1['pos'][2])-f(DEATH_ORIGIN)),
               'after1': {'life': bits(n1['life']), 'pos': bits(n1['pos'][2])},
               'after2': {'life': bits(n2['life']), 'pos': bits(n2['pos'][2])}}
    return {'evaluable': True, 'shape': shape, 'clocks': frame, 'native_bits': derived, 'verdicts': verdicts,
            'fits': sorted(k for k, v in verdicts.items() if v == 'FIT')}, table


def fit_birth(values, startup):
    frame = clocks_of(values, 'case.birth')
    states = [values.get(f'case.birth.child.{i}') for i in range(guards.BIRTH_CALLS)]
    first = next((i for i, s in enumerate(states) if isinstance(s, dict) and s.get('count', 0) > 0), None)
    if not frame or first is None or first+1 >= len(states) or not states[first]['particles']:
        return {'evaluable': False, 'why': 'no Birth child appears before the last call'}, {}
    p0 = states[first]['particles'][0]
    later = next((p for p in states[first+1]['particles'] if p['seed'] == p0['seed']), None)
    if later is None:
        return {'evaluable': False, 'why': 'the first Birth child is not in the next record'}, {}
    shape, table = predict_birth(0.05, frame, startup)
    verdicts = {}
    for name, pred in table.items():
        if pred is None:
            verdicts[name] = 'UNDEFINED'
            continue
        a = bits(p0['life']) == pred['first']['life']
        b = bits(later['life']) == pred['next']['life']
        verdicts[name] = 'FIT' if a and b else 'FIRST' if a else 'NO'
    return {'evaluable': True, 'shape': shape, 'firstRecord': first, 'seed': p0['seed'],
            'native_bits': {'first': {'life': bits(p0['life']), 'pos': [bits(x) for x in p0['pos']]},
                            'next': {'life': bits(later['life']), 'pos': [bits(x) for x in later['pos']]},
                            'first_age_from_life': f(f(BIRTH_START)-f(p0['life']))},
            'verdicts': verdicts, 'fits': sorted(k for k, v in verdicts.items() if v == 'FIT')}, table


def outcome(names, table_by_case):
    if not names:
        return 'NONE', []
    groups = [c for c in classes({k: {n: table_by_case[k].get(n) for n, _, _ in candidates()} for k in table_by_case})
              if any(n in names for n in c)]
    groups = [[n for n in c if n in names] for c in groups]
    return ('ONE' if len(groups) == 1 else 'MULTIPLE'), groups


def fit(path, report):
    values = load(path)
    startup = values.get('clock.startup.clocks') or {}
    cases, tables = {}, {}
    for key in guards.DEATH_CASES:
        cases[key], tables[key] = fit_death(values, key, startup)
    birth, birth_table = fit_birth(values, startup)
    evaluable = all(cases[k]['evaluable'] for k in cases)
    death_fits = sorted(set.intersection(*(set(cases[k]['fits']) for k in cases))) if evaluable else []
    verdict, groups = outcome(death_fits, tables) if evaluable else ('NOT_EVALUABLE', [])
    shared = {name: birth['verdicts'].get(name) if birth['evaluable'] else 'NOT_EVALUABLE' for name in death_fits}
    result = {'format': 1, 'capture': str(path), 'scope': 'evaluation only; never a publication gate',
              'death_outcome': verdict, 'death_fit_classes': groups, 'death_fits': death_fits,
              'birth_under_death_fits': shared, 'birth_fits': birth.get('fits', []),
              'startup_clocks': startup, 'cases': cases, 'birth': birth,
              'ordinary_children': {k: [values.get(f'{k}.ordinary.after{i}') for i in (1, 2)] for k in guards.DEATH_CASES},
              'parents': {k: [values.get(f'{k}.parent.after{i}') for i in (1, 2)] for k in guards.DEATH_CASES}}
    if report:
        Path(report).write_text(json.dumps(result, indent=1)+'\n')
    print('death outcome:', verdict, groups)
    for key, c in cases.items():
        print(' ', key, 'fits' if c['evaluable'] else 'NOT EVALUABLE', c.get('fits', c.get('why')))
    print('  birth fits:', birth.get('fits', birth.get('why')))
    print('  birth under the Death fits:', shared)
    return 0


def history(run, report):
    """The engine on the cached particledeathtiming Native pairs (stationary parent, .46, life .013)."""
    run = Path(run)
    rows = {}
    startup = {'deltaTime': TD, 'unscaledDeltaTime': TD, 'fixedDeltaTime': TD, 'fixedUnscaledDeltaTime': TD,
               'captureDeltaTime': 0.0, 'timeScale': 1.0, 'maximumParticleDeltaTime': 0.03}
    for name, rel, call in (('default', 'focused/native-a.jsonl', 0.025), ('fixed-0.01', 'focused/fixed-native-a.jsonl', 0.025),
                            ('delta-0.015', 'eventdelta/native-a.jsonl', 0.015)):
        recs = {(r['scenario'], r['step']): r for r in map(json.loads, (run/rel).read_text().splitlines())}
        n1, n2 = (native_particle(recs[('death_child', f'after{i}')]) for i in (1, 2))
        first = recs[('death_child', 'before')]
        # Only what the cached records hold; unrecorded frame clocks leave their candidates UNDEFINED.
        # startup.* = Td, the frame-1 deltaTime/fixedDeltaTime the default and delta-0.015 records hold.
        frame = {'fixedDeltaTime': first['fixedDeltaTime'], 'maximumParticleDeltaTime': first['maximumParticleDeltaTime'],
                 'deltaTime': first.get('deltaTime')}
        _, table = predict_death(call, 0.013, frame, startup)
        verdicts = {k: judge(v, n1, n2) for k, v in table.items()}
        rows[name] = {'fits': sorted(k for k, v in verdicts.items() if v == 'FIT'), 'verdicts': verdicts,
                      'native': {'after1': [bits(n1['life']), bits(n1['pos'][2])], 'after2': [bits(n2['life']), bits(n2['pos'][2])]}}
        print(name, rows[name]['fits'])
    common = sorted(set.intersection(*(set(r['fits']) for r in rows.values())))
    print('fit by all three cached histories:', common)
    if report:
        Path(report).write_text(json.dumps({'run': str(run), 'histories': rows, 'common_fits': common}, indent=1)+'\n')
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest='mode', required=True)
    p = sub.add_parser('predict')
    p.add_argument('--out', type=Path)
    q = sub.add_parser('fit')
    q.add_argument('values', type=Path)
    q.add_argument('--report', type=Path)
    h = sub.add_parser('history')
    h.add_argument('run', type=Path)
    h.add_argument('--report', type=Path)
    args = parser.parse_args()
    if args.mode == 'predict':
        return predict(args.out)
    if args.mode == 'fit':
        return fit(args.values, args.report)
    return history(args.run, args.report)


if __name__ == '__main__':
    sys.exit(main())
