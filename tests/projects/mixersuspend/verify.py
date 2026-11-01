"""Validate complete blocks, including silent gaps, without removing any samples."""
import array
import json
import math
import re
from compare import raw
from pathlib import Path

def verify(root, release, rate):
    meta = json.loads((root/'capture.json').read_text())
    assert meta['release'] == release and meta['rate'] == rate
    source_file = root.parent/'project/Assets/Scripts/SuspendProbe.cs'
    if not source_file.exists(): source_file=Path(__file__).parent/'Assets/Scripts/SuspendProbe.cs'
    source=source_file.read_text()
    cases = re.findall(r'"([a-z0-9_]+)"',source.split('Cases = {',1)[1].split('};',1)[0])
    blocks = int(re.search(r'const int Blocks = (\d+)',source)[1])
    expected = {'release','rate'}
    for case in cases:
        for suffix in ('','.master','.listener'):
            key=case+suffix; expected.add(key)
            record=meta[key]
            frames=record['frames']
            assert record==dict(file=key+'.f32',frames=frames,channels=2),key
            assert frames==blocks*1024 or case=='reset' and suffix=='.listener' and frames>=0 and frames%1024==0,key
            values=array.array('f'); values.frombytes(raw(root,record))
            assert len(values)==frames*2 and all(map(math.isfinite,values)),key
            if case != "muted": assert any(abs(v)>1e-6 for v in values),key
    assert set(meta)==expected
    print('Complete finite capture:',root)
