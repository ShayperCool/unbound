#!/usr/bin/env python3
"""Writes the texture compression probe's source images and importer cases (deterministic).

    python3 make_textures.py

Every case is one PNG (or EXR) in Assets/Textures and one line of Assets/Editor/TextureCases.txt:
`<name> key=value ...`. `ProbeRunner.Create` applies the settings to each importer in the Editor
(which writes the .meta files) and builds the scene. `format=` is a Standalone override,
`android=` an Android override (both at the case's `quality`); neither changes the other
target's import. The image writers and fields are the
textureimport probe's.

Source images (written top row first as PNG stores them):
  Smooth  64x64 opaque gradients with a few hard edges
  Noise   64x64 random colour and alpha
  Cutout  64x64 random colour, alpha a soft disc, thin lines and a transparent border
  Soft    64x64 smooth colour with a soft alpha disc and fine stripes
  Flat    64x64 4x4 blocks that are constant, two-coloured, or hold alpha 0 and 255 only
  Height  64x64 gray (r=g=b) bumps, ramps, steps and single texels
  Normals 64x64 tangent-space normals, smoothly varying
  Hdr     64x64 the Soft field as 32-bit float EXR, colour up to about 4
  Odd     30x18 random colour and alpha (no side a multiple of 4)
  Npot    20x12 random colour and alpha (both sides multiples of 4)
  NpotOpaque 20x12 the top left of Smooth (opaque)
  Tiny    2x2 random colour and alpha (below one block)
  Wide    64x16 random colour and alpha (power-of-two sides, not square)
"""
import importlib.util
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
_spec = importlib.util.spec_from_file_location('textureimport_make', PROBE.parent/'textureimport/make_textures.py')
base = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(base)


def flat():
    rng = base.Lcg(11)
    rows = [[None] * 64 for _ in range(64)]
    for by in range(16):
        for bx in range(16):
            kind = (bx + 3 * by) % 4
            c0 = tuple(rng.byte() for _ in range(3))
            c1 = tuple(rng.byte() for _ in range(3))
            for y in range(4):
                for x in range(4):
                    if kind == 0:
                        p = c0 + (255,)
                    elif kind == 1:
                        p = (c0 if (x + y) % 2 else c1) + (255,)
                    elif kind == 2:
                        p = c0 + (0 if x < 2 else 255,)
                    else:
                        p = c1 + (rng.byte(),)
                    rows[by * 4 + y][bx * 4 + x] = p
    return 64, 64, rows


SOURCES = {
    'Smooth': base.smooth,
    'Noise': lambda: base.noise(64, 64, 1),
    'Cutout': base.cutout,
    'Soft': lambda: base.soft(64, 64),
    'Flat': flat,
    'Height': base.height,
    'Normals': base.normals,
    'Hdr': lambda: base.soft_hdr(64, 64),
    'Odd': lambda: base.noise(30, 18, 5),
    'Npot': lambda: base.noise(20, 12, 6),
    'NpotOpaque': lambda: (20, 12, [row[:20] for row in base.smooth()[2][:12]]),
    'Tiny': lambda: base.noise(2, 2, 7),
    'Wide': lambda: base.noise(64, 16, 8),
}

# name: (source, settings). Every case is readable, with mips, sRGB and on the default platform
# (no override) unless its settings say otherwise.
CASES = {
    # Automatic formats on the Standalone target: Normal, High and Low compression, three
    # compressor qualities.
    'SmoothNormal': ('Smooth', 'compression=normal'),
    'SmoothNormalQ0': ('Smooth', 'compression=normal quality=0'),
    'SmoothNormalQ100': ('Smooth', 'compression=normal quality=100'),
    'SmoothHigh': ('Smooth', 'compression=high'),
    'SmoothHighQ0': ('Smooth', 'compression=high quality=0'),
    'SmoothHighQ100': ('Smooth', 'compression=high quality=100'),
    'SmoothLow': ('Smooth', 'compression=low'),
    'SmoothLinear': ('Smooth', 'compression=normal srgb=0'),
    'SmoothNoMips': ('Smooth', 'compression=normal mips=0'),
    'SmoothUnreadable': ('Smooth', 'compression=normal readable=0'),
    'NoiseNormal': ('Noise', 'compression=normal'),
    'NoiseNormalQ0': ('Noise', 'compression=normal quality=0'),
    'NoiseNormalQ100': ('Noise', 'compression=normal quality=100'),
    'NoiseHigh': ('Noise', 'compression=high'),
    'NoiseHighQ100': ('Noise', 'compression=high quality=100'),
    'NoiseLow': ('Noise', 'compression=low'),
    'NoiseAlphaNone': ('Noise', 'compression=normal alpha=none'),
    'CutoutNormal': ('Cutout', 'compression=normal'),
    'SoftNormal': ('Soft', 'compression=normal'),
    'SoftHigh': ('Soft', 'compression=high android=ETC2_RGBA8'),
    'FlatNormal': ('Flat', 'compression=normal android=ETC2_RGB4'),
    'FlatOpaque': ('Flat', 'compression=normal alpha=none'),
    'HeightRed': ('Height', 'type=single channel=red compression=normal'),
    'HeightRedQ0': ('Height', 'type=single channel=red compression=normal quality=0'),
    'HeightRedHigh': ('Height', 'type=single channel=red compression=high'),
    'HeightAlpha': ('Height', 'type=single channel=alpha compression=normal'),
    'NormalsNormal': ('Normals', 'type=normal compression=normal'),
    'NormalsHigh': ('Normals', 'type=normal compression=high android=ETC2_RGB4'),
    'NormalsLow': ('Normals', 'type=normal compression=low android=ETC_RGB4'),
    'HdrNormal': ('Hdr', 'compression=normal'),
    'HdrHigh': ('Hdr', 'compression=high'),
    'HdrNormalQ0': ('Hdr', 'compression=normal quality=0'),
    'OddNormal': ('Odd', 'compression=normal npot=none'),
    'NpotNormal': ('Npot', 'compression=normal npot=none'),
    'NpotOpaqueNormal': ('NpotOpaque', 'compression=normal npot=none'),
    'NpotDXT5': ('Npot', 'format=DXT5 npot=none'),
    'TinyNormal': ('Tiny', 'compression=normal'),
    'WideNormal': ('Wide', 'compression=normal'),
    'HdrLow': ('Hdr', 'compression=low'),
    # Explicit formats (a Standalone override).
    'SmoothDXT1': ('Smooth', 'format=DXT1 android=ETC_RGB4'),
    'NoiseDXT1': ('Noise', 'format=DXT1 android=ETC2_RGB4'),
    'NoiseDXT5': ('Noise', 'format=DXT5 android=ETC2_RGBA8'),
    'NoiseDXT5Q0': ('Noise', 'format=DXT5 quality=0 android=ETC2_RGBA8'),
    'NoiseDXT5Q100': ('Noise', 'format=DXT5 quality=100 android=ETC2_RGBA8'),
    'HeightBC4': ('Height', 'format=BC4 android=EAC_R'),
    'NormalsBC5': ('Normals', 'format=BC5 srgb=0 android=EAC_RG'),
    'SmoothBC7': ('Smooth', 'format=BC7 android=ETC2_RGB4_PUNCHTHROUGH_ALPHA'),
    'NoiseBC7': ('Noise', 'format=BC7 android=ASTC_6x6'),
    'NoiseBC7Q0': ('Noise', 'format=BC7 quality=0 android=ASTC_4x4'),
    'NoiseBC7Q100': ('Noise', 'format=BC7 quality=100 android=ASTC_8x8'),
    'HdrBC6H': ('Hdr', 'format=BC6H android=ASTC_HDR_6x6'),
    'SmoothDXT1Crunched': ('Smooth', 'format=DXT1Crunched android=ETC_RGB4Crunched'),
    'OddDXT5': ('Odd', 'format=DXT5 npot=none android=ETC2_RGBA8'),
    # Uncompressed sources for the runtime controls (Compress, CopyTexture, LoadRawTextureData).
    'NoiseRGBA32': ('Noise', 'format=RGBA32'),
    'NoiseUnreadable': ('Noise', 'format=RGBA32 readable=0'),
    # Writes to a compressed import, its copies, and more sizes and texture types.
    'SmoothWrite': ('Smooth', 'compression=normal'),
    'NpotNoMips': ('Npot', 'compression=normal npot=none mips=0 android=ETC_RGB4'),
    'SmoothSprite': ('Smooth', 'type=sprite compression=normal android=ETC2_RGB4'),
    'NoiseGui': ('Noise', 'type=gui compression=normal'),
    # Explicit byte formats of a coloured, transparent source; a compressed copy destination.
    'NoiseRGB24': ('Noise', 'format=RGB24'),
    'NoiseR8': ('Noise', 'format=R8 srgb=0'),
    'FlatCopyDst': ('Flat', 'compression=normal alpha=none'),
    'SmoothRGB24': ('Smooth', 'format=RGB24'),
    'HeightR8': ('Height', 'format=R8 srgb=0'),
    'NormalsRG16': ('Normals', 'format=RG16 srgb=0'),
    'OddRGBA32': ('Odd', 'format=RGBA32 npot=none mips=0'),
    # Additional import-encoder controls: flat channels, alpha edges, linear colour,
    # rectangular levels and mipless non-power-of-two sizes.
    'FlatBC7': ('Flat', 'format=BC7'),
    'CutoutBC7': ('Cutout', 'format=BC7 quality=100'),
    'NoiseLinearBC7': ('Noise', 'format=BC7 srgb=0'),
    'NoiseBC7Q49': ('Noise', 'format=BC7 quality=49'),
    'NoiseBC7Q99': ('Noise', 'format=BC7 quality=99'),
    'NpotBC7': ('Npot', 'format=BC7 npot=none mips=0'),
    'OddBC7': ('Odd', 'format=BC7 npot=none'),
    'WideBC7': ('Wide', 'format=BC7'),
    'FlatBC4': ('Flat', 'format=BC4 srgb=0'),
    'FlatBC5': ('Flat', 'format=BC5 srgb=0'),
    'NoiseBC4': ('Noise', 'format=BC4 srgb=0'),
    'NoiseBC5': ('Noise', 'format=BC5 srgb=0'),
    'WideBC5': ('Wide', 'format=BC5 srgb=0'),
    'NpotBC5': ('Npot', 'format=BC5 srgb=0 npot=none mips=0'),
    'HdrBC6HQ100': ('Hdr', 'format=BC6H quality=100'),
    'HdrBC6HQ49': ('Hdr', 'format=BC6H quality=49'),

}


def main():
    textures = PROBE/'Assets/Textures'
    textures.mkdir(parents=True, exist_ok=True)
    images = {name: make() for name, make in SOURCES.items()}
    lines = []
    for name, (source, settings) in CASES.items():
        if source == 'Hdr':
            base.exr(textures/f'{name}.exr', *images[source])
        else:
            base.png(textures/f'{name}.png', *images[source])
        lines.append(f'{name} {settings}')
    (PROBE/'Assets/Editor/TextureCases.txt').write_text('\n'.join(lines) + '\n')
    return 0


if __name__ == '__main__':
    sys.exit(main())
