#!/usr/bin/env python3
"""Writes the probe's textures, audio clips and user-property model (Assets/Tex/*.png,
Assets/Trees/*.png, Assets/Audio/*.wav, Assets/Models/UserProps.fbx).

The patterns are small and deterministic: every texel differs from its neighbours, so a resize,
a mip level or an inverted colour changes the hash the probe records. The tree textures are the
maps of `tests/projects/speedtree9wind`'s `Calm.st9` (copied into the probe's project by run.py)."""
from pathlib import Path
import struct
import wave
import zlib

ASSETS = Path(__file__).resolve().parent/'Assets'


def png(path, size, alpha):
    rows = b''
    for y in range(size):
        rows += b'\0'
        for x in range(size):
            r = (x * 37 + y * 11) & 255
            g = (x * 5 + y * 53) & 255
            b = ((x ^ y) * 29) & 255
            a = ((x + y) * 8) & 255 if alpha else 255
            rows += bytes((r, g, b, a))

    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff)
    header = struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', header) + chunk(b'IDAT', zlib.compress(rows, 9))
                     + chunk(b'IEND', b''))


def tone(path):
    """0.1 s of 16-bit stereo at 22050 Hz: different ramps per channel, so a mono mix is visible."""
    frames = b''
    for i in range(2205):
        left = ((i * 97) % 2000) - 1000
        right = ((i * 31) % 3000) - 1500
        frames += struct.pack('<hh', left * 8, right * 8)
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), 'wb') as out:
        out.setnchannels(2)
        out.setsampwidth(2)
        out.setframerate(22050)
        out.writeframes(frames)


def ramp(i):
    """The mono clips' signal, in [-1, 1): a ramp with a fine step, so that 16-bit storage of a
    finer format changes the hash the probe records."""
    return ((i * 7919) % 20000) / 10000.0 - 1.0 + 1.0 / 3000000.0


def tone_pcm(path, width):
    """0.05 s of mono integer PCM at 22050 Hz, `width` bytes per sample (8-bit is unsigned)."""
    frames = b''
    for i in range(1102):
        v = ramp(i)
        if width == 1:
            frames += bytes((int(round(v * 127)) + 128,))
        else:
            q = int(v * (1 << (8 * width - 1)))
            frames += q.to_bytes(width, 'little', signed=True)
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), 'wb') as out:
        out.setnchannels(1)
        out.setsampwidth(width)
        out.setframerate(22050)
        out.writeframes(frames)


def tone_float(path):
    """0.05 s of mono 32-bit float PCM at 22050 Hz (WAVE_FORMAT_IEEE_FLOAT)."""
    data = b''.join(struct.pack('<f', ramp(i)) for i in range(1102))
    fmt = struct.pack('<HHIIHH', 3, 1, 22050, 22050 * 4, 4, 32)
    body = b'WAVE' + b'fmt ' + struct.pack('<I', len(fmt)) + fmt + b'data' + struct.pack('<I', len(data)) + data
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(b'RIFF' + struct.pack('<I', len(body)) + body)


# The user-property model: (name, parent, has a mesh, [(property, type, label, flags, value)]).
# FBX flags: "U" marks a user property, "A" an animatable one; the last of Tagged's is a custom
# property that is not a user property.
USER_NODES = [
    ('Tagged', None, True, [
        ('collider', 'bool', '', 'U', 1),
        ('count', 'int', 'Integer', 'U', 3),
        ('weight', 'double', 'Number', 'U', 2.5),
        ('label', 'KString', '', 'U', 'hello'),
        ('offset', 'Vector3D', 'Vector', 'U', (1.0, 2.0, 3.0)),
        ('tint', 'ColorRGB', 'Color', 'U', (0.25, 0.5, 0.75)),
        ('animatedWeight', 'Number', '', 'A+U', 0.75),
        ('UDP3DSMAX', 'KString', '', 'U', 'key = value'),
        ('notUser', 'Number', '', 'A', 9.0)]),
    ('Bare', None, True, []),
    ('Group', None, False, [('isGroup', 'bool', '', 'U', 1)]),
    ('Inner', 'Group', True, [('speed', 'float', '', 'U', 1.5), ('collider', 'bool', '', 'U', 0)]),
]


def user_props_fbx(path):
    """An ASCII FBX 7.4 file whose nodes carry user properties of each FBX type."""
    def value(v):
        if isinstance(v, str):
            return f' "{v}"'
        if isinstance(v, tuple):
            return ',' + ','.join(repr(float(c)) for c in v)
        return ',' + (repr(v) if isinstance(v, float) else str(v))
    corners = [(x, y, z) for z in (-0.5, 0.5) for y in (-0.5, 0.5) for x in (-0.5, 0.5)]
    quads = [(0, 2, 3, 1), (4, 5, 7, 6), (0, 1, 5, 4), (2, 6, 7, 3), (0, 4, 6, 2), (1, 3, 7, 5)]
    vertices = ','.join(repr(float(c)) for corner in corners for c in corner)
    indices = ','.join(str(i if k < 3 else -i - 1) for quad in quads for k, i in enumerate(quad))
    ids = {name: 200000 + 10 * n for n, (name, _, _, _) in enumerate(USER_NODES)}
    objects, connections = [], []
    meshes = 0
    for n, (name, parent, mesh, props) in enumerate(USER_NODES):
        mid = ids[name]
        lines = [f'\t\t\tP: "Lcl Translation", "Lcl Translation", "", "A",{2.0 * n!r},0,0']
        for prop, kind, label, flags, v in props:
            if kind == 'KString':
                lines.append(f'\t\t\tP: "{prop}", "KString", "", "{flags}",{value(v)}')
            else:
                lines.append(f'\t\t\tP: "{prop}", "{kind}", "{label}", "{flags}"{value(v)}')
        body = '\n'.join(lines)
        objects.append(f'''\tModel: {mid}, "Model::{name}", "{'Mesh' if mesh else 'Null'}" {{
\t\tVersion: 232
\t\tProperties70:  {{
{body}
\t\t}}
\t\tShading: T
\t\tCulling: "CullingOff"
\t}}''')
        connections.append(f'\tC: "OO",{mid},{ids[parent] if parent else 0}')
        if mesh:
            meshes += 1
            gid = mid + 1
            objects.append(f'''\tGeometry: {gid}, "Geometry::{name}", "Mesh" {{
\t\tVertices: *24 {{
\t\t\ta: {vertices}
\t\t}}
\t\tPolygonVertexIndex: *24 {{
\t\t\ta: {indices}
\t\t}}
\t\tGeometryVersion: 124
\t}}''')
            connections.append(f'\tC: "OO",{gid},{mid}')
    counts = {'Model': len(USER_NODES), 'Geometry': meshes}
    definitions = '\n'.join(f'\tObjectType: "{k}" {{\n\t\tCount: {v}\n\t}}' for k, v in counts.items())
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(f'''; FBX 7.4.0 project file
; Generated by tests/projects/assetpostprocessors/make_assets.py
FBXHeaderExtension:  {{
\tFBXHeaderVersion: 1003
\tFBXVersion: 7400
\tCreator: "Unbound asset postprocessor probe"
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


if __name__ == '__main__':
    png(ASSETS/'Tex/Plain.png', 16, False)
    png(ASSETS/'Tex/Shrink.png', 64, True)
    png(ASSETS/'Tex/Invert.png', 32, False)
    png(ASSETS/'Tex/Sprite.png', 32, True)
    tone(ASSETS/'Audio/Tone.wav')
    tone_pcm(ASSETS/'Audio/Tone8.wav', 1)
    tone_pcm(ASSETS/'Audio/Tone24.wav', 3)
    tone_float(ASSETS/'Audio/ToneFloat.wav')
    user_props_fbx(ASSETS/'Models/UserProps.fbx')
    # Calm.st9's maps: colour maps with alpha, a normal map and an extra map.
    png(ASSETS/'Trees/Bark_Color.png', 16, True)
    png(ASSETS/'Trees/Bark_Normal.png', 16, False)
    png(ASSETS/'Trees/Leaf_Color.png', 32, True)
    png(ASSETS/'Trees/Leaf_Extra.png', 16, True)
    png(ASSETS/'Trees/Billboard_Color.png', 16, True)
