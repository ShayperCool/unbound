"""The gate requires the safe Append skip and explicit errors for unsupported mechanisms."""
import json
from pathlib import Path
import tempfile
import unittest
import run


class SafeAppendGateTests(unittest.TestCase):
    def setUp(self):
        (run.ROOT/'Library'/'Unbound').mkdir(parents=True, exist_ok=True)
        self.directory = tempfile.TemporaryDirectory(dir=run.ROOT/'Library'/'Unbound')
        self.addCleanup(self.directory.cleanup)
        self.out = Path(self.directory.name)
        self.rows = [json.loads(line) for line in run.reference_file(run.ROOT, 'jsonl').read_text().splitlines()]
        self.rows = [row for row in self.rows if row['k'] != 'uav.append.nocounter.log']
        next(row for row in self.rows if row['k'] == 'uav.append.nocounter')['v'] = [0xAAAA0000] * 48
        self.unsupported('device.stereo.1',
                         'Unbound: side-by-side stereo clipping without an XR device is not supported.')
        mono = next(row['v'] for row in self.rows if row['k'] == 'device.stereo.0.pixels')
        next(row for row in self.rows if row['k'] == 'device.stereo.1.pixels')['v'] = mono
        for kind in ['blit', 'texture', 'texture1', 'texture2']:
            key = 'device.plugin.' + kind + '.callback'
            self.unsupported(key, 'Unbound: native rendering plugin callbacks require graphics-device interop, which is not supported.')
            next(row for row in self.rows if row['k'] == key + '.events')['v'] = []
            next(row for row in self.rows if row['k'] == key + '.texture')['v'] = ['000000ff' * 4]
        blank = next(row['v'] for row in self.rows if row['k'] == 'device.stereo.1.pixels')
        # The stereo candidate above is mono; mesh failures leave the cleared target instead.
        blank = ['0' * len(blank[0])]
        for multiplier in (2, 3):
            for field in ('colors', 'ids'):
                key = f'device.multiplier.mesh.{multiplier}.{field}'
                self.rows.append({'k': key + '.log', 'v': ['Exception: NotSupportedException: Unbound: instance multipliers on CPU-expanded mesh instances are not supported.']})
                next(row for row in self.rows if row['k'] == key + '.pixels')['v'] = blank
        (self.out/'engine.log').write_text('shader uses the counter of buffer `_App`, which is not set or not an Append or Counter buffer: the draw is skipped')

    def unsupported(self, key, message):
        self.rows = [row for row in self.rows if row['k'] != key + '.record.ok']
        self.rows.append({'k': key + '.record.error', 'v': 'NotSupportedException: ' + message})

    def verify(self, allow=True):
        candidate = self.out/'values.jsonl'
        candidate.write_text(''.join(json.dumps(row)+'\n' for row in self.rows))
        return run.verify(candidate, safe_append_skip=allow)

    def test_safe_skip_is_separate_from_strict_verdict(self):
        self.assertNotEqual(self.verify(allow=False), 0)
        self.assertEqual(self.verify(), 0)

    def test_exact_editor_candidate_still_fails_safe_gate(self):
        self.rows = [json.loads(line) for line in run.reference_file(run.ROOT, 'jsonl').read_text().splitlines()]
        self.assertEqual(self.verify(allow=False), 0)
        self.assertNotEqual(self.verify(), 0)

    def test_payload_write_still_fails(self):
        next(row for row in self.rows if row['k'] == 'uav.append.nocounter')['v'][0] = 1
        self.assertNotEqual(self.verify(), 0)

    def test_missing_warning_still_fails(self):
        (self.out/'engine.log').write_text('')
        self.assertNotEqual(self.verify(), 0)

    def test_ignored_plugin_callback_still_fails(self):
        self.rows = [row for row in self.rows if row['k'] != 'device.plugin.texture2.callback.record.error']
        self.assertNotEqual(self.verify(), 0)

    def test_mesh_multiplier_silently_ignored_still_fails(self):
        self.rows = [row for row in self.rows if row['k'] != 'device.multiplier.mesh.2.colors.log']
        self.assertNotEqual(self.verify(), 0)

    def test_unrelated_device_difference_still_fails(self):
        next(row for row in self.rows if row['k'] == 'device.rayTracing')['v'] = True
        self.assertNotEqual(self.verify(), 0)

    def test_unrelated_missing_value_still_fails(self):
        self.rows = [row for row in self.rows if row['k'] != 'cmd.instanced.blockscalar']
        self.assertNotEqual(self.verify(), 0)

    def test_unrelated_extra_value_still_fails(self):
        self.rows.append({'k': 'unexpected', 'v': 123})
        self.assertNotEqual(self.verify(), 0)


if __name__ == '__main__':
    unittest.main()
