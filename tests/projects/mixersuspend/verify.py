"""Validate complete blocks, including silent gaps, without removing any samples."""
import array
import json
import math
import re
from pathlib import Path

def verify(root, release, rate):
    meta = json.loads((root/'capture.json').read_text())
    assert meta['release'] == release and meta['rate'] == rate
    source = (Path(__file__).parent/'Assets/Scripts/SuspendProbe.cs').read_text()
    cases = re.findall(r'"([a-z0-9_]+)"',source.split('Cases = {',1)[1].split('};',1)[0])
    blocks = int(re.search(r'const int Blocks = (\d+)',source)[1])
    expected = {'release','rate'}
    for case in cases:
        for suffix in ('','.master','.listener'):
            key=case+suffix; expected.add(key)
            record=meta[key]
            assert record==dict(file=key+'.f32',frames=blocks*1024,channels=2),key
            values=array.array('f'); values.frombytes((root/record['file']).read_bytes())
            assert len(values)==blocks*2048 and all(map(math.isfinite,values)),key
            if case != "muted": assert any(abs(v)>1e-6 for v in values),key
    assert set(meta)==expected
    print('Complete finite capture:',root)
