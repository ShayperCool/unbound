"""Controls for the cap-count layout proposal: exact order delta and key-wise byte checks."""
import importlib.util
from pathlib import Path
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


if __name__ == '__main__':
    unittest.main()
