#!/usr/bin/env python3
"""Writes the probe's textures and their TextureImporter metas (Assets/Sprites).

Each case is a sprite texture whose alpha exercises one part of Unity's sprite mesh generation:
shapes and holes, extrude, tessellation detail, alpha tolerance, rect sizes under 32 pixels,
pivots and pixels per unit, compressed alpha, a downscaled texture, custom outlines and physics
shapes (Single, Polygon and Multiple modes) and fractional sprite rects. Run it after changing a
case; the files are deterministic (GUIDs from the case names).
"""
import hashlib
import math
from pathlib import Path
import struct
import zlib

OUT = Path(__file__).resolve().parent/'Assets/Sprites'


def write_png(path, width, height, alpha, rgb=(255, 255, 255)):
    """alpha(x, y) with y = 0 the bottom row (Unity's order)."""
    raw = bytearray()
    for row in range(height):
        raw.append(0)
        y = height - 1 - row
        for x in range(width):
            raw.extend(bytes((*rgb, alpha(x, y))))

    def chunk(kind, data):
        c = struct.pack('>I', len(data)) + kind + data
        return c + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff)
    data = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0))
    data += chunk(b'IDAT', zlib.compress(bytes(raw), 9)) + chunk(b'IEND', b'')
    path.write_bytes(data)


def guid_of(name):
    return hashlib.md5(('spriteimport:' + name).encode()).hexdigest()


def sprite_id(name):
    return hashlib.md5(('spriteid:' + name).encode()).hexdigest()[:16] + '0800000000000000'


def fmt(f):
    return str(int(f)) if float(f) == int(f) else repr(float(f))


def vec2(v):
    return '{x: %s, y: %s}' % (fmt(v[0]), fmt(v[1]))


def paths_yaml(paths, indent):
    if not paths:
        return ' []\n'
    out = '\n'
    for path in paths:
        for i, p in enumerate(path):
            out += indent + ('- - ' if i == 0 else '  - ') + vec2(p) + '\n'
    return out


def sprite_entry(e, i='    '):
    return (f"{i}- serializedVersion: 2\n"
            f"{i}  name: {e['name']}\n"
            f"{i}  rect:\n{i}    serializedVersion: 2\n"
            f"{i}    x: {fmt(e['rect'][0])}\n{i}    y: {fmt(e['rect'][1])}\n"
            f"{i}    width: {fmt(e['rect'][2])}\n{i}    height: {fmt(e['rect'][3])}\n"
            f"{i}  alignment: {e.get('alignment', 0)}\n"
            f"{i}  pivot: {vec2(e.get('pivot', (0.5, 0.5)))}\n"
            f"{i}  border: {{x: 0, y: 0, z: 0, w: 0}}\n"
            f"{i}  customData: \n"
            f"{i}  outline:" + paths_yaml(e.get('outline'), i + '  ') +
            f"{i}  physicsShape:" + paths_yaml(e.get('physicsShape'), i + '  ') +
            f"{i}  tessellationDetail: {fmt(e.get('tessellationDetail', -1))}\n"
            f"{i}  bones: []\n"
            f"{i}  spriteID: {sprite_id(e['name'])}\n"
            f"{i}  internalID: {e['internalID']}\n"
            f"{i}  vertices: []\n{i}  indices: \n{i}  edges: []\n{i}  weights: []\n")


def platform(target, compression, max_size):
    return f"""  - serializedVersion: 4
    buildTarget: {target}
    maxTextureSize: {max_size}
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: {compression}
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
"""


def texture_meta(name, mode=1, mesh_type=1, extrude=1, ppu=1, alignment=6, pivot=(0.5, 0.5),
                 physics=1, detail=-1, compression=0, max_size=2048, outline=None,
                 physics_shape=None, sprites=None):
    if sprites:
        names = '\n' + ''.join(f"  - first:\n      213: {e['internalID']}\n    second: {e['name']}\n" for e in sprites)
        sheet_sprites = '\n' + ''.join(sprite_entry(e) for e in sprites)
        name_table = '\n' + ''.join(f"      {e['name']}: {e['internalID']}\n" for e in sprites)
    else:
        names, sheet_sprites, name_table = ' []\n', ' []\n', ' {}\n'
    return f"""fileFormatVersion: 2
guid: {guid_of(name)}
TextureImporter:
  internalIDToNameTable:{names}  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 1
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: {mode}
  spriteExtrude: {extrude}
  spriteMeshType: {mesh_type}
  alignment: {alignment}
  spritePivot: {vec2(pivot)}
  spritePixelsToUnits: {fmt(ppu)}
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: {physics}
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: {fmt(detail)}
  textureType: 8
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
""" + platform('DefaultTexturePlatform', compression, max_size) + platform('Standalone', compression, max_size) + f"""  spriteSheet:
    serializedVersion: 2
    sprites:{sheet_sprites}    outline:{paths_yaml(outline, '    ')}    customData:
    physicsShape:{paths_yaml(physics_shape, '    ')}    bones: []
    spriteID: {sprite_id(name)}
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    spriteCustomMetadata:
      entries: []
    nameFileIdTable:{name_table}  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  spritePackingTag:
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def box(x0, y0, x1, y1, a=255):
    return lambda x, y: a if x0 <= x < x1 and y0 <= y < y1 else 0


def circle(cx, cy, r):
    return lambda x, y: 255 if (x + 0.5 - cx) ** 2 + (y + 0.5 - cy) ** 2 <= r * r else 0


def soft_circle(cx, cy, r, edge):
    def a(x, y):
        d = math.hypot(x + 0.5 - cx, y + 0.5 - cy)
        return max(0, min(255, int(round(255 * (r - d) / edge)))) if d > r - edge else 255
    return a


def union(*fs):
    return lambda x, y: max(f(x, y) for f in fs)


def minus(f, g):
    return lambda x, y: 0 if g(x, y) else f(x, y)


def star(cx, cy, r0, r1, n):
    def inside(x, y):
        px, py = x + 0.5 - cx, y + 0.5 - cy
        ang = math.atan2(py, px)
        k = (ang % (2 * math.pi / n)) / (2 * math.pi / n)
        rr = r0 + (r1 - r0) * abs(1 - 2 * k)
        return 255 if math.hypot(px, py) <= rr else 0
    return inside


CASES = []


def add(name, alpha, w=64, h=64, **meta):
    CASES.append((name, w, h, alpha, meta))


add('box_a', box(10, 20, 30, 40))
add('box_full', lambda x, y: 255)
add('empty', lambda x, y: 0)
add('pixel', box(31, 31, 32, 32))
add('block2', box(31, 31, 33, 33))
for r in (4, 8, 16, 28):
    add(f'circle{r:02d}', circle(32, 32, r))
add('tri', lambda x, y: 255 if 8 <= x < 56 and 8 <= y < 56 and y - 8 <= (x - 8) else 0)
add('ring', lambda x, y: 255 if 14 ** 2 < (x + .5 - 32) ** 2 + (y + .5 - 32) ** 2 <= 28 ** 2 else 0)
add('small_hole', minus(box(4, 4, 60, 60), box(20, 20, 30, 30)))
add('islands', lambda x, y: 255 if (4 <= x < 20 and 4 <= y < 20) or (40 <= x < 60 and 30 <= y < 60) else 0)
add('ramp', lambda x, y: x * 4)
add('Lshape', lambda x, y: 255 if (8 <= x < 24 and 8 <= y < 56) or (8 <= x < 56 and 8 <= y < 24) else 0)
for e in (0, 2, 4, 8, 16, 32):
    add(f'circle16_e{e:02d}', circle(32, 32, 16), extrude=e)
    add(f'box_a_e{e:02d}', box(10, 20, 30, 40), extrude=e)
for d in (0, 0.25, 0.5, 0.75, 1):
    add(f'circle28_d{d}', circle(32, 32, 28), detail=d)
add('odd', box(1, 1, 6, 4), w=7, h=5)
add('narrow', box(2, 2, 30, 60), w=31, h=64)
add('circle16_ppu100', circle(32, 32, 16), ppu=100, alignment=0)
add('circle16_fullrect', circle(32, 32, 16), mesh_type=0)
add('circle16_nophys', circle(32, 32, 16), physics=0)
add('soft_raw', soft_circle(32, 32, 24, 10))
add('soft_dxt', soft_circle(32, 32, 24, 10), compression=1)
add('alpha_mid', union(box(6, 6, 30, 58, 100), box(30, 20, 58, 44)))
add('npot', union(circle(40, 30, 22), box(70, 10, 95, 60)), w=100, h=70, alignment=0, ppu=50)
add('star', minus(union(star(128, 128, 50, 110, 7), circle(30, 220, 18)), circle(128, 128, 20)),
    w=256, h=256, alignment=9, pivot=(0.3, 0.7), ppu=32)
add('downscaled', minus(union(star(128, 128, 50, 110, 5), box(10, 10, 60, 40)), circle(128, 128, 25)),
    w=256, h=256, alignment=0, ppu=100, max_size=128)
OUTLINE = [[(-20.5, -20), (20, -21.25), (24, 10.5), (0, 25), (-22, 12.75)]]
PHYSICS = [[(-18, -18), (18, -18), (18, 18), (-18, 18)], [(-4.5, 22), (4, 22), (0, 28.5)]]
add('outline_single', circle(32, 32, 16), alignment=0, ppu=100, outline=OUTLINE, physics_shape=PHYSICS)
add('outline_fullrect', circle(32, 32, 16), mesh_type=0, outline=OUTLINE)
add('outline_polygon', circle(32, 32, 16), mode=3, alignment=0, outline=OUTLINE)
SHEET = [
    dict(name='sheet_circle', rect=(0, 0, 64, 64), internalID=1001),
    dict(name='sheet_islands', rect=(64, 0, 64, 64), alignment=9, pivot=(0.25, 0.6), internalID=1002),
    dict(name='sheet_fraction', rect=(10.5, 70.25, 40, 40), internalID=1003),
    dict(name='sheet_small', rect=(100, 100, 20, 20), internalID=1004),
    dict(name='sheet_detail', rect=(64, 64, 64, 64), tessellationDetail=0.5, internalID=1005),
    dict(name='sheet_custom', rect=(0, 64, 64, 64), internalID=1006,
         outline=[[(-10, -10), (12, -10), (12, 14), (-10, 14)]],
         physicsShape=[[(-8, -8), (8, -8), (0, 9.5)]]),
]
add('sheet', union(circle(32, 32, 26), box(70, 6, 86, 22), box(100, 30, 122, 58), circle(30, 90, 18),
                   star(96, 96, 10, 30, 5), box(104, 104, 116, 116)),
    w=128, h=128, mode=2, sprites=SHEET)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for name, w, h, alpha, meta in CASES:
        write_png(OUT/f'{name}.png', w, h, alpha)
        (OUT/f'{name}.png.meta').write_text(texture_meta(name, **meta))
    print(len(CASES), 'textures in', OUT)


if __name__ == '__main__':
    main()
