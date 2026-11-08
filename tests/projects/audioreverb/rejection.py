#!/usr/bin/env python3
"""Require explicit refusal and intact dry audio for unsupported zone activation during playback."""
import argparse
import gzip
import json
from pathlib import Path
from common import FIXTURE, REJECTED_ACTIVATIONS, header, samples
from compare import bad


def gate(candidate, log, report, release, rate):
    reference = FIXTURE/'Reference'/('unity-'+release)/('rate-'+str(rate))
    native, engine = header(reference), header(candidate)
    diagnostics = log.read_text()
    tested = failed = 0
    with gzip.open(report, 'xt') as output:
        def check(key, expected, actual, index=None, sample=False):
            nonlocal tested, failed
            tested += 1
            if bad(expected, actual, sample, False):
                failed += 1
                output.write(json.dumps(dict(key=key, sample=index, expected=expected, actual=actual))+'\n')
        for case in REJECTED_ACTIVATIONS:
            check(case+'.diagnostic', True,
                  "AudioReverbZone on '"+case+"." in diagnostics and
                  any("AudioReverbZone on '"+case+"." in line and 'activating zones during playback is not supported' in line
                      for line in diagnostics.splitlines()))
            for suffix in ('', '.master'):
                check(case+suffix+'.metadata', native.get(case+suffix), engine.get(case+suffix))
            dry = samples(candidate, engine.get(case+'.master'))
            expected = samples(reference, native.get(case+'.master'))
            device = samples(candidate, engine.get(case))
            native_device = samples(reference, native.get(case))
            first_rejected_sample = 10 * 1024 * 2 if case == 'zone_lifecycle_pauseactivate_mono1' else 0
            check(case+'.master.length', len(expected), len(dry))
            check(case+'.length', len(dry), len(device))
            for i, (a, b) in enumerate(zip(expected, dry)):
                check(case+'.master', a, b, i, True)
            for i, (a, b) in enumerate(zip(dry, device)):
                if i < first_rejected_sample:
                    # Before activation this case retains its ordinary measured zone return.
                    if bad(native_device[i], b, True, False, pcm_steps=1):
                        failed += 1
                        output.write(json.dumps(dict(key=case+'.before', sample=i,
                                                     expected=native_device[i], actual=b))+'\n')
                    tested += 1
                else:
                    pcm = max(-32768, min(32767, int(a*32768)))/32768
                    check(case+'.dry', pcm, b, i)
        result = dict(tested=tested, failed=failed, cases=REJECTED_ACTIVATIONS)
        output.write(json.dumps(dict(summary=result))+'\n')
    print(json.dumps(result), flush=True)
    return failed


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--release', required=True)
    p.add_argument('--rate', type=int, choices=(48000, 44100), required=True)
    p.add_argument('--candidate', type=Path, required=True)
    p.add_argument('--log', type=Path, required=True)
    p.add_argument('--report', type=Path, required=True)
    a = p.parse_args()
    raise SystemExit(bool(gate(a.candidate, a.log, a.report, a.release, a.rate)))
