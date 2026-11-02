#!/usr/bin/env python3
"""Build with a private Cargo target and a shared compiler cache.

Use `tools/build.py -- build --release --locked -p unbound`.
The installed heavy-job launcher is used once. --direct is for an existing lease.
No global Cargo/shell settings are changed and no target is copied or seeded.
"""
import argparse
import fcntl
import hashlib
import json
import os
from pathlib import Path
import shutil
import shlex
import sys


def environment(root, cache, backend, env):
    env = dict(env)
    cargo_bin = Path.home()/'.cargo/bin'
    env['PATH'] = str(cargo_bin)+os.pathsep+env.get('PATH', '')
    identity = hashlib.sha256(os.fsencode(root.resolve())).hexdigest()[:16]
    target = Path(env.get('CARGO_TARGET_DIR') or cache/'targets'/identity)
    if not target.is_absolute():
        target = root/target
    target = target.resolve()
    env['CARGO_TARGET_DIR'] = str(target)
    # Cargo's separate build-dir overrides target-dir for intermediates/OUT_DIR.
    # Give it the same isolation even if the shell inherited a shared directory.
    env['CARGO_BUILD_BUILD_DIR'] = str(target/'build')
    env.setdefault('CARGO_BUILD_JOBS', '6')
    pinned = Path.home()/'.cache/unbound/tools/kache-2.0.0/kache'
    kache = str(pinned) if pinned.is_file() else shutil.which('kache', path=env['PATH'])
    if backend == 'auto':
        backend = 'kache' if kache else 'sccache'
    wrapper = kache if backend == 'kache' else shutil.which('sccache', path=env['PATH'])
    if backend == 'none':
        wrapper = ''
    elif not wrapper:
        raise ValueError(f'{backend} is not installed; see tools/validation/README.md')
    env['RUSTC_WRAPPER'] = wrapper
    if backend == 'kache':
        env.setdefault('KACHE_CACHE_DIR', str(cache/'kache'))
        env.setdefault('KACHE_BASE_DIR', str(root.resolve()))
        env['KACHE_LOCAL_ONLY'] = '1'
        # unbound-host writes managed/native files OUTSIDE OUT_DIR. A cached
        # build-script run would omit those side effects in a fresh target.
        env['KACHE_BUILD_SCRIPT_CACHE'] = '0'
        env['KACHE_BUILD_SCRIPT_HERMETIC'] = '0'
        env['KACHE_SEED_NEW_TARGETS'] = '0'
        env['KACHE_SEED_INCREMENTAL'] = '0'
        env['KACHE_AUTO_RECOVER_TARGETS'] = '0'
        env['KACHE_AUTO_CLEAN_UNUSED_UNITS_DAYS'] = '0'
        env['KACHE_AUTO_CLEAN_IDLE_TARGETS_DAYS'] = '0'
        env['KACHE_TRUST_DAEMON_EXECUTION'] = '0'
        env.setdefault('KACHE_MAX_SIZE', '30GiB')
    elif backend == 'sccache':
        env.setdefault('SCCACHE_BASEDIRS', str(root.resolve()))
    # cc-rs recognizes both wrappers. Explicit CC/CXX also cover other native
    # build systems. Preserve an explicitly selected compiler or cross compiler.
    if wrapper:
        env.setdefault('CC', shlex.join([wrapper, 'cc']))
        env.setdefault('CXX', shlex.join([wrapper, 'c++']))
    return env, backend


def claim_target(target, root):
    target.mkdir(parents=True, exist_ok=True)
    # Readers cannot see the empty interval between creating and writing a marker.
    with (target/'.unbound-source-root').open('a+') as handle:
        fcntl.flock(handle, fcntl.LOCK_EX)
        handle.seek(0)
        owner = handle.read().strip()
        if owner and owner != str(root):
            raise ValueError(f'target belongs to another checkout: {owner}')
        if not owner:
            handle.write(str(root)+'\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cache', choices=['auto', 'kache', 'sccache', 'none'], default='auto')
    parser.add_argument('--direct', action='store_true', help='caller already holds a build lease')
    parser.add_argument('--print-env', action='store_true', help='inspect the selected build environment')
    parser.add_argument('cargo_args', nargs=argparse.REMAINDER)
    args = parser.parse_args()
    root = Path.cwd().resolve()
    cache = Path(os.environ.get('UNBOUND_BUILD_ROOT') or
                 Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound/builds').resolve()
    try:
        env, backend = environment(root, cache, args.cache, os.environ)
    except ValueError as error:
        parser.error(str(error))
    selected = {key: value for key, value in env.items()
                if key.startswith(('CARGO_', 'KACHE_')) or key in ('RUSTC_WRAPPER', 'CC', 'CXX')}
    if args.print_env:
        print(json.dumps(selected, indent=2))
        return 0
    cargo_args = args.cargo_args
    if cargo_args[:1] == ['--']:
        cargo_args = cargo_args[1:]
    if not cargo_args:
        parser.error('supply a Cargo command after --')
    if any(arg == '--target-dir' or arg.startswith('--target-dir=') for arg in cargo_args):
        parser.error('set CARGO_TARGET_DIR in the environment so native/managed outputs use the same target')
    try:
        claim_target(Path(env['CARGO_TARGET_DIR']), root)
    except ValueError as error:
        parser.error(str(error))
    launcher = Path.home()/'.cache/compat/with-heavy-job'
    command = ['cargo', *cargo_args]
    if not args.direct and launcher.is_file():
        command = [str(launcher), '--kind', 'build', *command]
    print(f'build: {backend}; target={env["CARGO_TARGET_DIR"]}', file=sys.stderr, flush=True)
    os.execvpe(command[0], command, env)


if __name__ == '__main__':
    raise SystemExit(main())
