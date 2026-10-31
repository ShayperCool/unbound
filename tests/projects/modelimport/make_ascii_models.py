#!/usr/bin/env python3
"""Writes Assets/Models/Exotic.fbx (and its copy ExoticLegacy.fbx): an ASCII FBX 7.4 file with one cube per material, for the
material kinds Blender does not export (Lambert without transparency values, texture transforms,
Bump, and the property sets 3ds Max, Maya Arnold and Autodesk Interactive write).

    python3 tests/projects/modelimport/make_ascii_models.py
"""
from pathlib import Path

PROBE = Path(__file__).resolve().parent
OUT = PROBE/'Assets/Models/Exotic.fbx'
# The same file imported by the legacy material importer (its .meta).
LEGACY = PROBE/'Assets/Models/ExoticLegacy.fbx'

# Each material: (name, shading model, [(property, type, flags, value)], {property: texture}).
# Types are FBX property types; "U" marks user properties as exporters write them.
COLOR, NUMBER, INT, STRING, VECTOR = 'Color', 'Number', 'int', 'KString', 'Vector'
MATERIALS = [
    ('M_LambertBare', 'lambert', [('DiffuseColor', COLOR, 'A', (0.5, 0.6, 0.7))], {}),
    ('M_TransparentColor', 'lambert', [
        ('DiffuseColor', COLOR, 'A', (0.9, 0.2, 0.2)),
        ('TransparentColor', COLOR, 'A', (0.3, 0.3, 0.3)),
        ('TransparencyFactor', NUMBER, 'A', 1.0)], {}),
    ('M_TransparentTex', 'phong', [
        ('DiffuseColor', COLOR, 'A', (0.2, 0.9, 0.2)),
        ('TransparencyFactor', NUMBER, 'A', 1.0)], {'TransparentColor': 'Checker'}),
    ('M_Bump', 'phong', [
        ('DiffuseColor', COLOR, 'A', (0.6, 0.6, 0.2)),
        ('DiffuseFactor', NUMBER, 'A', 0.5),
        ('BumpFactor', NUMBER, 'A', 0.5),
        ('Shininess', NUMBER, 'A', 49.0),
        ('EmissiveColor', COLOR, 'A', (0.0, 0.0, 0.0)),
        ('TransparencyFactor', NUMBER, 'A', 0.0)], {'DiffuseColor': 'CheckerMoved', 'Bump': 'Bumps'}),
    ('M_Interactive', 'phong', [
        ('renderAPI', STRING, 'U', 'SFX_PBS_SHADER'),
        ('base_color', COLOR, 'A+U', (0.4, 0.3, 0.2)),
        ('opacity', NUMBER, 'A+U', 0.8),
        ('roughness', NUMBER, 'A+U', 0.3),
        ('metallic', NUMBER, 'A+U', 0.6),
        ('emissive', COLOR, 'A+U', (0.2, 0.1, 0.0)),
        ('emissive_intensity', NUMBER, 'A+U', 2.0),
        ('uv_offset', VECTOR, 'A+U', (0.25, 0.5, 0.0)),
        ('uv_scale', VECTOR, 'A+U', (2.0, 3.0, 0.0)),
        ('use_normal_map', NUMBER, 'A+U', 1.0)], {'TEX_normal_map': 'Bumps'}),
    ('M_InteractiveCutout', 'phong', [
        ('renderAPI', STRING, 'U', 'SFX_PBS_SHADER'),
        ('mask_threshold', NUMBER, 'A+U', 0.4),
        ('use_color_map', NUMBER, 'A+U', 1.0)], {'TEX_color_map': 'Checker'}),
    ('M_ArnoldMaya', 'phong', [
        ('TypeId', INT, 'U', 1138001),
        ('baseColor', COLOR, 'A+U', (0.4, 0.5, 0.6)),
        ('specularRoughness', NUMBER, 'A+U', 0.35),
        ('transmission', NUMBER, 'A+U', 0.25),
        ('emission', NUMBER, 'A+U', 1.0),
        ('emissionColor', COLOR, 'A+U', (0.1, 0.2, 0.3))], {'normalCamera': 'Bumps'}),
    ('M_ArnoldMax', 'phong', [
        ('ClassIDa', INT, 'U', 2121471519),
        ('ClassIDb', INT, 'U', 1660373836),
        ('base_color', COLOR, 'A+U', (0.7, 0.2, 0.5)),
        ('specular_roughness', NUMBER, 'A+U', 0.45),
        ('emission', NUMBER, 'A+U', 0.5),
        ('emission_color', COLOR, 'A+U', (0.3, 0.3, 0.9))], {}),
    ('M_Physical', 'phong', [
        ('ORIGINAL_MTL', STRING, 'U', 'PHYSICAL_MTL'),
        ('base_color', COLOR, 'A+U', (0.6, 0.2, 0.2)),
        ('transparency', NUMBER, 'A+U', 0.25),
        ('bump_map_amt', NUMBER, 'A+U', 0.7),
        ('emission', NUMBER, 'A+U', 1.5),
        ('emit_color', COLOR, 'A+U', (0.5, 0.4, 0.3))], {'bump_map': 'Bumps'}),
    ('M_SimplifiedPhysical', 'phong', [
        ('ClassIDa', INT, 'U', -804315650),
        ('ClassIDb', INT, 'U', -1099438848),
        ('base_color', VECTOR, 'A+U', (0.1, 0.8, 0.4)),
        ('basecolor', COLOR, 'A+U', (0.1, 0.8, 0.4)),
        ('roughness', NUMBER, 'A+U', 0.4),
        ('metalness', NUMBER, 'A+U', 0.7),
        ('emit_color', COLOR, 'A+U', (0.0, 0.2, 0.0))], {}),
]

TEXTURES = {
    # name: (relative file, translation, scaling)
    'Checker': ('Textures/Checker.png', None, None),
    'CheckerMoved': ('Textures/Checker.png', (0.25, 0.5, 0.0), (2.0, 4.0, 1.0)),
    'Bumps': ('Textures/Bumps.png', None, None),
}

# A unit cube: 8 corners, 6 quads (the last index of each polygon is stored as -(i+1)).
CORNERS = [(x, y, z) for z in (-0.5, 0.5) for y in (-0.5, 0.5) for x in (-0.5, 0.5)]
QUADS = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]


def number(v):
    return repr(float(v)) if isinstance(v, float) else str(v)


def prop(name, kind, flags, value):
    label = 'Integer' if kind == INT else ''
    if kind == STRING:
        return f'\t\t\tP: "{name}", "KString", "", "{flags}", "{value}"'
    if isinstance(value, tuple):
        values = ','.join(number(float(v)) for v in value)
    else:
        values = number(value)
    return f'\t\t\tP: "{name}", "{kind}", "{label}", "{flags}",{values}'


def main():
    ids = iter(range(100000, 1000000))
    objects, connections = [], []
    texture_ids = {}
    for name, (path, translation, scaling) in TEXTURES.items():
        tid, vid = next(ids), next(ids)
        texture_ids[name] = tid
        props = []
        if translation:
            props.append(f'\t\t\tP: "Translation", "Vector", "", "A",{",".join(map(number, translation))}')
        if scaling:
            props.append(f'\t\t\tP: "Scaling", "Vector", "", "A",{",".join(map(number, scaling))}')
        props.append('\t\t\tP: "UseMaterial", "bool", "", "",1')
        objects.append(f'''\tVideo: {vid}, "Video::{name}", "Clip" {{
\t\tType: "Clip"
\t\tProperties70:  {{
\t\t\tP: "Path", "KString", "XRefUrl", "", "{path}"
\t\t}}
\t\tUseMipMap: 0
\t\tFilename: "{path}"
\t\tRelativeFilename: "{path}"
\t}}
\tTexture: {tid}, "Texture::{name}", "" {{
\t\tType: "TextureVideoClip"
\t\tVersion: 202
\t\tTextureName: "Texture::{name}"
\t\tProperties70:  {{
{chr(10).join(props)}
\t\t}}
\t\tMedia: "Video::{name}"
\t\tFileName: "{path}"
\t\tRelativeFilename: "{path}"
\t\tModelUVTranslation: 0,0
\t\tModelUVScaling: 1,1
\t\tTexture_Alpha_Source: "None"
\t\tCropping: 0,0,0,0
\t}}''')
        connections.append(f'\tC: "OO",{vid},{tid}')

    vertices = ','.join(number(c) for corner in CORNERS for c in corner)
    indices = ','.join(str(i if k < 3 else -i - 1) for quad in QUADS for k, i in enumerate(quad))
    uvs = ','.join(number(c) for quad in QUADS for c in (0.0, 0.0, 1.0, 0.0, 1.0, 1.0, 0.0, 1.0))
    uv_index = ','.join(str(i) for i in range(24))
    for index, (name, shading, props, textures) in enumerate(MATERIALS):
        gid, mid, matid = next(ids), next(ids), next(ids)
        cube = 'Cube_' + name[2:]
        objects.append(f'''\tGeometry: {gid}, "Geometry::{cube}", "Mesh" {{
\t\tVertices: *{len(CORNERS) * 3} {{
\t\t\ta: {vertices}
\t\t}}
\t\tPolygonVertexIndex: *24 {{
\t\t\ta: {indices}
\t\t}}
\t\tGeometryVersion: 124
\t\tLayerElementUV: 0 {{
\t\t\tVersion: 101
\t\t\tName: "UVMap"
\t\t\tMappingInformationType: "ByPolygonVertex"
\t\t\tReferenceInformationType: "IndexToDirect"
\t\t\tUV: *48 {{
\t\t\t\ta: {uvs}
\t\t\t}}
\t\t\tUVIndex: *24 {{
\t\t\t\ta: {uv_index}
\t\t\t}}
\t\t}}
\t\tLayerElementMaterial: 0 {{
\t\t\tVersion: 101
\t\t\tName: ""
\t\t\tMappingInformationType: "AllSame"
\t\t\tReferenceInformationType: "IndexToDirect"
\t\t\tMaterials: *1 {{
\t\t\t\ta: 0
\t\t\t}}
\t\t}}
\t\tLayer: 0 {{
\t\t\tVersion: 100
\t\t\tLayerElement:  {{
\t\t\t\tType: "LayerElementUV"
\t\t\t\tTypedIndex: 0
\t\t\t}}
\t\t\tLayerElement:  {{
\t\t\t\tType: "LayerElementMaterial"
\t\t\t\tTypedIndex: 0
\t\t\t}}
\t\t}}
\t}}
\tModel: {mid}, "Model::{cube}", "Mesh" {{
\t\tVersion: 232
\t\tProperties70:  {{
\t\t\tP: "Lcl Translation", "Lcl Translation", "", "A",{number(2.0 * index)},0,0
\t\t}}
\t\tShading: T
\t\tCulling: "CullingOff"
\t}}
\tMaterial: {matid}, "Material::{name}", "" {{
\t\tVersion: 102
\t\tShadingModel: "{shading}"
\t\tMultiLayer: 0
\t\tProperties70:  {{
{chr(10).join(prop(*p) for p in props)}
\t\t}}
\t}}''')
        connections += [f'\tC: "OO",{mid},0', f'\tC: "OO",{gid},{mid}', f'\tC: "OO",{matid},{mid}']
        for slot, texture in textures.items():
            connections.append(f'\tC: "OP",{texture_ids[texture]},{matid}, "{slot}"')

    counts = {'Model': len(MATERIALS), 'Geometry': len(MATERIALS), 'Material': len(MATERIALS),
              'Texture': len(TEXTURES), 'Video': len(TEXTURES)}
    definitions = '\n'.join(f'\tObjectType: "{k}" {{\n\t\tCount: {v}\n\t}}' for k, v in counts.items())
    OUT.write_text(f'''; FBX 7.4.0 project file
; Generated by tests/projects/modelimport/make_ascii_models.py
FBXHeaderExtension:  {{
\tFBXHeaderVersion: 1003
\tFBXVersion: 7400
\tCreator: "Unbound model import probe"
}}
GlobalSettings:  {{
\tVersion: 1000
\tProperties70:  {{
\t\tP: "UpAxis", "int", "Integer", "",1
\t\tP: "UpAxisSign", "int", "Integer", "",1
\t\tP: "FrontAxis", "int", "Integer", "",2
\t\tP: "FrontAxisSign", "int", "Integer", "",1
\t\tP: "CoordAxis", "int", "Integer", "",0
\t\tP: "CoordAxisSign", "int", "Integer", "",1
\t\tP: "UnitScaleFactor", "double", "Number", "",100
\t}}
}}
Definitions:  {{
\tVersion: 100
\tCount: {sum(counts.values())}
{definitions}
}}
Objects:  {{
{chr(10).join(objects)}
}}
Connections:  {{
{chr(10).join(connections)}
}}
''')
    LEGACY.write_text(OUT.read_text())
    print('wrote', OUT, LEGACY)


if __name__ == '__main__':
    main()
