#!/usr/bin/env python3
"""Writes the texture import probe's source images and importer cases (deterministic).

    python3 make_textures.py

Every case is one PNG in Assets/Textures (a copy of one of the source images below) and one line
of Assets/Editor/TextureCases.txt: `<name> key=value ...`. `ProbeRunner.Create` applies the
settings to each importer in the Editor (which writes the .meta files) and builds the scene.

Source images (8-bit RGBA, written top row first as PNG stores them):
  Noise     64x64 random colour and alpha
  Smooth    64x64 opaque gradients with a few hard edges
  Impulse   32x32 opaque black with single white texels (filter kernels)
  Cutout    64x64 random colour, alpha a soft disc, thin lines and a transparent border
  Height    64x64 gray (r=g=b) bumps, ramps, steps and single texels
  HeightRGB 64x64 independent red, green and blue height fields (which channels make height)
  Normals   64x64 tangent-space normals, smoothly varying
  ImpulseGray 64x64 mid-gray with brighter and darker single texels (filter lobes below zero)
  OddGray   37x19 the same
  HeightSmall 32x32 and HeightWide 64x16: the Height field at other sizes
  Dots      16x16 transparent but for a few opaque texels (alpha-is-transparency fill)
  Npot      50x30 random colour and alpha
  Odd       37x19 random colour and alpha
  Mixed     40x30 random colour and alpha (one side shrinks, the other grows to a power of two)
"""
import math
from pathlib import Path
import struct
import zlib

PROBE = Path(__file__).resolve().parent


class Lcg:
    def __init__(self, seed):
        self.state = seed

    def byte(self):
        self.state = (self.state * 1103515245 + 12345) & 0x7fffffff
        return (self.state >> 16) & 0xff


def png(path, width, height, pixels):
    """pixels: rows top-down of (r, g, b, a)."""
    raw = b''.join(b'\0' + bytes(c for p in row for c in p) for row in pixels)

    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
    data = (b'\x89PNG\r\n\x1a\n'
            + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0))
            + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))
    path.write_bytes(data)


def noise(w, h, seed):
    rng = Lcg(seed)
    return w, h, [[(rng.byte(), rng.byte(), rng.byte(), rng.byte()) for _ in range(w)] for _ in range(h)]


def smooth():
    rows = []
    for y in range(64):
        row = []
        for x in range(64):
            r = x * 4
            g = y * 4
            b = 255 if (x // 8 + y // 8) % 2 else 32
            if 20 <= x < 44 and 20 <= y < 44:
                r, g = 255 - r, 255 - g
            row.append((min(r, 255), min(g, 255), b, 255))
        rows.append(row)
    return 64, 64, rows


def impulse():
    rows = [[(0, 0, 0, 255)] * 32 for _ in range(32)]
    for x, y in ((0, 0), (5, 7), (16, 16), (31, 10), (20, 31), (11, 24), (26, 3)):
        rows[y][x] = (255, 255, 255, 255)
    return 32, 32, rows


def cutout():
    rng = Lcg(7)
    rows = []
    for y in range(64):
        row = []
        for x in range(64):
            d = math.hypot(x - 31.5, y - 31.5)
            a = max(0.0, min(1.0, (20 - d) / 6))
            if x in (6, 7) or y == 52:
                a = 1.0
            if x == 58 and y % 3:
                a = 0.6
            if x == 0 or y == 0 or x == 63 or y == 63:
                a = 0.0
            row.append((rng.byte(), rng.byte(), rng.byte(), round(a * 255)))
        rows.append(row)
    return 64, 64, rows


def height_field(x, y, phase=0.0):
    v = 0.0
    for cx, cy, s, amp in ((16, 16, 5, 0.8), (44, 20, 3, 0.6), (30, 46, 9, 0.9), (52, 52, 2, -0.5)):
        v += amp * math.exp(-((x - cx) ** 2 + (y - cy) ** 2) / (2 * s * s))
    v += 0.3 * math.sin((x + phase) * 0.35) * math.cos(y * 0.21)
    if 4 <= x < 12 and 40 <= y < 60:
        v += 0.5
    return v


def height():
    rows = []
    for y in range(64):
        row = []
        for x in range(64):
            v = round(max(0.0, min(1.0, 0.3 + 0.5 * height_field(x, y))) * 255)
            if (x, y) in ((60, 4), (58, 30), (33, 33)):
                v = 255
            if x == 63 and y < 16:
                v = 0
            row.append((v, v, v, 255))
        rows.append(row)
    return 64, 64, rows


def height_rgb():
    rows = []
    for y in range(64):
        row = []
        for x in range(64):
            r = round(max(0.0, min(1.0, 0.3 + 0.5 * height_field(x, y))) * 255)
            g = round(max(0.0, min(1.0, 0.5 + 0.4 * math.sin(x * 0.5 + y * 0.2))) * 255)
            b = 255 if 24 <= x < 40 and 8 <= y < 56 else 40
            row.append((r, g, b, 255))
        rows.append(row)
    return 64, 64, rows


def normals():
    rows = []
    for y in range(64):
        row = []
        for x in range(64):
            nx = 0.7 * math.sin(x * 0.4) * math.cos(y * 0.13)
            ny = 0.6 * math.cos(x * 0.17 + y * 0.31)
            nz = 0.3 + abs(math.sin((x - y) * 0.05))
            n = math.sqrt(nx * nx + ny * ny + nz * nz)
            row.append(tuple(round((c / n * 0.5 + 0.5) * 255) for c in (nx, ny, nz)) + (255,))
        rows.append(row)
    return 64, 64, rows


def impulse_gray(w, h, points):
    rows = [[(128, 128, 128, 255)] * w for _ in range(h)]
    for x, y, v in points:
        rows[y][x] = (v, v, v, 255)
    return w, h, rows


def height_sized(w, h, step_x, step_y):
    rows = []
    for y in range(h):
        row = []
        for x in range(w):
            v = round(max(0.0, min(1.0, 0.3 + 0.5 * height_field(x * step_x, y * step_y))) * 255)
            row.append((v, v, v, 255))
        rows.append(row)
    return w, h, rows


def dots():
    rng = Lcg(11)
    rows = [[(rng.byte(), rng.byte(), rng.byte(), 0) for _ in range(16)] for _ in range(16)]
    for x, y in ((3, 4), (4, 4), (11, 2), (12, 12), (2, 13), (7, 9), (8, 9), (7, 10)):
        rows[y][x] = (rng.byte(), rng.byte(), rng.byte(), 255)
    rows[6][13] = rows[6][13][:3] + (128,)
    return 16, 16, rows


SOURCES = {
    'Noise': lambda: noise(64, 64, 1),
    'Smooth': smooth,
    'Impulse': impulse,
    'Cutout': cutout,
    'Height': height,
    'HeightRGB': height_rgb,
    'Normals': normals,
    'Npot': lambda: noise(50, 30, 2),
    'Odd': lambda: noise(37, 19, 3),
    'ImpulseGray': lambda: impulse_gray(64, 64, ((12, 12, 255), (45, 13, 0), (13, 44, 0), (44, 45, 255),
                                                 (28, 30, 255), (0, 31, 255), (63, 52, 0))),
    'OddGray': lambda: impulse_gray(37, 19, ((6, 6, 255), (21, 7, 0), (33, 12, 255))),
    'HeightSmall': lambda: height_sized(32, 32, 2, 2),
    'HeightWide': lambda: height_sized(64, 16, 1, 4),
    'Dots': dots,
    'Mixed': lambda: noise(40, 30, 4),
}

# name: (source, settings). Settings default to a Default-type, sRGB, box-filtered texture with
# mipmaps, input alpha and NPOT ToNearest (Unity's defaults); every case is readable and stored as
# RGBA32 (Standalone override, default platform uncompressed).
CASES = {
    'NoiseBoxSrgb': ('Noise', ''),
    'NoiseBoxLinear': ('Noise', 'srgb=0'),
    'NoiseKaiserSrgb': ('Noise', 'filter=kaiser'),
    'NoiseKaiserLinear': ('Noise', 'filter=kaiser srgb=0'),
    'NoiseNoMips': ('Noise', 'mips=0'),
    'NoiseAlphaNone': ('Noise', 'alpha=none'),
    'NoiseAlphaGray': ('Noise', 'alpha=gray srgb=0'),
    'ImpulseBoxLinear': ('Impulse', 'srgb=0'),
    'ImpulseKaiserLinear': ('Impulse', 'filter=kaiser srgb=0'),
    'ImpulseKaiserSrgb': ('Impulse', 'filter=kaiser'),
    'SmoothFadeSrgb': ('Smooth', 'fade=1,3'),
    'SmoothFadeLinear': ('Smooth', 'fade=1,3 srgb=0'),
    'SmoothFadeKaiser': ('Smooth', 'fade=2,5 filter=kaiser srgb=0'),
    'SmoothFadeZero': ('Smooth', 'fade=0,1'),
    'NoiseFadeSrgb': ('Noise', 'fade=0,4'),
    'NoiseBorderLinear': ('Noise', 'border=1 srgb=0'),
    'NoiseBorderSrgb': ('Noise', 'border=1'),
    'NoiseBorderKaiser': ('Noise', 'border=1 filter=kaiser srgb=0'),
    'CutoutBoxSrgb': ('Cutout', ''),
    'CutoutCoverageLinear': ('Cutout', 'coverage=0.5 srgb=0'),
    'CutoutCoverageSrgb': ('Cutout', 'coverage=0.5'),
    'CutoutCoverageLow': ('Cutout', 'coverage=0.2 srgb=0'),
    'CutoutCoverageHigh': ('Cutout', 'coverage=0.8 filter=kaiser'),
    'CutoutTransparency': ('Cutout', 'transparency=1'),
    'CutoutTransparencyLinear': ('Cutout', 'transparency=1 srgb=0 filter=kaiser'),
    'CutoutTransparencyCoverage': ('Cutout', 'transparency=1 coverage=0.5'),
    'NpotNoneBox': ('Npot', 'npot=none'),
    'NpotNoneKaiser': ('Npot', 'npot=none filter=kaiser srgb=0'),
    'OddNoneBox': ('Odd', 'npot=none srgb=0'),
    'OddNoneSrgb': ('Odd', 'npot=none'),
    'NpotNearest': ('Npot', 'srgb=0'),
    'NpotLarger': ('Npot', 'npot=larger'),
    'NpotSmaller': ('Npot', 'npot=smaller srgb=0'),
    'OddNearestNoMips': ('Odd', 'npot=nearest mips=0'),
    'NormalsBox': ('Normals', 'type=normal'),
    'NormalsKaiser': ('Normals', 'type=normal filter=kaiser'),
    'NormalsFlipGreen': ('Normals', 'type=normal flipgreen=1'),
    'NormalsFade': ('Normals', 'type=normal fade=1,3'),
    'HeightStandard': ('Height', 'type=normal grayscale=standard,0.25'),
    'HeightSobel': ('Height', 'type=normal grayscale=sobel,0.25'),
    'HeightStandardLow': ('Height', 'type=normal grayscale=standard,0.05'),
    'HeightSobelHigh': ('Height', 'type=normal grayscale=sobel,0.3 filter=kaiser'),
    'HeightStandardOne': ('Height', 'type=normal grayscale=standard,1'),
    'HeightStandardFade': ('Height', 'type=normal grayscale=standard,0.1 fade=1,3'),
    'HeightStandardFlip': ('Height', 'type=normal grayscale=standard,0.25 flipgreen=1'),
    'HeightRgbStandard': ('HeightRGB', 'type=normal grayscale=standard,0.25'),
    'HeightRgbSobel': ('HeightRGB', 'type=normal grayscale=sobel,0.25'),
    'ImpulseStandard': ('Impulse', 'type=normal grayscale=standard,0.25'),
    'ImpulseSobel': ('Impulse', 'type=normal grayscale=sobel,0.25'),
    'NpotHeightStandard': ('Npot', 'type=normal grayscale=standard,0.25 npot=none'),
    'ImpulseGrayKaiserLinear': ('ImpulseGray', 'filter=kaiser srgb=0'),
    'ImpulseGrayKaiserSrgb': ('ImpulseGray', 'filter=kaiser'),
    'ImpulseGrayBorderKaiser': ('ImpulseGray', 'filter=kaiser srgb=0 border=1'),
    'OddGrayKaiser': ('OddGray', 'filter=kaiser srgb=0 npot=none'),
    'OddGrayBox': ('OddGray', 'srgb=0 npot=none'),
    'NpotNoneBorder': ('Npot', 'npot=none border=1 srgb=0'),
    'OddNoneBorder': ('Odd', 'npot=none border=1'),
    'HeightScale001': ('Height', 'type=normal grayscale=standard,0.01'),
    'HeightScale002': ('Height', 'type=normal grayscale=standard,0.02'),
    'HeightScale010': ('Height', 'type=normal grayscale=standard,0.1'),
    'HeightScale015': ('Height', 'type=normal grayscale=standard,0.15'),
    'HeightScale020': ('Height', 'type=normal grayscale=standard,0.2'),
    'HeightScale030': ('Height', 'type=normal grayscale=standard,0.3'),
    'HeightScale050': ('Height', 'type=normal grayscale=standard,0.5'),
    'HeightScale200': ('Height', 'type=normal grayscale=standard,2'),
    'HeightSmallStandard': ('HeightSmall', 'type=normal grayscale=standard,0.25'),
    'HeightWideStandard': ('HeightWide', 'type=normal grayscale=standard,0.25'),
    'HeightWideSobel': ('HeightWide', 'type=normal grayscale=sobel,0.1'),
    'DotsTransparency': ('Dots', 'transparency=1 mips=0'),
    'DotsTransparencyMips': ('Dots', 'transparency=1 srgb=0'),
    'MixedNoneKaiser': ('Mixed', 'npot=none filter=kaiser srgb=0'),
    'MixedNoneBox': ('Mixed', 'npot=none'),
    'MixedNearest': ('Mixed', 'srgb=0'),
    'MaxSizeMitchell': ('Noise', 'maxsize=32'),
    'MaxSizeBilinear': ('Noise', 'maxsize=32,bilinear'),
}


def main():
    textures = PROBE/'Assets/Textures'
    textures.mkdir(parents=True, exist_ok=True)
    images = {name: make() for name, make in SOURCES.items()}
    for name, (source, _) in CASES.items():
        png(textures/f'{name}.png', *images[source])
    lines = [f'{name} {settings}'.rstrip() for name, (_, settings) in CASES.items()]
    (PROBE/'Assets/Editor').mkdir(parents=True, exist_ok=True)
    (PROBE/'Assets/Editor/TextureCases.txt').write_text('\n'.join(lines) + '\n')


if __name__ == '__main__':
    main()
