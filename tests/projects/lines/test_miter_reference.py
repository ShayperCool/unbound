import gzip
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import miter_reference as ref


def record(key, value):
    return json.dumps(dict(k=key, v=value)).encode()+b'\n'


class PublicationTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        root = Path(temp.name)
        self.target, self.work = root/'reference', root/'work'
        self.target.mkdir()
        self.old = [record('old', 1), record('complete', True), record('blend.x', 2)]
        self.cap = [record(k, 3) for k in ref.layout.EXPECTED_KEYS]
        (self.target/'values.jsonl').write_bytes(b''.join(self.old))
        (self.target/ref.layout.FILE).write_bytes(b''.join(self.cap))
        (self.target/'old.rgba.gz').write_bytes(gzip.compress(b'old'))
        self.before = ref.layout.tree(self.target)
        self.new = [record(k, 4) for k in ref.KEYS]
        self.rows = [self.old[0], self.old[2], *self.cap, *self.new, self.old[1]]
        self.receipt = root/'receipt.json'
        run = ref.layout.run
        for obj, name, value in [
            (run, 'reference_root', lambda p: self.target),
            (run, 'run_root', lambda p, l: self.work),
            (run, 'editor_problem', lambda r: None),
            (run, 'capture_problem', lambda r: None),
            (ref.layout, 'source_binding', lambda *a: {}),
            (ref.layout, 'cache_artifact_path', lambda p: p),
        ]:
            self.enterContext(patch.object(obj, name, value))
        self.capture()

    def capture(self):
        for repeat in ref.layout.run.REPEATS:
            d = self.work/repeat
            d.mkdir(parents=True, exist_ok=True)
            (d/'values.jsonl').write_bytes(b''.join(self.rows))
            (d/'old.rgba').write_bytes(b'old')
            (d/ref.IMAGE).write_bytes(b'new')

    def refused(self, message):
        with self.assertRaisesRegex(SystemExit, message):
            ref.publish('test', 'test', self.receipt)
        self.assertEqual(ref.layout.tree(self.target), self.before)
        self.assertFalse(self.receipt.exists())

    def test_adds_only_new_records_and_image(self):
        ref.publish('test', 'test', self.receipt)
        self.assertEqual((self.target/ref.FILE).read_bytes(), b''.join(self.new))
        self.assertEqual(gzip.decompress((self.target/(ref.IMAGE+'.gz')).read_bytes()), b'new')
        self.assertEqual({k: ref.layout.tree(self.target)[k] for k in self.before}, self.before)
        self.assertEqual(len(ref.load(self.target)), len(self.rows))

    def test_refuses_changed_existing_record(self):
        self.rows[0] = record('old', 9)
        self.capture()
        self.refused('existing reference records changed')

    def test_refuses_changed_existing_image(self):
        for repeat in ref.layout.run.REPEATS:
            (self.work/repeat/'old.rgba').write_bytes(b'changed')
        self.refused('existing image changed')

    def test_refuses_different_independent_repeat(self):
        (self.work/'native2'/ref.IMAGE).write_bytes(b'different')
        self.refused('independent repeats differ')

    def test_refuses_missing_new_key(self):
        self.rows.pop(-2)
        self.capture()
        self.refused('new records differ')

    def test_refuses_unrelated_order_change(self):
        self.rows[0], self.rows[1] = self.rows[1], self.rows[0]
        self.capture()
        self.refused('old records moved')

    def test_chain_extension_keeps_all_previous_records_and_images(self):
        ref.publish('test', 'test', self.receipt)
        self.receipt.unlink()
        before = ref.layout.tree(self.target)
        chain = [record(k, 5) for k in ref.CHAIN_KEYS]
        self.rows = self.rows[:-1] + chain + self.rows[-1:]
        self.capture()
        ref.publish('test', 'test', self.receipt, chains=True)
        self.assertEqual((self.target/ref.CHAIN_FILE).read_bytes(), b''.join(chain))
        self.assertEqual({k: ref.layout.tree(self.target)[k] for k in before}, before)
        self.assertEqual(len(ref.load(self.target)), len(self.rows))


if __name__ == '__main__':
    unittest.main()
