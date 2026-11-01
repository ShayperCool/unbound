#!/usr/bin/env python3
"""Build and run only the focused CPU tests, with private managed outputs."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_file, run_root

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--release', required=True)
parser.add_argument('--label', required=True)
args = parser.parse_args()
if args.release != '6000.0.71f1':
    parser.error('focused test project currently selects the 6000.0.71f1 reference')
if not args.label or Path(args.label).name != args.label or args.label in ('.', '..'):
    parser.error('label must be a single directory name')
os.environ['UNBOUND_PROBE_UNITY_VERSION'] = args.release
reference = reference_file(PROBE, 'jsonl')
if not reference.is_file():
    parser.error('publish the independent Native pair first')
work = run_root(PROBE, args.label)
work.mkdir(parents=True, exist_ok=False)
sources = sorted((REPO/'managed/UnityEngine').rglob('*.cs')) + sorted((REPO/'managed/TextureRaw.Tests').rglob('*')) + sorted((PROBE/'Assets').rglob('*.cs')) + [reference]
source_hashes = {str(p.relative_to(REPO)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources if p.is_file() and 'Generated' not in p.parts}
command = [str(Path.home()/'.cache/compat/with-heavy-job'), 'timeout', '300',
           str(Path.home()/'.dotnet/dotnet'), 'test', str(REPO/'managed/TextureRaw.Tests/TextureRaw.Tests.csproj'),
           '-c', 'Release', '-p:UnboundNoStubs=true', '-p:UnboundTarget='+str(work/'build')+'/',
           '--disable-build-servers', '-m:1', '-p:UseSharedCompilation=false', '/nodeReuse:false',
           '--logger', 'trx;LogFileName=focused.trx', '--results-directory', str(work/'tests')]
(work/'command.json').write_text(json.dumps({'command':command, 'head':subprocess.check_output(['git','rev-parse','HEAD'],cwd=REPO,text=True).strip(), 'source_hashes':source_hashes},indent=2)+'\n')
env = dict(os.environ, DOTNET_CLI_USE_MSBUILD_SERVER='0', MSBUILDDISABLENODEREUSE='1',
           UseSharedCompilation='false', TEXTURE_RAW_UNIT_OUT=str(work/'witness'))
with (work/'managed.log').open('x') as log:
    result = subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT)
changed = [name for name,sha in source_hashes.items() if hashlib.sha256((REPO/name).read_bytes()).hexdigest() != sha]
(work/'result.json').write_text(json.dumps({'exit':result.returncode, 'sources_changed_during_run':changed,
                                         'combined_runtime_replay':False},indent=2)+'\n')
print(f'focused managed exit={result.returncode}; {work}')
raise SystemExit(result.returncode or int(bool(changed)))
