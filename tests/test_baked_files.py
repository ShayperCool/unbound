"""The comparison of a bake's lighting data: light probe cells compared whatever their order, and
what the cells mean (their neighbors, weights and matrices) still compared."""
import copy
import json
from pathlib import Path
import sys
import unittest

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent))
import baked_files  # noqa: E402

REFERENCE = Path(__file__).resolve().parent/'projects/bakedbuffers/Reference/unity-6000.0.71f1'
TOLERANCE = 1e-3


def fields(scene):
    return json.loads((REFERENCE/scene/'lightingData.json').read_text())


def cells_of(objects):
    probes = next(o for o in objects if o['classId'] == 258)
    return probes['fields']['m_Data']['m_Tetrahedralization']['m_Tetrahedra']


def reordered(objects):
    """The same lighting data with its light probe cells in reverse order (neighbors renumbered)."""
    out = copy.deepcopy(objects)
    cells = cells_of(out)
    n = len(cells)
    cells.reverse()
    for c in cells:
        for k in range(4):
            v = int(c[f'neighbors[{k}]'])
            if v >= 0:
                c[f'neighbors[{k}]'] = str(n - 1 - v)
    return out


class CellComparison(unittest.TestCase):
    def test_cells_in_another_order_are_the_same_cells_in_another_order(self):
        want = fields('ProbesGrid')
        got = reordered(want)
        self.assertIsNone(baked_files.lighting_data_difference(got, want, TOLERANCE))
        self.assertIn('16 of 16 cells in another place', baked_files.cell_order_difference(got, want, TOLERANCE))
        self.assertIsNone(baked_files.cell_order_difference(want, want, TOLERANCE))

    def test_a_wrong_neighbor_matrix_or_validity_still_differs(self):
        want = fields('ProbesGrid')
        changes = (
            lambda c, i, o: c[i].__setitem__('neighbors[0]', c[i]['neighbors[1]']),
            lambda c, i, o: c[o].__setitem__('neighbors[3]', str((int(c[o]['neighbors[3]']) + 1) % len(c))),
            lambda c, i, o: c[i]['matrix'].__setitem__('e00', str(float(c[i]['matrix']['e00']) + 0.1)),
            lambda c, i, o: c[o]['matrix'].__setitem__('e13', str(float(c[o]['matrix']['e13']) + 0.1)),
            lambda c, i, o: c[i].__setitem__('isValid', 'false'),
        )
        for change in changes:
            got = reordered(want)
            cells = cells_of(got)
            inner = next(k for k, c in enumerate(cells) if int(c['indices[3]']) >= 0)
            outer = next(k for k, c in enumerate(cells) if int(c['indices[3]']) < 0)
            change(cells, inner, outer)
            self.assertIsNotNone(baked_files.lighting_data_difference(got, want, TOLERANCE))


if __name__ == '__main__':
    unittest.main()
