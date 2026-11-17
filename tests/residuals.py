"""Recorded residuals: the differences a probe's gate admits, each listed with its bounds and reason.

A residual list is a JSON object, one entry per comparison key:

    {"<key>": {"<measure>": <bound>, ..., "reason": "<cause, scope, where measured>"}}

A comparator measures each key that differs from the reference (an image's differing "pixels" and
"worst" difference, a value's largest absolute difference "max_abs", its "actual" text, ...). The
gate admits a difference only when the list names its key and bounds every measure the comparator
took: a number bounds a number from above, anything else, and a value measure ("actual",
"capture", "candidate", "missing", "pinned") whatever its type, must be equal. The strict comparison,
its counts and its report stay as they are; the verdict is separate:

* a key that differs and is not listed, or goes beyond a bound, fails;
* a listed key the reference does not have fails (a stale entry);
* a listed key that now matches is named (NOW MATCHES) so that its entry can be withdrawn.
"""
import json
import math
from numbers import Real


class ResidualListError(ValueError):
    pass


def load(path):
    """The residual list at path (or an empty one for None), checked."""
    if path is None:
        return {}
    with open(path) as handle:
        entries = json.load(handle)
    if not isinstance(entries, dict):
        raise ResidualListError(f'{path}: a residual list is a JSON object of entries by key')
    for key, entry in entries.items():
        if not isinstance(entry, dict):
            raise ResidualListError(f'{path}: {key}: an entry is an object of bounds and a reason')
        reason = entry.get('reason')
        if not isinstance(reason, str) or not reason.strip():
            raise ResidualListError(f'{path}: {key}: an entry needs a nonempty reason')
        if len(entry) < 2:
            raise ResidualListError(f'{path}: {key}: an entry needs at least one bound')
    return entries


# Measures that are values, not sizes: they must equal the recorded one whatever their type.
VALUES = {'actual', 'capture', 'candidate', 'missing', 'pinned'}


def within(measured, bound, name=None):
    if name in VALUES:
        return measured == bound
    if isinstance(bound, Real) and not isinstance(bound, bool):
        return (isinstance(measured, Real) and not isinstance(measured, bool)
                and not math.isnan(measured) and measured <= bound)
    return measured == bound


def admits(entry, measured):
    """Whether a residual entry admits a measured difference: it bounds every measure taken (a
    number from above, anything else by equality)."""
    if entry is None or not measured:
        return False
    bounds = {k: v for k, v in entry.items() if k != 'reason'}
    return set(measured) <= set(bounds) and all(within(measured[k], bounds[k], k) for k in measured)


class Gate:
    """One comparison's verdict against a residual list. A comparator reports every key of the
    reference as exact(), differs() or missing() and then calls finish()."""

    def __init__(self, residuals):
        self.residuals = residuals
        self.seen = set()
        self.exact = 0
        self.recorded = []      # (key, measured, entry)
        self.failed = []        # (key, measured or None, entry or None, text or None)
        self.now_matching = []

    def exact_key(self, key):
        self.seen.add(key)
        self.exact += 1
        if key in self.residuals:
            self.now_matching.append(key)

    def differs(self, key, measured):
        """True when a listed residual admits the measured difference."""
        self.seen.add(key)
        entry = self.residuals.get(key)
        if admits(entry, measured):
            self.recorded.append((key, measured, entry))
            return True
        self.failed.append((key, measured, entry, None))
        return False

    def missing(self, key, text):
        self.seen.add(key)
        self.failed.append((key, None, None, text))

    def finish(self, reference_keys, noun='key'):
        for key in sorted(set(self.residuals) - set(reference_keys)):
            self.failed.append((key, None, None, f'a recorded residual for {"an" if noun[0] in "aeiou" else "a"} {noun} the reference does not have'))

    @property
    def ok(self):
        return not self.failed

    def lines(self, describe=None, bounds=None):
        """Every recorded, now-matching and failing key, then the summary line. describe(measured)
        and bounds(entry) word a comparator's measures (default: each measure and its value)."""
        def show(values):
            return ', '.join(f'{k} {json.dumps(v)}' for k, v in values.items() if k != 'reason')
        describe = describe or show
        bounds = bounds or (lambda entry: 'recorded ' + show(entry))
        out = []
        for key, measured, entry in self.recorded:
            out.append(f'recorded {key}: {describe(measured)} ({entry["reason"]})')
        for key in self.now_matching:
            out.append(f'NOW MATCHES {key}: its recorded residual can be withdrawn')
        for key, measured, entry, text in self.failed:
            if text is None:
                text = f'{describe(measured)} ({bounds(entry) if entry else "not recorded"})'
            out.append(f'FAIL {key}: {text}')
        out.append(f'gate: {self.exact} exact, {len(self.recorded)} within recorded residuals, {len(self.failed)} failed')
        return out
