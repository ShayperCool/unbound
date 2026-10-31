#!/usr/bin/env python3
"""Run repository validation fixtures from private copies, including their caches."""
import os
from pathlib import Path
import shutil
import sys
sys.path.insert(0,str(Path(__file__).resolve().parents[2]))
from probe_paths import run_root

repo = Path(__file__).resolve().parents[3]
work = Path(os.environ.get('QUERY_WORK',str(run_root(Path(__file__).resolve().parent))))
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
cli=Path(os.environ.get('UNBOUND_PROBE_CLI',str(work/'unbound')))
os.execv(str(cli),[str(cli),*args])
