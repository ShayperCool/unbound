#!/usr/bin/env python3
"""Multiplayer Play Mode virtual project clones and --virtual-player activation; see README.md."""
import argparse
import os
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parents[2]
sys.path.insert(0, str(REPO / 'tests'))
from probe_paths import project_copy, run_root, private_label  # noqa: E402
import value_probe  # noqa: E402


def parse_kv(path: Path) -> dict[str, str]:
    if not path.exists():
        return {}
    out = {}
    for line in path.read_text(errors='replace').splitlines():
        if '=' in line:
            k, v = line.split('=', 1)
            out[k] = v
    return out


def check():
    if sys.platform != 'linux':
        print("mppmclones: virtual player launch interception is Linux-only for now; not run")
        return 0
    out = run_root(ROOT, private_label())
    proj = project_copy(ROOT, out / 'project')
    main_data = out / 'main-data'
    cli = value_probe.engine_cli()

    # 1. Activate Player 2 via --virtual-player 2 (MPPM's own UnityPlayer.Activate path).
    env1 = dict(os.environ, MPPM_PROBE_OUT=str(out), MPPM_PROBE_MODE='activate')
    with open(out / 'main.log', 'w') as log:
        code1 = subprocess.run(
            [str(value_probe.LAUNCHER), '--kind', 'engine-gpu', 'timeout', '900', cli,
             '--project-data', str(main_data), 'run', '-p', str(proj),
             '--virtual-player', '2', '--headless', '100000', '--no-watch'],
            env=env1, stdout=log, stderr=subprocess.STDOUT,
        ).returncode

    main_kv = parse_kv(out / 'main.txt')
    clone_kv = parse_kv(out / 'clone.txt')

    # Wait briefly for the clone process to finish exiting after Main's quitting handler closed it.
    clone_pid = int(clone_kv['pid']) if clone_kv.get('pid', '').isdigit() else -1
    clone_exited = False
    if clone_pid > 0:
        deadline = time.time() + 15
        while time.time() < deadline:
            try:
                os.kill(clone_pid, 0)
            except ProcessLookupError:
                clone_exited = True
                break
            time.sleep(0.2)

    vp_dir = main_data / 'Library' / 'VP'
    vp_clones = sorted(p for p in vp_dir.iterdir() if p.is_dir() and p.name.startswith('mppm')) if vp_dir.is_dir() else []
    clone_dir = vp_clones[0] if vp_clones else None

    # 2. Second run WITHOUT --virtual-player: must NOT auto-activate even with saved SystemData.json.
    out2 = out / 'run2'
    out2.mkdir(parents=True, exist_ok=True)
    env2 = dict(os.environ, MPPM_PROBE_OUT=str(out2), MPPM_PROBE_MODE='no-auto')
    with open(out / 'noauto.log', 'w') as log:
        code2 = subprocess.run(
            [str(value_probe.LAUNCHER), '--kind', 'engine-gpu', 'timeout', '300', cli,
             '--project-data', str(main_data), 'run', '-p', str(proj),
             '--headless', '100000', '--no-watch'],
            env=env2, stdout=log, stderr=subprocess.STDOUT,
        ).returncode

    # 3. Third run activating by player name ("Player 2") reusing the existing Library/VP clone and
    # saved SystemData.json.
    out3 = out / 'run3'
    out3.mkdir(parents=True, exist_ok=True)
    env3 = dict(os.environ, MPPM_PROBE_OUT=str(out3), MPPM_PROBE_MODE='activate')
    with open(out / 'reuse.log', 'w') as log:
        code3 = subprocess.run(
            [str(value_probe.LAUNCHER), '--kind', 'engine-gpu', 'timeout', '900', cli,
             '--project-data', str(main_data), 'run', '-p', str(proj),
             '--virtual-player', 'Player 2', '--headless', '100000', '--no-watch'],
            env=env3, stdout=log, stderr=subprocess.STDOUT,
        ).returncode
    reuse_main_kv = parse_kv(out3 / 'main.txt')
    reuse_clone_kv = parse_kv(out3 / 'clone.txt')
    reuse_pid = int(reuse_clone_kv['pid']) if reuse_clone_kv.get('pid', '').isdigit() else -1
    reuse_exited = False
    if reuse_pid > 0:
        deadline = time.time() + 15
        while time.time() < deadline:
            try:
                os.kill(reuse_pid, 0)
            except ProcessLookupError:
                reuse_exited = True
                break
            time.sleep(0.2)

    # 4. Mixed valid + invalid --virtual-player fails loudly without launching any clone.
    out4 = out / 'run4'
    out4.mkdir(parents=True, exist_ok=True)
    with open(out / 'invalid.log', 'w') as log:
        code_bad = subprocess.run(
            [str(value_probe.LAUNCHER), '--kind', 'engine-gpu', 'timeout', '300', cli,
             '--project-data', str(main_data), 'run', '-p', str(proj),
             '--virtual-player', '2', '--virtual-player', '1', '--headless', '10', '--no-watch'],
            env=dict(os.environ, MPPM_PROBE_OUT=str(out4), MPPM_PROBE_MODE='activate'),
            stdout=log, stderr=subprocess.STDOUT,
        ).returncode

    checks = [
        ('main exit 0', code1 == 0, code1),
        ('main sees IsMainEditor=True', main_kv.get('isMainEditor') == 'True', main_kv),
        ('main ChannelService started', int(main_kv.get('channelPort', '0') or '0') > 0, main_kv),
        ('main saw clone report', main_kv.get('cloneReportExists') == 'True', main_kv),
        ('main saw clone ChannelClient connected', int(main_kv.get('channelClients', '0') or '0') >= 1, main_kv),
        ('Unity Library/ untouched in project root', not (proj / 'Library').exists(), str(proj / 'Library')),
        ('MPPM created Library/VP clone in project data', clone_dir is not None, [str(p) for p in vp_clones]),
        ('clone Assets is symlink', clone_dir is not None and (clone_dir / 'Assets').is_symlink(), str(clone_dir)),
        ('clone ProjectSettings is symlink', clone_dir is not None and (clone_dir / 'ProjectSettings').is_symlink(), str(clone_dir)),
        ('clone Packages dir exists without copied manifest',
         clone_dir is not None and (clone_dir / 'Packages').is_dir() and not (clone_dir / 'Packages' / 'manifest.json').exists(),
         str(clone_dir)),
        ('VP SystemData.json written by MPPM', (vp_dir / 'SystemData.json').is_file(), str(vp_dir)),
        ('clone sees IsMainEditor=False', clone_kv.get('isMainEditor') == 'False', clone_kv),
        ('clone read SystemData.json tags', clone_kv.get('tagsOk') == 'True', clone_kv),
        ('clone args include -library-redirect', clone_kv.get('hasLibraryRedirect') == 'True', clone_kv),
        ('clone args include -readonly', clone_kv.get('hasReadOnly') == 'True', clone_kv),
        ('clone -readonly suppresses asset writes',
         clone_kv.get('readOnlyNoWrite') == 'True' and not (proj / 'Assets' / 'CloneShouldNotWrite.asset').exists(),
         clone_kv),
        ('clone args include -vpId=mppm*', clone_kv.get('hasVpId') == 'True', clone_kv),
        ('clone args include -vp-channel-name=vp-channel', clone_kv.get('hasChannelName') == 'True', clone_kv),
        ('clone args include -ump-channel-service-port', clone_kv.get('hasChannelPort') == 'True', clone_kv),
        ('clone args include -mainProcessId', clone_kv.get('hasMainProcessId') == 'True', clone_kv),
        ('clone closed when main exited', clone_exited, clone_pid),
        ('no-auto run exited 0', code2 == 0, code2),
        ('no-auto run did not launch a clone', not (out2 / 'clone.txt').exists(), str(out2 / 'clone.txt')),
        ('by-name reuse run exited 0', code3 == 0, code3),
        ('by-name reuse saw clone report and client',
         reuse_main_kv.get('cloneReportExists') == 'True' and int(reuse_main_kv.get('channelClients', '0') or '0') >= 1,
         reuse_main_kv),
        ('by-name reuse clone closed on main exit', reuse_exited, reuse_pid),
        ('mixed valid+invalid --virtual-player fails loudly without launching clone',
         code_bad != 0 and not (out4 / 'clone.txt').exists(), (code_bad, str(out4 / 'clone.txt'))),
    ]
    failed = [(name, detail) for name, ok, detail in checks if not ok]
    for name, detail in failed:
        print(f'FAIL {name}: {detail}')
    print(f'mppmclones: {len(checks) - len(failed)}/{len(checks)} checks pass; logs {out}')
    return 1 if failed else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['check'])
    parser.parse_args()
    return check()


if __name__ == '__main__':
    sys.exit(main())
