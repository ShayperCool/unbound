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


if __name__ == '__main__':
    unittest.main()
