import unittest
from unittest.mock import patch

import check_miters as gate


class ReportTests(unittest.TestCase):
    def test_keeps_every_failing_leaf_and_missing_or_extra_value(self):
        with patch.object(gate.compare, 'load_reference', return_value={'mesh': {'v': [1., 2., 3.]}, 'missing': 4}), \
                patch.object(gate.compare, 'load', return_value={'mesh': {'v': [1.1, 2.2]}, 'extra': 5}):
            out = gate.components('reference', 'capture')
        self.assertEqual(set(out), {'.mesh.v[0]', '.mesh.v[1]', '.mesh.v[2]', '.mesh.v.length', '.missing', '.extra'})
        self.assertEqual(out['.mesh.v[2]']['reference'], 3.)
        self.assertEqual(out['.missing']['kind'], 'missing')
        self.assertEqual(out['.extra']['kind'], 'extra')

    def test_only_the_named_residuals_are_open(self):
        for key in ('trail.f1.time', 'trail.f15.time', 'line.color.PerceptualBlend'):
            self.assertTrue(gate.open_residual(key), key)
        for key in ('trail.f1.time.log', 'trail.fx.time', 'line.color.PerceptualBlend.log', 'trail.f1.round.end.mesh',
                    'line.color.Blend', 'space.trail.far.mesh', 'simplify.closed.positions'):
            self.assertFalse(gate.open_residual(key), key)

if __name__ == '__main__':
    unittest.main()
