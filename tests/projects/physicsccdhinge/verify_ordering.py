"""Check capture integrity and native/legacy contact consistency, preserving order reports."""
import math
from collections import defaultdict

from analyze_contacts import multiset_equal
from compare import compare, equal


def verify(expected, actual):
    problems = []
    for key in expected:
        if key.startswith('frames/pending/') and not equal(expected[key], actual.get(key)):
            problems.append(dict(key=key, reason='Deferred convex frame or overlap query differs', expected=expected[key], actual=actual.get(key)))
        if key.endswith(('/body', '/events', '/bounds')) and key not in actual:
            problems.append(dict(key=key, reason='Missing body, count or bounds'))
        elif key.startswith('frames/') and not key.startswith('frames/5/') and key.endswith(('/body', '/bounds')) and not equal(expected[key], actual.get(key)):
            problems.append(dict(key=key, reason='Frame control motion or bounds differs', expected=expected[key], actual=actual.get(key)))
    for key, value in actual.items():
        try:
            if key.endswith('/events'):
                count = int(value)
                if count < 0 or any(key[:-7]+'/event/'+str(i) not in actual for i in range(count)):
                    raise ValueError('Missing callback')
            elif key.endswith(('/body', '/bounds')):
                values = [float(x) for x in value.split(',')]
                if len(values) != (13 if key.endswith('/body') else 6) or not all(math.isfinite(x) for x in values):
                    raise ValueError('Malformed body or bounds')
            elif '/event/' in key or '/native/' in key:
                native = '/native/' in key
                suffix = key.split('/native/' if native else '/event/')[1]
                fields = value.split(',')
                if '/' not in suffix:
                    if len(fields) != 9:
                        raise ValueError('Malformed pair')
                    count = int(fields[5 if native else 2])
                    if native and (any(x not in ('True', 'False') for x in fields[2:5]) or 'True' not in fields[2:5]):
                        raise ValueError('Invalid native callback flags')
                    if native and any(len(x.split('/')) != 2 for x in fields[:2]):
                        raise ValueError('Invalid native collider identity')
                    if count < 0 or any(key+'/'+str(i) not in actual for i in range(count)):
                        raise ValueError('Missing contact')
                    if not native:
                        context = key.split('/event/')[0]
                        if fields[0] not in ('enter', 'stay', 'exit') or not 0 <= int(suffix) < int(actual[context+'/events']):
                            raise ValueError('Orphan callback')
                    numeric = fields[6:] if native else fields[3:]
                else:
                    parent, index = key.rsplit('/', 1)
                    if len(fields) != (10 if native else 9) or not 0 <= int(index) < int(actual[parent].split(',')[5 if native else 2]):
                        raise ValueError('Orphan or malformed contact')
                    numeric = fields if native else fields[:7]
                if not all(math.isfinite(float(x)) for x in numeric):
                    raise ValueError('Non-finite contact')
        except (ValueError, KeyError, IndexError) as error:
            problems.append(dict(key=key, reason=str(error)))
    if problems:
        return dict(unresolved=problems)
    callbacks = defaultdict(list)
    covered_callbacks = set()
    for key, value in actual.items():
        if '/event/' in key and '/' not in key.split('/event/')[1]:
            fields = value.split(',')
            callbacks[key.split('/event/')[0]].append((fields, [actual[key+'/'+str(i)] for i in range(int(fields[2]))]))
    for key, value in actual.items():
        if '/native/' not in key or '/' in key.split('/native/')[1]:
            continue
        context = key.split('/native/')[0]
        fields = value.split(',')
        points = [actual[key+'/'+str(i)].split(',') for i in range(int(fields[5]))]
        total = ','.join(str(sum(float(p[i]) for p in points)) for i in range(7, 10))
        if not equal(total, ','.join(fields[6:])):
            problems.append(dict(key=key, reason='Native point impulses do not sum to pair impulse'))
        for first in (True, False):
            self, other = fields[0 if first else 1].split('/'), fields[1 if first else 0].split('/')
            for phase, flag in zip(('enter', 'stay', 'exit'), fields[2:5]):
                if flag != 'True' or phase == 'exit':
                    continue
                wanted = []
                for point in points:
                    normal = point[3:6] if first else [str(-float(x)) for x in point[3:6]]
                    wanted.append(','.join(point[:3]+normal+[point[6], self[1], other[1]]))
                matches = [(event, contacts) for event, contacts in callbacks[context+'/'+self[0]]
                           if event[:3] == [phase, other[1], fields[5]] and multiset_equal(contacts, wanted)]
                if not matches or not any(equal(','.join(event[3:6]), ','.join(fields[6:])) for event, _ in matches):
                    problems.append(dict(key=key, reason='Native/legacy contact or impulse disagreement for '+self[0]))
                else:
                    covered_callbacks.update(id(event) for event, _ in matches if equal(','.join(event[3:6]), ','.join(fields[6:])))
    for context, events in callbacks.items():
        if context.startswith('ordering/'):
            for event, _ in events:
                if event[0] != 'exit' and id(event) not in covered_callbacks:
                    problems.append(dict(key=context, reason='Legacy contact has no native pair'))
    if 'frames/5/0/bullet/body' in expected:
        previous = [-1, 0, 0, 0, 0, 0, 1, 100, 0, 0, 0, 0, 0]
        for step in range(4):
            prefix = f'frames/5/{step}/bullet'
            values = [float(x) for x in actual[prefix+'/body'].split(',')]
            delta = ','.join(str(values[i]-previous[i]) for i in range(3))
            integrated = ','.join(str(values[i+7]*.02) for i in range(3))
            if not equal(delta, integrated):
                problems.append(dict(key=prefix, reason='Far-origin motion does not integrate its velocity'))
            for fields, _ in callbacks[prefix]:
                if fields[0] != 'exit' and not equal(','.join(fields[6:]), ','.join(str(-x) for x in previous[7:10])):
                    problems.append(dict(key=prefix, reason='Far-origin callback has wrong incoming velocity'))
            previous = values
    # The far-origin control retains its complete strict report. Its quantized
    # contact coordinates and motion are a separate numeric compatibility limit.
    retained = [x for x in compare(expected, actual)['failures'] if x['key'].startswith('frames/5/')]
    return dict(unresolved=problems, large_coordinate_trace=retained)
