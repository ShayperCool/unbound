"""A sealed capture also needs to come from the selected profile's Editor method."""
import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.dont_write_bytecode = True

import run
from probe_paths import validation_cache
from value_probe import editor_runner, VALUES

RELEASE = '6000.0.71f1'
FIXED_METHOD = run.PROFILES['fixed'][0]
LIGHT_METHOD = run.PROFILES['light'][0]


class CaptureSelectionTests(unittest.TestCase):
    def test_fixed_record_cannot_publish_into_the_light_profile(self):
        cache = validation_cache()
        cache.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='component-capture-selection-', dir=cache) as directory:
            root = Path(directory)
            capture = root/'capture'
            capture.mkdir()
            (capture/VALUES).write_text(
                json.dumps({'k': 'unity', 'v': RELEASE}) + '\n'
                + json.dumps({'k': 'complete', 'v': True}) + '\n'
            )
            (root/editor_runner.RESULT).write_text(json.dumps({
                'command': ['Unity', '-executeMethod', FIXED_METHOD],
            }))
            run.validate_capture(root, RELEASE, FIXED_METHOD)
            with self.assertRaisesRegex(SystemExit, LIGHT_METHOD):
                run.validate_capture(root, RELEASE, LIGHT_METHOD)


if __name__ == '__main__':
    unittest.main()
