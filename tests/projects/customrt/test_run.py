"""Verify that publishing refuses incomplete, crashed or inconsistent Editor captures."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('customrt_run', Path(__file__).with_name('run.py'))
run = importlib.util.module_from_spec(spec)
spec.loader.exec_module(run)


class PublishTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.work = Path(self.temp.name)/'work'
        self.reference = Path(self.temp.name)/'Reference'/'unity-test'
        self.saved = run.reference_root
        run.reference_root = lambda probe: self.reference
        self.addCleanup(setattr, run, 'reference_root', self.saved)
        for repeat in ('native1', 'native2'):
            self.capture(repeat)

    def capture(self, repeat, rows=None, images=None, exit_code='0', renderer='NVIDIA GeForce RTX 4060'):
        d = self.work/repeat
        d.mkdir(parents=True, exist_ok=True)
        rows = rows if rows is not None else [{'k': 'a.pixels', 'v': 2}, {'k': 'x', 'v': 1}, {'k': 'complete', 'v': True}]
        (d/'values.jsonl').write_text(''.join(json.dumps(r, separators=(',', ':')) + '\n' for r in rows))
        for name, size in (images if images is not None else {'a': 32}).items():
            (d/(name + '.rgba')).write_bytes(bytes(size))
        (d/'editor.log').write_text(f'Renderer: {renderer}\n')
        if exit_code is not None:
            (d/'exit.txt').write_text(exit_code + '\n')
        return d

    def refused(self, reason):
        with self.assertRaises(SystemExit) as raised:
            run.publish(self.work)
        self.assertIn(reason, str(raised.exception))
        self.assertFalse(self.reference.exists())

    def both(self, **kwargs):
        for repeat in ('native1', 'native2'):
            for p in (self.work/repeat).glob('*.rgba'):
                p.unlink()
            self.capture(repeat, **kwargs)

    def test_complete_captures_publish(self):
        self.assertEqual(run.publish(self.work), 0)
        self.assertTrue((self.reference/'values.jsonl').exists() and (self.reference/'a.rgba.gz').exists())

    def test_matching_partial_captures_are_refused(self):
        self.both(rows=[{'k': 'a.pixels', 'v': 2}, {'k': 'x', 'v': 1}])
        self.refused('complete: true')

    def test_failed_probe_is_refused(self):
        self.both(rows=[{'k': 'a.pixels', 'v': 2}, {'k': 'fatal', 'v': 'boom'}, {'k': 'complete', 'v': False}])
        self.refused('complete: true')
        self.both(rows=[{'k': 'a.pixels', 'v': 2}, {'k': 'fatal', 'v': 'boom'}, {'k': 'complete', 'v': True}])
        self.refused('fatal')

    def test_editor_exit_status_is_required(self):
        self.both(exit_code='1')
        self.refused('exit with 0')
        for repeat in ('native1', 'native2'):
            (self.work/repeat/'exit.txt').unlink()
        self.refused('exit with 0')

    def test_images_must_match_the_record(self):
        self.both(images={})
        self.refused('images differ')
        self.both(images={'a': 32, 'b': 16})
        self.refused('images differ')
        self.both(images={'a': 16})
        self.refused('sizes differ')

    def test_watchdog_and_renderer(self):
        (self.work/'native2'/'watchdog.txt').write_text('late\n')
        self.refused('did not finish')
        (self.work/'native2'/'watchdog.txt').unlink()
        self.capture('native1', renderer='llvmpipe')
        self.refused('NVIDIA')

    def test_differing_complete_captures_are_refused(self):
        self.capture('native2', rows=[{'k': 'a.pixels', 'v': 2}, {'k': 'x', 'v': 2}, {'k': 'complete', 'v': True}])
        self.refused('differ in values')


if __name__ == '__main__':
    unittest.main()
