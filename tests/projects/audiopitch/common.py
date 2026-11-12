"""Case list and capture readers for the Pitch Shifter probe."""
import array
import gzip
import json
from pathlib import Path
import re
from functools import cache

FIXTURE = Path(__file__).resolve().parent
@cache
def blocks(case):
    if case.startswith('context_wide_'):
        return int(next(token[7:] for token in wide_rows()[case] if token.startswith('blocks=')))
    source = (FIXTURE/'Assets/Scripts/EffectProbe.cs').read_text()
    row = next(line for line in source.splitlines() if 'new[] { "'+case+'"' in line)
    value = re.search(r'"blocks=(\d+)"', row)
    return int(value[1]) if value else 32
SUFFIXES = ('', '.master')
RESIDUALS = {}


@cache
def wide_rows():
    path = FIXTURE/'Assets/StreamingAssets/WideCases.tsv'
    return {row[0]: row for line in path.read_text().splitlines() if (row := line.split('\t'))}


def wide_pcm_rows():
    """Certified settings; the full wide diagnostic retains the unrepeatable pairs."""
    return {key: row for key, row in wide_rows().items()
            if not key.startswith(('context_wide_32000_340_', 'context_wide_48000_256_'))}


def all_cases():
    source = (FIXTURE/'Assets/Scripts/EffectProbe.cs').read_text()
    body = source.split('Cases = {', 1)[1].split('};', 1)[0]
    return re.findall(r'new\[\] \{ "([a-z0-9_]+)"', body) + list(wide_rows())


def cases(contexts=False):
    return [key for key in all_cases() if not key.startswith(('device_', 'context_fx_', 'context_live_', 'context_wet_', 'context_wetedge_', 'context_settings_', 'context_prologic_', 'context_zone_', 'context_monofx_', 'context_recovery_', 'context_wide_', 'context_scriptzone_')) and key.startswith('context_') == contexts]


def surround_cases():
    return [key for key in all_cases() if key.startswith('context_fx_')]


def recovery_cases():
    return [key for key in all_cases() if key.startswith('context_recovery_')]


def listener_script_cases():
    return [key for key in all_cases() if key.startswith('context_scriptzone_')]


def mono_cases():
    return [key for key in all_cases() if key.startswith('context_monofx_')]


def zone_cases():
    return [key for key in all_cases() if key.startswith('context_zone_')]


def prologic_cases():
    return [key for key in all_cases() if key.startswith('context_prologic_')]


def context_sessions(name):
    """Cases whose output configurations need separate processes on both backends."""
    if name == 'settings': return settings_sessions()
    if name in ('wide', 'wide-pcm'):
        sessions = {}
        for key in (wide_pcm_rows() if name == 'wide-pcm' else wide_rows()):
            sessions.setdefault('_'.join(key.split('_')[2:5]), []).append(key)
        return sessions
    if name == 'prologic':
        return {str(mode): [key for key in prologic_cases() if key.startswith(f'context_prologic_{mode}_')] for mode in (2, 7)}
    return {'': list(profile(name, 48000))}


def settings_cases():
    return [key for key in all_cases() if key.startswith('context_settings_')]


def settings_sessions():
    """Static settings and speaker widths run in separate processes on both backends."""
    sessions = {}
    for key in settings_cases():
        session = '_'.join(key.split('_')[2:5])
        sessions.setdefault(session, []).append(key)
    return sessions


def wet_edge_cases():
    return [key for key in all_cases() if key.startswith('context_wetedge_')]


def wet_cases():
    return [key for key in all_cases() if key.startswith('context_wet_')]


def transition_cases():
    return [key for key in all_cases() if key.startswith('context_live_') and not key.startswith('context_live_reset_')]


def scheduled_reset_cases():
    return [key for key in all_cases() if key.startswith('context_live_') and (not key.startswith('context_live_reset_') or key.endswith('_scheduled'))]


def reset_cases():
    return [key for key in all_cases() if key.startswith('context_live_') and not key.endswith('_scheduled')]


def devices():
    """Device controls: (key, output channels of its private device), recorded at 48000 Hz only."""
    return [(key, int(key.split('_')[1])) for key in all_cases() if key.startswith('device_')]


def profile(name, rate):
    """Maps each selected case to the number of frames compared (None: all)."""
    keys = cases()
    if name == 'all': return dict.fromkeys(keys)
    if name == 'layouts': return dict.fromkeys(cases(True))
    if name == 'surround': return dict.fromkeys(surround_cases())
    if name == 'settings': return dict.fromkeys(settings_cases())
    if name == 'wide': return dict.fromkeys(wide_rows())
    if name == 'wide-pcm': return dict.fromkeys(wide_pcm_rows())
    if name == 'prologic': return dict.fromkeys(prologic_cases())
    if name == 'zones': return dict.fromkeys(zone_cases())
    if name == 'listener-scripts': return dict.fromkeys(listener_script_cases())
    if name == 'mono': return dict.fromkeys(mono_cases())
    if name == 'recovery': return dict.fromkeys(recovery_cases())
    if name == 'wet-edges': return dict.fromkeys(wet_edge_cases())
    if name == 'wet': return dict.fromkeys(wet_cases())
    if name == 'transitions': return dict.fromkeys(transition_cases())
    if name == 'scheduled-resets': return dict.fromkeys(scheduled_reset_cases())
    if name == 'resets': return dict.fromkeys(reset_cases())
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

