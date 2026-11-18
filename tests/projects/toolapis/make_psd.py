#!/usr/bin/env python3
"""Writes Assets/Layers.psd: a 64x48 RGB Photoshop document with three raw (uncompressed) layers,
the third hidden, and its flattened image. Deterministic; run it again to regenerate the file."""
from pathlib import Path
import struct

W, H = 64, 48


def layer_pixels(kind):
    """RGBA rows top-down for one layer, transparent outside its shape."""
    px = [[(0, 0, 0, 0)] * W for _ in range(H)]
    for y in range(H):
        for x in range(W):
            if kind == 'box' and 4 <= x < 28 and 6 <= y < 30:
                px[y][x] = (220, 40, 30, 255)
            elif kind == 'disc' and (x - 44) ** 2 + (y - 30) ** 2 <= 12 ** 2:
                # Soft edge: alpha falls off over the last two pixels of the radius.
                d = ((x - 44) ** 2 + (y - 30) ** 2) ** 0.5
                a = 255 if d <= 10 else int(255 * (12 - d) / 2)
                px[y][x] = (30, 180, 60, max(a, 0))
            elif kind == 'hidden' and 30 <= x < 60 and 2 <= y < 10:
                px[y][x] = (20, 60, 230, 255)
    return px


def bounds(px):
    ys = [y for y in range(H) if any(p[3] for p in px[y])]
    xs = [x for x in range(W) if any(px[y][x][3] for y in range(H))]
    return min(ys), min(xs), max(ys) + 1, max(xs) + 1


def pascal(name):
    raw = bytes([len(name)]) + name.encode('ascii')
    return raw + b'\0' * (-len(raw) % 4)


def block(key, payload):
    return b'8BIM' + key + struct.pack('>I', len(payload)) + payload


def unicode_name(name):
    return struct.pack('>I', len(name)) + name.encode('utf-16-be')


def main():
    layers = [('Box', 'box', True, 1), ('Disc', 'disc', True, 2), ('Hidden', 'hidden', False, 3)]
    records, data = b'', b''
    flat = [[(255, 255, 255, 0)] * W for _ in range(H)]
    for name, kind, visible, layer_id in layers:
        px = layer_pixels(kind)
        top, left, bottom, right = bounds(px)
        channels = []
        for channel, index in ((-1, 3), (0, 0), (1, 1), (2, 2)):
            raw = bytes(px[y][x][index] for y in range(top, bottom) for x in range(left, right))
            channels.append((channel, struct.pack('>H', 0) + raw))
        extra = struct.pack('>I', 0) + struct.pack('>I', 0) + pascal(name)
        extra += block(b'luni', unicode_name(name)) + block(b'lyid', struct.pack('>I', layer_id))
        records += struct.pack('>iiiiH', top, left, bottom, right, len(channels))
        records += b''.join(struct.pack('>hI', c, len(d)) for c, d in channels)
        records += b'8BIMnorm' + bytes([255, 0, 0 if visible else 2, 0]) + struct.pack('>I', len(extra)) + extra
        data += b''.join(d for _, d in channels)
        if visible:
            for y in range(H):
                for x in range(W):
                    r, g, b, a = px[y][x]
                    if a:
                        pr, pg, pb, pa = flat[y][x]
                        t = a / 255
                        flat[y][x] = (round(r * t + pr * (1 - t)), round(g * t + pg * (1 - t)),
                                      round(b * t + pb * (1 - t)), max(a, pa))
    info = struct.pack('>h', len(layers)) + records + data
    info += b'\0' * (len(info) % 2)
    layer_section = struct.pack('>I', len(info)) + info + struct.pack('>I', 0)
    out = b'8BPS' + struct.pack('>H', 1) + b'\0' * 6 + struct.pack('>HIIHH', 4, H, W, 8, 3)
    out += struct.pack('>I', 0) + struct.pack('>I', 0)
    out += struct.pack('>I', len(layer_section)) + layer_section
    out += struct.pack('>H', 0) + b''.join(bytes(flat[y][x][i] for y in range(H) for x in range(W)) for i in range(4))
    Path(__file__).resolve().parent.joinpath('Assets/Layers.psd').write_bytes(out)


if __name__ == '__main__':
    main()
