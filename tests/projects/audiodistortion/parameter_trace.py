"""Exact public parameter/time trace for the live Pitch snapshot control."""
import json
import re
import struct
from common import FIXTURE

CASE = 'pitch_pitch_timed'


def read_trace(log):
    rows = []
    for line in log.read_text(errors='replace').splitlines():
        match = re.search(r'PARAMETER '+CASE+r' (\d+) (\S+) (\S+) (\S+)', line)
        if match:
            rows.append([int(match[1]), *[struct.unpack('<f', struct.pack('<f', float(match[i])))[0] for i in (2, 3, 4)]])
    assert [row[0] for row in rows] == list(range(24)), log
    return rows


def verify(log, release, rate):
    reference = FIXTURE/'Reference'/('unity-'+release)/f'live-parameter-trace-{rate}.json'
    expected = json.loads(reference.read_text())
    actual = dict(case=CASE, columns=['block', 'value', 'deltaTime', 'captureDeltaTime'], values=read_trace(log))
    assert actual == expected, (reference, log, actual)
    print('Exact live parameter/time trace:', log)
