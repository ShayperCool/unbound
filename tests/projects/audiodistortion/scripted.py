#!/usr/bin/env python3
"""Complete spatial-script PCM controls, using the fixture's unchanged comparator."""
from pathlib import Path
import sys
FIXTURE = Path(__file__).resolve().parent
sys.path.insert(0, str(FIXTURE.parents[1]))
from audio_pcm_profile import main


def accept(reference, candidate, report, keys, log):
    from compare import compare
    failed = compare(reference, candidate, report, keys, pcm_steps=1)
    # Live bypass changes reconnect the filter chain; none may be refused.
    assert 'Unsupported live bypass ' not in log.read_text()
    print('Scripted spatial chains:', len(keys), 'controls, including live bypass reconnection')
    return failed


if __name__ == '__main__':
    raise SystemExit(main(FIXTURE, 'spatial-script', 'EFFECT_PROBE_', 'EffectProbe.cs', 'ScriptCases', 32, acceptance=accept))
