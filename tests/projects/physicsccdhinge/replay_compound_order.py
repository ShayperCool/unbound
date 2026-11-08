"""Reconstruct measured compounds' first-step normal response from public capture data.

This diagnostic is confined to the fixture's unit-mass, rotation-locked bodies,
prescribed incoming velocities and separated contacts. It changes no capture or
engine contact order and does not define a general Unity solver-order contract.
"""
import struct

from compare import equal


PREFIX = 'geometry/kinematic-target/4/2/3/0/bullet'
POSITION_ITERATIONS = 6
VELOCITY_ITERATIONS = 1
DT = .02
# Normal-response fraction used in native/physx/source/lowleveldynamics/src/DyContactPrep.cpp.
# Checked against the captured body readings; this is not an engine compatibility flag.
NORMAL_RESPONSE_FRACTION = .8
CASES = [(f'geometry/dynamic/3/{mode}/3', 'a', 'b', (-1, 1), (100, -100), (1, 1)) for mode in (0, 1, 3)] + [
    ('geometry/kinematic/2/1/3', 'mover', 'target', (-1, 0), (100, 0), (0, 1)),
    ('geometry/kinematic/3/0/3', 'mover', 'target', (-1, 0), (100, 0), (0, 1)),
    ('geometry/kinematic-target/2/2/3', 'bullet', 'wall', (-1, 0), (100, 0), (1, 0)),
    ('geometry/kinematic-target/4/2/3', 'bullet', 'wall', (-1, 0), (100, 0), (1, 0)),
]


def f32(value):
    return struct.unpack('f', struct.pack('f', value))[0]


def contacts(rows, prefix=PREFIX):
    result = []
    for slot in range(int(rows[prefix+'/events'])):
        key = prefix+'/event/'+str(slot)
        fields = rows[key].split(',')
        if len(fields) != 9 or fields[0] != 'enter' or fields[2] != '1':
            raise ValueError('Normal replay requires single-point Enter callbacks')
        point = rows[key+'/0'].split(',')
        normal = [float(x) for x in point[3:6]]
        separation = float(point[6])
        if separation <= 0:
            raise ValueError('Normal replay requires separated contacts')
        result.append((tuple(point[7:]), normal, separation))
    if len(result) not in (2, 4) or len({x[0] for x in result}) != len(result):
        raise ValueError('Normal replay requires distinct authored child pairs')
    return result


def replay(inputs, initial_positions=(-1, 0), initial_velocities=(100, 0), inverse_masses=(1, 0)):
    velocities = [[v, 0., 0.] for v in initial_velocities]
    impulses = [0.] * len(inputs)
    positions = None
    bias_scale = f32(f32(1 / DT) * f32(NORMAL_RESPONSE_FRACTION))
    for iteration in range(POSITION_ITERATIONS + VELOCITY_ITERATIONS):
        for index, (_, normal, separation) in enumerate(inputs):
            relative = [f32(a-b) for a, b in zip(*velocities)]
            normal_velocity = f32(f32(f32(relative[0]*normal[0]) + f32(relative[1]*normal[1])) + f32(relative[2]*normal[2]))
            target = -f32(separation * bias_scale)
            change = max(f32(f32(target-normal_velocity)/sum(inverse_masses)), -impulses[index])
            impulses[index] = f32(impulses[index]+change)
            for body, sign in ((0, 1), (1, -1)):
                velocities[body] = [f32(v+sign*f32(f32(change*inverse_masses[body])*n)) for v, n in zip(velocities[body], normal)]
        if iteration == POSITION_ITERATIONS-1:
            positions = [[f32(start+f32(v*f32(DT))) for start, v in zip((initial_positions[body], 0, 0), velocity)]
                         for body, velocity in enumerate(velocities)]
    return dict(position=positions[0], velocity=velocities[0], other_position=positions[1], other_velocity=velocities[1], impulses=impulses)


def matches_body(prediction, body, other=False):
    fields = body.split(',')
    wanted = ','.join(fields[:3]+fields[7:10])
    prefix = 'other_' if other else ''
    predicted = ','.join(str(x) for x in prediction[prefix+'position']+prediction[prefix+'velocity'])
    return equal(wanted, predicted)


def diagnose(expected, actual):
    cases = {}
    for scenario, role, other_role, positions, velocities, inverse_masses in CASES:
        prefix, other_prefix = scenario+'/0/'+role, scenario+'/0/'+other_role
        reference_contacts, engine_contacts = contacts(expected, prefix), contacts(actual, prefix)
        reference_replay = replay(reference_contacts, positions, velocities, inverse_masses)
        engine_replay = replay(engine_contacts, positions, velocities, inverse_masses)
        by_pair = {x[0]: x for x in engine_contacts}
        # Explicit counterfactual in the reference's recorded child-pair order.
        # The comparator and actual capture retain their original order.
        counterfactual = replay([by_pair[x[0]] for x in reference_contacts], positions, velocities, inverse_masses)
        def matches(prediction, rows):
            return matches_body(prediction, rows[prefix+'/body']) and matches_body(prediction, rows[other_prefix+'/body'], other=True)
        cases[scenario] = dict(reference_order=[x[0] for x in reference_contacts],
                               engine_order=[x[0] for x in engine_contacts],
                               reference_replay=reference_replay, engine_replay=engine_replay,
                               counterfactual=counterfactual,
                               reference_reconstructed=matches(reference_replay, expected),
                               engine_reconstructed=matches(engine_replay, actual),
                               reference_response_from_engine_contacts=matches(counterfactual, expected))
    return dict(cases=cases, **{name: all(case[name] for case in cases.values()) for name in
                ('reference_reconstructed', 'engine_reconstructed', 'reference_response_from_engine_contacts')})
