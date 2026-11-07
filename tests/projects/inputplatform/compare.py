#!/usr/bin/env python3
"""Strict API comparison, with explicit rules for host and Game view geometry."""
import json
import math
from pathlib import Path

MACHINE = {'currentResolution', 'resolutions', 'dpi', 'mainWindowDisplayInfo', 'displayLayout'}
WARP = {f'warp.{x}.{kind}' for x in (120, 240) for kind in ('immediate', 'legacy', 'mouse')}


def read(path):
    rows = [json.loads(line) for line in Path(path).read_text().splitlines() if line.strip()]
    keys = [row['k'] for row in rows]
    if len(keys) != len(set(keys)):
        raise ValueError('duplicate records')
    if rows[-1] != {'k': 'complete', 'v': True}:
        raise ValueError('incomplete capture')
    return {row['k']: row['v'] for row in rows}


def compare(reference, candidate):
    failures = []
    for key in sorted(reference.keys() | candidate.keys()):
        if key not in reference or key not in candidate:
            failures.append(f'{key}: missing from {"reference" if key not in reference else "candidate"}')
        elif key not in MACHINE | WARP and reference[key] != candidate[key]:
            failures.append(f'{key}: expected {reference[key]!r}, got {candidate[key]!r}')
    if any(key not in candidate for key in MACHINE | WARP):
        return failures
    # These runs deliberately use a private 1280x720 Xvfb with no physical dimensions, mode
    # clock or desktop panel. Compare the measured OS rule, not this machine's primary display.
    current = candidate['currentResolution']
    if current != [1280, 720, 60, 1]:
        failures.append(f'currentResolution: private desktop should be 1280x720 at 60/1, got {current}')
    if candidate['resolutions'] != reference['resolutions']:
        failures.append(f'resolutions: measured Editor choices differ: {candidate["resolutions"]!r}')
    if candidate['dpi'] != 0:
        failures.append(f'dpi: unknown physical size must answer 0, got {candidate["dpi"]}')
    info = candidate['mainWindowDisplayInfo']
    if info != [1280, 720, 'screen', 0, 1, 0, 0, 1280, 720]:
        failures.append(f'mainWindowDisplayInfo: does not describe the private Xvfb: {info!r}')
    if candidate['displayLayout'] != [info]:
        failures.append(f'displayLayout: expected the one private display, got {candidate["displayLayout"]!r}')
    # The Editor's Game view toolbar and fit scale introduce a constant pointer offset.
    # Check the displacement and shared legacy/Input System answer without copying that offset
    # into an application whose Game view fills its window. Immediate reads keep the old state.
    for source in (reference, candidate):
        label = 'reference' if source is reference else 'candidate'
        for x in (120, 240):
            if source[f'warp.{x}.legacy'] != source[f'warp.{x}.mouse']:
                failures.append(f'{label}: warp.{x}: legacy and Input System positions differ')
        if source['warp.240.immediate'] != source['warp.120.legacy']:
            failures.append(f'{label}: warp changed legacy state synchronously')
        delta = [b - a for a, b in zip(source['warp.120.legacy'], source['warp.240.legacy'])]
        if len(delta) != 2 or any(not math.isfinite(actual) or abs(actual - expected) > 0.001 for actual, expected in zip(delta, [120, 60])):
            failures.append(f'{label}: warp displacement expected [120, 60], got {delta}')
    return failures
