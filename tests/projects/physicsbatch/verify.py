"""Classify the retained stress-mesh slot differences by complete hit identity.

No sorted hit order is required or written back to a capture. The strict report
stays intact. This check admits only permutations of the same unique mesh face
identities with unchanged hit fields, at the strict comparator's tolerances.
"""
from compare import equal


def verify(expected, actual, report):
    failures = report['failures']
    if not failures:
        return dict(unresolved=[], order_only=[])
    accepted = set()
    checked = 0
    for prefix in ('stress/faces/', 'stress/mixed/'):
        candidates = {key for key in expected if key.startswith(prefix) and key[len(prefix):].isdigit()}
        changed = {item['key'] for item in failures if item['key'] in candidates}
        if not changed:
            continue

        def identities(rows):
            result = {}
            ended = False
            for key in sorted(candidates, key=lambda item:int(item[len(prefix):])):
                value = rows.get(key)
                if value is None:
                    return None
                fields = value.split(',')
                if fields[0] == 'none':
                    ended = True
                    continue
                if ended:
                    return None
                identity = (fields[0], fields[-1])
                if identity in result:
                    return None
                result[identity] = value
            return result

        left, right = identities(expected), identities(actual)
        complete = left is not None and right is not None and left.keys() == right.keys()
        if complete and all(equal(left[key], right[key]) for key in left):
            accepted.update(changed)
            checked += len(left)
    return dict(unresolved=[item for item in failures if item['key'] not in accepted],
        order_only=[item for item in failures if item['key'] in accepted], checked_unique_faces=checked)
