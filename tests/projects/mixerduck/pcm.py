#!/usr/bin/env python3
"""Check device PCM against exact saturating truncation of its pre-quantization float mix."""
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
            # An earlier mixer's effect tail is audible on the device too. New placement
            # controls record the complete listener mix alongside the unchanged group sink.
            source = case + ('.listener' if case + '.listener' in meta else '.master')
            mix = samples(root, meta[source])
            device = samples(root, meta[case])
            assert len(mix) == len(device) == 65536, case
            for index, (value, actual) in enumerate(zip(mix, device)):
                assert math.isfinite(value) and math.isfinite(actual), (case, index)
                expected = max(-32768, min(32767, math.trunc(value * 32768))) / 32768
                tested += 1
                if actual != expected:
                    failed += 1
                    output.write(json.dumps(dict(case=case, sample=index, source=source, mix=value,
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
