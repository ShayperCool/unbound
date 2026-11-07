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

    def test_insignificant_exception_is_bounded_to_both_documented_components(self):
        key = 'trail.f6.api2Emitting.mid.asline'
        detail = {f'.{key}.v[{i}]': dict(reference=1., actual=1.0000442) for i in (6, 9)}
        self.assertTrue(gate.insignificant_centre_rounding(key, detail))
        self.assertFalse(gate.insignificant_centre_rounding('trail.f6.api2Emitting.mid.mesh', detail))
        detail[f'.{key}.v[6]']['actual'] = 1.00006
        self.assertFalse(gate.insignificant_centre_rounding(key, detail))
        detail[f'.{key}.v[6]']['actual'] = 1.00004
        detail[f'.{key}.n[0]'] = dict(reference=0., actual=1.)
        self.assertFalse(gate.insignificant_centre_rounding(key, detail))


if __name__ == '__main__':
    unittest.main()
