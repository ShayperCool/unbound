#!/usr/bin/env python3
"""Complete unscaled PCM controls, using the fixture's unchanged comparator."""
from pathlib import Path
import sys
FIXTURE = Path(__file__).resolve().parent
sys.path.insert(0, str(FIXTURE.parents[1]))
from audio_pcm_profile import main
if __name__ == '__main__':
    raise SystemExit(main(FIXTURE, 'unscaled', 'DUCK_PROBE_', 'DuckProbe.cs', 'UnscaledCases', 32))
