#!/usr/bin/env python3
"""Install the shared bounded Editor runner without modifying frozen artifacts."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parent
CACHE = Path.home()/'.cache/compat'
UNITS = Path.home()/'.config/systemd/user'
CACHE.mkdir(parents=True,exist_ok=True)
UNITS.mkdir(parents=True,exist_ok=True)


def replace(path, content, mode=0o644):
    with tempfile.NamedTemporaryFile(dir=path.parent,delete=False) as output:
        output.write(content)
        temporary=Path(output.name)
    temporary.chmod(mode)
    os.replace(temporary,path)


old = CACHE/'with-heavy-job'
backup = CACHE/'with-heavy-job.serial-20261031'
if old.exists() and not backup.exists():
    shutil.copy2(old,backup)
replace(CACHE/'job_runner.py',(ROOT/'job_runner.py').read_bytes())
replace(CACHE/'job-limits.json',(ROOT/'job-limits.json').read_bytes())
replace(UNITS/'unbound-editors.slice',(ROOT/'unbound-editors.slice').read_bytes())
subprocess.run(['systemctl','--user','daemon-reload'],check=True)
replace(old,b'#!/usr/bin/env bash\nset -euo pipefail\nexec python3 "$HOME/.cache/compat/job_runner.py" "$@"\n',0o755)
print(json.dumps(json.loads((CACHE/'job-limits.json').read_text()),indent=2))
