"""Strict row/slot comparison; retains every missing, extra and differing value."""
import math


def load(path):
    result = {}
    for line in path.read_text().splitlines():
        key, value = line.split('\t', 1)
        if key in result:
            raise ValueError('Duplicate key ' + key)
        result[key] = value
    if result.get('complete') != 'yes' or 'fatal' in result:
        raise ValueError('Incomplete capture ' + str(path))
    return result


def equal(expected, actual):
    if expected == actual:
        return True
    if expected is None or actual is None:
        return False
    left, right = expected.split(','), actual.split(',')
    if len(left) != len(right):
        return False
    for x, y in zip(left, right):
        if x == y:
            continue
        try:
            if math.isclose(float(x), float(y), abs_tol=1e-4, rel_tol=1e-5):
                continue
        except ValueError:
            pass
        return False
    return True


def compare(expected, actual):
    keys = expected.keys() | actual.keys()
    return dict(total=len(keys), failures=[dict(key=key, expected=expected.get(key), actual=actual.get(key))
        for key in sorted(keys) if not equal(expected.get(key), actual.get(key))])
