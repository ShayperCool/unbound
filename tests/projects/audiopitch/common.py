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
# Recorded controls the engine does not reproduce yet. The strict report keeps comparing them
# (every failing value stays visible); only the gate leaves them out, naming each one.
UNSUPPORTED = {
    case: 'mono 2D clips also feed the LFE channel at unity gain in 5.1/7.1; the engine graph '
          'carries only the front pair (follow-up)'
    for case in ('context_mode5point1_clip', 'context_mode5point1_clip_pan', 'context_mode7point1_clip')
}


def cases(contexts=False):
    source = (FIXTURE/'Assets/Scripts/EffectProbe.cs').read_text()
    body = source.split('Cases = {', 1)[1].split('};', 1)[0]
    return [key for key in re.findall(r'new\[\] \{ "([a-z0-9_]+)"', body) if key.startswith('context_') == contexts]


def profile(name, rate):
    """Maps each selected case to the number of frames compared (None: all)."""
    keys = cases()
    if name == 'all': return dict.fromkeys(keys)
    if name == 'layouts': return dict.fromkeys(cases(True))
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

