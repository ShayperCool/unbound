"""Case list and capture readers shared by the AudioReverbFilter/AudioReverbZone probe scripts."""
import array
import gzip
import json
from pathlib import Path
import re

FIXTURE = Path(__file__).resolve().parent
BLOCKS = 24
SUFFIXES = ('', '.master')
PROFILES = ('all', 'filter', 'zone', 'gate')


def cases():
    source = (FIXTURE/'Assets/Scripts/AudioReverbProbe.cs').read_text()
    body = source.split('Cases = {', 1)[1].split('};', 1)[0]
    return re.findall(r'new\[\] \{ "([a-z0-9_]+)"', body)


def profile(name):
    """Maps each selected case to the number of frames compared (None: all).

    filter: every case without a reverb zone; zone: the zone cases; all: both. gate: the cases the
    engine reproduces, including every recorded mid-playback activation control.
    """
    keys = cases()
    zone = [k for k in keys if k.startswith('zone')]
    if name == 'all': return dict.fromkeys(keys)
    if name == 'zone': return dict.fromkeys(zone)
    selected = dict.fromkeys(k for k in keys if k not in zone)
    if name == 'gate': selected.update(dict.fromkeys(GATE_ZONE))
    return selected


# Every recorded zone control is required, including activation during playback.
GATE_ZONE = tuple(key for key in cases() if key.startswith('zone'))
# Metadata outside the cases: the property tables of each component.
FILTER_TABLES = ('filter.', 'serialized.filter_')
ZONE_TABLES = ('zone.', 'serialized.zone_', 'serialized.reverb_zone_mix')


def tables(name):
    """The metadata key prefixes a profile compares."""
    return {'all': FILTER_TABLES + ZONE_TABLES, 'gate': FILTER_TABLES + ZONE_TABLES, 'zone': ZONE_TABLES}.get(name, FILTER_TABLES)


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
