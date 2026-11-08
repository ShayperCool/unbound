"""Case list and capture readers for the Distortion probe."""
import array
import gzip
import json
from pathlib import Path
import re

FIXTURE = Path(__file__).resolve().parent
BLOCKS = 32
SUFFIXES = ('', '.master')
RESIDUALS = {}


def cases():
    source = (FIXTURE/'Assets/Scripts/EffectProbe.cs').read_text()
    body = source.split('Cases = {', 1)[1].split('};', 1)[0]
    return re.findall(r'new\[\] \{ "([a-z0-9_]+)"', body)


def profile(name, rate):
    """Maps each selected case to the number of frames compared (None: all)."""
    keys = cases()
    if name == 'all': return dict.fromkeys(keys)
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


SPATIAL_PROFILES = ('spatial', 'spatial-factor0', 'spatial-factor2')


def configure_spatial_profile(project, name):
    """The factor profiles change only the documented Audio Manager Doppler Factor."""
    if name == 'spatial': return
    factor = {'spatial-factor0': 0, 'spatial-factor2': 2}[name]
    path = project/'ProjectSettings/AudioManager.asset'
    if path.exists():
        text = path.read_text()
        text, count = re.subn(r'(?m)^  Doppler Factor:.*$', '  Doppler Factor: '+str(factor), text)
        assert count == 1, path
    else:
        text = (FIXTURE/'Authored/unity-6000.0.71f1/default/ProjectSettings/AudioManager.asset').read_text()
        text, count = re.subn(r'(?m)^  Doppler Factor:.*$', '  Doppler Factor: '+str(factor), text)
        assert count == 1
    path.write_text(text)
