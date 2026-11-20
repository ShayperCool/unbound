#!/usr/bin/env python3
"""Record and replay the Inspector toggle group controls."""
import json
from importlib.util import module_from_spec, spec_from_file_location
import os
from pathlib import Path
import sys
from unittest.mock import patch

PROBE = Path(__file__).resolve().parent
sys.path.insert(0, str(PROBE.parents[1]))
import value_probe  # noqa: E402

BASE = (PROBE.parent/'imguiskin', ('Assets/Scenes', 'Packages', 'ProjectSettings'))

def run():
    args = ['--frames', '60', '--size', '320x240', '--focus', '1:on']
    steps = json.loads((PROBE/'Assets/Resources/ToggleGroupSteps.json').read_text())['steps']
    for step in steps:
        value = f"{step['x']},{step['y']}" if step['command'] == 'move' else step['key']
        args += ['--' + step['command'], f"{step['frame']}:{value}"]
    return value_probe.main(PROBE, 'ToggleGroupRunner.Record', 'EDITOR_TOGGLE_GROUP_OUT',
                            tolerance=0, engine_args=args, batch=False, base=BASE)


if __name__ == '__main__':
    if sys.argv[1:2] != ['record']:
        sys.exit(run())
    if sys.platform != 'linux':
        raise SystemExit('Recording toggle group input requires a Linux X11 display.')
    spec = spec_from_file_location('editorstyles_runner', PROBE.parent/'editorstyles/run.py')
    editorstyles = module_from_spec(spec)
    spec.loader.exec_module(editorstyles)
    with patch.dict(os.environ, EDITOR_TOGGLE_GROUP_NATIVE='1'), patch.object(
            value_probe.editor_runner, 'EditorJob', editorstyles.PrivatePrefsEditorJob):
        sys.exit(run())
