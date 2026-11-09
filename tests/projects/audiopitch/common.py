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


def split_contexts(root):
    """Keep the measured multichannel controls separate until the mixer graph supports them."""
    meta = header(root)
    context = {key: value for key, value in meta.items() if key in ('release', 'rate') or key.startswith('context_')}
    target = root.parent/'contexts'
    target.mkdir(exist_ok=False)
    for key, value in context.items():
        if isinstance(value, dict) and 'file' in value:
            (root/value['file']).rename(target/value['file'])
    (target/'capture.json').write_text(json.dumps(context, indent=2)+'\n')
    (root/'capture.json').write_text(json.dumps({key: value for key, value in meta.items() if not key.startswith('context_')}, indent=2)+'\n')
