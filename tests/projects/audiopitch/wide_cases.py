#!/usr/bin/env python3
"""Regenerate the explicit wide-settings fixture from the original setting controls."""
import math
from pathlib import Path
import re

FIXTURE = Path(__file__).resolve().parent
RATES = (32000, 44100, 48000, 96000)
BUFFERS = (32, 64, 128, 256, 340, 480, 512, 1024, 2048, 4096, 8192)
MODES = {2: 'Stereo', 3: 'Quad', 4: 'Surround', 5: 'Mode5point1', 6: 'Mode7point1'}
CHANNELS = {2: 2, 3: 4, 4: 5, 5: 6, 6: 8}


def generate():
    source = (FIXTURE/'Assets/Scripts/EffectProbe.cs').read_text()
    templates = [re.findall(r'"([^"]+)"', line) for line in source.splitlines()
                 if 'new[] { "context_settings_48000_512_2_' in line]
    assert len(templates) == 14
    for rate in RATES:
        for size in BUFFERS:
            # Four approximately equal phases keep every edit inside the capture,
            # including sizes larger than the original edit intervals.
            phases = [max(1, math.ceil(frame/size)) for frame in (4096, 8192, 16384)]
            phases = [max(value, index+1) for index, value in enumerate(phases)]
            for mode, name in MODES.items():
                for template in templates:
                    suffix = template[0].removeprefix('context_settings_48000_512_2_')
                    row = [f'context_wide_{rate}_{size}_{mode}_{suffix}']
                    for token in template[1:]:
                        if token.startswith('rate='): token = f'rate={rate}'
                        elif token.startswith('buffer='): token = f'buffer={size}'
                        elif token.startswith('mode='): token = 'mode='+name
                        elif token.startswith('blocks='): token = f'blocks={math.ceil(32768/size)}'
                        elif token.startswith('preflight='): token = f'preflight={max(4, math.ceil(4096/size))}'
                        elif token.startswith('dominant='): token = f'dominant={CHANNELS[mode]-1}'
                        elif token.startswith('live:'):
                            _, block, edit = token.split(':', 2)
                            token = f'live:{dict(zip((8,16,32), phases))[int(block)]}:{edit}'
                        elif token == 'gen=burst' and suffix != 'duck': token = 'gen=burst_full'
                        elif token == 'music=noise:0.2': token = 'music=noise_full:0.2'
                        row.append(token)
                    row.append('asset='+template[0])
                    yield row


if __name__ == '__main__':
    target = FIXTURE/'Assets/StreamingAssets/WideCases.tsv'
    target.parent.mkdir(exist_ok=True)
    target.write_text(''.join('\t'.join(row)+'\n' for row in generate()))
