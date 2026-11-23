"""A sealed capture also needs to come from the selected profile's Editor method."""
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

sys.dont_write_bytecode = True

import run
from probe_paths import validation_cache
from value_probe import editor_runner, VALUES

RELEASE = '6000.0.71f1'
FIXED_METHOD = run.PROFILES['fixed'][0]
LIGHT_METHOD = run.PROFILES['light'][0]


class CaptureSelectionTests(unittest.TestCase):
    def test_run_labels_do_not_share_between_profiles(self):
        fixed = run.directory('sample-light', run.PROFILES['fixed'][2])
        light = run.directory('sample', run.PROFILES['light'][2])
        self.assertNotEqual(fixed, light)
        self.assertNotEqual(fixed.parent, light.parent)

    def test_capture_metadata_matches_the_selected_method_and_device(self):
        cache = validation_cache()
        cache.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='component-capture-selection-', dir=cache) as directory:
            root = Path(directory)
            capture = root/'capture'
            capture.mkdir()
            (capture/VALUES).write_text(
                json.dumps({'k': 'unity', 'v': RELEASE}) + '\n'
                + json.dumps({'k': 'device', 'v': 'OpenGLCore'}) + '\n'
                + json.dumps({'k': 'complete', 'v': True}) + '\n'
            )
            (root/editor_runner.RESULT).write_text(json.dumps({
                'command': ['Unity', '-executeMethod', FIXED_METHOD],
            }))
            run.validate_capture(root, RELEASE, FIXED_METHOD, 'OpenGLCore')
            with self.assertRaisesRegex(SystemExit, LIGHT_METHOD):
                run.validate_capture(root, RELEASE, LIGHT_METHOD)
            with self.assertRaisesRegex(SystemExit, 'expected Editor capture on Metal'):
                run.validate_capture(root, RELEASE, FIXED_METHOD, 'Metal')

    def test_camera_gate_selects_the_host_devices(self):
        cases = [
            ('linux', [], [('camera-opengl', '--force-glcore'), ('camera-vulkan', '--force-vulkan')], [1, 0]),
            ('linux', ['--backend', 'vulkan'], [('camera-vulkan', '--force-vulkan')], [0]),
            ('darwin', [], [('camera-metal', None)], [0]),
        ]
        for platform, options, expected, results in cases:
            with self.subTest(platform=platform, options=options):
                argv = ['run.py', 'gate', '--release', RELEASE, '--profile', 'camera', *options]
                with mock.patch.object(sys, 'platform', platform), mock.patch.object(sys, 'argv', argv), \
                        mock.patch.dict(os.environ), mock.patch.object(run.value_probe, 'gate', side_effect=results) as gate:
                    self.assertEqual(run.main(), max(results))
                calls = [(call.kwargs['profile'], next(iter(call.args[4][2:]), None)) for call in gate.call_args_list]
                self.assertEqual(calls, expected)


if __name__ == '__main__':
    unittest.main()
