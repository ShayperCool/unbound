"""Publication checks for the Linux and macOS selected-path profiles."""
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock

import run
from editor_runner import RESULT


class PublicationTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(self.enterContext(tempfile.TemporaryDirectory())).resolve()
        self.probe = self.root / 'probe'
        self.runs = self.root / 'runs'
        self.enterContext(mock.patch.object(run, 'PROBE', self.probe))
        self.enterContext(mock.patch.object(run.value_probe, 'run_root', self.run_root))
        self.labels = {
            'macos': self.record('macos', 'Metal: test renderer', 'metal'),
            'linux': self.record('linux', 'NVIDIA test renderer'),
        }

    def run_root(self, probe, label):
        return self.runs / label

    def record(self, profile, renderer, device=None):
        labels = [f'{profile}-a', f'{profile}-b']
        for label in labels:
            root = self.run_root(self.probe, label)
            capture = root / 'capture'
            capture.mkdir(parents=True)
            (capture / run.value_probe.VALUES).write_text(self.values(profile))
            launch = {'label': label, 'exit_code': 0, 'renderer': renderer}
            if device:
                launch['device'] = device
            (root / RESULT).write_text(json.dumps(launch))
            run.value_probe.seal_value_capture(root)
        return labels

    def values(self, profile):
        return json.dumps({'k': 'host', 'v': profile}) + '\n{"k":"complete","v":true}\n'

    def publish(self, labels, profile):
        host = 'darwin' if profile == 'macos' else 'linux'
        with mock.patch.object(run.sys, 'platform', host), mock.patch.object(
                run.sys, 'argv', ['run.py', 'publish', *labels]):
            return run.main()

    def test_other_profile_and_mixed_launches_are_rejected(self):
        for destination, other in (('linux', 'macos'), ('macos', 'linux')):
            with self.subTest(destination=destination):
                for labels in (self.labels[other], [self.labels[destination][0], self.labels[other][0]]):
                    with self.assertRaisesRegex(SystemExit, f'does not match the {destination} profile'):
                        self.publish(labels, destination)
                self.assertFalse((self.probe / 'Reference').exists())

    def test_both_publication_orders_protect_existing_payloads(self):
        for order in (('macos', 'linux'), ('linux', 'macos')):
            with self.subTest(order=order):
                self.probe = self.root / ('probe-' + order[0])
                with mock.patch.object(run, 'PROBE', self.probe):
                    for profile in order:
                        self.assertEqual(self.publish(self.labels[profile], profile), 0)
                        with self.assertRaisesRegex(SystemExit, 'refusing to overwrite reference'):
                            self.publish(self.labels[profile], profile)
                    for profile in order:
                        target = run.value_probe.reference_dir(self.probe, profile=profile)
                        self.assertEqual((target / run.value_probe.VALUES).read_text(), self.values(profile))


if __name__ == '__main__':
    unittest.main()
