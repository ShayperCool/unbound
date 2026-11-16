#!/usr/bin/env python3
"""Writes Assets/Resources: the probe's materials, each saved in a state its shader's editor
validates (keywords, render queue, render states and GI flags that do not follow its properties),
a 4x4 texture they reference, and the metas of everything under Assets.

    python3 tests/projects/materialvalidation/make_materials.py
"""
import hashlib
import struct
import zlib
from pathlib import Path

PROBE = Path(__file__).resolve().parent
ASSETS = PROBE/'Assets'
BUILTIN = '0000000000000000f000000000000000'


def guid(name):
    return hashlib.md5(('materialvalidation/' + name).encode()).hexdigest()


TEXTURE = guid('Resources/ProbeTexture.png')
SHADERS = {name: guid('Shaders/' + name + '.shader') for name in ('ProbeValidated', 'ProbeNamespaced', 'ProbePlain')}

STANDARD_FLOATS = dict(_BumpScale=1, _Cutoff=0.5, _DetailNormalMapScale=1, _DstBlend=0, _GlossMapScale=1,
                       _Glossiness=0.5, _GlossyReflections=1, _Metallic=0, _Mode=0, _OcclusionStrength=1,
                       _Parallax=0.02, _SmoothnessTextureChannel=0, _SpecularHighlights=1, _SrcBlend=1,
                       _UVSec=0, _ZWrite=1)
STANDARD_TEXTURES = ('_BumpMap', '_DetailAlbedoMap', '_DetailMask', '_DetailNormalMap', '_EmissionMap', '_MainTex',
                     '_MetallicGlossMap', '_OcclusionMap', '_ParallaxMap')
SPECULAR_TEXTURES = tuple(t if t != '_MetallicGlossMap' else '_SpecGlossMap' for t in STANDARD_TEXTURES)
RED = (1, 0.5, 0.25, 1)
BLACK = (0, 0, 0, 1)

# name: shader (fileID, guid), its texture slots, and what differs from a consistent opaque material.
CASES = {
    'EmissionRealtimeNoKeyword': dict(colors={'_EmissionColor': RED}, flags=1),
    'EmissionBakedNoKeyword': dict(colors={'_EmissionColor': RED}, flags=2),
    'EmissionNoneNoKeyword': dict(colors={'_EmissionColor': RED}, flags=0),
    'EmissionBothNoKeyword': dict(colors={'_EmissionColor': RED}, flags=3),
    'BlackBakedKeyword': dict(colors={'_EmissionColor': BLACK}, flags=2, keywords=['_EMISSION']),
    'BlackRealtimeKeyword': dict(colors={'_EmissionColor': BLACK}, flags=1, keywords=['_EMISSION']),
    'EmissiveIsBlackKeyword': dict(colors={'_EmissionColor': RED}, flags=4, keywords=['_EMISSION']),
    'BlackBakedFlagsSix': dict(colors={'_EmissionColor': BLACK}, flags=6),
    'CutoutLowQueue': dict(floats={'_Mode': 1}, queue=2000),
    'FadeQueueInRange': dict(floats={'_Mode': 2}, queue=3100),
    'TransparentShaderQueue': dict(floats={'_Mode': 3}),
    'OpaqueHighQueue': dict(queue=2500, tags={'RenderType': 'Transparent'}, floats={'_SrcBlend': 5, '_ZWrite': 0},
                            keywords=['_ALPHABLEND_ON']),
    'NormalMapNoKeyword': dict(textures={'_BumpMap': TEXTURE}),
    'DetailNormalNoKeyword': dict(textures={'_DetailNormalMap': TEXTURE}),
    'StrayKeywords': dict(keywords=['_PARALLAXMAP', '_DETAIL_MULX2', '_METALLICGLOSSMAP', '_NORMALMAP', 'PROBE_OTHER']),
    'MetallicMapNoKeyword': dict(textures={'_MetallicGlossMap': TEXTURE}),
    'SmoothnessAlbedo': dict(floats={'_SmoothnessTextureChannel': 1}),
    'SpecularMapNoKeyword': dict(shader=(45, BUILTIN), slots=SPECULAR_TEXTURES, textures={'_SpecGlossMap': TEXTURE},
                                 colors={'_SpecColor': (0.2, 0.2, 0.2, 1)}, keywords=['_METALLICGLOSSMAP']),
    'ProjectGui': dict(shader=(4800000, SHADERS['ProbeValidated']), slots=(), plain=True, floats={'_Value': 1}),
    'ProjectGuiNamespaced': dict(shader=(4800000, SHADERS['ProbeNamespaced']), slots=(), plain=True, floats={'_Value': 1}),
    'NoGui': dict(shader=(4800000, SHADERS['ProbePlain']), slots=(), plain=True, floats={'_Value': 1},
                  keywords=['PROBE_OTHER'], queue=2600, colors={'_EmissionColor': RED}, flags=1),
}


def material(name, case):
    shader = case.get('shader', (46, BUILTIN))
    floats = {} if case.get('plain') else dict(STANDARD_FLOATS)
    floats.update(case.get('floats', {}))
    colors = {} if case.get('plain') else {'_Color': (1, 1, 1, 1), '_EmissionColor': BLACK}
    colors.update(case.get('colors', {}))
    textures = {slot: None for slot in case.get('slots', STANDARD_TEXTURES)}
    textures.update(case.get('textures', {}))
    keywords = case.get('keywords', [])
    tags = case.get('tags', {})
    out = ['%YAML 1.1', '%TAG !u! tag:unity3d.com,2011:', '--- !u!21 &2100000', 'Material:',
           '  serializedVersion: 8', '  m_ObjectHideFlags: 0', '  m_CorrespondingSourceObject: {fileID: 0}',
           '  m_PrefabInstance: {fileID: 0}', '  m_PrefabAsset: {fileID: 0}', f'  m_Name: {name}',
           f'  m_Shader: {{fileID: {shader[0]}, guid: {shader[1]}, type: {0 if shader[1] == BUILTIN else 3}}}',
           '  m_Parent: {fileID: 0}', '  m_ModifiedSerializedProperties: 0']
    out.append('  m_ValidKeywords:' + ('' if keywords else ' []'))
    out += [f'  - {k}' for k in keywords]
    out += ['  m_InvalidKeywords: []', f'  m_LightmapFlags: {case.get("flags", 4)}', '  m_EnableInstancingVariants: 0',
            '  m_DoubleSidedGI: 0', f'  m_CustomRenderQueue: {case.get("queue", -1)}']
    out.append('  stringTagMap:' + ('' if tags else ' {}'))
    out += [f'    {k}: {v}' for k, v in tags.items()]
    out += ['  disabledShaderPasses: []', '  m_LockedProperties: ', '  m_SavedProperties:', '    serializedVersion: 3']
    out.append('    m_TexEnvs:' + ('' if textures else ' []'))
    for slot, texture in sorted(textures.items()):
        ref = f'{{fileID: 2800000, guid: {texture}, type: 3}}' if texture else '{fileID: 0}'
        out += [f'    - {slot}:', f'        m_Texture: {ref}', '        m_Scale: {x: 1, y: 1}', '        m_Offset: {x: 0, y: 0}']
    out.append('    m_Ints: []')
    out.append('    m_Floats:' + ('' if floats else ' []'))
    out += [f'    - {k}: {v}' for k, v in sorted(floats.items())]
    out.append('    m_Colors:' + ('' if colors else ' []'))
    out += [f'    - {k}: {{r: {c[0]}, g: {c[1]}, b: {c[2]}, a: {c[3]}}}' for k, c in sorted(colors.items())]
    out += ['  m_BuildTextureStacks: []', '  m_AllowLocking: 1']
    return '\n'.join(out) + '\n'


def png(size=4):
    rows = b''.join(b'\x00' + bytes((128, 128, 255, 255)) * size for _ in range(size))
    chunk = lambda kind, data: struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(rows)) + chunk(b'IEND', b''))


def meta(relative, body=''):
    path = ASSETS/(relative + '.meta')
    path.write_text(f'fileFormatVersion: 2\nguid: {guid(relative)}\n' + body)


def main():
    resources = ASSETS/'Resources'
    resources.mkdir(parents=True, exist_ok=True)
    for path in resources.glob('*.mat*'):
        path.unlink()
    for name, case in CASES.items():
        (resources/(name + '.mat')).write_text(material(name, case))
        meta(f'Resources/{name}.mat', 'NativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 2100000\n'
             '  userData: \n  assetBundleName: \n  assetBundleVariant: \n')
    (resources/'ProbeTexture.png').write_bytes(png())
    meta('Resources/ProbeTexture.png', 'TextureImporter:\n  externalObjects: {}\n  serializedVersion: 13\n'
         '  mipmaps:\n    enableMipMap: 0\n  textureType: 0\n  textureShape: 1\n  userData: \n'
         '  assetBundleName: \n  assetBundleVariant: \n')
    for name in SHADERS:
        meta(f'Shaders/{name}.shader')
    meta('Scenes/Probe.unity', 'DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n'
         '  assetBundleVariant: \n')
    for folder in ('Editor', 'Scripts', 'Shaders', 'Resources', 'Scenes'):
        meta(folder, 'folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n'
             '  assetBundleName: \n  assetBundleVariant: \n')
    for script in sorted(ASSETS.glob('*/*.cs')):
        meta(str(script.relative_to(ASSETS)))
    print('wrote', len(CASES), 'materials')


if __name__ == '__main__':
    main()
