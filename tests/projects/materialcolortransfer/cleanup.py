#!/usr/bin/env python3
"""Stop only Unity Roslyn servers started with this isolated fixture's PWD."""
import os
from pathlib import Path
import signal

fixture = os.environ.get("MATERIAL_COLOR_TRANSFER_PROJECT", str(Path(__file__).resolve().parent))
for process in Path('/proc').glob('[0-9]*'):
    try:
        command = (process/'cmdline').read_bytes()
        if b'/Editor/Data/DotNetSdkRoslyn/VBCSCompiler.dll' not in command:
            continue
        environment = dict(item.split(b'=', 1) for item in (process/'environ').read_bytes().split(b'\0') if b'=' in item)
        if environment.get(b'PWD') == fixture.encode():
            os.kill(int(process.name), signal.SIGTERM)
    except (FileNotFoundError, ProcessLookupError, PermissionError):
        pass
