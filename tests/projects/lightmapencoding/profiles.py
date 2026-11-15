"""The probe's profiles: a build target, a project color space and the Player Settings lightmap and
HDR cubemap encoding qualities (None: the target's default, no entry). Each profile runs in its own
project copy; `project(...)` writes one."""
from pathlib import Path
import shutil

PROBE = Path(__file__).resolve().parent

# name: (build target group, color space, lightmap encoding quality, HDR cubemap encoding quality)
PROFILES = {
    'StandaloneLinear': ('Standalone', 'Linear', None, None),
    'StandaloneLowLinear': ('Standalone', 'Linear', 0, 0),
    'AndroidLinear': ('Android', 'Linear', None, None),
    'AndroidGamma': ('Android', 'Gamma', None, None),
    'AndroidLowLinear': ('Android', 'Linear', 0, 0),
    'AndroidNormalLinear': ('Android', 'Linear', 1, 1),
    'AndroidNormalGamma': ('Android', 'Gamma', 1, 1),
    'AndroidHighLinear': ('Android', 'Linear', 2, 2),
}


def encoding_list(key, target, quality):
    if quality is None:
        return f'  {key}: []\n'
    return f'  {key}:\n  - serializedVersion: 2\n    m_BuildTarget: {target}\n    m_EncodingQuality: {quality}\n'


def project(destination, profile, release=None):
    """Copies the probe's sources to `destination` with the profile's Player Settings."""
    target, space, lightmap, cubemap = PROFILES[profile]
    if destination.exists():
        raise SystemExit(f'refusing to overwrite project: {destination}')
    for part in ('Assets', 'Packages', 'ProjectSettings'):
        shutil.copytree(PROBE/part, destination/part)
    if release:
        (destination/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: '+release+'\n')
    settings = destination/'ProjectSettings/ProjectSettings.asset'
    text = settings.read_text()
    for key, quality in (('m_BuildTargetGroupLightmapEncodingQuality', lightmap),
                         ('m_BuildTargetGroupHDRCubemapEncodingQuality', cubemap)):
        old = f'  {key}: []\n'
        if old not in text:
            raise SystemExit(f'{settings}: no empty {key}')
        text = text.replace(old, encoding_list(key, target, quality))
    old = 'm_ActiveColorSpace: 1\n'
    if old not in text:
        raise SystemExit(f'{settings}: not in Linear color space')
    text = text.replace(old, f'm_ActiveColorSpace: {0 if space == "Gamma" else 1}\n')
    settings.write_text(text)
    return target
