#!/usr/bin/env python3
"""Compare release references; disposable results are stored in the validation cache."""
from pathlib import Path
import sys
sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from query_references import verify_single

if __name__=='__main__':
    raise SystemExit(verify_single(Path(__file__).resolve().parent))
