#!/usr/bin/env python3
"""Writes the probe's textures and audio clip (Assets/Tex/*.png, Assets/Audio/Tone.wav).

The patterns are small and deterministic: every texel differs from its neighbours, so a resize,
a mip level or an inverted colour changes the hash the probe records."""
from pathlib import Path
import struct
import wave
import zlib

ASSETS = Path(__file__).resolve().parent/'Assets'


def png(path, size, alpha):
    rows = b''
    for y in range(size):
        rows += b'\0'
        for x in range(size):
            r = (x * 37 + y * 11) & 255
            g = (x * 5 + y * 53) & 255
            b = ((x ^ y) * 29) & 255
            a = ((x + y) * 8) & 255 if alpha else 255
            rows += bytes((r, g, b, a))

    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff)
    header = struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', header) + chunk(b'IDAT', zlib.compress(rows, 9))
                     + chunk(b'IEND', b''))


def tone(path):
    """0.1 s of 16-bit stereo at 22050 Hz: different ramps per channel, so a mono mix is visible."""
    frames = b''
    for i in range(2205):
        left = ((i * 97) % 2000) - 1000
        right = ((i * 31) % 3000) - 1500
        frames += struct.pack('<hh', left * 8, right * 8)
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), 'wb') as out:
        out.setnchannels(2)
        out.setsampwidth(2)
        out.setframerate(22050)
        out.writeframes(frames)


if __name__ == '__main__':
    png(ASSETS/'Tex/Plain.png', 16, False)
    png(ASSETS/'Tex/Shrink.png', 64, True)
    png(ASSETS/'Tex/Invert.png', 32, False)
    png(ASSETS/'Tex/Sprite.png', 32, True)
    tone(ASSETS/'Audio/Tone.wav')
