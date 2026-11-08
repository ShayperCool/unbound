import contextlib
import io
import unittest
from scene_loop_compare import compare


class SceneLoopComparisonTests(unittest.TestCase):
    def verdict(self, left, right):
        with contextlib.redirect_stdout(io.StringIO()) as output:
            code = compare(left, right)
        return code, output.getvalue()

    def test_unspecified_receiver_order_is_reported_with_all_raw_deltas(self):
        a = 'load|2|preloading|Awake|A|First|loaded=False'
        b = 'load|2|preloading|Awake|B|First|loaded=False'
        code, output = self.verdict([a, b], [b, a])
        self.assertEqual(0, code)
        self.assertIn('-' + b, output)
        self.assertIn('+' + b, output)
        self.assertIn('Deferred:', output)

    def test_receiver_sequence_frames_counts_and_state_remain_strict(self):
        a = 'load|2|preloading|Awake|A|First|loaded=False'
        b = 'load|2|preloading|OnEnable|A|First|loaded=False'
        for actual in ([b, a], [a], [a, b, b], [a.replace('|2|', '|1|'), b],
                       [a.replace('False', 'True'), b]):
            self.assertEqual(1, self.verdict([a, b], actual)[0])

    def test_root_order_and_event_order_remain_strict(self):
        a = 'load|2|preloading|loaded.roots|Prefab:0,Plain:1'
        b = 'load|2|preloading|completed|progress=1.000,done=True,allow=True'
        self.assertEqual(1, self.verdict([a, b], [a.replace('Prefab:0,Plain:1', 'Plain:0,Prefab:1'), b])[0])
        self.assertEqual(1, self.verdict([a, b], [b, a])[0])
        self.assertEqual(1, self.verdict([a, b], [a, b, 'extra'])[0])


if __name__ == '__main__':
    unittest.main()
