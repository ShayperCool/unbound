"""Classify documented residuals after the unchanged comparator writes every raw failure."""
import gzip
import json
import math
from collections import Counter

# Original reset capture: one extra listener block and same-frame replay that does not
# restart in the Editor. These are unresolved device-reset behavior, not suspension.
RESET_FRAMES = 1024 * 1024
# Replay begins on the update after the block-384 request. These are the measured
# output sample positions, including that complete pending-submission block.
RESET_PULSES = {792576: 0.25, 794625: -0.125}
STATE_PCM_SAMPLE = {48000: 46619, 44100: 28553}


def records(report):
    with gzip.open(report, 'rt') as source:
        for line in source:
            row = json.loads(line)
            if 'samples' in row:
                for sample, reference, candidate in zip(row['samples'], row['reference'], row['candidate'], strict=True):
                    yield dict(key=row['key'], sample=sample, reference=reference, candidate=candidate)
            else:
                yield row


def reset_residual(row):
    key = row['key']
    if key not in ('reset', 'reset.master', 'reset.listener'):
        return False
    if 'sample' not in row:
        return (key == 'reset.listener'
                and row['reference'] == dict(file='reset.listener.f32', frames=RESET_FRAMES + 1024, channels=2)
                and row['candidate'] == dict(file='reset.listener.f32', frames=RESET_FRAMES, channels=2))
    sample, reference, candidate = row['sample'], row['reference'], row['candidate']
    if not isinstance(reference, (int, float)) or not math.isfinite(reference) or abs(reference) > 2e-5:
        return False
    if sample in RESET_PULSES:
        return candidate == RESET_PULSES[sample]
    return (key == 'reset.listener' and 2 * RESET_FRAMES <= sample < 2 * (RESET_FRAMES + 1024)
            and candidate == '<missing>')


def check(report, profile, rate):
    counts = Counter()
    unexpected = 0
    summary = None
    for row in records(report):
        if 'summary' in row:
            summary = row['summary']
            continue
        counts[row['key']] += 1
        accepted = profile == 'suspension' and reset_residual(row)
        if profile == 'state' and row['key'] == 'reverb_disabled' and row.get('sample') == STATE_PCM_SAMPLE.get(rate):
            a, b = row['reference'], row['candidate']
            accepted = (isinstance(a, (int, float)) and isinstance(b, (int, float))
                        and math.isfinite(a) and math.isfinite(b) and abs(a-b) == 1/32768)
        if not accepted:
            unexpected += 1
    if summary is None or sum(counts.values()) != summary['failed']:
        raise ValueError('incomplete comparison report: '+str(report))
    if profile == 'suspension' and summary['failed'] and counts != Counter({'reset': 2, 'reset.master': 2, 'reset.listener': 2051}):
        unexpected += 1
        print('Documented reset failure shape changed:', dict(counts), flush=True)
    if profile == 'state' and sum(counts.values()) > 1:
        unexpected += 1
    print(f'{profile} {rate}: {summary["tested"]} raw values, {summary["failed"]} raw failures, '
          f'{unexpected} unexplained; {report}', flush=True)
    return unexpected
