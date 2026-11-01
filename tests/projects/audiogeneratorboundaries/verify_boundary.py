"""Check completeness without imposing a scheduling or mixing interpretation."""
import json
import math
from pathlib import Path
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'mixersuspend'))
from compare import raw

CASES = ['idle', 'shotPlain', 'shotFilter', 'generator', 'generatorShot',
         'startInside', 'endInside', 'windowInside',
         'startClipInside', 'endClipInside', 'windowClipInside']


def verify(root, release):
    meta = json.loads((root / 'capture.json').read_text())
    assert meta['release'] == release and meta['rate'] == 48000 and meta['complete'] is True
    assert meta['layout'] == dict(dspBufferSize=1024, speakerMode='Stereo')
    assert meta['cases'] in (CASES, ['shotFilter', 'windowInside'])
    expected = {'release', 'rate', 'complete', 'layout', 'cases'}
    for key in meta['cases']:
        blocks = 88 if key.endswith('Inside') else 12
        events = meta[key + '.events']
        assert [(e['phase'], e['block']) for e in events] == [
            ('beforeSubmit', -1), ('afterSubmit', -1),
            *[(phase, b) for b in range(blocks) for phase in ('beforeRender', 'afterRender')],
            ('naturalFinal', blocks), ('afterStop', blocks)]
        assert all(set(e) == {'phase', 'block', 'dsp', 'playing', 'virtual', 'timeSamples', 'calls', 'listenerCalls'} for e in events)
        calls = meta[key + '.callbacks']
        assert len(calls) == events[-2]['calls']
        total = sum(c['length'] for c in calls)
        for suffix in ('output', 'master', 'child', 'input', 'emitted'):
            stream = key + '.' + suffix
            expected.add(stream)
            frames, channels = (total, 1) if suffix in ('input', 'emitted') else (blocks * 1024, 2)
            assert meta[stream] == dict(file=stream + '.f32', frames=frames, channels=channels)
            data = raw(root, meta[stream])
            assert len(data) == frames * channels * 4
            assert all(math.isfinite(v) for (v,) in struct.iter_unpack('<f', data))
        for suffix in ('events', 'callbacks', 'schedule'):
            expected.add(key + '.' + suffix)
    assert set(meta) == expected, set(meta) ^ expected
