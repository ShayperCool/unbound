#!/usr/bin/env python3
"""Writes the cube probe's sources: 6:1 horizontal strips of 32-pixel faces, each face a distinct
colour with smooth gradients (so block compression errors stay small), as PNG (opaque and with a
transparent band) and as Radiance HDR (values up to 4), and Assets/Editor/TextureCases.txt.

    python3 tests/projects/cubeblocks/make_textures.py      (needs Pillow: /usr/bin/python3)
"""
from pathlib import Path
import struct

from PIL import Image

PROBE = Path(__file__).resolve().parent
TEXTURES = PROBE/'Assets/Textures'
FACE = 32
BASES = [(0.9, 0.2, 0.1), (0.1, 0.8, 0.2), (0.2, 0.3, 0.9), (0.9, 0.9, 0.2), (0.7, 0.2, 0.8), (0.2, 0.8, 0.8)]

# name: (source kind, importer settings)
CASES = {
    'LdrOpaque': ('opaque', 'compression=normal'),
    'LdrAlpha': ('alpha', 'compression=normal'),
    'LdrHigh': ('opaque', 'compression=high'),
    'LdrLow': ('opaque', 'compression=low'),
    'LdrSpecular': ('opaque', 'compression=normal convolution=specular'),
    'LdrUncompressed': ('opaque', 'compression=none'),
    'LdrNoMips': ('opaque', 'compression=normal mips=0'),
    'LdrLinear': ('opaque', 'compression=normal srgb=0'),
    'HdrNormal': ('hdr', 'compression=normal'),
    'HdrSpecular': ('hdr', 'compression=normal convolution=specular'),
    'HdrHigh': ('hdr', 'compression=high'),
    'HdrUncompressed': ('hdr', 'compression=none'),
    'HdrNoMips': ('hdr', 'compression=normal mips=0'),
}


def texel(face, x, y, hdr):
    base = BASES[face]
    u, v = x / (FACE - 1), y / (FACE - 1)
    scale = 4.0 if hdr else 1.0
    return tuple(min(scale, max(0.0, scale * (b * (0.6 + 0.4 * u) * (0.7 + 0.3 * v)))) for b in base)


def png(name, alpha):
    image = Image.new('RGBA' if alpha else 'RGB', (FACE * 6, FACE))
    for face in range(6):
        for y in range(FACE):
            for x in range(FACE):
                rgb = tuple(round(255 * c) for c in texel(face, x, y, False))
                a = 0 if alpha and 10 <= y < 16 else 255
                image.putpixel((face * FACE + x, y), rgb + ((a,) if alpha else ()))
    image.save(TEXTURES/f'{name}.png')


def rgbe(r, g, b):
    m = max(r, g, b)
    if m < 1e-32:
        return bytes(4)
    import math
    mantissa, exponent = math.frexp(m)
    scale = mantissa * 256.0 / m
    return bytes((int(r * scale), int(g * scale), int(b * scale), exponent + 128))


def hdr(name):
    width, height = FACE * 6, FACE
    rows = bytearray()
    for y in range(height):
        for face in range(6):
            for x in range(FACE):
                rows += rgbe(*texel(face, x, y, True))
    header = f'#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y {height} +X {width}\n'.encode()
    (TEXTURES/f'{name}.hdr').write_bytes(header + bytes(rows))


def main():
    TEXTURES.mkdir(parents=True, exist_ok=True)
    lines = []
    for name, (kind, settings) in CASES.items():
        if kind == 'hdr':
            hdr(name)
        else:
            png(name, kind == 'alpha')
        lines.append(f'{name} {settings}')
    (PROBE/'Assets/Editor/TextureCases.txt').write_text('\n'.join(lines) + '\n')


if __name__ == '__main__':
    main()
