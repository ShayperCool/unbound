#!/usr/bin/env python3
"""Install and enable the hourly user disk janitor from the main checkout."""
import os
from pathlib import Path
import subprocess
import sys

from slot import main_checkout


def units(repo, environment):
    # systemd expands percent specifiers even inside quoted arguments.
    def quote(value):
        return '"'+str(value).replace('\\', '\\\\').replace('"', '\\"').replace('%', '%%')+'"'

    keys = ('UNBOUND_BUILD_ROOT', 'UNBOUND_SLOT_ROOT', 'UNBOUND_RUNTIME_CACHE', 'XDG_CACHE_HOME')
    env = '\n'.join('Environment='+quote(key+'='+environment[key]) for key in keys if key in environment)
    service = f'''[Unit]
Description=Unbound disk janitor

[Service]
Type=oneshot
WorkingDirectory={str(repo).replace('%', '%%')}
{env}
ExecStart={quote(sys.executable)} {quote(repo/'tools/agents/janitor.py')} --apply --repo {quote(repo)}
Nice=15
IOSchedulingClass=idle
MemoryMax=4G
OOMScoreAdjust=500
TimeoutStartSec=45min
'''
    timer = '''[Unit]
Description=Hourly Unbound disk hygiene

[Timer]
OnCalendar=hourly
Persistent=true
RandomizedDelaySec=5min

[Install]
WantedBy=timers.target
'''
    return {'unbound-janitor.service': service, 'unbound-janitor.timer': timer}


def main():
    repo = main_checkout(Path(__file__).resolve().parents[2])
    if not (repo/'tools/agents/janitor.py').is_file():
        raise SystemExit('land the janitor before installing the timer from main')
    destination = Path.home()/'.config/systemd/user'
    destination.mkdir(parents=True, exist_ok=True)
    for name, content in units(repo, os.environ).items():
        target = destination/name
        temporary = target.with_suffix(target.suffix+'.tmp')
        temporary.write_text(content)
        temporary.replace(target)
    subprocess.run(['systemctl', '--user', 'daemon-reload'], check=True)
    subprocess.run(['systemctl', '--user', 'enable', '--now', 'unbound-janitor.timer'], check=True)
    print(f'hourly janitor enabled from {repo}')


if __name__ == '__main__':
    main()
