#!/usr/bin/env python3
"""Writes Assets/Resources/M: materials saved in states the Editor's loading, validation and URP's
material upgrade treat differently, made from the URP Lit material the Editor wrote
(Assets/Settings/Lit.mat) and a Standard material of tests/projects/materialvalidation."""
from pathlib import Path

ROOT = Path(__file__).resolve().parent
OUT = ROOT/'Assets/Resources/M'
LIT = (ROOT/'Assets/Settings/Lit.mat').read_text()
STANDARD = (ROOT.parent/'materialvalidation/Assets/Resources/EmissionRealtimeNoKeyword.mat').read_text()
GUIDS = {
    'LitClean': 'c1a0c0de000000000000000000000001',
    'LitStale': 'c1a0c0de000000000000000000000002',
    'LitStaleEdited': 'c1a0c0de000000000000000000000003',
    'LitV9': 'c1a0c0de000000000000000000000004',
    'LitNoVersion': 'c1a0c0de000000000000000000000005',
    'StandardStale': 'c1a0c0de000000000000000000000006',
    'LitPassesAddedEdited': 'c1a0c0de000000000000000000000007',
    'LitOpaquePassesEdited': 'c1a0c0de000000000000000000000008',
    'PassNamesEdited': 'c1a0c0de000000000000000000000009',
}


def named(text, name, old='Lit'):
    return text.replace(f'  m_Name: {old}\n', f'  m_Name: {name}\n', 1)


def stale(text):
    """A transparent Lit material saved the way an older URP left it: no alpha blend factors and
    its disabled passes in their old order and spelling (validation sets both)."""
    for old, new in [
        ('  m_CustomRenderQueue: -1\n', '  m_CustomRenderQueue: 3000\n'),
        ('    RenderType: Opaque\n', '    RenderType: Transparent\n'),
        ('  disabledShaderPasses:\n  - MOTIONVECTORS\n',
         '  disabledShaderPasses:\n  - SHADOWCASTER\n  - DepthOnly\n  - MOTIONVECTORS\n'),
        ('    - _DstBlendAlpha: 0\n', ''),
        ('    - _SrcBlendAlpha: 1\n', ''),
        ('    - _Surface: 0\n', '    - _Surface: 1\n'),
        ('    - _DstBlend: 0\n', '    - _DstBlend: 10\n'),
        ('    - _SrcBlend: 1\n', '    - _SrcBlend: 5\n'),
        ('    - _ZWrite: 1\n', '    - _ZWrite: 0\n'),
    ]:
        assert old in text, old
        text = text.replace(old, new, 1)
    return text


def passes(text, names):
    """The material with these disabled passes saved, in this order."""
    start = text.index('  disabledShaderPasses:')
    end = text.index('  m_LockedProperties:')
    listed = ''.join(f'  - {name}\n' for name in names)
    return text[:start] + ('  disabledShaderPasses:\n' + listed if names else '  disabledShaderPasses: []\n') + text[end:]


def without_version(text):
    start, end = text.index('--- !u!114'), text.index('--- !u!21')
    return text[:start] + text[end:]


MATERIALS = {
    'LitClean': named(LIT, 'LitClean'),
    'LitStale': stale(named(LIT, 'LitStale')),
    'LitStaleEdited': stale(named(LIT, 'LitStaleEdited')),
    'LitV9': named(LIT, 'LitV9').replace('  version: 10\n', '  version: 9\n', 1),
    'LitNoVersion': without_version(named(LIT, 'LitNoVersion')),
    'StandardStale': named(STANDARD, 'StandardStale', 'EmissionRealtimeNoKeyword'),
    # Validation disables passes the list does not hold, and enables passes it holds.
    'LitPassesAddedEdited': passes(stale(named(LIT, 'LitPassesAddedEdited')), []),
    'LitOpaquePassesEdited': passes(named(LIT, 'LitOpaquePassesEdited'), ['SHADOWCASTER', 'DepthOnly', 'MOTIONVECTORS']),
    # The probe disables and enables passes by script (the names' spelling and order as written).
    'PassNamesEdited': passes(named(LIT, 'PassNamesEdited'), []),
}


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for name, text in MATERIALS.items():
        assert f'  m_Name: {name}\n' in text, name
        (OUT/f'{name}.mat').write_text(text)
        (OUT/f'{name}.mat.meta').write_text(
            f'fileFormatVersion: 2\nguid: {GUIDS[name]}\nNativeFormatImporter:\n  externalObjects: {{}}\n'
            '  mainObjectFileID: 2100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n')


if __name__ == '__main__':
    main()
