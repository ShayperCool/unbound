#!/usr/bin/env python3
"""Compares a cube blocks capture with the Editor's, every value exactly: the texture compression
probe's comparator (tests/projects/texturecompression/compare.py, same arguments and residuals).
"""
import runpy
from pathlib import Path

if __name__ == '__main__':
    runpy.run_path(str(Path(__file__).resolve().parents[1]/'texturecompression'/'compare.py'), run_name='__main__')
