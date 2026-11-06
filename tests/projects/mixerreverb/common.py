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


def profile(name):
    """steady: signals after the first DSP block; startup: impulses inside it (all = both)."""
    keys = cases()
    if name == 'all': return keys
    startup = [k for k in keys if k.startswith('startup')]
    return startup if name == 'startup' else [k for k in keys if k not in startup]


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
