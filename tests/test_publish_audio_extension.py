"""An interrupted publication must preserve the previous reference and allow a retry."""
import gzip
import json
from pathlib import Path
import struct
import tempfile
import unittest
from unittest.mock import patch

from publish_audio_extension import publish


class AudioPublicationTests(unittest.TestCase):
    def test_interrupted_writes_preserve_reference_and_allow_retry(self):
        for failure in ('sample', 'header'):
            with self.subTest(failure=failure), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                fixture = root/'fixture'
                target = fixture/'Reference/unity-example/rate-48000'
                target.mkdir(parents=True)
                prior = b'{"release":"example","rate":48000}\n'
                (target/'capture.json').write_bytes(prior)
                (fixture/'verify.py').write_text('def verify(root, release, rate):\n    assert (root/"capture.json").is_file()\n')
                capture, repeat = root/'capture', root/'repeat'
                raw = struct.pack('<ff', 0.25, -0.5)
                for source in [capture, repeat]:
                    source.mkdir()
                    (source/'wet.f32').write_bytes(raw)
                    (source/'capture.json').write_text(json.dumps(dict(release='example', rate=48000,
                        wet=dict(file='wet.f32', frames=1, channels=2))))

                class InterruptedGzip:
                    def __init__(self, *args, **kwargs): pass
                    def __enter__(self): return self
                    def __exit__(self, *args): return False
                    def write(self, data): raise OSError('disk full')

                interruption = (patch('publish_audio_extension.gzip.GzipFile', InterruptedGzip)
                                if failure == 'sample' else
                                patch('publish_audio_extension.shutil.copyfile', side_effect=OSError('disk full')))
                with interruption, self.assertRaisesRegex(OSError, 'disk full'):
                    publish(fixture, capture, repeat, 'example', 48000)
                self.assertEqual(prior, (target/'capture.json').read_bytes())
                self.assertEqual([], list(target.glob('*.tmp')))
                if failure == 'sample': self.assertFalse((target/'wet.f32.gz').exists())
                publish(fixture, capture, repeat, 'example', 48000)
                self.assertEqual(raw, gzip.decompress((target/'wet.f32.gz').read_bytes()))
                self.assertIn('wet', json.loads((target/'capture.json').read_text()))
