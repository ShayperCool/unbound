"""Reject incomplete, nonfinite or silent purported Echo measurements."""
import array
import json
import math
from pathlib import Path

def verify(root, release, rate):
    meta = json.loads((root/'capture.json').read_text())
    assert meta['release'] == release and meta['rate'] == rate
    source = (Path(__file__).parent/'Assets/Scripts/EchoProbe.cs').read_text()
    cases = source.split('Cases = {',1)[1].split('};',1)[0]
    import re
    for case in re.findall(r'"([a-z0-9_]+)"', cases):
        for suffix in ('', '.master', '.listener'):
            record = meta[case+suffix]
            frames = (768 if case == 'delay5000' else 96 if case.startswith('default') else 32)*1024
            assert record == dict(file=case+suffix+'.f32', frames=frames, channels=2)
            values = array.array('f'); values.frombytes((root/record['file']).read_bytes())
            assert len(values) == frames*2 and all(map(math.isfinite,values)), case+suffix
            if case not in ('silence', 'both0'): assert any(abs(v)>1e-6 for v in values), case+suffix
            else: assert all(v == 0 for v in values), case+suffix
    print('Complete finite capture:', root)
