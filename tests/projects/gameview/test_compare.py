import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('gameview_runner', Path(__file__).with_name('run.py'))
runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(runner)


class CompareTests(unittest.TestCase):
    def compare(self, expected, actual):
        with tempfile.TemporaryDirectory() as directory:
            paths = [Path(directory)/name for name in ('reference', 'capture')]
            for path, rows in zip(paths, (expected, actual)):
                path.write_text(''.join(json.dumps({'k': k, 'v': v})+'\n' for k,v in rows.items()))
            return runner.compare(*paths)

    def test_complete_capture_matches(self):
        self.assertEqual(0, self.compare({'point': [-1, 444.444458], 'complete': True},
                                        {'point': [-1, 444.444458], 'complete': True}))

    def test_missing_and_extra_rows_fail(self):
        self.assertEqual(1, self.compare({'point': [1, 2]}, {}))
        self.assertEqual(1, self.compare({}, {'point': [1, 2]}))

    def test_raw_pointer_difference_is_not_tolerated(self):
        self.assertEqual(1, self.compare({'point': [1, 2]}, {'point': [1, 2.0000001]}))

    def test_gate_accepts_only_the_exact_recorded_precision_pair(self):
        row = ('1.pointer2', [480, -124.444443], [480, -124.444458])
        self.assertTrue(runner.precision_only([row]))
        self.assertFalse(runner.precision_only([('other', row[1], row[2])]))
        self.assertFalse(runner.precision_only([('1.pointer2', row[1], [480, -124.44446])]))
        self.assertFalse(runner.precision_only([row, ('missing', [1,2], None)]))
        self.assertFalse(runner.precision_only([('1.pointer2',row[1],None)]))

    def test_reference_extension_keeps_every_existing_row_byte(self):
        old=b'{"k":"a","v":1}\n{"k":"complete","v":true}\n'
        new=b'{"k":"a","v":1}\n{"k":"new","v":2}\n{"k":"complete","v":true}\n'
        runner.validate_extension(old,new)
        with self.assertRaises(RuntimeError):
            runner.validate_extension(old,new.replace(b'"v":1}',b'"v":1.0}'))
        with self.assertRaises(RuntimeError):
            runner.validate_extension(old,new.replace(b'{"k":"a","v":1}\n',b''))
        with self.assertRaises(RuntimeError):
            runner.validate_extension(old,b'{"k":"complete","v":true}\n{"k":"a","v":1}\n')


if __name__ == '__main__':
    unittest.main()
