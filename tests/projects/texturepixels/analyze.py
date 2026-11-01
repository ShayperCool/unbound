#!/usr/bin/env python3
"""Classifies how a texture pixels capture turns stored bytes into floats.

For every float value (px, pixel, bilinear, block, all) the byte is taken from the same texel of
the level's GetPixels32 bytes and the float is classified as `div` (equals b / 255 in single
precision), `mul` (equals b * (1 / 255) in single precision), `both` (the two agree for this
byte) or `other` (neither: packed formats, filtering, or a constant channel). One line per
texture and value kind with its counts; a texture whose kind has both `div` and `mul` values is
marked `mixed`.

    analyze.py <capture>/Gamma.jsonl
"""
import base64
import json
import re
import struct
import sys
from collections import Counter

PIXELS = re.compile(r'^(\d+)x(\d+):(.*)$', re.S)


def f32(x):
    return struct.unpack('<f', struct.pack('<f', x))[0]


ONE_OVER = f32(1.0 / 255.0)
DIV = [f32(f32(float(b)) / f32(255.0)) for b in range(256)]
MUL = [f32(float(b) * ONE_OVER) for b in range(256)]


def main(path):
    values = {}
    for line in open(path, encoding='utf-8'):
        if line.strip():
            row = json.loads(line)
            values[row['k']] = row['v']
    for key in sorted(values):
        name, _, kind = key.rpartition('.')
        if kind not in ('px', 'pixel', 'bilinear', 'block'):
            continue
        texture, level = name.rsplit('.', 1)
        source = f'{texture}.mip0.px32' if level == 'all' else f'{name}.px32'
        m, s = PIXELS.match(values[key]), PIXELS.match(values.get(source, ''))
        if not m or not s:
            print(key, 'no values:', values[key][:80])
            continue
        floats = struct.unpack(f'<{len(base64.b64decode(m.group(3))) // 4}f', base64.b64decode(m.group(3)))
        stored = base64.b64decode(s.group(3))
        w, sw = int(m.group(1)), int(s.group(1))
        offset = 1 if kind == 'block' else 0
        counts = Counter()
        for i, value in enumerate(floats):
            texel, channel = divmod(i, 4)
            y, x = divmod(texel, w)
            b = stored[((y + offset) * sw + x + offset) * 4 + channel]
            d, u = DIV[b] == value, MUL[b] == value
            counts['both' if d and u else 'div' if d else 'mul' if u else 'other'] += 1
        mixed = ' mixed' if counts['div'] and counts['mul'] else ''
        print(key, ' '.join(f'{k} {counts[k]}' for k in ('div', 'mul', 'both', 'other')) + mixed)


if __name__ == '__main__':
    main(sys.argv[1])
