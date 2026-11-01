#!/usr/bin/env python3
"""Compare a ScreenCapture capture with a reference: the full key union and every float channel.

    compare.py REFERENCE ACTUAL [--report REPORT.json]

The render texture probe's comparator (tests/projects/rtdepth/compare.py) unchanged: values and
image channels with absolute and relative tolerance 2e-4, strings, booleans and log lists exactly;
every missing, extra and failing value and image is kept in the report and printed.
"""
import importlib.util
from pathlib import Path
import sys

spec = importlib.util.spec_from_file_location('rtdepth_compare', Path(__file__).resolve().parents[1]/'rtdepth/compare.py')
compare = importlib.util.module_from_spec(spec)
spec.loader.exec_module(compare)

if __name__ == '__main__':
    sys.exit(compare.main())
