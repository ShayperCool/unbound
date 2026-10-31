#!/usr/bin/env python3
"""Run repository validation fixtures from private copies, including their caches."""
import os
from pathlib import Path
import shutil
import sys

repo = Path(__file__).resolve().parents[3]
work = Path(os.environ['QUERY_WORK'])
args = sys.argv[1:]
for option in ['-p','--project']:
    if option not in args:
        continue
    index=args.index(option)+1
    source=Path(args[index]).resolve()
    if not source.is_relative_to(repo/'tests/projects'):
        continue
    private=work/'gate-projects'/source.relative_to(repo/'tests/projects')
    if not private.exists():
        for name in ['Assets','Packages','ProjectSettings']:
            shutil.copytree(source/name,private/name)
    args[index]=str(private)
os.execv(str(work/'unbound'),[str(work/'unbound'),*args])
