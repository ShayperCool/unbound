#!/usr/bin/env python3
"""Install and enable the hourly user disk janitor from the main checkout.

Linux: a systemd user timer and service. macOS: a launchd user agent
(~/Library/LaunchAgents/com.unbound.janitor.plist) run every hour, at low priority.
`--run-hours H` keeps validation runs H hours after their last write instead of the janitor's
default (tools/validation/README.md, "Disk janitor").
"""
import argparse
import os
from pathlib import Path
import plistlib
import subprocess
import sys

from slot import main_checkout
import janitor
import processes

# The variables the janitor's paths come from, passed on to the service.
ENVIRONMENT_KEYS = ('UNBOUND_BUILD_ROOT', 'UNBOUND_SLOT_ROOT', 'UNBOUND_RUNTIME_CACHE', 'XDG_CACHE_HOME')
LAUNCHD_LABEL = 'com.unbound.janitor'
# Hourly, as the Linux timer's OnCalendar=hourly.
LAUNCHD_INTERVAL_SECONDS = 3600
# lsof and ps (processes.py) live in /usr/sbin and /bin.
LAUNCHD_PATH = '/usr/bin:/bin:/usr/sbin:/sbin'


def janitor_arguments(repo, run_hours=None):
    arguments = [sys.executable, str(repo/'tools/agents/janitor.py'), '--apply', '--repo', str(repo),
                 '--scan-seconds', str(janitor.SERVICE_SCAN_SECONDS), '--run-seconds', str(janitor.SERVICE_RUN_SECONDS)]
    return arguments + (['--run-hours', str(run_hours)] if run_hours is not None else [])


def units(repo, environment, run_hours=None):
    # systemd expands percent specifiers even inside quoted arguments.
    def quote(value):
        return '"'+str(value).replace('\\', '\\\\').replace('"', '\\"').replace('%', '%%')+'"'

    env = '\n'.join('Environment='+quote(key+'='+environment[key]) for key in ENVIRONMENT_KEYS if key in environment)
    service = f'''[Unit]
Description=Unbound disk janitor

[Service]
Type=oneshot
SuccessExitStatus=3
WorkingDirectory={str(repo).replace('%', '%%')}
{env}
ExecStart={' '.join(value if value.startswith('--') or value.isdigit() else quote(value) for value in janitor_arguments(repo, run_hours))}
Nice=15
IOSchedulingClass=idle
MemoryMax=4G
OOMScoreAdjust=500
TimeoutStartSec=60min
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


def launchd_agent(repo, environment, run_hours=None):
    """The macOS user agent: the janitor every hour at low priority, its output appended to the
    janitor's log directory (it writes its own JSON log there too)."""
    cache = Path(environment.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound'
    log = cache/'agents/janitor/launchd.log'
    variables = {key: environment[key] for key in ENVIRONMENT_KEYS if key in environment}
    variables['PATH'] = LAUNCHD_PATH
    return plistlib.dumps({
        'Label': LAUNCHD_LABEL,
        'ProgramArguments': janitor_arguments(repo, run_hours),
        'WorkingDirectory': str(repo),
        'EnvironmentVariables': variables,
        'StartInterval': LAUNCHD_INTERVAL_SECONDS,
        'ProcessType': 'Background',
        'Nice': 15,
        'LowPriorityIO': True,
        'StandardOutPath': str(log),
        'StandardErrorPath': str(log),
    })


def install_launchd(repo, run_hours):
    destination = Path.home()/'Library/LaunchAgents'/f'{LAUNCHD_LABEL}.plist'
    destination.parent.mkdir(parents=True, exist_ok=True)
    (Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound/agents/janitor').mkdir(parents=True, exist_ok=True)
    temporary = destination.with_suffix('.plist.tmp')
    temporary.write_bytes(launchd_agent(repo, os.environ, run_hours))
    temporary.replace(destination)
    domain = f'gui/{os.getuid()}'
    # A loaded agent is replaced: unloading one that is not loaded fails harmlessly.
    subprocess.run(['launchctl', 'bootout', f'{domain}/{LAUNCHD_LABEL}'], capture_output=True)
    subprocess.run(['launchctl', 'bootstrap', domain, str(destination)], check=True)
    print(f'hourly janitor agent loaded from {repo} ({destination})')


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--run-hours', type=float, help='keep validation runs this long after their last write')
    args = parser.parse_args(argv)
    if args.run_hours is not None and args.run_hours <= 0:
        parser.error('--run-hours must be positive')
    repo = main_checkout(Path(__file__).resolve().parents[2])
    if not (repo/'tools/agents/janitor.py').is_file():
        raise SystemExit('land the janitor before installing the timer from main')
    if processes.MACOS:
        return install_launchd(repo, args.run_hours)
    destination = Path.home()/'.config/systemd/user'
    destination.mkdir(parents=True, exist_ok=True)
    for name, content in units(repo, os.environ, args.run_hours).items():
        target = destination/name
        temporary = target.with_suffix(target.suffix+'.tmp')
        temporary.write_text(content)
        temporary.replace(target)
    subprocess.run(['systemctl', '--user', 'daemon-reload'], check=True)
    subprocess.run(['systemctl', '--user', 'enable', '--now', 'unbound-janitor.timer'], check=True)
    print(f'hourly janitor enabled from {repo}')


if __name__ == '__main__':
    main()
