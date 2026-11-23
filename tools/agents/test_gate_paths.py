"""Every probe names in its gate.json engine_paths the parts of other probes it uses."""
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import gate_paths  # noqa: E402

SOURCE = '''
"""Docstring naming tests/projects/fifth."""
BASE = [(PROBE.parent/'other', ('Packages', 'Assets/Scenes'))]
COMPARE = PROBE.parent / 'third' / 'compare.py'
ROOT = REPO / 'tests/projects/fourth'
SIBLING = Path(__file__).resolve().parents[1] / 'sixth/compare.py'
COPIED = {'Assets/A.mat': 'seventh/Assets/B.mat', 'note': 'not a/path here', 'prefix': 'seventh/'}
'''


class GatePathsTest(unittest.TestCase):
    def test_every_used_part_of_another_probe_is_declared(self):
        self.assertEqual(gate_paths.undeclared(), {}, 'run python3 tools/agents/gate_paths.py --write')

    def test_base_parts_named_files_and_directories_are_read_but_not_docstrings(self):
        self.assertEqual(gate_paths.uses(SOURCE, {'seventh'}),
                         {'other/Packages', 'other/Assets/Scenes', 'third/compare.py', 'fourth', 'sixth/compare.py',
                          'seventh/Assets/B.mat'})


if __name__ == '__main__':
    unittest.main()
