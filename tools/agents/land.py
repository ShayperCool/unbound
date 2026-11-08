#!/usr/bin/env python3
"""Land the current worktree branch on main: lock, rebase, oracle guard, gate, fast-forward.

Run from a worker worktree with everything committed:

    python3 tools/agents/land.py --check --probe 'python3 tests/projects/foo/run.py ...'
    python3 tools/agents/land.py         --probe 'python3 tests/projects/foo/run.py ...'

--check runs the gate on the current tree without the lock and without merging. A pass is
remembered per tree and probe list. Landing right after a passing --check, with main
unchanged, therefore does not rebuild. Landing registers the branch and its probes in a queue.
The machine-wide lock holder rebases queued branches in order and runs one combined gate in
an integration worktree. On pass it fast-forwards main through every branch and pushes once.
Conflicts reject only their branch. A failed train is bisected to land the passing prefix;
each caller waits for its own result and receives its gate log. A single queued branch keeps
its original worktree and warm build target.


Gate:
  - the oracle guard (tools/agents/oracle_guard.py);
  - the release build, which fails on "WITHOUT compatibility stubs";
  - when managed/ or the compatibility profile changed: `$UNBOUND gen-stubs` with the gate's own
    build, which fails when the hand-written code does not build without stubs or when the
    generator's output differs from the committed managed/UnityEngine/Generated/ (restored after);
  - managed unit tests, where only failures not listed in known-test-failures.txt count;
  - Rust tests of the touched crates;
  - each --probe command, which must exit 0. $UNBOUND names the built CLI and
    $UNBOUND_PROBE_UNITY_VERSION the release: the exported value, else the release of the probe
    projects the command names (their ProjectSettings/ProjectVersion.txt), else the default of
    tests/probe_paths.py (6000.0.71f1). Probe steps share the suite's Editor launch guard.
    Guarded probes require Linux /proc and pidfd support; unsupported platforms fail explicitly.
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
import tomllib
import xml.etree.ElementTree as ET

GATE_VERSION = '5'
STATE = Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound/agents'
LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'
DOCS_ONLY = re.compile(r'(\.md$)|(^docs/)')
GENERATED = 'managed/UnityEngine/Generated'
# What the generated stubs are made from: the hand-written API and the generator (managed/), and
# the profile's stub family and platform modules (unbound-compat).
STUB_INPUTS = re.compile(r'^(managed/|crates/unbound-compat/)')
STUB_PROJECT = 'tests/projects/packages'
HERE = Path(__file__).resolve().parent
from probe_runtime import (RELEASE_VARIABLE, default_release, probe_release, NO_EDITOR,
                           EDITOR_WATCH, EDITOR_MARK, EDITOR_EXIT, require_process_watch, stop_tree)
from runtime_snapshot import snapshot_runtime, pin_main, atomic_json
from slot import hidden_index_paths, slot_root


def integration_root():
    return slot_root().parent/'land-trains'


def die(code, message):
    print('land: ' + message, file=sys.stderr, flush=True)
    raise SystemExit(code)


def git(*args, cwd=None, check=True):
    result = subprocess.run(['git', *args], cwd=cwd, capture_output=True, text=True)
    if check and result.returncode:
        die(2, f'git {" ".join(args)} failed:\n{result.stderr.strip()}')
    return result.stdout.strip()


def main_worktree(cwd=None):
    path = None
    for line in git('worktree', 'list', '--porcelain', cwd=cwd).splitlines():
        if line.startswith('worktree '):
            path = Path(line[len('worktree '):])
        elif line == 'branch refs/heads/main':
            return path
    return None


def require_clean(where, label, allow_untracked=False):
    dirty = [line for line in git('--no-optional-locks', 'status', '--porcelain', '--untracked-files=all',
                                 '--ignore-submodules=none', cwd=where).splitlines()
             if not (allow_untracked and line.startswith('??'))]
    if dirty:
        listing = '\n  '.join(dirty[:20])
        die(2, f'{label} has uncommitted or untracked files (commit them, delete them or add them '
               f'to .gitignore; untracked sources would pass here and break main):\n  {listing}')

    if hidden_index_paths(where):
        die(2, f'{label} has tracked paths hidden by index flags; preserve any work and clear the flags first')


def tail(log, lines=30):
    text = log.read_text(errors='replace').splitlines()
    return '\n'.join(text[-lines:])


class Gate:
    def __init__(self, worktree, base, probes, full, log, priority=False, *,
                 changed=None, crates=None, releases=None, environment=None):
        self.worktree, self.base, self.probes, self.full, self.log = worktree, base, probes, full, log
        self.env = dict(os.environ if environment is None else environment)
        self.changed, self.crates, self.releases = changed, crates, releases
        self.env.pop('UNBOUND_LAND_GATE', None)
        if priority:
            self.env['UNBOUND_LAND_GATE'] = '1'
        self.probe_runtime = None

    def step(self, name, command, shell=False, env=None, guard_editor=False):
        if guard_editor:
            require_process_watch()
        print(f'land: {name} ...', flush=True)
        started = time.time()
        with self.log.open('a') as out:
            shown = command if shell else shlex.join(command)
            out.write(f'\n=== {name}: {shown}\n')
            out.flush()
            process = subprocess.Popen(command, cwd=self.worktree, env=env or self.env, shell=shell,
                                       stdout=out, stderr=subprocess.STDOUT,
                                       start_new_session=guard_editor)
            if guard_editor:
                EDITOR_WATCH.add(process.pid)
            try:
                code = process.wait()
            except BaseException:
                if guard_editor:
                    stop_tree(process)
                raise
            finally:
                editor = EDITOR_WATCH.remove(process.pid) if guard_editor else None
            if editor:
                out.write(f'\nland: {EDITOR_MARK}: {editor}\n')
                code = EDITOR_EXIT
        print(f'land: {name}: exit {code} after {time.time() - started:.0f}s', flush=True)
        return code

    def fail(self, name):
        print(tail(self.log), file=sys.stderr)
        die(5, f'gate failed at {name}; full log: {self.log}')

    def run(self):
        changed = self.changed if self.changed is not None else git(
            'diff', '--name-only', '--no-renames', f'{self.base}...HEAD', cwd=self.worktree).splitlines()
        guard = subprocess.run([sys.executable, str(HERE/'oracle_guard.py'), self.base, 'HEAD'],
                               cwd=self.worktree)
        if guard.returncode:
            die(4, 'oracle guard refused the change')
        if not self.full and all(DOCS_ONLY.search(path) for path in changed):
            print('land: docs-only change, build and tests skipped', flush=True)
            return
        build_py = [sys.executable, 'tools/build.py']
        selected = json.loads(subprocess.run([*build_py, '--print-env'], cwd=self.worktree, check=True,
                                             capture_output=True, text=True, env=self.env).stdout)
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

        crates = (self.touched_crates(changed) if self.crates is None else
                  sorted(set(self.crates) & set(self.touched_crates(['Cargo.toml']))))
        if self.full or crates:
            args = ['--workspace'] if self.full else [arg for crate in crates for arg in ('-p', crate)]
            if self.step('rust tests', [*build_py, '--', 'test', '--release', '--locked', *args]):
                self.fail('rust tests')

        self.prepare_probe_runtime(target)
        self.run_probes()

    def prepare_probe_runtime(self, target):
        """Freeze matching build outputs before any probe, retaining their run reference."""
        self.probe_runtime = snapshot_runtime(Path(self.env['UNBOUND']), target/'managed',
                                              self.log.with_suffix('.runtime'))
        runtime = self.probe_runtime
        libraries = str(runtime/'managed')
        if self.env.get('LD_LIBRARY_PATH'):
            libraries += os.pathsep+self.env['LD_LIBRARY_PATH']
        self.env.update(UNBOUND=str(runtime/'unbound'), UNBOUND_CLI=str(runtime/'unbound'), UNBOUND_RUNTIME=str(runtime),
                        UNBOUND_MANAGED_DIR=str(runtime/'managed'), LD_LIBRARY_PATH=libraries)
    def run_probes(self):
        """Each --probe command, with the release it runs for in $UNBOUND_PROBE_UNITY_VERSION."""
        for index, probe in enumerate(self.probes, 1):
            try:
                release = self.releases[index-1] if self.releases is not None else probe_release(probe, self.worktree)
            except ValueError as error:
                die(2, str(error))
            if self.step(f'probe {index} ({release}): {probe[:80]}', probe, shell=True,
                         env=dict(self.env, **{RELEASE_VARIABLE: release, NO_EDITOR: '1'}), guard_editor=True):
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
            command = [str(LAUNCHER), '--kind', 'test', *command]
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
        """Crates whose tests the change can affect: crates with changed files, crates whose
        build.rs compiles a changed native/ entry, and consumers of changed locked packages.
        Workspace manifest edits conservatively select every crate. Changed probe files select
        the crates whose Rust sources name that probe. This keeps importer output guards active
        for native sources, dependency changes and probe inputs as well."""
        crates = self.worktree/'crates'
        directories = {path.split('/')[1] for path in changed if path.startswith('crates/')}
        probes = {path.split('/')[2] for path in changed
                  if path.startswith('tests/projects/') and path.count('/') >= 3}
        if probes:
            # Rust tests that read a probe's committed files (importer output guards, Editor
            # reference fixtures) name its directory, as a `projects/<probe>` path or a
            # "<probe>" literal joined onto the probes' root. Over-selection only adds tests.
            names = '|'.join(re.escape(probe) for probe in sorted(probes))
            named = re.compile(rf'projects/(?:{names})(?![\w.-])|"(?:{names})"')
            for manifest in crates.glob('*/Cargo.toml'):
                if any(named.search(source.read_text(errors='replace'))
                       for source in manifest.parent.rglob('*.rs')):
                    directories.add(manifest.parent.name)
        if 'Cargo.toml' in changed:
            directories.update(path.parent.name for path in crates.glob('*/Cargo.toml'))
        native = {path.split('/')[1] for path in changed if path.startswith('native/')}
        if native:
            for build in crates.glob('*/build.rs'):
                text = build.read_text(errors='replace')
                if any(re.search(r'\b' + re.escape(entry) + r'\b', text) for entry in native):
                    directories.add(build.parent.name)
        if 'Cargo.lock' in changed:
            # Compare whole package records; diff context can include unchanged
            # neighbors and misses changes to sources, checksums or dependencies.
            snapshots = []
            for revision in [self.base, 'HEAD']:
                text = git('show', f'{revision}:Cargo.lock', cwd=self.worktree)
                packages = tomllib.loads(text).get('package', [])
                grouped = {}
                for package in packages:
                    key = (package['name'], package['version'], package.get('source', ''))
                    grouped[key] = package
                snapshots.append(grouped)
            before, after = snapshots
            bumped = {key[0] for key in before.keys() | after.keys() if before.get(key)!=after.get(key)}
            # Follow reverse dependencies in both snapshots, including removed
            # edges and renamed manifest dependencies, without repeated scans.
            consumers = {}
            for package in [*before.values(), *after.values()]:
                for dependency in package.get('dependencies', []):
                    consumers.setdefault(dependency.split()[0], set()).add(package['name'])
            pending = list(bumped)
            while pending:
                for consumer in consumers.get(pending.pop(), set())-bumped:
                    bumped.add(consumer)
                    pending.append(consumer)
            for manifest in crates.glob('*/Cargo.toml'):
                data = tomllib.loads(manifest.read_text())
                deps = {data['package']['name']}
                sections = [data, *data.get('target', {}).values()]
                for section in sections:
                    for group in ['dependencies', 'build-dependencies', 'dev-dependencies']:
                        for name, spec in section.get(group, {}).items():
                            deps.add(spec.get('package', name) if isinstance(spec, dict) else name)
                if deps & bumped:
                    directories.add(manifest.parent.name)
        names = set()
        for directory in directories:
            manifest = crates/directory/'Cargo.toml'
            if manifest.is_file():
                match = re.search(r'^\[package\][^\[]*?^name\s*=\s*"([^"]+)"', manifest.read_text(), re.M | re.S)
                if match:
                    names.add(match.group(1))
        return sorted(names)


def stamp_path(tree, probes, full, root, *, base=None, changed=(), crates=(), releases=None):
    releases = releases if releases is not None else [probe_release(probe, root) for probe in probes]
    values = [GATE_VERSION, tree, base, full, probes, releases, sorted(changed), sorted(crates)]
    key = hashlib.sha256(json.dumps(values, sort_keys=True).encode()).hexdigest()
    return STATE/'gate-pass'/key


def run_gate(worktree, base, probes, full, branch, priority=False, *, log=None, changed=None,
             crates=None, releases=None, environment=None):
    tree = git('rev-parse', 'HEAD^{tree}', cwd=worktree)
    base = git('rev-parse', base, cwd=worktree)
    if changed is None:
        changed = git('diff', '--name-only', '--no-renames', base, 'HEAD', cwd=worktree).splitlines()
    selector = Gate(worktree, base, [], full, Path('/dev/null'))
    if crates is None:
        crates = selector.touched_crates(changed)
    releases = releases if releases is not None else [probe_release(probe, worktree) for probe in probes]
    stamp = stamp_path(tree, probes, full, worktree, base=base, changed=changed, crates=crates, releases=releases)
    runtime_record = stamp.with_suffix('.runtime.json')
    if stamp.is_file():
        record = json.loads(runtime_record.read_text()) if runtime_record.is_file() else {}
        runtime = record.get('runtime')
        if runtime is None or Path(runtime).is_dir():
            message = f'land: gate already passed for tree {tree[:12]} ({stamp.read_text().strip()})'
            print(message, flush=True)
            if log is not None:
                log.parent.mkdir(parents=True, exist_ok=True)
                log.write_text(message+'\n'+str(record.get('log', ''))+'\n')
            return Path(runtime) if runtime else None
    logs = STATE/'logs'
    logs.mkdir(parents=True, exist_ok=True)
    safe = re.sub(r'[^A-Za-z0-9._-]+', '-', branch)
    log = log or logs/f'{datetime.datetime.now():%Y%m%d-%H%M%S}-{safe}.log'
    print(f'land: gate for tree {tree[:12]}, log {log}', flush=True)
    gate = Gate(worktree, base, probes, full, log, priority=priority, changed=changed, crates=crates,
                releases=releases, environment=environment)
    gate.run()
    atomic_json(runtime_record, {'runtime': str(gate.probe_runtime) if gate.probe_runtime else None, 'log': str(log)})
    stamp.parent.mkdir(parents=True, exist_ok=True)
    stamp.write_text(f'{branch} {datetime.datetime.now():%F %T}\n')
    return gate.probe_runtime


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
    remote = git('config', '--get', 'branch.main.remote', cwd=main_checkout, check=False)
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

    from land_train import LandTrain
    return LandTrain(sys.modules[__name__]).run(worktree, branch, args.probe, args.full)


if __name__ == '__main__':
    raise SystemExit(main())
