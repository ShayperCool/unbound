#!/usr/bin/env python3
"""Check whether device PCM is exactly the saturating truncation of each master sample."""
import argparse
import gzip
import json
import math
from pathlib import Path
from common import FIXTURE, cases, header, samples


def compare(root, release, rate, report):
    meta = header(root)
    assert meta['release'] == release and meta['rate'] == rate
    tested = failed = 0
    report.parent.mkdir(parents=True, exist_ok=True)
    with gzip.open(report, 'xt') as output:
        for case in cases():
            master = samples(root, meta[case + '.master'])
            device = samples(root, meta[case])
            assert len(master) == len(device) == 65536, case
            for index, (value, actual) in enumerate(zip(master, device)):
                assert math.isfinite(value) and math.isfinite(actual), (case, index)
                expected = max(-32768, min(32767, math.trunc(value * 32768))) / 32768
                tested += 1
                if actual != expected:
                    failed += 1
                    output.write(json.dumps(dict(case=case, sample=index, master=value,
                                                 expected=expected, actual=actual)) + '\n')
        output.write(json.dumps(dict(summary=dict(tested=tested, failed=failed))) + '\n')
    print(f'PCM quantization: {tested} samples, {failed} differences; {report}', flush=True)
    return bool(failed)


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--release', required=True)
    p.add_argument('--rate', type=int, choices=(48000, 44100), required=True)
    p.add_argument('--capture', type=Path)
    p.add_argument('--report', type=Path, required=True)
    a = p.parse_args()
    root = a.capture or FIXTURE / 'Reference' / ('unity-' + a.release) / ('rate-' + str(a.rate))
    raise SystemExit(compare(root, a.release, a.rate, a.report))
