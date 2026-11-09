#!/usr/bin/env python3
"""Complete spatial-script PCM controls, using the fixture's unchanged comparator."""
from pathlib import Path
import sys
FIXTURE = Path(__file__).resolve().parent
sys.path.insert(0, str(FIXTURE.parents[1]))
from audio_pcm_profile import main
# Complete raw reports retain these unresolved live reconnection controls. The four
# scripted clip cases must explicitly refuse the setter; component-only reconnection
# retains its earlier implementation and is a separate recorded follow-up.
SCRIPT_RECONNECT = frozenset({
    'script3d_live_bypass', 'script3d_live_bypass_initial',
    'script3d_live_bypass_after', 'script3d_live_bypass_restart',
})
COMPONENT_RECONNECT = 'script3d_live_bypass_component'


def accept(reference, candidate, report, keys, log):
    from compare import compare
    supported = {key: value for key, value in keys.items()
                 if key not in SCRIPT_RECONNECT and key != COMPONENT_RECONNECT}
    failed = compare(reference, candidate, report, supported, pcm_steps=1)
    diagnostics = log.read_text()
    for key in SCRIPT_RECONNECT:
        assert diagnostics.count('Unsupported live bypass '+key+': ') == 2, key
    for key in supported:
        assert 'Unsupported live bypass '+key+': ' not in diagnostics, key
    print('Scripted spatial chains:', len(supported), 'supported controls; complete reconnection differences retained')
    return failed


if __name__ == '__main__':
    raise SystemExit(main(FIXTURE, 'spatial-script', 'EFFECT_PROBE_', 'EffectProbe.cs', 'ScriptCases', 32, acceptance=accept))
