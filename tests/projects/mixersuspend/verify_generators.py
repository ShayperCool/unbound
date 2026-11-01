import json
import math
import struct
from compare import raw


def verify(root, release, rate):
    meta = json.loads((root / 'capture.json').read_text())
    assert set(meta) == {'release', 'rate', 'complete', 'attached', 'attached.master', 'late', 'late.master'}
    assert meta['release'] == release and meta['rate'] == rate and meta['complete'] is True
    for key in ('attached', 'attached.master', 'late', 'late.master'):
        item = meta[key]
        assert item == {'file': key + '.f32', 'frames': 88 * 1024, 'channels': 2}
        data = raw(root, item)
        assert len(data) == 88 * 1024 * 2 * 4
        assert all(math.isfinite(value) for (value,) in struct.iter_unpack('<f', data))
