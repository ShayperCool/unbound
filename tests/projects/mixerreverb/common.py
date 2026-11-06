"""Case list and capture readers shared by the SFX Reverb probe scripts."""
import array
import gzip
import json
from pathlib import Path
import re

FIXTURE = Path(__file__).resolve().parent
BLOCKS = 24
SUFFIXES = ('', '.master')


def cases():
    source = (FIXTURE/'Assets/Scripts/ReverbProbe.cs').read_text()
    body = source.split('Cases = {', 1)[1].split('};', 1)[0]
    return re.findall(r'new\[\] \{ "([a-z0-9_]+)"', body)


def profile(name, rate):
    """Maps each selected case to the number of frames compared (None: all).

    steady: every case except `startup*`; startup: only those; all: both. gate: steady, plus the
    startup cases before the first reverb arrival of their in-block impulses (the impulse at frame 1
    reaches the first early tap at frame floor(0.02 * rate)), which still covers their dry path,
    group fade and wet-mix ramp.
    """
    keys = cases()
    startup = [k for k in keys if k.startswith('startup')]
    if name == 'all': return dict.fromkeys(keys)
    if name == 'startup': return dict.fromkeys(startup)
    selected = {k: None for k in keys if k not in startup}
    if name == 'gate': selected.update(dict.fromkeys(startup, int(0.02 * rate)))
    return selected


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
