#!/usr/bin/env python3
"""Writes the texture array probe's source images and importer cases (deterministic).

    python3 make_textures.py

Every imported case is one PNG in Assets/Textures (a copy of one of the source images below) and
one line of Assets/Editor/TextureCases.txt: `<name> key=value ...`. `ProbeRunner.Create` applies
the settings in the Editor (which writes the .meta files), makes the Texture3D, Texture2DArray
and CubemapArray `.asset` cases itself and builds the scene.

Source images (8-bit RGBA, written top row first as PNG stores them):
  Grid8     128x64 random colour and alpha (a 4x2 or 2x4 flipbook)
  Grid6     60x40 random colour and alpha (3x2 cells of 20x20: non-power-of-two cells)
  Grid3Odd  100x64 random colour and alpha (3 columns do not divide its width)
  Single    32x32 random colour and alpha (a single cell)
  SoftPair  256x128 two soft fields side by side (max-size downscales)
  CutoutPair 128x64 two cutouts side by side (coverage, alpha is transparency)
  NormalQuad 64x64 tangent-space normals (2x2 cells)
  Lut       256x16 a 16x16x16 colour grading LUT strip (red x, green y, blue the cell)
  Vol16     64x64 random colour and alpha (4x4 cells of 16x16)
  VolOdd    48x32 random colour and alpha (3x4 cells of 16x8)
  VolSmall  16x8 random colour and alpha (4x2 cells of 4x4)
  VolDeep   64x4 random colour and alpha (16 cells of 4x4: deeper than wide)
  CubeStrip 96x16 random colour and alpha (six 16x16 faces in a row)
"""
import importlib.util
from pathlib import Path

PROBE = Path(__file__).resolve().parent
_spec = importlib.util.spec_from_file_location('textureimport_sources', PROBE.parent/'textureimport/make_textures.py')
base = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(base)


def side_by_side(*images):
    h = images[0][1]
    rows = [sum((img[2][y] for img in images), []) for y in range(h)]
    return sum(img[0] for img in images), h, rows


def lut():
    rows = []
    for y in range(16):
        rows.append([((x % 16) * 17, (15 - y) * 17, (x // 16) * 17, 255) for x in range(256)])
    return 256, 16, rows


SOURCES = {
    'Grid8': lambda: base.noise(128, 64, 21),
    'Grid6': lambda: base.noise(60, 40, 22),
    'Grid3Odd': lambda: base.noise(100, 64, 24),
    'Single': lambda: base.noise(32, 32, 25),
    'SoftPair': lambda: side_by_side(base.soft(128, 128), base.soft(128, 128)[:2] + (
        [list(reversed(r)) for r in base.soft(128, 128)[2]],)),
    'CutoutPair': lambda: side_by_side(base.cutout(), base.cutout()[:2] + ([list(reversed(r)) for r in base.cutout()[2]],)),
    'NormalQuad': base.normals,
    'Lut': lut,
    'Vol16': lambda: base.noise(64, 64, 23),
    'VolOdd': lambda: base.noise(48, 32, 26),
    'VolSmall': lambda: base.noise(16, 8, 27),
    'VolDeep': lambda: base.noise(64, 4, 29),
    'CubeStrip': lambda: base.noise(96, 16, 28),
}

# name: (source, settings). shape=array|volume|cube, cells=columns,rows (the flipbook),
# filtermode=point|bilinear|trilinear, wrap=U,V,W (repeat|clamp|mirror|mirroronce), readable=0;
# the other keys are textureimport's.
CASES = {
    'ArrayGridSrgb': ('Grid8', 'shape=array cells=4,2'),
    'ArrayGridLinearKaiser': ('Grid8', 'shape=array cells=4,2 srgb=0 filter=kaiser'),
    'ArrayGridTall': ('Grid8', 'shape=array cells=2,4 srgb=0'),
    'ArrayNoMips': ('Grid8', 'shape=array cells=4,2 mips=0 filtermode=point wrap=clamp,mirror,repeat'),
    'ArrayNpot': ('Grid6', 'shape=array cells=3,2'),
    'ArrayNpotNone': ('Grid6', 'shape=array cells=3,2 npot=none srgb=0'),
    'ArrayMaxSize': ('SoftPair', 'shape=array cells=2,1 maxsize=64'),
    'ArrayFadeBorder': ('Grid8', 'shape=array cells=4,2 fade=0,2 border=1 srgb=0'),
    'ArrayCoverage': ('CutoutPair', 'shape=array cells=2,1 coverage=0.5 transparency=1'),
    'ArrayNormal': ('NormalQuad', 'shape=array cells=2,2 type=normal'),
    'ArrayIndivisible': ('Grid3Odd', 'shape=array cells=3,1'),
    'ArraySingle': ('Single', 'shape=array cells=1,1 filtermode=trilinear'),
    'ArrayUnreadable': ('Grid8', 'shape=array cells=4,2 readable=0'),
    'VolumeLut': ('Lut', 'shape=volume cells=16,1 srgb=0 mips=0 wrap=clamp,clamp,clamp'),
    'VolumeGridSrgb': ('Vol16', 'shape=volume cells=4,4'),
    'VolumeGridLinear': ('Vol16', 'shape=volume cells=4,4 srgb=0'),
    'VolumeKaiser': ('Vol16', 'shape=volume cells=4,4 srgb=0 filter=kaiser'),
    'VolumeOdd': ('VolOdd', 'shape=volume cells=3,4 srgb=0'),
    'VolumeNpot': ('Grid6', 'shape=volume cells=3,2'),
    'VolumeMaxSize': ('SoftPair', 'shape=volume cells=2,1 maxsize=64 srgb=0'),
    'VolumeWrapClamp': ('VolSmall', 'shape=volume cells=4,2 mips=0 filtermode=point wrap=repeat,repeat,clamp'),
    'VolumeWrapRepeat': ('VolSmall', 'shape=volume cells=4,2 mips=0 filtermode=point wrap=clamp,clamp,repeat'),
    'VolumeUnreadable': ('Vol16', 'shape=volume cells=4,4 readable=0'),
    'VolumeDeep': ('VolDeep', 'shape=volume cells=16,1 srgb=0 filtermode=point wrap=clamp,clamp,clamp'),
    'ArrayDeep': ('VolDeep', 'shape=array cells=16,1 srgb=0 filtermode=point wrap=clamp,clamp,clamp'),
    'CubeStrip': ('CubeStrip', 'shape=cube'),
    'CubeStripLinear': ('CubeStrip', 'shape=cube srgb=0 mips=0'),
}


def main():
    textures = PROBE/'Assets/Textures'
    textures.mkdir(parents=True, exist_ok=True)
    images = {name: make() for name, make in SOURCES.items()}
    for name, (source, _) in CASES.items():
        base.png(textures/f'{name}.png', *images[source])
    lines = [f'{name} {settings}'.rstrip() for name, (_, settings) in CASES.items()]
    (PROBE/'Assets/Editor').mkdir(parents=True, exist_ok=True)
    (PROBE/'Assets/Editor/TextureCases.txt').write_text('\n'.join(lines) + '\n')


if __name__ == '__main__':
    main()
