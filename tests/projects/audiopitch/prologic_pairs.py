#!/usr/bin/env python3
"""Check the measured Stereo/Prologic relationship in a complete reference capture."""
import argparse
import array
import gzip
import json
from pathlib import Path
from common import header, prologic_cases, samples, SUFFIXES
from verify import verify


def compare_pairs(root, report, release, rate):
    verify(root, release, rate, contexts=True, selected=prologic_cases())
    meta = header(root)
    tested = failed = 0
    report.parent.mkdir(parents=True, exist_ok=True)
    with gzip.open(report, 'xt') as output:
        for key in prologic_cases():
            if not key.startswith('context_prologic_2_'): continue
            other = key.replace('context_prologic_2_', 'context_prologic_7_', 1)
            for suffix in SUFFIXES:
                left = samples(root, meta[key+suffix])
                right = samples(root, meta[other+suffix])
                left_bits = array.array('I'); left_bits.frombytes(left.tobytes())
                right_bits = array.array('I'); right_bits.frombytes(right.tobytes())
                tested += max(len(left), len(right))
                if left_bits == right_bits: continue
                for index in range(max(len(left), len(right))):
                    if index < min(len(left), len(right)) and left_bits[index] == right_bits[index]: continue
                    failed += 1
                    output.write(json.dumps(dict(key=other+suffix, stereo_key=key+suffix, sample=index,
                        stereo=left[index] if index < len(left) else '<missing>',
                        prologic=right[index] if index < len(right) else '<missing>'))+'\n')
        result = dict(tested=tested, failed=failed, exact=True, release=release, rate=rate)
        output.write(json.dumps(dict(summary=result))+'\n')
    print(json.dumps(result), flush=True)
    return failed


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('--root', type=Path, required=True)
    p.add_argument('--report', type=Path, required=True)
    p.add_argument('--release', required=True)
    p.add_argument('--rate', type=int, choices=(48000, 44100), required=True)
    a = p.parse_args()
    raise SystemExit(bool(compare_pairs(a.root, a.report, a.release, a.rate)))
