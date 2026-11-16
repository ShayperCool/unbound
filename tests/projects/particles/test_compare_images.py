"""compare_images.py: the measurement without --gate, and the gate with recorded residuals.

    python3 -m unittest discover -s tests/projects/particles -p test_compare_images.py
"""
import json
import os
import struct
import subprocess
import sys
import tempfile
import unittest
import zlib

SCRIPT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "compare_images.py")


def write_png(path, pixels):
    """An 8-bit RGBA PNG of rows of (r, g, b, a) tuples, unfiltered."""
    height, width = len(pixels), len(pixels[0])
    raw = b"".join(b"\x00" + bytes(c for p in row for c in p) for row in pixels)

    def chunk(kind, body):
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body) & 0xFFFFFFFF)

    with open(path, "wb") as handle:
        handle.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
                     + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b""))


def image(changed=0, by=0):
    """4x4 grey; the first `changed` pixels raised by `by`."""
    rows = [[(100, 100, 100, 255)] * 4 for _ in range(4)]
    for i in range(changed):
        rows[i // 4][i % 4] = (100 + by, 100, 100, 255)
    return rows


class CompareImagesTests(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        self.ref, self.cand = os.path.join(self.dir.name, "ref"), os.path.join(self.dir.name, "cand")
        os.makedirs(self.ref)
        os.makedirs(self.cand)
        for name in ("a", "b"):
            write_png(os.path.join(self.ref, name + ".png"), image())

    def tearDown(self):
        self.dir.cleanup()

    def run_script(self, *extra, residuals=None):
        args = [sys.executable, SCRIPT, self.ref, self.cand, *extra]
        if residuals is not None:
            path = os.path.join(self.dir.name, "residuals.json")
            with open(path, "w") as handle:
                json.dump(residuals, handle)
            args.append(path)
        done = subprocess.run(args, capture_output=True, text=True)
        return done.returncode, done.stdout

    def test_measurement_alone_never_fails(self):
        write_png(os.path.join(self.cand, "a.png"), image(3, 9))
        code, out = self.run_script()
        self.assertEqual(0, code)
        self.assertIn("a: mean 1.69, worst 9, over 8: 3", out)
        self.assertIn("b.png: MISSING", out)
        self.assertNotIn("FAIL", out)
        self.assertTrue(out.rstrip().endswith("worst mean inf"))

    def test_gate_passes_exact_images(self):
        for name in ("a", "b"):
            write_png(os.path.join(self.cand, name + ".png"), image())
        code, out = self.run_script("--gate")
        self.assertEqual(0, code, out)
        self.assertTrue(out.endswith("worst mean 0.00\ngate: 2 exact, 0 within recorded residuals, 0 failed\n"), out)

    def test_gate_fails_one_level_on_one_pixel(self):
        write_png(os.path.join(self.cand, "a.png"), image(1, 1))
        write_png(os.path.join(self.cand, "b.png"), image())
        code, out = self.run_script("--gate")
        self.assertEqual(1, code)
        self.assertIn("FAIL a: 1 pixels differ by up to 1 (not recorded)", out)

    def test_gate_fails_a_missing_image(self):
        write_png(os.path.join(self.cand, "a.png"), image())
        code, out = self.run_script("--gate")
        self.assertEqual(1, code)
        self.assertIn("FAIL b: missing", out)

    def test_recorded_residual_holds_within_its_bounds_only(self):
        residuals = {"a": {"pixels": 2, "worst": 3, "reason": "test"}}
        write_png(os.path.join(self.cand, "b.png"), image())
        write_png(os.path.join(self.cand, "a.png"), image(2, 3))
        code, out = self.run_script("--gate", residuals=residuals)
        self.assertEqual(0, code, out)
        self.assertIn("recorded a: 2 pixels differ by up to 3 (test)", out)
        write_png(os.path.join(self.cand, "a.png"), image(3, 3))
        code, out = self.run_script("--gate", residuals=residuals)
        self.assertEqual(1, code)
        self.assertIn("FAIL a: 3 pixels differ by up to 3 (recorded 2 pixels up to 3)", out)
        write_png(os.path.join(self.cand, "a.png"), image(1, 4))
        code, out = self.run_script("--gate", residuals=residuals)
        self.assertEqual(1, code)

    def test_a_residual_that_now_matches_is_named(self):
        for name in ("a", "b"):
            write_png(os.path.join(self.cand, name + ".png"), image())
        code, out = self.run_script("--gate", residuals={"a": {"pixels": 2, "worst": 3, "reason": "test"}})
        self.assertEqual(0, code)
        self.assertIn("NOW MATCHES a", out)

    def test_gate_before_the_directories_takes_no_residual_list(self):
        for name in ("a", "b"):
            write_png(os.path.join(self.cand, name + ".png"), image())
        done = subprocess.run([sys.executable, SCRIPT, "--gate", self.ref, self.cand], capture_output=True, text=True)
        self.assertEqual(0, done.returncode, done.stderr)
        self.assertIn("gate: 2 exact", done.stdout)

    def test_a_residual_for_an_unknown_image_fails(self):
        for name in ("a", "b"):
            write_png(os.path.join(self.cand, name + ".png"), image())
        code, out = self.run_script("--gate", residuals={"c": {"pixels": 1, "worst": 1, "reason": "test"}})
        self.assertEqual(1, code)
        self.assertIn("FAIL c: a recorded residual for an image the reference does not have", out)


if __name__ == "__main__":
    unittest.main()
