"""Case list and capture readers for the Pitch Shifter probe."""
import array
import gzip
import json
from pathlib import Path
import re

FIXTURE = Path(__file__).resolve().parent
def blocks(case):
    source = (FIXTURE/'Assets/Scripts/EffectProbe.cs').read_text()
    row = next(line for line in source.splitlines() if 'new[] { "'+case+'"' in line)
    value = re.search(r'"blocks=(\d+)"', row)
    return int(value[1]) if value else 32
SUFFIXES = ('', '.master')
RESIDUALS = {}
# Recorded channels the engine does not produce yet, as (channel index, reason). The strict
# report compares them as recorded; the gate expects exactly that channel's absence (silence)
# and compares every other value of the control as usual.
LFE = 3
UNSUPPORTED = {
    case: (LFE, 'mono 2D clips also feed the LFE channel at unity gain in 5.1/7.1; the engine '
                'graph carries only the front pair (follow-up)')
    for case in ('context_mode5point1_clip', 'context_mode5point1_clip_pan', 'context_mode7point1_clip')
}


def all_cases():
    source = (FIXTURE/'Assets/Scripts/EffectProbe.cs').read_text()
    body = source.split('Cases = {', 1)[1].split('};', 1)[0]
    return re.findall(r'new\[\] \{ "([a-z0-9_]+)"', body)


def cases(contexts=False):
    return [key for key in all_cases() if not key.startswith('device_') and key.startswith('context_') == contexts]


def devices():
    """Device controls: (key, output channels of its private device), recorded at 48000 Hz only."""
    return [(key, int(key.split('_')[1])) for key in all_cases() if key.startswith('device_')]


def profile(name, rate):
    """Maps each selected case to the number of frames compared (None: all)."""
    keys = cases()
    if name == 'all': return dict.fromkeys(keys)
    if name == 'layouts': return dict.fromkeys(cases(True))
    if name == 'devices': return dict.fromkeys(key for key, _ in devices())
    raise ValueError(name)


def header(root):
    file = root/'capture.json'
    return json.loads(file.read_text()) if file.exists() else {}


def samples(root, meta):
    """Reads `file` raw or as the gzip copy published in Reference/."""
    values = array.array('f')
    if isinstance(meta, dict) and 'file' in meta:
        raw, packed = root/meta['file'], root/(meta['file']+'.gz')
        if raw.exists(): values.frombytes(raw.read_bytes())
        elif packed.exists(): values.frombytes(gzip.decompress(packed.read_bytes()))
    return values

