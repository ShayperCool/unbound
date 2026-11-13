#!/usr/bin/env python3
"""Verify the recorded six-input Stereo/Prologic availability relationship."""
import argparse
from pathlib import Path
from common import encoding_cases, SUFFIXES, header, samples
from prologic_pairs import compare_mode_pairs
from verify import verify


def compare_encoding_pairs(root, report, release, rate, device_channels=8):
    verify(root, release, rate, contexts=True, selected=encoding_cases(), device_channels=device_channels)
    pairs = [(key, key.replace('context_encoding_2_', 'context_encoding_7_', 1))
             for key in encoding_cases() if key.startswith('context_encoding_2_')]
    failed = compare_mode_pairs(root, report, release, rate, pairs, SUFFIXES+('.input',))
    if failed:
        return failed
    # All six basis inputs must reach the recorded output before identical modes
    # establish availability. A silent pair would not distinguish an encoder.
    meta = header(root)
    for mode in (2, 7):
        key = f'context_encoding_{mode}_impulses.master'
        observed = samples(root, meta[key])
        coefficients = ((1, 0), (0, 1), (0.5, 0.5), (0.5, 0.5), (0.5, 0), (0, 0.5))
        expected = [(2*(4096+2048*channel)+output, 0.25*gain)
                    for channel, gains in enumerate(coefficients)
                    for output, gain in enumerate(gains) if gain]
        assert [(i, value) for i, value in enumerate(observed) if value] == expected, key
    return 0


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('--root', type=Path, required=True)
    p.add_argument('--report', type=Path, required=True)
    p.add_argument('--release', required=True)
    p.add_argument('--rate', type=int, choices=(48000, 44100), required=True)
    p.add_argument('--device-channels', type=int, choices=(2, 4, 6, 8), default=8)
    a = p.parse_args()
    raise SystemExit(bool(compare_encoding_pairs(a.root, a.report, a.release, a.rate, a.device_channels)))
