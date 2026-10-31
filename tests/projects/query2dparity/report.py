#!/usr/bin/env python3
"""Compare release references; disposable results are stored in the validation cache."""
from pathlib import Path
import sys
sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from query_references import report_suites

if __name__=='__main__':
    raise SystemExit(report_suites(Path(__file__).resolve().parent,include_initial=False,include_ties=False,include_skin=False))
