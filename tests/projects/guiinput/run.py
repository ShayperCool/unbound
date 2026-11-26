#!/usr/bin/env python3
"""The order OnGUI behaviours get input events; see README.md."""
from pathlib import Path
import sys

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402


def engine_args():
    """The steps as `unbound run` input: Time.frameCount N is engine frame N - 1."""
    import re
    text = (PROBE/'Assets/Scripts/GuiInputProbe.cs').read_text()
    args = ['--frames', '200', '--size', '320x180']
    for frame, tokens in re.findall(r'S\((\d+), "([^"]*)"\)', text):
        at = int(frame) - 1
        tokens = tokens.split(' ')
        for token in tokens:
            if token[0] == '@':
                args += ['--move', f'{at}:{token[1:]}']
        keys = [t for t in tokens if t[0] in '+-']
        if keys:
            args += ['--keys', f'{at}:{",".join(keys)}']
    return args


if __name__ == '__main__':
    # Windowed: the batch-mode Editor calls no OnGUI.
    sys.exit(value_probe.main(PROBE, 'ProbeRunner.Record', 'GUI_INPUT_OUT', tolerance=0, engine_args=engine_args(), batch=False))
