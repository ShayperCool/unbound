"""urp/compare.py --gate (one image, keyed by its reference path relative to the residual list), and
every committed residual list naming images that exist.

    python3 -m unittest discover -s tests/projects/urp -p test_image_gate.py
"""
import glob
import json
import os
import subprocess
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECTS = os.path.dirname(HERE)
sys.path.insert(0, os.path.join(PROJECTS, "particles"))
from test_compare_images import image, write_png  # noqa: E402

SCRIPT = os.path.join(HERE, "compare.py")


class CompareGateTests(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        self.ref = os.path.join(self.dir.name, "Reference", "scenarios", "lit.png")
        os.makedirs(os.path.dirname(self.ref))
        write_png(self.ref, image())
        self.cand = os.path.join(self.dir.name, "out.png")

    def tearDown(self):
        self.dir.cleanup()

    def run_script(self, *extra):
        done = subprocess.run([sys.executable, SCRIPT, self.ref, self.cand, *extra], capture_output=True, text=True)
        return done.returncode, done.stdout

    def residuals(self, entries):
        path = os.path.join(self.dir.name, "image-residuals.json")
        with open(path, "w") as handle:
            json.dump(entries, handle)
        return path

    def test_measurement_alone_never_fails(self):
        write_png(self.cand, image(5, 20))
        code, out = self.run_script()
        self.assertEqual(0, code)
        self.assertIn("pixels over 8: 5 of 16", out)
        self.assertNotIn("gate", out)

    def test_exact_image_passes_and_one_level_fails(self):
        write_png(self.cand, image())
        self.assertEqual((0, "gate: exact"), (self.run_script("--gate")[0], self.run_script("--gate")[1].splitlines()[-1]))
        write_png(self.cand, image(1, 1))
        code, out = self.run_script("--gate")
        self.assertEqual(1, code)
        self.assertIn("FAIL", out)

    def test_residual_keyed_by_path_relative_to_the_list(self):
        write_png(self.cand, image(2, 3))
        listed = self.residuals({"Reference/scenarios/lit.png": {"pixels": 2, "worst": 3, "reason": "test"}})
        code, out = self.run_script("--gate", listed)
        self.assertEqual(0, code, out)
        self.assertIn("recorded Reference/scenarios/lit.png: 2 pixels differ by up to 3 (test)", out)
        self.assertTrue(out.rstrip().endswith("gate: within recorded residuals"))
        other = self.residuals({"Reference/scenarios/unlit.png": {"pixels": 2, "worst": 3, "reason": "test"}})
        self.assertEqual(1, self.run_script("--gate", other)[0])
        write_png(self.cand, image(2, 4))
        self.assertEqual(1, self.run_script("--gate", listed)[0])

    def test_entries_need_counts_and_a_reason(self):
        write_png(self.cand, image())
        broken = self.residuals({"Reference/scenarios/lit.png": {"pixels": 2, "worst": 3}})
        code, _ = self.run_script("--gate", broken)
        self.assertNotEqual(0, code)


class CommittedListsTests(unittest.TestCase):
    # The reference directory a directory comparison (compare_images.py) reads each list against;
    # lists keyed by path (urp/compare.py) name their images themselves.
    DIRECTORIES = {
        "grass/image-residuals.json": "grass/Reference",
        "video/image-residuals.json": "video/Reference",
        "particles/image-residuals.json": "particles/Reference/images",
        "lighting/image-residuals.json": "lighting/Reference/images",
        "lighting/image-residuals-vulkan.json": "lighting/Reference/unity-6000.0.71f1/vulkan",
    }

    def test_every_entry_names_a_reference_image(self):
        lists = glob.glob(os.path.join(PROJECTS, "*", "image-residuals*.json"))
        self.assertTrue(lists)
        for path in lists:
            name = os.path.relpath(path, PROJECTS)
            with open(path) as handle:
                entries = json.load(handle)
            for key, entry in entries.items():
                with self.subTest(list=name, key=key):
                    if key.endswith(".png"):
                        self.assertTrue(os.path.isfile(os.path.join(os.path.dirname(path), key)))
                    else:
                        self.assertIn(name, self.DIRECTORIES)
                        self.assertTrue(os.path.isfile(os.path.join(PROJECTS, self.DIRECTORIES[name], key + ".png")))
                    self.assertTrue(entry["pixels"] > 0 and entry["worst"] > 0 and entry["reason"])


if __name__ == "__main__":
    unittest.main()
