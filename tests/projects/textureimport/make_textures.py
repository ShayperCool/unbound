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
  Soft      512x512 smooth colour gradients and waves with a soft alpha disc and fine stripes
            (max-size downscales whose rows the Editor splits among its job workers)
  SoftWide  600x360 the same field (a non-power-of-two downscale to 256x153)
  SoftHdr   256x256 the same field as 32-bit float EXR, colour up to about 4 (float resize)
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


def exr(path, width, height, pixels):
    """An uncompressed scanline OpenEXR file with 32-bit float A, B, G, R channels; pixels: rows
    top-down of (r, g, b, a) floats."""
    def attribute(name, kind, data):
        return name.encode() + b'\0' + kind.encode() + b'\0' + struct.pack('<i', len(data)) + data
    channels = b''.join(c.encode() + b'\0' + struct.pack('<iBBBBii', 2, 0, 0, 0, 0, 1, 1) for c in 'ABGR') + b'\0'
    box = struct.pack('<iiii', 0, 0, width - 1, height - 1)
    header = (b'\x76\x2f\x31\x01' + struct.pack('<i', 2)
              + attribute('channels', 'chlist', channels)
              + attribute('compression', 'compression', b'\0')
              + attribute('dataWindow', 'box2i', box)
              + attribute('displayWindow', 'box2i', box)
              + attribute('lineOrder', 'lineOrder', b'\0')
              + attribute('pixelAspectRatio', 'float', struct.pack('<f', 1.0))
              + attribute('screenWindowCenter', 'v2f', struct.pack('<ff', 0.0, 0.0))
              + attribute('screenWindowWidth', 'float', struct.pack('<f', 1.0)) + b'\0')
    lines = []
    for y, row in enumerate(pixels):
        data = b''.join(struct.pack(f'<{width}f', *(p[c] for p in row)) for c in (3, 2, 1, 0))
        lines.append(struct.pack('<ii', y, len(data)) + data)
    offset = len(header) + 8 * height
    table = b''
    for line in lines:
        table += struct.pack('<Q', offset)
        offset += len(line)
    path.write_bytes(header + table + b''.join(lines))


def soft_field(w, h, x, y):
    """Smooth colour (0..1) and soft alpha at texel (x, y) of a w x h image."""
    u, v = x / w, y / h
    r = 0.5 + 0.45 * math.sin(6.1 * u + 2.3 * v)
    g = 0.5 + 0.45 * math.cos(4.7 * v - 1.9 * u * u)
    b = (u * 0.7 + v * 0.3) * (0.75 + 0.25 * math.sin(40 * u) * math.sin(37 * v))
    if (x // 3 + y // 5) % 7 == 0:
        b = 1.0 - b
    d = math.hypot(u - 0.5, (v - 0.47) * w / h)
    a = max(0.0, min(1.0, (0.42 - d) / 0.12))
    return r, g, b, a


def soft(w, h):
    rows = [[tuple(round(c * 255) for c in soft_field(w, h, x, y)) for x in range(w)] for y in range(h)]
    return w, h, rows


def soft_hdr(w, h):
    def texel(x, y):
        r, g, b, a = soft_field(w, h, x, y)
        boost = 1.0 + 3.0 * max(0.0, math.sin(9 * x / w) * math.cos(7 * y / h))
        return (r * boost, g * boost * 0.8, b, a)
    return w, h, [[texel(x, y) for x in range(w)] for y in range(h)]


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



def deep(w, h, seed, channels):
    """16-bit texels: low bytes around every 8-bit rounding boundary (0x7f, 0x80, 0x81, ...)."""
    rng = Lcg(seed)
    rows = []
    for y in range(h):
        row = []
        for x in range(w):
            texel = []
            for c in range(channels):
                high = (x * 8 + y * 3 + c * 85) & 0xff if c == 0 else rng.byte()
                low = (0x7e, 0x7f, 0x80, 0x81, 0x00, 0xff, rng.byte())[(x + y + c) % 7]
                texel.append(high << 8 | low)
            row.append(tuple(texel))
        rows.append(row)
    return w, h, rows


def packbits(data):
    """PackBits: literal runs of at most 128 bytes (enough for a valid RLE stream)."""
    out = bytearray()
    for at in range(0, len(data), 128):
        chunk = data[at:at + 128]
        out.append(len(chunk) - 1)
        out += chunk
    return bytes(out)


def matted(w, h, seed, channels, depth=8, below=False):
    """Photoshop's flattened colour over white: alpha steps through 0, 1, 2, ..., 255 and a few
    random values; colour c is stored as c * a / 255 + 255 - a, rounded. A 5th channel (if any)
    is a spot channel of noise."""
    rng = Lcg(seed)
    rows = []
    for y in range(h):
        row = []
        for x in range(w):
            a = (y * w + x) % 256 if (x + y) % 5 else rng.byte()
            colour = [rng.byte() for _ in range(3)]
            stored = [(c * a + 255 * (255 - a) + 127) // 255 for c in colour]
            if below:
                # Colour under the white blend (not what Photoshop writes).
                stored = [rng.byte() * (255 - a) // 255 for _ in range(3)]
            texel = (stored[0], a) if channels == 2 else tuple(stored) + (a,)
            if channels == 5:
                texel += (rng.byte(),)
            if depth == 16:
                texel = tuple(v * 257 + (rng.byte() & 0x7f if 0 < v < 255 else 0) for v in texel)
            row.append(texel)
        rows.append(row)
    return w, h, rows


def psd(path, width, height, pixels, depth, rle, merged_alpha=False):
    """A flattened Photoshop file: pixels rows top-down of channel tuples (1 or 2 gray and alpha,
    3 RGB, 4 RGBA, 5 RGBA and a spot channel), 8- or 16-bit, raw or PackBits-compressed planar
    channels. Without layers, or with `merged_alpha` one empty layer and a negative layer count
    (Photoshop's mark that the first extra channel is the flattened image's transparency)."""
    channels = len(pixels[0][0])
    mode = 1 if channels <= 2 else 3
    header = (b'8BPS' + struct.pack('>H', 1) + b'\0' * 6
              + struct.pack('>HIIHH', channels, height, width, depth, mode))
    planes = []
    for c in range(channels):
        for row in pixels:
            if depth == 16:
                planes.append(b''.join(struct.pack('>H', p[c]) for p in row))
            else:
                planes.append(bytes(p[c] for p in row))
    if rle:
        packed = [packbits(line) for line in planes]
        data = struct.pack('>H', 1) + b''.join(struct.pack('>H', len(p)) for p in packed) + b''.join(packed)
    else:
        data = struct.pack('>H', 0) + b''.join(planes)
    layers = b''
    if merged_alpha:
        # One empty layer (0x0, four channels of 2 bytes: their compression code only).
        record = (struct.pack('>iiiiH', 0, 0, 0, 0, 4)
                  + b''.join(struct.pack('>hI', c, 2) for c in (-1, 0, 1, 2))
                  + b'8BIMnorm' + bytes([255, 0, 0, 0])
                  + struct.pack('>I', 12) + struct.pack('>II', 0, 0) + b'\0\0\0\0')
        info = struct.pack('>h', -1) + record + b'\0\0' * 4
        info += b'\0' * (len(info) % 2)
        layers = struct.pack('>I', len(info)) + info + struct.pack('>I', 0)
    path.write_bytes(header + struct.pack('>II', 0, 0) + struct.pack('>I', len(layers)) + layers + data)


def lzw(data):
    """TIFF LZW (MSB-first codes, Clear at the start and before the code width would grow)."""
    out, acc, bits = bytearray(), 0, 0

    def put(code):
        nonlocal acc, bits
        acc = acc << 9 | code
        bits += 9
        while bits >= 8:
            bits -= 8
            out.append(acc >> bits & 0xff)
    table = {bytes([i]): i for i in range(256)}
    put(256)
    w = b''
    for c in data:
        wc = w + bytes([c])
        if wc in table:
            w = wc
            continue
        put(table[w])
        table[wc] = len(table) + 2
        w = bytes([c])
        if len(table) + 2 >= 510:
            put(256)
            table = {bytes([i]): i for i in range(256)}
    if w:
        put(table[w])
    put(257)
    if bits:
        out.append(acc << (8 - bits) & 0xff)
    return bytes(out)


def tiff(path, width, height, pixels, depth=8, extra=(), photometric=2, compression=1, predictor=1):
    """A little-endian baseline TIFF of one strip: pixels rows top-down of sample tuples, 8 or 16
    bits, `extra` the ExtraSamples values (0 unspecified, 1 associated alpha, 2 unassociated),
    photometric 2 (RGB) or 1 (gray), uncompressed (1) or LZW (5) with the horizontal differencing
    predictor (2) or none (1)."""
    samples = len(pixels[0][0])
    fmt = '<H' if depth == 16 else 'B'
    mask = 0xffff if depth == 16 else 0xff
    rows = []
    for row in pixels:
        values = [list(p) for p in row]
        if predictor == 2:
            for x in range(width - 1, 0, -1):
                values[x] = [(a - b) & mask for a, b in zip(values[x], values[x - 1])]
        rows.append(b''.join(struct.pack(fmt, v) for p in values for v in p))
    data = b''.join(rows)
    if compression == 5:
        data = lzw(data)
    entries = []
    def entry(tag, kind, values):
        entries.append((tag, kind, values if isinstance(values, (list, tuple)) else [values]))
    entry(256, 3, width)
    entry(257, 3, height)
    entry(258, 3, [depth] * samples)
    entry(259, 3, compression)
    entry(262, 3, photometric)
    entry(273, 4, 0)
    entry(277, 3, samples)
    entry(278, 3, height)
    entry(279, 4, len(data))
    entry(284, 3, 1)
    if predictor != 1:
        entry(317, 3, predictor)
    if extra:
        entry(338, 3, list(extra))
    entries.sort()
    ifd_offset = 8 + len(data) + (len(data) & 1)
    extra_area = ifd_offset + 2 + 12 * len(entries) + 4
    blobs = b''
    ifd = struct.pack('<H', len(entries))
    for tag, kind, values in entries:
        size = 2 if kind == 3 else 4
        packed = b''.join(struct.pack('<H' if kind == 3 else '<I', v) for v in values)
        if tag == 273:
            packed = struct.pack('<I', 8)
        if len(packed) <= 4:
            ifd += struct.pack('<HHI', tag, kind, len(values)) + packed.ljust(4, b'\0')
        else:
            ifd += struct.pack('<HHII', tag, kind, len(values), extra_area + len(blobs))
            blobs += packed
    ifd += struct.pack('<I', 0)
    path.write_bytes(b'II*\0' + struct.pack('<I', ifd_offset) + data + b'\0' * (len(data) & 1) + ifd + blobs)


def png16(path, width, height, pixels):
    """A 16-bit RGBA PNG; pixels rows top-down of (r, g, b, a) 16-bit values."""
    raw = b''.join(b'\0' + b''.join(struct.pack('>4H', *p) for p in row) for row in pixels)

    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
    path.write_bytes(b'\x89PNG\r\n\x1a\n'
                     + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 16, 6, 0, 0, 0))
                     + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b''))


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
    'Soft': lambda: soft(512, 512),
    'SoftWide': lambda: soft(600, 360),
    'SoftHdr': lambda: soft_hdr(256, 256),
}
# Sources written as float EXR rather than PNG.
FLOAT_SOURCES = {'SoftHdr'}
# Sources written as other files: (writer, arguments after the image).
FILE_SOURCES = {
    'Matte': ('psd', 8, True, True),
    'Matte5': ('psd', 8, True, True),
    'MatteFlat': ('psd', 8, False, False),
    'GrayAlpha': ('psd', 8, False),
    'GrayAlphaMarked': ('psd', 8, False, True),
    'MatteBelow': ('psd', 8, False, True),
    'Matte16': ('psd', 16, False, True),
    'Deep': ('psd', 16, False),
    'DeepAlphaRle': ('psd', 16, True),
    'DeepGray': ('psd', 16, False),
    'PsdRle': ('psd', 8, True),
    'Deep16Png': ('png16',),
    'TiffExtraLzw': ('tiff', dict(extra=(0,), compression=5, predictor=2)),
    'TiffExtraRaw': ('tiff', dict(extra=(0,))),
    'TiffUnassociated': ('tiff', dict(extra=(2,), compression=5, predictor=2)),
    'TiffAssociated': ('tiff', dict(extra=(1,))),
    'TiffRgb': ('tiff', dict(compression=5, predictor=2)),
    'TiffTwoExtra': ('tiff', dict(extra=(0, 0))),
    'TiffDeepExtra': ('tiff', dict(depth=16, extra=(0,))),
    'TiffGrayExtra': ('tiff', dict(photometric=1, extra=(0,))),
}
SOURCES.update({
    'Matte': lambda: matted(32, 32, 25, 4),
    'Matte5': lambda: matted(32, 32, 26, 5),
    'MatteFlat': lambda: matted(32, 32, 25, 4),
    'GrayAlpha': lambda: matted(32, 32, 27, 2),
    'GrayAlphaMarked': lambda: matted(32, 32, 27, 2),
    'MatteBelow': lambda: matted(32, 32, 28, 4, below=True),
    'Matte16': lambda: matted(32, 32, 29, 4, depth=16),
    'Deep': lambda: deep(32, 32, 21, 3),
    'DeepAlphaRle': lambda: deep(32, 32, 22, 4),
    'DeepGray': lambda: deep(32, 32, 23, 1),
    'PsdRle': lambda: noise(32, 32, 24),
    'Deep16Png': lambda: deep(32, 32, 22, 4),
    'TiffExtraLzw': lambda: noise(32, 32, 30),
    'TiffExtraRaw': lambda: noise(32, 32, 30),
    'TiffUnassociated': lambda: noise(32, 32, 30),
    'TiffAssociated': lambda: noise(32, 32, 30),
    'TiffRgb': lambda: (32, 32, [[p[:3] for p in row] for row in noise(32, 32, 30)[2]]),
    'TiffTwoExtra': lambda: (32, 32, [[p + (p[0] ^ 0x5a,) for p in row] for row in noise(32, 32, 30)[2]]),
    'TiffDeepExtra': lambda: deep(32, 32, 31, 4),
    'TiffGrayExtra': lambda: (32, 32, [[p[:1] + p[3:] for p in row] for row in noise(32, 32, 30)[2]]),
})

# name: (source, settings). Settings default to a Default-type, sRGB, box-filtered texture with
# mipmaps, input alpha and NPOT ToNearest (Unity's defaults); every case is readable and stored as
# RGBA32 (Standalone override, default platform uncompressed), or RGBAFloat with format=float.
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
    'MaxSizeSoft': ('Soft', 'maxsize=128'),
    'MaxSizeSoftWide': ('SoftWide', 'maxsize=256 npot=none srgb=0'),
    'MaxSizeSoftHdr': ('SoftHdr', 'maxsize=128 mips=0 format=float'),
    # Photoshop files (16-bit raw RGB, 16-bit PackBits RGBA, 16-bit gray, 8-bit PackBits RGBA)
    # and a 16-bit PNG: how deep channels become bytes, before and after mipmapping.
    'Psd16Rgb': ('Deep', ''),
    'Psd16RgbLinear': ('Deep', 'srgb=0'),
    'Psd16RgbaRle': ('DeepAlphaRle', ''),
    'Psd16Gray': ('DeepGray', 'srgb=0'),
    'Psd8RgbaRle': ('PsdRle', ''),
    'Png16Rgba': ('Deep16Png', ''),
    # Flattened colour over white with a merged-transparency mark (default, Remove Matte on and
    # off), the same without the mark, 5 channels (the 5th a spot channel) and gray with alpha.
    'PsdMatte': ('Matte', 'mips=0 srgb=0'),
    'PsdMatteOn': ('Matte', 'mips=0 srgb=0 matte=1'),
    'PsdMatteOff': ('Matte', 'mips=0 srgb=0 matte=0'),
    'PsdMatteFlat': ('MatteFlat', 'mips=0 srgb=0'),
    'PsdMatte5': ('Matte5', 'mips=0 srgb=0'),
    'PsdGrayAlpha': ('GrayAlpha', 'mips=0 srgb=0'),
    # matte=none: ProbeRunner.Create removes the pSDRemoveMatte line from its .meta (as in metas
    # written before the setting existed).
    'PsdMatteNoKey': ('Matte', 'mips=0 srgb=0 matte=none'),
    # Remove Matte on: without the mark, 5 channels, colour under the white blend, 16 bits; and a
    # marked gray file with alpha.
    'PsdMatteFlatOn': ('MatteFlat', 'mips=0 srgb=0 matte=1'),
    'PsdMatte5On': ('Matte5', 'mips=0 srgb=0 matte=1'),
    'PsdMatteBelowOn': ('MatteBelow', 'mips=0 srgb=0 matte=1'),
    'PsdMatte16On': ('Matte16', 'mips=0 srgb=0 matte=1'),
    'PsdGrayAlphaMarked': ('GrayAlphaMarked', 'mips=0 srgb=0 matte=1'),
    # TIFF files: RGB with an extra sample of unspecified meaning, LZW with the horizontal
    # differencing predictor (also with mipmaps) and uncompressed; unassociated and associated
    # alpha; plain RGB; two unspecified extra samples; 16 bits; gray with an extra sample.
    'TiffExtraLzw': ('TiffExtraLzw', 'mips=0 srgb=0'),
    'TiffExtraLzwMips': ('TiffExtraLzw', ''),
    'TiffExtraRaw': ('TiffExtraRaw', 'mips=0 srgb=0'),
    'TiffUnassociated': ('TiffUnassociated', 'mips=0 srgb=0'),
    'TiffAssociated': ('TiffAssociated', 'mips=0 srgb=0'),
    'TiffRgb': ('TiffRgb', 'mips=0 srgb=0'),
    'TiffTwoExtra': ('TiffTwoExtra', 'mips=0 srgb=0'),
    'TiffDeepExtra': ('TiffDeepExtra', 'mips=0 srgb=0'),
    'TiffGrayExtra': ('TiffGrayExtra', 'mips=0 srgb=0'),
}


def main():
    textures = PROBE/'Assets/Textures'
    textures.mkdir(parents=True, exist_ok=True)
    images = {name: make() for name, make in SOURCES.items()}
    for name, (source, _) in CASES.items():
        if source in FILE_SOURCES:
            kind, *args = FILE_SOURCES[source]
            if kind == 'psd':
                psd(textures/f'{name}.psd', *images[source], *args)
            elif kind == 'tiff':
                tiff(textures/f'{name}.tif', *images[source], **args[0])
            else:
                png16(textures/f'{name}.png', *images[source])
        elif source in FLOAT_SOURCES:
            exr(textures/f'{name}.exr', *images[source])
        else:
            png(textures/f'{name}.png', *images[source])
    lines = [f'{name} {settings}'.rstrip() for name, (_, settings) in CASES.items()]
    (PROBE/'Assets/Editor').mkdir(parents=True, exist_ok=True)
    (PROBE/'Assets/Editor/TextureCases.txt').write_text('\n'.join(lines) + '\n')


if __name__ == '__main__':
    main()
