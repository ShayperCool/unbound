#!/usr/bin/env python3
"""Guard evidence accounting and the inherited motion/field tolerances."""
import contextlib,io,json,pathlib,tempfile,unittest
from unittest.mock import patch
import compare
class ComparisonTests(unittest.TestCase):
    def result(self, candidate, key='body/motion'):
        with tempfile.TemporaryDirectory() as d:
            a,b=pathlib.Path(d)/'a',pathlib.Path(d)/'b'
            a.write_text(json.dumps({'key':key,'value':[1.]})+'\n')
            b.write_text(''.join(json.dumps(r)+'\n' for r in candidate))
            with patch('sys.argv',['compare.py',str(a),str(b)]),contextlib.redirect_stdout(io.StringIO()):
                return compare.main()
    def test_original_motion_tolerance(self):
        self.assertFalse(self.result([{'key':'body/motion','value':[1.000999]}]))
        self.assertTrue(self.result([{'key':'body/motion','value':[1.001001]}]))
    def test_fields_keep_stricter_tolerance(self):
        self.assertTrue(self.result([{'key':'field','value':[1.000002]}],key='field'))
    def test_missing_record_fails(self):
        self.assertTrue(self.result([{'key':'other','value':[1.]}]))
    def test_extra_record_fails(self):
        self.assertTrue(self.result([{'key':'body/motion','value':[1.]},{'key':'other','value':[1.]}]))
    def test_duplicate_record_rejected(self):
        with self.assertRaises(ValueError):
            self.result([{'key':'body/motion','value':[1.]},{'key':'body/motion','value':[1.]}])
    def test_nan_cannot_pass(self):
        self.assertTrue(self.result([{'key':'body/motion','value':[float('nan')]}]))
if __name__=='__main__':unittest.main()
