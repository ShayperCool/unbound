#!/usr/bin/env python3
"""Writes the probe's DDS, KTX and ASTC source files (deterministic bytes) into Assets/Sources.

Run once to (re)create them; the files are committed. Each block's bytes come from a fixed
generator, so the files are the same on every run.
"""
from pathlib import Path
import struct

OUT = Path(__file__).resolve().parent/'Assets'/'Sources'


def rng(seed):
    state = seed & 0xFFFFFFFF
    while True:
        state = (state * 1103515245 + 12345) & 0x7FFFFFFF
        yield state >> 7 & 0xFF


def data(seed, size):
    gen = rng(seed)
    return bytes(next(gen) for _ in range(size))


def mips(width, height, count):
    for level in range(count):
        yield max(1, width >> level), max(1, height >> level)


def blocks(width, height, block_w, block_h, block_bytes):
    return ((width + block_w - 1) // block_w) * ((height + block_h - 1) // block_h) * block_bytes


# DDS -------------------------------------------------------------------------------------------

DDSD_CAPS, DDSD_HEIGHT, DDSD_WIDTH, DDSD_PITCH = 0x1, 0x2, 0x4, 0x8
DDSD_PIXELFORMAT, DDSD_MIPMAPCOUNT, DDSD_LINEARSIZE = 0x1000, 0x20000, 0x80000
DDSCAPS_COMPLEX, DDSCAPS_TEXTURE, DDSCAPS_MIPMAP = 0x8, 0x1000, 0x400000
DDSCAPS2_CUBEMAP_ALL = 0x200 | 0x400 | 0x800 | 0x1000 | 0x2000 | 0x4000 | 0x8000
DDPF_FOURCC, DDPF_RGB, DDPF_ALPHAPIXELS = 0x4, 0x40, 0x1


def dds(name, width, height, mip_count, fourcc=None, dxgi=None, block=(4, 4, 8), rgba=False, cube=False, seed=1):
    faces = 6 if cube else 1
    payload = b''
    for face in range(faces):
        for level, (w, h) in enumerate(mips(width, height, mip_count)):
            size = w * h * 4 if rgba else blocks(w, h, *block)
            payload += data(seed * 1000 + face * 100 + level, size)
    flags = DDSD_CAPS | DDSD_HEIGHT | DDSD_WIDTH | DDSD_PIXELFORMAT
    flags |= DDSD_PITCH if rgba else DDSD_LINEARSIZE
    if mip_count > 1:
        flags |= DDSD_MIPMAPCOUNT
    pitch = width * 4 if rgba else blocks(width, height, *block)
    if rgba:
        pixel_format = struct.pack('<II4sIIIII', 32, DDPF_RGB | DDPF_ALPHAPIXELS, b'\0\0\0\0', 32,
                                   0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000)
    else:
        pixel_format = struct.pack('<II4sIIIII', 32, DDPF_FOURCC, (b'DX10' if dxgi else fourcc), 0, 0, 0, 0, 0)
    caps = DDSCAPS_TEXTURE | (DDSCAPS_COMPLEX | DDSCAPS_MIPMAP if mip_count > 1 else 0) | (DDSCAPS_COMPLEX if cube else 0)
    header = struct.pack('<4sIIIIIII', b'DDS ', 124, flags, height, width, pitch, 0, mip_count)
    header += b'\0' * 44 + pixel_format + struct.pack('<IIIII', caps, DDSCAPS2_CUBEMAP_ALL if cube else 0, 0, 0, 0)
    if dxgi:
        header += struct.pack('<IIIII', dxgi, 3, 4 if cube else 0, 1, 0)
    (OUT/name).write_bytes(header + payload)


# KTX 1 -----------------------------------------------------------------------------------------

KTX_ID = bytes([0xAB, 0x4B, 0x54, 0x58, 0x20, 0x31, 0x31, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A])


def ktx(name, width, height, mip_count, internal_format, base_format, block, seed):
    body = b''
    for level, (w, h) in enumerate(mips(width, height, mip_count)):
        level_data = data(seed * 1000 + level, blocks(w, h, *block))
        body += struct.pack('<I', len(level_data)) + level_data
    header = KTX_ID + struct.pack('<13I', 0x04030201, 0, 1, 0, internal_format, base_format, width, height, 0, 0, 1, mip_count, 0)
    (OUT/name).write_bytes(header + body)


# ASTC ------------------------------------------------------------------------------------------

def astc(name, width, height, block_w, block_h, seed):
    header = struct.pack('<I', 0x5CA1AB13) + bytes([block_w, block_h, 1])
    header += width.to_bytes(3, 'little') + height.to_bytes(3, 'little') + (1).to_bytes(3, 'little')
    (OUT/name).write_bytes(header + data(seed, blocks(width, height, block_w, block_h, 16)))


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    dds('BC1.dds', 16, 16, 5, fourcc=b'DXT1', block=(4, 4, 8), seed=1)
    dds('BC3.dds', 16, 16, 5, fourcc=b'DXT5', block=(4, 4, 16), seed=2)
    dds('BC1NoMips.dds', 16, 8, 1, fourcc=b'DXT1', block=(4, 4, 8), seed=3)
    dds('BC1Npot.dds', 12, 20, 1, fourcc=b'DXT1', block=(4, 4, 8), seed=4)
    dds('BC7.dds', 16, 16, 5, dxgi=98, block=(4, 4, 16), seed=5)
    dds('BC7Srgb.dds', 16, 16, 1, dxgi=99, block=(4, 4, 16), seed=6)
    dds('BC5.dds', 8, 8, 1, dxgi=83, block=(4, 4, 16), seed=7)
    dds('BC6H.dds', 8, 8, 1, dxgi=95, block=(4, 4, 16), seed=8)
    dds('RGBA8.dds', 8, 8, 4, rgba=True, seed=9)
    dds('CubeBC1.dds', 8, 8, 1, fourcc=b'DXT1', block=(4, 4, 8), cube=True, seed=10)
    # GL_COMPRESSED_RGBA8_ETC2_EAC (0x9278), GL_RGBA; GL_COMPRESSED_RGB8_ETC2 (0x9274), GL_RGB;
    # GL_COMPRESSED_RGBA_ASTC_4x4_KHR (0x93B0), GL_RGBA.
    ktx('ETC2RGBA.ktx', 8, 8, 4, 0x9278, 0x1908, (4, 4, 16), seed=11)
    ktx('ETC2RGB.ktx', 8, 8, 1, 0x9274, 0x1907, (4, 4, 8), seed=12)
    ktx('ASTC4x4.ktx', 8, 8, 1, 0x93B0, 0x1908, (4, 4, 16), seed=13)
    astc('Block4x4.astc', 8, 8, 4, 4, seed=14)
    astc('Block6x6.astc', 12, 12, 6, 6, seed=15)
    astc('Block8x8Npot.astc', 10, 10, 8, 8, seed=16)


if __name__ == '__main__':
    main()
