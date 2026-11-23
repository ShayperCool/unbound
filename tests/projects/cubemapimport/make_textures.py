#!/usr/bin/env python3
"""Writes the cubemap import probe's sources and their .meta files (Assets/Textures).

Every source is an RGBA8 PNG whose texel (x, y), counted from the image's top-left, holds
R = x, G = y, B = 255, A = 255 (images at most 256 texels wide and high; cases named ...Shift
hold R = (x + 100) mod 256, G = (y + 100) mod 256), so a face texel read back tells which source
position it was taken or interpolated from. Each case names the image's
size, generateCubemap (1 Spheremap, 2 Cylindrical, 5 FullCubemap, 6 AutoCubemap) and nPOTScale
(0 None, 1 ToNearest). Imports are readable, uncompressed, linear, without mipmaps.
"""
from pathlib import Path
import hashlib
import struct
import zlib

PROBE = Path(__file__).resolve().parent
SHIFT = 100
OUT = PROBE/'Assets/Textures'
# name: (width, height, generateCubemap, nPOTScale)
CASES = {
    'Wide21Auto': (256, 128, 6, 1),
    'Wide21Cylindrical': (256, 128, 2, 1),
    'Wide21Spheremap': (256, 128, 1, 1),
    'Wide169Auto': (192, 108, 6, 1),
    'Wide169Cylindrical': (192, 108, 2, 1),
    'Wide169Spheremap': (192, 108, 1, 1),
    'Wide169Full': (192, 108, 5, 1),
    'Wide169AutoNoScale': (192, 108, 6, 0),
    'Square11Auto': (128, 128, 6, 1),
    'Square11Cylindrical': (128, 128, 2, 1),
    'Square11Spheremap': (128, 128, 1, 1),
    'Wide32Auto': (192, 128, 6, 1),
    'Tall12Auto': (64, 128, 6, 1),
    'Wide21OddAuto': (200, 100, 6, 1),
    'Strip61Cylindrical': (192, 32, 2, 1),
    'Cross43Spheremap': (128, 96, 1, 1),
    # Where AutoCubemap stops taking a panorama, a sphere map or a single face.
    'Aspect090Auto': (90, 100, 6, 1),
    'Aspect110Auto': (110, 100, 6, 1),
    'Aspect125Auto': (125, 100, 6, 1),
    'Aspect160Auto': (160, 100, 6, 1),
    'Aspect170Auto': (170, 100, 6, 1),
    'Aspect175Auto': (175, 100, 6, 1),
    'Aspect190Auto': (190, 100, 6, 1),
    'Aspect210Auto': (210, 100, 6, 1),
    'Aspect225Auto': (225, 100, 6, 1),
    'Aspect250Auto': (250, 100, 6, 1),
    'Aspect300Auto': (240, 80, 6, 1),
    'Aspect500Auto': (250, 50, 6, 1),
    'Aspect112Auto': (112, 100, 6, 1),
    'Aspect115Auto': (115, 100, 6, 1),
    'Aspect118Auto': (118, 100, 6, 1),
    'Aspect120Auto': (120, 100, 6, 1),
    'Aspect145Auto': (145, 100, 6, 1),
    'Aspect148Auto': (148, 100, 6, 1),
    'Aspect152Auto': (152, 100, 6, 1),
    'Aspect155Auto': (155, 100, 6, 1),
    'Aspect260Auto': (208, 80, 6, 1),
    'Aspect270Auto': (216, 80, 6, 1),
    'Aspect280Auto': (224, 80, 6, 1),
    'Aspect290Auto': (232, 80, 6, 1),
    'Aspect085Auto': (100, 118, 6, 1),
    'Aspect080Auto': (100, 125, 6, 1),
    'Aspect070Auto': (100, 143, 6, 1),
    'Aspect067Auto': (100, 150, 6, 1),
    'Aspect060Auto': (100, 167, 6, 1),
    # The same sizes with the positions coded from another start (SHIFTED): does the image's
    # content take part in AutoCubemap's choice?
    'Aspect085Shift': (100, 118, 6, 1),
    'Aspect118Shift': (118, 100, 6, 1),
    'Aspect125Shift': (125, 100, 6, 1),
    'Aspect150Shift': (150, 100, 6, 1),
    'Aspect178Shift': (192, 108, 6, 1),
    # How a face size is rounded: 80 is nearer 64 than 128; 40 and 48 are no power of two.
    'Wide21Small': (160, 80, 2, 1),
    'Wide21SmallNoScale': (160, 80, 2, 0),
    'Square11SmallNoScale': (100, 100, 1, 0),
    'Strip61Auto': (240, 40, 6, 1),
    'Strip61AutoNoScale': (240, 40, 6, 0),
    'Wide32AutoNoScale': (144, 96, 6, 0),
}


def png(width, height, shift=0):
    rows = b''.join(b'\0' + b''.join(bytes(((x + shift) % 256, (y + shift) % 256, 255, 255)) for x in range(width))
                    for y in range(height))

    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff)
    return (b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(rows, 9)) + chunk(b'IEND', b''))


def meta(name, mode, npot):
    guid = hashlib.md5(('cubemapimport/' + name).encode()).hexdigest()
    return (PROBE/'meta_template.txt').read_text().replace('GUID', guid).replace('GENERATE', str(mode)).replace('NPOT', str(npot))


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for name, (width, height, mode, npot) in CASES.items():
        assert width <= 256 and height <= 256
        (OUT/f'{name}.png').write_bytes(png(width, height, SHIFT if name.endswith('Shift') else 0))
        (OUT/f'{name}.png.meta').write_text(meta(name, mode, npot))
    (PROBE/'Assets/Editor/CubemapCases.txt').write_text(''.join(f'{name}\n' for name in CASES))


if __name__ == '__main__':
    main()
