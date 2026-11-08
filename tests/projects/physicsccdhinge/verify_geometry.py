"""Gate measured CCD motion and retain the broader contact trace as a parity report.

The strict comparison is never rewritten. Contact manifolds, native pair order
and the explicitly open mixed-compound case remain visible residuals, not a
claim of contact-payload parity. See the probe README's cause table.
"""
import math
from collections import Counter, defaultdict
from compare import equal


# These scenes are symmetric about y=0. Different contact processing order can
# select either side. Require the entire body reading to match one reflection.
MIRRORED = {
    'geometry/dynamic/3/0/3', 'geometry/dynamic/3/1/3', 'geometry/dynamic/3/3/3',
    'geometry/kinematic-target/2/2/3',
    'geometry/kinematic/2/1/3', 'geometry/kinematic/3/0/3',
    'geometry/kinematic-target/2/3/2',  # tied child hit, with unchanged body motion
}
OPEN_MANIFOLD = 'geometry/kinematic-target/4/2/3'
# These convex/speculative pairs have different manifold cardinalities. Keep
# positive, at-most-four-point manifolds; other callback counts stay exact.
OPEN_POINT_COUNTS = {'geometry/kinematic-target/1/3/3', 'geometry/kinematic-target/4/3/3'}
# Authored mixed compound: centers +/-0.15 and radius/half-width 0.1.
# Keep its center within one full compound width plus the target sphere radius.
COMPOUND_WIDTH = .5
TARGET_RADIUS = .1
FRONT_CENTER = .15


def headers(rows):
    groups = defaultdict(list)
    for key, value in rows.items():
        if '/event/' in key and '/' not in key.rsplit('/event/', 1)[1]:
            groups[key.rsplit('/event/', 1)[0]].append(value.split(','))
    return groups


def swap_child(name):
    return {'front':'rear', 'rear':'front'}.get(name, name)


def signature(fields):
    return tuple(fields[:3])


def open_callback_pattern(expected_groups, actual_groups, alternative):
    """Only the measured delayed exit and its child identity changes are open."""
    for step in range(5):
        for role in ('bullet', 'wall'):
            prefix = f'{OPEN_MANIFOLD}/{step}/{role}'
            wanted = Counter(signature(x) for x in expected_groups.get(prefix, ()))
            if alternative:
                if step == 1:
                    wanted[('exit','rear','0')] -= 1
                    wanted[('stay','rear','1')] += 1
                if step == 2:
                    wanted[('exit','front' if role == 'bullet' else 'rear','0')] += 1
                if step >= 2 and role == 'bullet':
                    wanted[('stay','front','1')] -= 1
                    wanted[('stay','rear','1')] += 1
            wanted = +wanted
            if wanted != Counter(signature(x) for x in actual_groups.get(prefix, ())):
                return False
    return True


def open_motion_valid(actual):
    """Require approach progress inside the authored sweep and consistent integration."""
    previous = None
    try:
        for step in range(5):
            values = [float(x) for x in actual[f'{OPEN_MANIFOLD}/{step}/bullet/body'].split(',')]
            if (len(values) != 13 or not all(math.isfinite(x) for x in values)
                    or not -(COMPOUND_WIDTH + TARGET_RADIUS) < values[0] < -FRONT_CENTER or abs(values[1]) >= 1
                    or values[2:7] != [0,0,0,0,1] or values[10:] != [0,0,0]
                    or not 0 < values[7] <= 100 or sum(v*v for v in values[7:10]) > 100**2):
                return False
            if previous is not None:
                delta = ','.join(str(values[i]-previous[i]) for i in range(3))
                integrated = ','.join(str(values[i+7]*.02) for i in range(3))
                if values[0] <= previous[0] or not equal(delta, integrated):
                    return False
            previous = values
    except (KeyError, ValueError):
        return False
    return True


def callback_checks(expected, actual, reflected_scenes):
    left, right = headers(expected), headers(actual)
    problems = []
    open_callbacks = (open_callback_pattern(left, right, False)
                      or open_callback_pattern(left, right, True))
    allowed_pairs = defaultdict(set)
    for key, value in expected.items():
        if '/event/' in key and '/' in key.rsplit('/event/', 1)[1]:
            prefix = key.rsplit('/event/', 1)[0]
            scenario, _, role = prefix.rsplit('/', 2)
            allowed_pairs[scenario,role].add(tuple(value.split(',')[-2:]))
    for prefix in left.keys() | right.keys():
        scenario, step, role = prefix.rsplit('/', 2)
        wanted, found = left[prefix], right[prefix]
        if scenario == OPEN_MANIFOLD:
            if not open_callbacks:
                problems.append(dict(key=prefix, reason='Unmeasured compound callback sequence'))
            # Stay callbacks expose incoming velocity even when the open path differs.
            try:
                incoming = [100,0,0] if int(step) == 0 else [float(x) for x in actual[f'{scenario}/{int(step)-1}/bullet/body'].split(',')[7:10]]
                relative = ','.join(str(v * (-1 if role == 'bullet' else 1)) for v in incoming)
                if any(not equal(','.join(x[6:]), relative) for x in found):
                    problems.append(dict(key=prefix, reason='Incorrect compound incoming velocity'))
            except (KeyError, ValueError):
                problems.append(dict(key=prefix, reason='Missing compound incoming body'))
            continue
        variants = [wanted]
        if scenario in reflected_scenes:
            reflected = []
            for fields in wanted:
                fields = list(fields)
                fields[1] = swap_child(fields[1])
                fields[7] = str(-float(fields[7]))
                reflected.append(fields)
            variants.append(reflected)
        def matches(candidate):
            remaining = list(candidate)
            for fields in found:
                for i, item in enumerate(remaining):
                    count_ok = fields[2] == item[2]
                    if scenario in OPEN_POINT_COUNTS and fields[0] != 'exit':
                        count_ok = 1 <= int(fields[2]) <= 4 and 1 <= int(item[2]) <= 4
                    if fields[:2] == item[:2] and count_ok and equal(','.join(fields[6:]), ','.join(item[6:])):
                        remaining.pop(i)
                        break
                else:
                    return False
            return not remaining
        if not any(matches(candidate) for candidate in variants):
            problems.append(dict(key=prefix, reason='Changed callback phase, collider, count or relative velocity'))
    for key, value in actual.items():
        if '/event/' in key and '/' in key.rsplit('/event/', 1)[1]:
            prefix = key.rsplit('/event/', 1)[0]
            scenario, _, role = prefix.rsplit('/', 2)
            pair = tuple(value.split(',')[-2:])
            if pair not in allowed_pairs[scenario,role]:
                problems.append(dict(key=key, reason='Unmeasured contact collider identity'))
    return problems, open_callbacks


def verify(expected, actual, report):
    unresolved, contacts, mirrors, open_motion = [], [], [], []
    checked = 0
    reflected_scenes = set()
    for scenario in MIRRORED:
        bodies = [key for key in expected if key.startswith(scenario+'/') and key.endswith('/body')]
        consistent = bool(bodies)
        for key in bodies:
            try:
                values = [float(x) for x in actual[key].split(',')]
                if len(values) != 13 or not all(math.isfinite(x) for x in values):
                    raise ValueError('Malformed body')
                values[1] = -values[1]
                values[8] = -values[8]
                consistent &= equal(expected[key], ','.join(map(str, values)))
            except (KeyError, ValueError):
                consistent = False
        if consistent:
            reflected_scenes.add(scenario)
    valid_open_motion = open_motion_valid(actual)
    # Even reporting-only contact payloads must be complete and internally consistent.
    for key, value in actual.items():
        if key.endswith('/events'):
            try:
                prefix = key[:-len('/events')]
                count = int(value)
                if count < 0 or any(prefix+'/event/'+str(i) not in actual for i in range(count)):
                    raise ValueError('Missing callback payload')
            except ValueError as error:
                unresolved.append(dict(key=key, reason=str(error)))
        if '/event/' not in key:
            continue
        fields = value.split(',')
        suffix = key.rsplit('/event/', 1)[1]
        try:
            if '/' not in suffix:
                if len(fields) != 9 or fields[0] not in ('enter', 'stay', 'exit'):
                    raise ValueError('Malformed event')
                prefix = key.rsplit('/event/', 1)[0]
                if not 0 <= int(suffix) < int(actual.get(prefix+'/events', '-1')):
                    raise ValueError('Orphan callback')
                count = int(fields[2])
                if count < 0 or any(key+'/'+str(i) not in actual for i in range(count)):
                    raise ValueError('Missing contact payload')
                numeric = fields[3:]
            else:
                if len(fields) != 9:
                    raise ValueError('Malformed contact')
                parent, index = key.rsplit('/', 1)
                if parent not in actual or not 0 <= int(index) < int(actual[parent].split(',')[2]):
                    raise ValueError('Orphan contact')
                numeric = fields[:7]
            if not all(math.isfinite(float(x)) for x in numeric):
                raise ValueError('Non-finite contact data')
        except (ValueError, IndexError) as error:
            unresolved.append(dict(key=key, reason=str(error)))
    open_callbacks = False
    if not unresolved:
        callback_problems, open_callbacks = callback_checks(expected, actual, reflected_scenes)
        unresolved.extend(callback_problems)
    for key in expected.keys() | actual.keys():
        if key.endswith('/body'):
            checked += 1
    for failure in report['failures']:
        key = failure['key']
        if '/event/' in key:
            contacts.append(failure)
            continue
        left, right = expected.get(key), actual.get(key)
        if left is None or right is None:
            unresolved.append(failure)
            continue
        scenario = key.rsplit('/', 3)[0]
        if key.endswith('/body'):
            try:
                values = [float(x) for x in right.split(',')]
                if len(values) != 13 or not all(math.isfinite(x) for x in values):
                    raise ValueError('Malformed body')
                if scenario in reflected_scenes:
                    mirrors.append(failure)
                    continue
                if scenario == OPEN_MANIFOLD and key.split('/')[-2] == 'bullet':
                    if valid_open_motion:
                        open_motion.append(failure)
                        continue
            except ValueError:
                pass
        if key.endswith('/events') and scenario == OPEN_MANIFOLD and open_callbacks:
            open_motion.append(failure)
            continue
        unresolved.append(failure)
    return dict(unresolved=unresolved, contact_trace=contacts, mirrored_motion=mirrors,
                open_manifold_motion=open_motion, checked_body_rows=checked)
