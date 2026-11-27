#!/usr/bin/env python3
"""Generate minimal, deterministic .mod, .xm, .s3m and .it tracker modules for the audiotracker probe.

Each song is 16 rows at speed 6, BPM 125 (1.92 seconds total), using a 32-sample sine wave:
- Rows 0..7  (0.00..0.96 s): left channel plays C (~520 Hz), right channel is silent.
- Rows 8..15 (0.96..1.92 s): left channel plays G (~780 Hz), right channel plays E (~650 Hz).
"""
import argparse
import ctypes
import math
from pathlib import Path
import struct

PROBE = Path(__file__).resolve().parent
AUDIO = PROBE / 'Assets' / 'Audio'


def sine_i8(n=32, amp=100):
    return [int(round(amp * math.sin(2.0 * math.pi * i / n))) for i in range(n)]


def sine_i16(n=32, amp=24000):
    return [int(round(amp * math.sin(2.0 * math.pi * i / n))) for i in range(n)]


def build_mod():
    """4-channel ProTracker M.K. module, 16 rows via pattern break D00 on row 15."""
    out = bytearray()
    out += b'UnboundModTest'.ljust(20, b'\x00')
    pcm = sine_i8(32, 100)
    # Sample 1: length=16 words (32 bytes), finetune=0, vol=64, loop_start=0, loop_len=16 words
    out += b'Sine32'.ljust(22, b'\x00')
    out += struct.pack('>HBBHH', 16, 0, 64, 0, 16)
    for _ in range(30):
        out += b'\x00' * 30
    out += bytes([1, 127])  # song_length=1, restart=127
    out += bytes([0] * 128)  # order table
    out += b'M.K.'

    def mod_cell(sample=0, period=0, eff=0, param=0):
        b0 = (sample & 0xF0) | ((period >> 8) & 0x0F)
        b1 = period & 0xFF
        b2 = ((sample & 0x0F) << 4) | (eff & 0x0F)
        b3 = param & 0xFF
        return bytes([b0, b1, b2, b3])

    # Amiga panning: ch0=L, ch1=R, ch2=R, ch3=L.
    # Period 214 = C-3 (f = 7093789.2 / (2 * 214) / 32 = 517.9 Hz)
    # Period 170 = E-3 (~652.0 Hz), Period 143 = G-3 (~775.1 Hz)
    pattern = bytearray()
    for row in range(64):
        if row == 0:
            pattern += mod_cell(1, 214, 0xF, 6)    # ch0 L: C-3, speed 6
            pattern += mod_cell(0, 0, 0xF, 125)    # ch1 R: BPM 125
            pattern += mod_cell()
            pattern += mod_cell()
        elif row == 8:
            pattern += mod_cell(1, 143)            # ch0 L: G-3
            pattern += mod_cell(1, 170)            # ch1 R: E-3
            pattern += mod_cell()
            pattern += mod_cell()
        elif row == 15:
            pattern += mod_cell(0, 0, 0xD, 0)      # D00: pattern break -> 16 rows
            pattern += mod_cell()
            pattern += mod_cell()
            pattern += mod_cell()
        else:
            pattern += b'\x00' * 16
    out += pattern
    out += struct.pack('32b', *pcm)
    return bytes(out)


def build_xm(bpm=125):
    """2-channel FastTracker 2 XM 1.04 module, 16 rows, 16-bit looped sine wave."""
    out = bytearray()
    out += b'Extended Module: '
    out += b'UnboundXmTest'.ljust(20, b'\x00')
    out += b'\x1a'
    out += b'UnboundProbe'.ljust(20, b'\x00')
    out += struct.pack('<H', 0x0104)
    # Header size = 276 (from here: 20 bytes + 256 order table)
    out += struct.pack('<IHHHHHHHH', 276, 1, 0, 2, 1, 1, 1, 6, bpm)
    out += bytes([0] * 256)

    # Pattern 0: 16 rows, 2 channels, uncompressed (5 bytes per cell)
    # XM note 49 = C-4 (~8363 Hz -> /32 = 261.3 Hz), so note 61 = C-5 (~522.7 Hz),
    # note 65 = E-5 (~658.5 Hz), note 68 = G-5 (~783.1 Hz).
    pat_data = bytearray()
    for row in range(16):
        if row == 0:
            pat_data += bytes([61, 1, 0x50, 0x08, 0x00])  # ch0: C-5, inst 1, vol 64, pan 0 (L)
            pat_data += bytes([0, 0, 0, 0x08, 0xFF])      # ch1: pan 255 (R)
        elif row == 8:
            pat_data += bytes([68, 1, 0x50, 0x08, 0x00])  # ch0: G-5, L
            pat_data += bytes([65, 1, 0x50, 0x08, 0xFF])  # ch1: E-5, R
        else:
            pat_data += bytes([0x80, 0x80])               # packed empty cells for ch0, ch1
    out += struct.pack('<IBHH', 9, 0, 16, len(pat_data))
    out += pat_data

    # Instrument 1: 1 sample, 16-bit delta-encoded forward loop
    inst = bytearray()
    inst += struct.pack('<I', 263)              # instrument header size
    inst += b'SineInst'.ljust(22, b'\x00')
    inst += struct.pack('<BH', 0, 1)            # type=0, num_samples=1
    inst += struct.pack('<I', 40)               # sample header size
    inst += bytes([0] * 96)                     # sample number for all notes
    inst += bytes([0] * 48)                     # volume envelope points
    inst += bytes([0] * 48)                     # panning envelope points
    inst += bytes([0] * 14)                     # envelope params + vibrato
    inst += struct.pack('<H', 0)                # volume fadeout
    inst += bytes([0] * 22)                     # reserved -> total 263 bytes
    assert len(inst) == 263
    out += inst

    # Sample header (40 bytes): 32 16-bit samples = 64 bytes, forward loop (0x01) + 16-bit (0x10)
    out += struct.pack('<IIIBbBBbB', 64, 0, 64, 64, 0, 0x11, 128, 0, 0)
    out += b'Sine32'.ljust(22, b'\x00')
    # Delta-encoded 16-bit little-endian PCM
    pcm = sine_i16(32, 24000)
    prev = 0
    for s in pcm:
        d = ((s - prev + 32768) % 65536) - 32768
        out += struct.pack('<h', d)
        prev = s
    return bytes(out)


def build_s3m():
    """2-channel Scream Tracker 3 SCRM module, 16 rows via pattern break C00 on row 15."""
    # Header is 96 bytes, followed by 2 orders (ordNum=2), 2-byte inst ptr, 2-byte pat ptr = 102 bytes -> pad to 112 (para 7)
    ord_num = 2
    ins_num = 1
    pat_num = 1

    # Layout in 16-byte paragraphs:
    # 0x00..0x6F (112 bytes): header + orders + ptrs
    # 0x70..0xBF (80 bytes, para 7): instrument 1 header
    # 0xC0..0xDF (32 bytes, para 12): sample 1 PCM data (unsigned 8-bit)
    # 0xE0..     (para 14): pattern 0
    ins_para = 7
    smp_para = 12
    pat_para = 14

    out = bytearray()
    out += b'UnboundS3mTest'.ljust(28, b'\x00')
    out += bytes([0x1A, 16, 0, 0])
    out += struct.pack('<HHHHHH', ord_num, ins_num, pat_num, 0, 0x1320, 2)  # ffi=2 unsigned
    out += b'SCRM'
    out += bytes([64, 6, 125, 0x80 | 48, 16, 0])  # gv, is, it, mv(stereo), uc, dp
    out += bytes([0] * 10)
    # Channel settings: ch0=0 (left), ch1=8 (right), rest 255 (unused)
    out += bytes([0, 8] + [255] * 30)
    assert len(out) == 96
    out += bytes([0, 255])  # orders: pattern 0, end
    out += struct.pack('<H', ins_para)
    out += struct.pack('<H', pat_para)
    while len(out) < ins_para * 16:
        out += b'\x00'

    # Instrument 1 (80 bytes, PCM type=1)
    ins = bytearray()
    ins += bytes([1])                                   # type = PCM
    ins += b'SINE32.SMP'.ljust(12, b'\x00')
    ins += bytes([0, smp_para & 0xFF, (smp_para >> 8) & 0xFF])  # memseg (hi, lo, mid)
    ins += struct.pack('<III', 32, 0, 32)               # len, loopbeg, loopend
    ins += bytes([64, 0, 0, 1])                         # vol=64, dsk=0, pack=0, flags=1 (loop)
    ins += struct.pack('<I', 16726)                     # c2spd (~2x8363 so C-4 = 522.7 Hz)
    ins += bytes([0] * 12)
    ins += b'Sine32'.ljust(28, b'\x00')
    ins += b'SCRS'
    assert len(ins) == 80
    out += ins

    # Sample data (unsigned 8-bit)
    assert len(out) == smp_para * 16
    pcm = [s + 128 for s in sine_i8(32, 100)]
    out += bytes(pcm)

    # Pattern 0 (packed rows; 64 rows, C00 break at row 15)
    assert len(out) == pat_para * 16
    body = bytearray()
    for row in range(64):
        if row == 0:
            # ch0: note C-4 (0x40), inst 1, vol 64
            body += bytes([0 | 0x20 | 0x40, 0x40, 1, 64])
        elif row == 8:
            # ch0: note G-4 (0x47), inst 1, vol 64; ch1: note E-4 (0x44), inst 1, vol 64
            body += bytes([0 | 0x20 | 0x40, 0x47, 1, 64])
            body += bytes([1 | 0x20 | 0x40, 0x44, 1, 64])
        elif row == 15:
            # ch0: effect C (3) param 00 (pattern break)
            body += bytes([0 | 0x80, 3, 0])
        body += bytes([0])  # end of row
    out += struct.pack('<H', len(body) + 2)
    out += body
    return bytes(out)


def build_it():
    """2-channel Impulse Tracker IMPM module (sample mode), 16 rows, 16-bit looped sine wave."""
    ord_num = 2
    ins_num = 0
    smp_num = 1
    pat_num = 1

    # Header = 192 bytes, orders = 2 bytes, smp offset = 4, pat offset = 4 -> 202 bytes
    smp_offset = 208
    pat_offset = smp_offset + 80 + 64  # sample header (80) + 32*2 sample bytes (64)

    out = bytearray()
    out += b'IMPM'
    out += b'UnboundItTest'.ljust(26, b'\x00')
    out += struct.pack('<H', 0x1004)
    out += struct.pack('<HHHHHHHH', ord_num, ins_num, smp_num, pat_num, 0x0214, 0x0200, 0x0009, 0)
    out += bytes([128, 48, 6, 125, 128, 0])  # gv, mv, is, it, sep, pwd
    out += struct.pack('<HII', 0, 0, 0)      # msg len, msg offset, reserved
    # Channel pan: ch0=0 (L), ch1=64 (R), rest disabled (128+32=160)
    out += bytes([0, 64] + [160] * 62)
    # Channel vol: 64
    out += bytes([64] * 64)
    assert len(out) == 192
    out += bytes([0, 255])                   # orders
    out += struct.pack('<I', smp_offset)
    out += struct.pack('<I', pat_offset)
    while len(out) < smp_offset:
        out += b'\x00'

    # Sample header (IMPS, 80 bytes): flags = 0x01 (has data) | 0x02 (16-bit) | 0x10 (loop) = 0x13
    # cvt = 0x01 (signed)
    pcm_offset = smp_offset + 80
    smp = bytearray()
    smp += b'IMPS'
    smp += b'SINE32.WAV'.ljust(12, b'\x00')
    smp += bytes([0, 64, 0x13, 64])          # 0, gvl=64, flags=0x13, vol=64
    smp += b'Sine32'.ljust(26, b'\x00')
    smp += bytes([0x01, 0])                  # cvt=signed, dfp=0
    smp += struct.pack('<IIIIII', 32, 0, 32, 16726, 0, 0)  # len, loopbeg, loopend, c5speed, susbeg, susend
    smp += struct.pack('<I', pcm_offset)
    smp += bytes([0, 0, 0, 0])               # vibrato
    assert len(smp) == 80
    out += smp

    # Sample PCM data (16-bit signed LE)
    pcm = sine_i16(32, 24000)
    out += struct.pack('<32h', *pcm)
    assert len(out) == pat_offset

    # Pattern 0: 16 rows
    body = bytearray()
    for row in range(16):
        if row == 0:
            # ch0 (1): set mask=note+inst+vol (0x07), note 60 (C-5 -> 16726/32=522.7 Hz), inst 1, vol 64
            body += bytes([1 | 128, 0x07, 60, 1, 64])
        elif row == 8:
            # ch0: G-5 (67); ch1: E-5 (64)
            body += bytes([1 | 128, 0x07, 67, 1, 64])
            body += bytes([2 | 128, 0x07, 64, 1, 64])
        body += bytes([0])
    out += struct.pack('<HH', len(body), 16) + bytes(4)
    out += body
    return bytes(out)


def meta_text(guid, *, load_type=0, rate_setting=0, rate_override=44100, compression=1,
              preload=1, force_mono=0, normalize=1, load_bg=0):
    return (
        f'fileFormatVersion: 2\n'
        f'guid: {guid}\n'
        f'AudioImporter:\n'
        f'  externalObjects: {{}}\n'
        f'  serializedVersion: 8\n'
        f'  defaultSettings:\n'
        f'    serializedVersion: 2\n'
        f'    loadType: {load_type}\n'
        f'    sampleRateSetting: {rate_setting}\n'
        f'    sampleRateOverride: {rate_override}\n'
        f'    compressionFormat: {compression}\n'
        f'    quality: 1\n'
        f'    conversionMode: 0\n'
        f'    preloadAudioData: {preload}\n'
        f'  platformSettingOverrides: {{}}\n'
        f'  forceToMono: {force_mono}\n'
        f'  normalize: {normalize}\n'
        f'  loadInBackground: {load_bg}\n'
        f'  ambisonic: 0\n'
        f'  3D: 1\n'
        f'  userData: \n'
        f'  assetBundleName: \n'
        f'  assetBundleVariant: \n'
    )


def folder_meta(guid):
    return (
        f'fileFormatVersion: 2\n'
        f'guid: {guid}\n'
        f'folderAsset: yes\n'
        f'DefaultImporter:\n'
        f'  externalObjects: {{}}\n'
        f'  userData: \n'
        f'  assetBundleName: \n'
        f'  assetBundleVariant: \n'
    )


def script_meta(guid):
    return (
        f'fileFormatVersion: 2\n'
        f'guid: {guid}\n'
        f'MonoImporter:\n'
        f'  externalObjects: {{}}\n'
        f'  serializedVersion: 2\n'
        f'  defaultReferences: []\n'
        f'  executionOrder: 0\n'
        f'  icon: {{instanceID: 0}}\n'
        f'  userData: \n'
        f'  assetBundleName: \n'
        f'  assetBundleVariant: \n'
    )


def write_all():
    AUDIO.mkdir(parents=True, exist_ok=True)
    (PROBE / 'Assets' / 'Audio.meta').write_text(folder_meta('a8410000000000000000000000000001'))
    (PROBE / 'Assets' / 'Scripts.meta').write_text(folder_meta('a8410000000000000000000000000002'))
    (PROBE / 'Assets' / 'Scripts' / 'AudioTrackerProbe.cs.meta').write_text(
        script_meta('a8410000000000000000000000000003'))

    mod = build_mod()
    xm = build_xm(125)
    xm150 = build_xm(150)
    s3m = build_s3m()
    it = build_it()

    files = [
        ('tone.mod', mod, 'a8410000000000000000000000000010', {}),
        ('tone.xm', xm, 'a8410000000000000000000000000011', {}),
        ('tone.s3m', s3m, 'a8410000000000000000000000000012', {}),
        ('tone.it', it, 'a8410000000000000000000000000013', {}),
        ('xm_compressed.xm', xm, 'a8410000000000000000000000000014', dict(load_type=1)),
        ('xm_compressed_pcm.xm', xm, 'a841000000000000000000000000001a', dict(load_type=1, compression=0)),
        ('xm_streaming.xm', xm, 'a8410000000000000000000000000015', dict(load_type=2)),
        ('xm_mono_rate.xm', xm, 'a8410000000000000000000000000016',
         dict(force_mono=1, normalize=1, rate_setting=2, rate_override=22050)),
        ('xm_bg_nopreload.xm', xm, 'a8410000000000000000000000000017', dict(preload=0, load_bg=1)),
        ('xm_pcm.xm', xm, 'a8410000000000000000000000000018', dict(compression=0)),
        ('xm_bpm150.xm', xm150, 'a8410000000000000000000000000019', {}),
    ]
    for name, data, guid, opts in files:
        (AUDIO / name).write_bytes(data)
        (AUDIO / f'{name}.meta').write_text(meta_text(guid, **opts))


def check_with_openmpt():
    lib = ctypes.CDLL('libopenmpt.so.0')
    lib.openmpt_module_create_from_memory2.restype = ctypes.c_void_p
    lib.openmpt_module_create_from_memory2.argtypes = [
        ctypes.c_void_p, ctypes.c_size_t, ctypes.c_void_p, ctypes.c_void_p,
        ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p,
    ]
    lib.openmpt_module_get_duration_seconds.restype = ctypes.c_double
    lib.openmpt_module_get_duration_seconds.argtypes = [ctypes.c_void_p]
    lib.openmpt_module_read_interleaved_float_stereo.restype = ctypes.c_size_t
    lib.openmpt_module_read_interleaved_float_stereo.argtypes = [
        ctypes.c_void_p, ctypes.c_int32, ctypes.c_size_t, ctypes.POINTER(ctypes.c_float)
    ]
    lib.openmpt_module_destroy.argtypes = [ctypes.c_void_p]
    for name in ('tone.mod', 'tone.xm', 'tone.s3m', 'tone.it'):
        data = (AUDIO / name).read_bytes()
        m = lib.openmpt_module_create_from_memory2(data, len(data), None, None, None, None, None, None, None)
        assert m, f'failed to open {name}'
        dur = lib.openmpt_module_get_duration_seconds(m)
        rate = 44100
        frames = int(rate * 2.5)
        buf = (ctypes.c_float * (frames * 2))()
        got = lib.openmpt_module_read_interleaved_float_stereo(m, rate, frames, buf)
        lib.openmpt_module_destroy(m)

        def stats(t0, t1):
            i0, i1 = int(t0 * rate), min(int(t1 * rate), got)
            sl = [buf[i * 2] for i in range(i0, i1)]
            sr = [buf[i * 2 + 1] for i in range(i0, i1)]
            rl = math.sqrt(sum(x * x for x in sl) / max(1, len(sl)))
            rr = math.sqrt(sum(x * x for x in sr) / max(1, len(sr)))
            zl = sum(1 for a, b in zip(sl, sl[1:]) if (a < 0) != (b < 0)) * 0.5 * rate / max(1, len(sl))
            zr = sum(1 for a, b in zip(sr, sr[1:]) if (a < 0) != (b < 0)) * 0.5 * rate / max(1, len(sr))
            return rl, rr, zl, zr

        s1 = stats(0.15, 0.75)
        s2 = stats(1.15, 1.75)
        print(f'{name}: dur={dur:.3f}s got={got} ({got/rate:.3f}s) '
              f'h1(L={s1[0]:.3f}/{s1[2]:.0f}Hz R={s1[1]:.3f}/{s1[3]:.0f}Hz) '
              f'h2(L={s2[0]:.3f}/{s2[2]:.0f}Hz R={s2[1]:.3f}/{s2[3]:.0f}Hz)')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--check', action='store_true')
    args = parser.parse_args()
    write_all()
    if args.check:
        check_with_openmpt()
