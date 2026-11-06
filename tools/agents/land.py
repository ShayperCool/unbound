#!/usr/bin/env python3
"""Land the current worktree branch on main: lock, rebase, oracle guard, gate, fast-forward.

Run from a worker worktree with everything committed:

    python3 tools/agents/land.py --check --probe 'python3 tests/projects/foo/run.py ...'
    python3 tools/agents/land.py         --probe 'python3 tests/projects/foo/run.py ...'

--check runs the gate on the current tree without the lock and without merging. A pass is
remembered per tree and probe list. Landing right after a passing --check, with main
unchanged, therefore does not rebuild. Landing holds one machine-wide lock while it:
  1. rebases onto main;
  2. re-runs the gate if the rebased tree has no pass;
  3. fast-forwards main;
  4. pushes main if main tracks a remote, like the post-commit hook does for commits.

Gate:
  - the oracle guard (tools/agents/oracle_guard.py);
  - the release build, which fails on "WITHOUT compatibility stubs";
  - when managed/ or the compatibility profile changed: `$UNBOUND gen-stubs` with the gate's own
    build, which fails when the hand-written code does not build without stubs or when the
    generator's output differs from the committed managed/UnityEngine/Generated/ (restored after);
  - managed unit tests, where only failures not listed in known-test-failures.txt count;
  - Rust tests of the touched crates;
  - each --probe command, which must exit 0. $UNBOUND names the built CLI.
Changes that touch only Markdown/docs skip the build and tests. --full forces every step
(all Rust crates).

Exit codes:
  0  ok
  2  usage or state error
  3  conflict with main: run `git rebase main`, resolve, re-test, land again
  4  refused by the oracle guard
  5  gate failed
"""
import argparse
import contextlib
import datetime
import fcntl
import hashlib
import json
import mmap
import os
from pathlib import Path
import re
import shlex
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

GATE_VERSION = '2'
STATE = Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound/agents'
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
DOCS_ONLY = re.compile(r'(\.md$)|(^docs/)')
GENERATED = 'managed/UnityEngine/Generated'
# What the generated stubs are made from: the hand-written API and the generator (managed/), and
# the profile's stub family and platform modules (unbound-compat).
STUB_INPUTS = re.compile(r'^(managed/|crates/unbound-compat/)')
STUB_PROJECT = 'tests/projects/packages'
HERE = Path(__file__).resolve().parent


def die(code, message):
    print('land: ' + message, file=sys.stderr, flush=True)
    raise SystemExit(code)


def git(*args, cwd=None, check=True):
    result = subprocess.run(['git', *args], cwd=cwd, capture_output=True, text=True)
    if check and result.returncode:
        die(2, f'git {" ".join(args)} failed:\n{result.stderr.strip()}')
    return result.stdout.strip()


def main_worktree():
    path = None
    for line in git('worktree', 'list', '--porcelain').splitlines():
        if line.startswith('worktree '):
            path = Path(line[len('worktree '):])
        elif line == 'branch refs/heads/main':
            return path
    return None


def require_clean(where, label, allow_untracked=False):
    dirty = [line for line in git('status', '--porcelain', cwd=where).splitlines()
             if not (allow_untracked and line.startswith('??'))]
    if dirty:
        listing = '\n  '.join(dirty[:20])
        die(2, f'{label} has uncommitted or untracked files (commit them, delete them or add them '
               f'to .gitignore; untracked sources would pass here and break main):\n  {listing}')


def tail(log, lines=30):
    text = log.read_text(errors='replace').splitlines()
    return '\n'.join(text[-lines:])


class Gate:
    def __init__(self, worktree, base, probes, full, log):
        self.worktree, self.base, self.probes, self.full, self.log = worktree, base, probes, full, log
        self.env = dict(os.environ)

    def step(self, name, command, shell=False, env=None):
        print(f'land: {name} ...', flush=True)
        started = time.time()
        with self.log.open('a') as out:
            shown = command if shell else shlex.join(command)
            out.write(f'\n=== {name}: {shown}\n')
            out.flush()
            code = subprocess.run(command, cwd=self.worktree, env=env or self.env, shell=shell,
                                  stdout=out, stderr=subprocess.STDOUT).returncode
        print(f'land: {name}: exit {code} after {time.time() - started:.0f}s', flush=True)
        return code

    def fail(self, name):
        print(tail(self.log), file=sys.stderr)
        die(5, f'gate failed at {name}; full log: {self.log}')

    def run(self):
        changed = git('diff', '--name-only', f'{self.base}...HEAD', cwd=self.worktree).splitlines()
        guard = subprocess.run([sys.executable, str(HERE/'oracle_guard.py'), self.base, 'HEAD'],
                               cwd=self.worktree)
        if guard.returncode:
            die(4, 'oracle guard refused the change')
        if not self.full and all(DOCS_ONLY.search(path) for path in changed):
            print('land: docs-only change, build and tests skipped', flush=True)
            return
        build_py = [sys.executable, 'tools/build.py']
        selected = json.loads(subprocess.run([*build_py, '--print-env'], cwd=self.worktree, check=True,
                                             capture_output=True, text=True).stdout)
        self.env.update(selected)
        target = Path(selected['CARGO_TARGET_DIR'])
        self.env['UNBOUND'] = str(target/'release/unbound')

        mark = self.log.stat().st_size if self.log.exists() else 0
        if self.step('build', [*build_py, '--', 'build', '--release', '--locked', '-p', 'unbound']):
            self.fail('build')
        with self.log.open(errors='replace') as out:
            out.seek(mark)
            if 'WITHOUT compatibility stubs' in out.read():
                print(tail(self.log), file=sys.stderr)
                die(5, 'the managed engine was built WITHOUT compatibility stubs: run '
                       '`$UNBOUND gen-stubs -p tests/projects/packages`, commit, land again')

        if self.full or any(STUB_INPUTS.match(path) for path in changed):
            self.stubs(Path(self.env['UNBOUND']))

        self.managed_tests(target)

        crates = self.touched_crates(changed)
        if self.full or crates:
            args = ['--workspace'] if self.full else [arg for crate in crates for arg in ('-p', crate)]
            if self.step('rust tests', [*build_py, '--', 'test', '--release', '--locked', *args]):
                self.fail('rust tests')

        for index, probe in enumerate(self.probes, 1):
            if self.step(f'probe {index}: {probe[:80]}', probe, shell=True):
                self.fail(f'probe {index}')

    def stubs(self, cli):
        """gen-stubs with the gate's build: the hand-written code must build without stubs and the
        generator must reproduce the committed Generated/ tree. gen-stubs writes into the checkout
        the CLI was compiled from, so it runs only when that is this worktree; Generated/ is
        restored afterwards either way."""
        # The CLI finds managed/UnityEngine from its crate's compile-time CARGO_MANIFEST_DIR.
        manifest = re.escape(str(self.worktree/'crates/unbound').encode()) + rb'(?!-)'  # not crates/unbound-host etc.
        with open(cli, 'rb') as binary, mmap.mmap(binary.fileno(), 0, access=mmap.ACCESS_READ) as image:
            if not re.search(manifest, image):
                die(5, f'{cli} was not compiled from this worktree ({self.worktree}): its gen-stubs would '
                       'write into another checkout; rebuild without a shared compiler cache result')
        command = [str(cli), 'gen-stubs', '-p', STUB_PROJECT]
        if LAUNCHER.is_file():  # the launcher's kind for unbound subcommands other than run
            command = [str(LAUNCHER), '--kind', 'tool', *command]
        env = dict(self.env)
        env.pop('UNBOUND_MANAGED_DIR', None)  # the build's own managed engine, next to the CLI
        try:
            code = self.step('generated stubs', command, env=env)
            drift = git('status', '--porcelain', '--untracked-files=all', '--', GENERATED, cwd=self.worktree)
            if drift:
                with self.log.open('a') as out:
                    out.write(f'\n=== {GENERATED} after gen-stubs:\n{drift}\n'
                              + git('diff', '--stat', '--', GENERATED, cwd=self.worktree) + '\n')
        finally:
            git('checkout', '--', GENERATED, cwd=self.worktree)
            git('clean', '-fdq', '--', GENERATED, cwd=self.worktree)
        if code:
            print(tail(self.log), file=sys.stderr)
            die(5, 'gate failed at generated stubs: gen-stubs failed (does the hand-written code build '
                   f'with -p:UnboundNoStubs=true?); full log: {self.log}')
        if drift:
            listing = '\n  '.join(drift.splitlines()[:20])
            die(5, f'gate failed at generated stubs: {GENERATED}/ is not what gen-stubs generates:\n  {listing}\n'
                   f'Run `$UNBOUND gen-stubs -p {STUB_PROJECT}`, commit the result, land again; full log: {self.log}')

    def managed_tests(self, target):
        dotnet = Path.home()/'.dotnet/dotnet'
        dotnet = str(dotnet) if dotnet.is_file() else 'dotnet'
        results = target/'land-test-results'
        trx = results/'land.trx'
        trx.unlink(missing_ok=True)
        command = [dotnet, 'test', 'managed/UnityEngine.Tests/UnityEngine.Tests.csproj', '-c', 'Release',
                   '--logger', 'trx;LogFileName=land.trx', '--results-directory', str(results)]
        if LAUNCHER.is_file():
            command = [str(LAUNCHER), '--kind', 'build', *command]
        code = self.step('managed tests', command)
        if not trx.is_file():
            self.fail('managed tests (no results: build error?)')
        failed = set()
        for result in ET.parse(trx).getroot().iter():
            if result.tag.endswith('UnitTestResult') and result.get('outcome') == 'Failed':
                failed.add(result.get('testName'))
        known = {line.strip() for line in (HERE/'known-test-failures.txt').read_text().splitlines()
                 if line.strip() and not line.startswith('#')}
        new = sorted(failed - known)
        fixed = sorted(known - failed)
        if fixed:
            print('land: now passing, remove from tools/agents/known-test-failures.txt: '
                  + ', '.join(fixed), flush=True)
        if new:
            print('land: new managed test failures:\n  ' + '\n  '.join(new), file=sys.stderr)
            die(5, f'gate failed at managed tests; full log: {self.log}')
        if code and not failed:
            self.fail('managed tests')

    def touched_crates(self, changed):
        names = set()
        for directory in {path.split('/')[1] for path in changed if path.startswith('crates/')}:
            manifest = self.worktree/'crates'/directory/'Cargo.toml'
            if manifest.is_file():
                match = re.search(r'^\[package\][^\[]*?^name\s*=\s*"([^"]+)"', manifest.read_text(), re.M | re.S)
                if match:
                    names.add(match.group(1))
        return sorted(names)


def stamp_path(tree, probes, full):
    key = hashlib.sha256('\n'.join([GATE_VERSION, tree, str(full), *probes]).encode()).hexdigest()
    return STATE/'gate-pass'/key


def run_gate(worktree, base, probes, full, branch):
    tree = git('rev-parse', 'HEAD^{tree}', cwd=worktree)
    stamp = stamp_path(tree, probes, full)
    if stamp.is_file():
        print(f'land: gate already passed for tree {tree[:12]} ({stamp.read_text().strip()})', flush=True)
        return
    logs = STATE/'logs'
    logs.mkdir(parents=True, exist_ok=True)
    safe = re.sub(r'[^A-Za-z0-9._-]+', '-', branch)
    log = logs/f'{datetime.datetime.now():%Y%m%d-%H%M%S}-{safe}.log'
    print(f'land: gate for tree {tree[:12]}, log {log}', flush=True)
    Gate(worktree, base, probes, full, log).run()
    stamp.parent.mkdir(parents=True, exist_ok=True)
    stamp.write_text(f'{branch} {datetime.datetime.now():%F %T}\n')


@contextlib.contextmanager
def land_lock(branch):
    STATE.mkdir(parents=True, exist_ok=True)
    holder = STATE/'land.holder'
    with open(STATE/'land.lock', 'a+') as handle:
        try:
            fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            who = holder.read_text().strip() if holder.exists() else 'unknown'
            print(f'land: waiting for the land lock, held by {who}', flush=True)
            fcntl.flock(handle, fcntl.LOCK_EX)
        holder.write_text(f'{branch} pid {os.getpid()} since {datetime.datetime.now():%F %T}\n')
        try:
            yield
        finally:
            holder.unlink(missing_ok=True)
            fcntl.flock(handle, fcntl.LOCK_UN)


def push_main(main_checkout):
    remote = git('config', '--get', 'branch.main.remote', check=False)
    if not remote or os.environ.get('UNBOUND_LAND_NO_PUSH'):
        return
    log = Path(git('rev-parse', '--git-common-dir', cwd=main_checkout))
    if not log.is_absolute():
        log = Path(main_checkout)/log
    with open(log/'auto-push.log', 'a') as out:
        out.write(f'[{datetime.datetime.now():%F %T}] main -> {remote}/main (land)\n')
        out.flush()
        subprocess.Popen(['git', 'push', '--quiet', remote, 'main:refs/heads/main'], cwd=main_checkout,
                         stdout=out, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL,
                         start_new_session=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--check', action='store_true', help='gate only: no lock, no rebase, no merge')
    parser.add_argument('--probe', action='append', default=[], metavar='CMD',
                        help='shell command run after the build; must exit 0 (repeatable)')
    parser.add_argument('--full', action='store_true', help='run every gate step and all Rust crates')
    args = parser.parse_args()

    worktree = Path(git('rev-parse', '--show-toplevel'))
    os.chdir(worktree)
    branch = git('symbolic-ref', '--quiet', '--short', 'HEAD', check=False)
    if not branch:
        die(2, 'HEAD is detached; land from a named branch')
    if branch == 'main':
        die(2, 'you are on main; land from a worker branch in its own worktree')
    main_checkout = main_worktree()
    if main_checkout and main_checkout.resolve() == worktree.resolve():
        die(2, 'this is the main checkout; land from a worker worktree')
    require_clean(worktree, 'this worktree')

    if args.check:
        if subprocess.run(['git', 'merge-base', '--is-ancestor', 'main', 'HEAD']).returncode:
            print('land: note: the branch is behind main; landing will rebase and re-run the gate', flush=True)
        run_gate(worktree, git('merge-base', 'main', 'HEAD'), args.probe, args.full, branch)
        print('land: check passed', flush=True)
        return 0

    with land_lock(branch):
        if subprocess.run(['git', 'merge-base', '--is-ancestor', 'main', 'HEAD']).returncode:
            print('land: rebasing onto main', flush=True)
            rebase = subprocess.run(['git', 'rebase', '--quiet', 'main'], capture_output=True, text=True)
            if rebase.returncode:
                conflicts = git('diff', '--name-only', '--diff-filter=U', check=False)
                subprocess.run(['git', 'rebase', '--abort'], capture_output=True)
                die(3, 'conflicts with main in:\n  ' + '\n  '.join(conflicts.splitlines() or ['(see git output)'])
                    + '\nRun `git rebase main` yourself and resolve: regenerate generated stubs instead of '
                      'merging them; for an ABI version both sides bumped, take main\'s value + 1. '
                      'Re-run the affected tests, then land again.')
        old = git('rev-parse', 'main')
        new = git('rev-parse', 'HEAD')
        if old == new:
            die(2, 'nothing to land: the branch has no commits beyond main')
        run_gate(worktree, old, args.probe, args.full, branch)
        if main_checkout:
            require_clean(main_checkout, f'the main checkout {main_checkout}', allow_untracked=True)
            merge = subprocess.run(['git', 'merge', '--ff-only', '--quiet', new], cwd=main_checkout,
                                   capture_output=True, text=True)
            if merge.returncode:
                die(2, f'fast-forward of main failed in {main_checkout}:\n{merge.stderr.strip()}')
        else:
            git('update-ref', 'refs/heads/main', new, old)
        commits = git('rev-list', '--count', f'{old}..{new}')
        STATE.mkdir(parents=True, exist_ok=True)
        with open(STATE/'landed.jsonl', 'a') as out:
            out.write(json.dumps({'time': f'{datetime.datetime.now():%F %T}', 'branch': branch, 'old': old,
                                  'new': new, 'commits': int(commits), 'worktree': str(worktree)}) + '\n')
        if main_checkout:
            push_main(main_checkout)
    print(f'land: landed {branch}: main {old[:10]} -> {new[:10]} ({commits} commits)', flush=True)
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
