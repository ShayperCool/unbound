import json
import math
import struct
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'mixersuspend'))
from compare import raw


def verify(root, release):
    meta = json.loads((root / 'capture.json').read_text())
    assert set(meta) == {'release', 'rate', 'complete', 'steps', 'output', 'master', 'trace'}
    assert meta['release'] == release and meta['rate'] == 48000 and meta['complete'] is True
    expected = ['noPlay.' + str(i) for i in range(3)]
    expected += ['play.immediate.' + str(i) for i in range(2)]
    expected += ['play.updated.' + str(i) for i in range(3)]
    expected += ['amplitude.' + str(i) for i in range(3)]
    for state in ('pause', 'unpause', 'stop', 'replay'):
        expected += [state + '.immediate', state + '.updated.0', state + '.updated.1']
    assert meta['steps'] == expected
    for key in ('output', 'master', 'trace'):
        frames, channels = (len(expected), 9) if key == 'trace' else (len(expected) * 1024, 2)
        assert meta[key] == dict(file=key + '.f32', frames=frames, channels=channels)
        data = raw(root, meta[key])
        assert len(data) == frames * channels * 4
        assert all(math.isfinite(v) for (v,) in struct.iter_unpack('<f', data))
