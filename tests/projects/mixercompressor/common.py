"""Case list and capture readers shared by the Compressor, ParamEQ and simple filter probe scripts."""
import array
import gzip
import json
from pathlib import Path
import re

FIXTURE = Path(__file__).resolve().parent
BLOCKS = 32
SUFFIXES = ('', '.master')
# Accepted ParamEQ residual (docs/COMPATIBILITY_GAPS.md), by key: Native's single-precision
# rounding of a 100 Hz peak's DC gain (about 2e-4 relative) and of four floats near the boosted
# 1 kHz peak of the sweep. The device PCM of the case after eq_startup still carries that case's
# ringing tail; its own master floats match. Only the gate admits these absolute bounds; strict
# reports keep every value.
RESIDUALS = {'eq_startup': 4e-4, 'eq_startup.master': 4e-4, 'eq_startup_group_bypass': 4e-4,
             'eq_sweep.master': 3e-5}


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
