#!/usr/bin/env python3
"""Describe all failing sample ranges and metadata alongside the complete union."""
import argparse
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'mixersuspend'))
from compare import ATOL, RTOL, MISSING, bad, header, samples


def ranges(indexes):
    result = []
    for i in indexes:
        if result and result[-1][1] == i:
            result[-1][1] = i + 1
        else:
            result.append([i, i + 1])
    return result


def summarize(reference, candidate):
    a, b = header(reference), header(candidate)
    result = dict(atol=ATOL, rtol=RTOL, interval='half-open', metadata={}, streams={})
    for key in sorted(a.keys() | b.keys()):
        x, y = a.get(key, MISSING), b.get(key, MISSING)
        if x != y:
            result['metadata'][key] = dict(native=x, candidate=y)
        file = next((v for v in (x, y) if isinstance(v, dict) and 'file' in v), None)
        if file is None:
            continue
        av = samples(reference, x if isinstance(x, dict) else file)
        bv = samples(candidate, y if isinstance(y, dict) else file)
        indexes = [i for i in range(max(len(av), len(bv)))
                   if bad(float(av[i]) if i < len(av) else MISSING,
                          float(bv[i]) if i < len(bv) else MISSING, True, False)]
        channels = file['channels']
        result['streams'][key] = dict(nativeSamples=len(av), candidateSamples=len(bv),
            failingSamples=len(indexes), sampleRanges=ranges(indexes),
            frameRanges=ranges(sorted({i // channels for i in indexes})))
    return result


def observations(root):
    m = header(root)
    result = {}
    for case in m['cases']:
        entry = dict(schedule=m[case + '.schedule'], callbacks=len(m[case + '.callbacks']),
                     naturalFinal=m[case + '.events'][-2], streams={})
        for suffix in ('output', 'master', 'child', 'input', 'emitted'):
            values = samples(root, m[case + '.' + suffix])
            entry['streams'][suffix] = dict(samples=len(values),
                minimum=min(values, default=None), maximum=max(values, default=None),
                nonzeroSampleRanges=ranges([i for i, v in enumerate(values) if v != 0]),
                untouchedSampleRanges=ranges([i for i, v in enumerate(values) if v == -3]))
        result[case] = entry
    return result


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('--reference', type=Path, required=True)
    p.add_argument('--candidate', type=Path)
    p.add_argument('--report', type=Path, required=True)
    a = p.parse_args()
    result = observations(a.reference) if a.candidate is None else summarize(a.reference, a.candidate)
    a.report.write_text(json.dumps(result, indent=2) + '\n')
