#!/usr/bin/env python3
"""Prepare the authored trail trial for public-only engine inspection; never runs Native."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
from run import fixture

p = argparse.ArgumentParser(description=__doc__)
p.add_argument('--release', required=True)
p.add_argument('--work', required=True, type=Path)
a = p.parse_args()
here = Path(__file__).resolve().parent
assert a.release == '6000.0.71f1'
a.work.mkdir(parents=True, exist_ok=False)
project = a.work/'project'
for folder in ('Assets', 'Packages', 'ProjectSettings'):
    shutil.copytree(here.parent/'particles'/folder, project/folder)
(project/'Assets/Scripts/ParticlesProbe.cs').write_text(fixture())
source = (here/'TrailWitness.cs').read_text()
start = source.index('            var trails = new ParticleSystem.Trails();')
end = source.index('            var renderer =', start)
source = source[:start] + source[end:]
(project/'Assets/Scripts/TrailWitness.cs').write_text(source)
(a.work/'capture').mkdir()
(a.work/'authored.json').write_text(json.dumps({str(f.relative_to(project)):hashlib.sha256(f.read_bytes()).hexdigest()
    for folder in ('Assets','Packages','ProjectSettings') for f in sorted((project/folder).rglob('*')) if f.is_file()}, indent=2)+'\n')
