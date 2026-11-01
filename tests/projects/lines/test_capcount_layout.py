"""Controls for the cap-count layout: exact order delta, key-wise byte checks, exclusive
atomic publication that leaves the reference unchanged on refusal, and the combined loader."""
import gzip
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('capcount_layout', Path(__file__).with_name('capcount_layout.py'))
layout = importlib.util.module_from_spec(spec)
spec.loader.exec_module(layout)


class LayoutTests(unittest.TestCase):
    def test_order_delta_lists_only_moved_canonical_keys(self):
        canonical = ['line.a', 'complete', 'blend.x', 'blend.y']
        captured = ['line.a', 'blend.x', 'blend.y', 'capcount.q', 'complete']
        moved = layout.order_delta(canonical, captured)
        self.assertEqual([m['key'] for m in moved], ['complete', 'blend.x', 'blend.y'])
        self.assertEqual(layout.order_delta(canonical, canonical + ['capcount.q']), [])

    def test_records_need_terminators_and_unique_keys(self):
        with self.assertRaises(SystemExit):
            layout.lines_by_key(b'{"k":"a","v":1}')
        with self.assertRaises(SystemExit):
            layout.lines_by_key(b'{"k":"a","v":1}\n{"k":"a","v":2}\n')
        rows, order = layout.lines_by_key(b'{"k":"a","v":1}\n{"k":"b","v":2}\n')
        self.assertEqual(order, ['a', 'b'])
        self.assertEqual(rows['b'], b'{"k":"b","v":2}\n')


def record(key, value):
    return json.dumps({'k': key, 'v': value}, separators=(',', ':')).encode()+b'\n'


IMAGE = bytes(range(64))
CANONICAL = [record('line.a', 1), record('complete', True), record('blend.x', 2), record('blend.y', [0.5])]
CONTROL = [record(k, {'i': i}) for i, k in enumerate(layout.EXPECTED_KEYS)]
CAPTURE = [CANONICAL[0], CANONICAL[2], CANONICAL[3], *CONTROL, CANONICAL[1]]


class PublishTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        root = Path(temp.name)
        self.probe, self.work = root/'probe', root/'work'
        self.target = self.probe/'Reference'/'unity-test'
        self.target.mkdir(parents=True)
        (self.target/'values.jsonl').write_bytes(b''.join(CANONICAL))
        (self.target/'a.rgba.gz').write_bytes(gzip.compress(IMAGE))
        (self.probe/'Assets').mkdir()
        (self.probe/'Assets/Probe.cs').write_bytes(b'class P {}')
        self.receipt = root/'receipt.json'
        run = layout.run
        for name, value in (('reference_root', lambda probe: self.target),
                            ('run_root', lambda probe, label: self.work),
                            ('editor_problem', lambda repeat: None),
                            ('capture_problem', lambda repeat: None),
                            ('cache_artifact_path', lambda path: path)):
            owner = layout if name == 'cache_artifact_path' else run
            self.addCleanup(setattr, owner, name, getattr(owner, name))
            setattr(owner, name, value)
        self.before = {f.name: f.read_bytes() for f in self.target.iterdir()}

    def capture(self, rows, source=b'class P {}'):
        for repeat in layout.run.REPEATS:
            d = self.work/repeat
            d.mkdir(parents=True, exist_ok=True)
            (d/'values.jsonl').write_bytes(b''.join(rows))
            (d/'a.rgba').write_bytes(IMAGE)
            (d/'prelaunch-inputs.json').write_text(json.dumps({'files': {
                'assets/Probe.cs': {'sha256': hashlib.sha256(source).hexdigest()},
                'runner': {'sha256': 'r'}}}))

    def refused(self, reason):
        with self.assertRaises(SystemExit) as raised:
            layout.publish('test', 'label', self.receipt, self.probe)
        self.assertIn(reason, str(raised.exception))
        self.assertEqual({f.name: f.read_bytes() for f in self.target.iterdir()}, self.before)
        self.assertFalse(self.receipt.exists())

    def test_publish_writes_only_the_control_rows_and_keeps_the_reference(self):
        self.capture(CAPTURE)
        self.assertEqual(layout.publish('test', 'label', self.receipt, self.probe), 0)
        self.assertEqual((self.target/layout.FILE).read_bytes(), b''.join(CONTROL))
        after = {f.name: f.read_bytes() for f in self.target.iterdir()}
        self.assertEqual(after.pop(layout.FILE), b''.join(CONTROL))
        self.assertEqual(after, self.before)
        receipt = json.loads(self.receipt.read_text())
        self.assertEqual(receipt['records'], 16)
        self.assertEqual(receipt['keys'], layout.EXPECTED_KEYS)
        self.receipt.unlink()
        self.before[layout.FILE] = b''.join(CONTROL)
        self.refused('already exists')

    def test_existing_control_file_is_never_replaced(self):
        self.capture(CAPTURE)
        (self.target/layout.FILE).write_bytes(b'old\n')
        self.before[layout.FILE] = b'old\n'
        self.refused('already exists')
        with self.assertRaises(FileExistsError):
            layout.create_exclusive(self.target/layout.FILE, b'new\n')
        self.assertEqual((self.target/layout.FILE).read_bytes(), b'old\n')
        self.assertEqual(sorted(f.name for f in self.target.iterdir()), sorted(self.before))

    def test_other_order_changes_and_sources_are_refused(self):
        self.capture([CANONICAL[0], CANONICAL[3], CANONICAL[2], *CONTROL, CANONICAL[1]])
        self.refused('order')
        self.capture(CAPTURE, source=b'class Q {}')
        self.refused('recorded probe inputs differ')
        self.capture([CANONICAL[0], CANONICAL[2], CANONICAL[3], *CONTROL[1:], CONTROL[0], CANONICAL[1]])
        self.refused('predeclared')

    def test_changed_canonical_record_or_image_is_refused(self):
        self.capture([CANONICAL[0].replace(b'1', b'7'), *CAPTURE[1:]])
        self.refused('do not reproduce the canonical records')
        self.capture(CAPTURE)
        (self.work/'native1/a.rgba').write_bytes(IMAGE[::-1])
        (self.work/'native2/a.rgba').write_bytes(IMAGE[::-1])
        self.refused('canonical images')

    def test_loader_combines_both_files_without_filtering(self):
        canonical = layout.load_reference(self.target, 'canonical')
        self.assertEqual(list(canonical), ['line.a', 'complete', 'blend.x', 'blend.y'])
        with self.assertRaises(SystemExit):
            layout.load_reference(self.target, 'capcount')
        (self.target/layout.FILE).write_bytes(b''.join(CONTROL))
        combined = layout.load_reference(self.target, 'capcount')
        self.assertEqual(list(combined), [*canonical, *layout.EXPECTED_KEYS])
        self.assertEqual(combined['blend.y'], [0.5])
        os.unlink(self.target/layout.FILE)
        (self.target/layout.FILE).write_bytes(b''.join(CONTROL[:-1]+[record('line.a', 1)]))
        with self.assertRaises(SystemExit):
            layout.load_reference(self.target, 'capcount')

    def test_known_order_delta_is_exact(self):
        canonical = ['line.a', 'complete', 'blend.x', 'blend.y']
        self.assertTrue(layout.known_order_delta(canonical, ['line.a', 'blend.x', 'blend.y', 'c.q', 'complete']))
        self.assertFalse(layout.known_order_delta(canonical, ['line.a', 'blend.y', 'blend.x', 'complete']))
        self.assertFalse(layout.known_order_delta(canonical, ['line.a', 'blend.x', 'complete', 'blend.y']))


if __name__ == '__main__':
    unittest.main()
