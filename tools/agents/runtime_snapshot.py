#!/usr/bin/env python3
"""Publish and reuse read-only probe runtimes identified by every file's content.

A runtime has unbound, managed/, and the existing flat runtime-manifest.json.
Run directories hold symlinks to snapshots, never links to mutable build outputs.
Use `create --cli ... --destination ...` or `cleanup --days 14` from the CLI.
"""
import argparse
import contextlib
import fcntl
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time
import uuid

FORMAT_VERSION = 1
MANIFEST = 'runtime-manifest.json'


def cache_root():
    return Path(os.environ.get('UNBOUND_RUNTIME_CACHE') or
                Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound/runtimes')


def digest(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def managed_for(cli, *, prefer_environment=True):
    if prefer_environment and os.environ.get('UNBOUND_MANAGED_DIR'):
        return Path(os.environ['UNBOUND_MANAGED_DIR']).resolve()
    cli = Path(cli).resolve()
    for candidate in (cli.parent/'managed', cli.parent.parent/'managed'):
        if (candidate/'Unbound.Host.dll').is_file():
            return candidate.resolve()
    if not prefer_environment and os.environ.get('UNBOUND_MANAGED_DIR'):
        return Path(os.environ['UNBOUND_MANAGED_DIR']).resolve()
    raise ValueError(f'no managed directory for {cli}; set UNBOUND_MANAGED_DIR')


def runtime_inputs(cli, managed):
    files = {'unbound': Path(cli).resolve()}
    managed = Path(managed).resolve()
    if not managed.is_dir():
        raise ValueError(f'not a managed directory: {managed}')
    for path in sorted(managed.rglob('*')):
        if path.is_symlink() and path.is_dir():
            raise ValueError(f'runtime directory links are unsupported: {path}')
        if path.is_dir():
            continue
        if not path.is_file():
            raise ValueError(f'not a regular runtime file: {path}')
        files['managed/'+path.relative_to(managed).as_posix()] = path
    if not os.access(files['unbound'], os.X_OK):
        raise ValueError(f'not an executable: {cli}')
    if 'managed/Unbound.Host.dll' not in files:
        raise ValueError('runtime lacks managed/Unbound.Host.dll')
    return files


@contextlib.contextmanager
def cache_lock(cache):
    cache.mkdir(parents=True, exist_ok=True)
    with (cache/'lock').open('a') as stream:
        fcntl.flock(stream, fcntl.LOCK_EX)
        yield


def atomic_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name+'.'+uuid.uuid4().hex+'.tmp')
    try:
        with temporary.open('x') as stream:
            json.dump(value, stream, indent=2, sort_keys=True)
            stream.write('\n')
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def remove_snapshot(path):
    # Only the collector/publisher needs writable directories. Files stay read-only.
    for directory, _, _ in os.walk(path):
        os.chmod(directory, 0o700)
    shutil.rmtree(path)


def process_start(pid):
    try:
        return Path(f'/proc/{pid}/stat').read_text().rsplit(')', 1)[1].split()[19]
    except (OSError, IndexError):
        return None


def task_owner():
    def git(*args):
        return subprocess.run(['git', *args], capture_output=True, text=True, timeout=2).stdout.strip()
    return {'worktree': git('rev-parse', '--show-toplevel'),
            'branch': git('symbolic-ref', '--quiet', '--short', 'HEAD'),
            'pid': os.getpid(), 'start': process_start(os.getpid())}


def register(snapshot, destination, cache, *, retained=False):
    """Called under the cache lock, so cleanup cannot race reference publication."""
    destination = Path(os.path.abspath(destination))
    destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.is_symlink():
        if destination.resolve() != snapshot.resolve():
            raise ValueError(f'refusing to change runtime reference: {destination}')
    elif destination.exists():
        raise ValueError(f'refusing to replace runtime directory: {destination}')
    else:
        temporary = destination.with_name(destination.name+'.'+uuid.uuid4().hex+'.tmp')
        try:
            temporary.symlink_to(snapshot.resolve(), target_is_directory=True)
            os.rename(temporary, destination)
        finally:
            temporary.unlink(missing_ok=True)
    key = hashlib.sha256(os.fsencode(destination)).hexdigest()
    previous = cache/'references'/key
    if previous.is_file():
        retained = retained or json.loads(previous.read_text()).get('retained', False)
    atomic_json(cache/'references'/key, {'path': str(destination), 'snapshot': snapshot.name,
                                       'owner': task_owner(), 'retained': retained})
    return destination


def active_snapshots(cache):
    """Also protect manual engine runs whose short-lived capture process exited."""
    if not Path('/proc/self/stat').is_file():
        raise OSError('cannot inspect live runtime processes; snapshot collection requires /proc')
    found = set()
    snapshots = (cache/'snapshots').resolve()
    run_directories = {}
    for path in (cache/'references').glob('*'):
        if path.name.endswith('.tmp'):
            continue
        record = json.loads(path.read_text())
        reference = Path(record['path'])
        if reference.name == 'runtime':
            run_directories.setdefault(reference.parent.resolve(), set()).add(record['snapshot'])
    for process in Path('/proc').glob('[0-9]*'):
        try:
            if process.stat().st_uid != os.getuid():
                continue
            paths = [os.readlink(process/'exe')]
            cwd = Path(os.readlink(process/'cwd'))
            for directory in (cwd, *cwd.parents):
                found.update(run_directories.get(directory, ()))
            # Admission wrappers can be alive before the engine executable starts.
            for argument in (process/'cmdline').read_bytes().split(b'\0'):
                value = os.fsdecode(argument).split('=', 1)[-1]
                if value.startswith('/'):
                    paths.append(value)
                elif value.startswith(('./', '../', 'runtime/')):
                    paths.append(str(cwd/value))
            for variable in (process/'environ').read_bytes().split(b'\0'):
                if variable.startswith((b'UNBOUND_RUNTIME=', b'UNBOUND_CLI=', b'UNBOUND=')):
                    paths.append(os.fsdecode(variable.split(b'=', 1)[1]))
            for value in paths:
                path = Path(value).resolve()
                if snapshots in path.parents:
                    found.add(path.relative_to(snapshots).parts[0])
        except (OSError, ValueError):
            continue
    return found


def release_task(worktree, branch=None, *, cache=None):
    """Retire only this completed task's links, retaining live and promoted runs."""
    cache = Path(cache or cache_root()).resolve()
    with cache_lock(cache):
        for path in (cache/'references').glob('*'):
            if path.name.endswith('.tmp'):
                continue
            record = json.loads(path.read_text())
            owner = record.get('owner', {})
            if owner.get('worktree') == str(Path(worktree).resolve()) and (
                    branch is None or owner.get('branch') == branch):
                record['release_requested'] = True
                atomic_json(path, record)


def release_reference(destination, *, cache=None):
    cache = Path(cache or cache_root()).resolve()
    key = hashlib.sha256(os.fsencode(Path(os.path.abspath(destination)))).hexdigest()
    with cache_lock(cache):
        path = cache/'references'/key
        if path.is_file():
            record = json.loads(path.read_text())
            record['release_requested'] = True
            atomic_json(path, record)


def finish_task(worktree, branch=None, *, repo=None):
    try:
        release_task(worktree, branch)
        return cleanup(days=1/24, repo=repo)
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        print(f'runtime cleanup deferred: {error}', file=sys.stderr)
        return []


def retain(runtime, destination, *, cache=None, replace=False):
    cache = Path(cache or cache_root()).resolve()
    snapshot = Path(runtime).resolve()
    if snapshot.parent != (cache/'snapshots').resolve():
        raise ValueError(f'not a shared snapshot: {snapshot}')
    with cache_lock(cache):
        if not snapshot.is_dir() or not (snapshot/MANIFEST).is_file():
            raise ValueError(f'runtime snapshot was collected before promotion: {snapshot}')
        if replace and Path(destination).is_symlink():
            Path(destination).unlink()
        return register(snapshot, destination, cache, retained=True)


def dedupe_managed(staging, hashes, modes, storage):
    blobs = storage/'.managed-blobs'
    blobs.mkdir(exist_ok=True)
    for name, expected in hashes.items():
        if not name.startswith('managed/'):
            continue
        blob = blobs/(expected+f'-{modes[name]:o}')
        target = staging/name
        if blob.exists():
            if blob.is_symlink() or not blob.is_file() or digest(blob) != expected or blob.stat().st_mode & 0o777 != modes[name]:
                raise ValueError(f'invalid managed blob: {blob}')
            target.unlink()
            os.link(blob, target)
        else:
            os.link(target, blob)


def validate_snapshot(snapshot, hashes, modes):
    if json.loads((snapshot/MANIFEST).read_text()) != hashes:
        raise ValueError(f'invalid snapshot manifest: {snapshot}')
    for path in [snapshot, *snapshot.rglob('*')]:
        if path.is_symlink() or path.stat().st_mode & 0o222:
            raise ValueError(f'snapshot is writable or contains a link: {path}')
    actual = {path.relative_to(snapshot).as_posix() for path in snapshot.rglob('*') if path.is_file()}
    if actual != set(hashes) | {MANIFEST}:
        raise ValueError(f'incomplete snapshot: {snapshot}')
    for name, expected in hashes.items():
        if digest(snapshot/name) != expected or (snapshot/name).stat().st_mode & 0o777 != modes[name]:
            raise ValueError(f'invalid snapshot file: {snapshot/name}')


def snapshot_runtime(cli, managed, destination, *, expected=None, cache=None):
    """Hash all inputs, publish a complete snapshot, and record this run's reference.

    expected is an optional existing flat SHA256 manifest; verification never removes
    or weakens a caller's manifest checks. Source files are copied, never hardlinked.
    """
    cache = Path(cache or cache_root()).resolve()
    files = runtime_inputs(cli, managed)
    hashes = {name: digest(path) for name, path in files.items()}
    if expected is not None and hashes != expected:
        differences = {
            'missing': sorted(set(expected)-set(hashes)),
            'extra': sorted(set(hashes)-set(expected)),
            'changed': {name: {'expected': expected[name], 'actual': hashes[name]}
                        for name in sorted(set(hashes) & set(expected)) if hashes[name] != expected[name]},
        }
        raise ValueError('runtime contents do not match the supplied manifest: '+json.dumps(differences))
    modes = {name: 0o555 if path.stat().st_mode & 0o111 else 0o444 for name, path in files.items()}
    identity = {'version': FORMAT_VERSION, 'sha256': hashes, 'modes': modes}
    key = hashlib.sha256(json.dumps(identity, sort_keys=True, separators=(',', ':')).encode()).hexdigest()
    snapshot = cache/'snapshots'/key
    with cache_lock(cache):
        if snapshot.is_dir():
            validate_snapshot(snapshot, hashes, modes)
            return register(snapshot, destination, cache)
    # Copy outside the publication lock. Other builds may publish different keys concurrently.
    staging_root = cache/'snapshots'
    staging_root.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=f'.tmp-{os.getpid()}-', dir=staging_root))
    try:
        for name, source in files.items():
            target = staging/name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, target)
            if digest(target) != hashes[name]:
                raise ValueError(f'runtime changed while being captured: {source}')
            target.chmod(modes[name])
        if runtime_inputs(cli, managed).keys() != files.keys():
            raise ValueError('runtime file set changed while being captured')
        (staging/MANIFEST).write_text(json.dumps(hashes, indent=2)+'\n')
        (staging/MANIFEST).chmod(0o444)
        for directory, _, _ in os.walk(staging, topdown=False):
            os.chmod(directory, 0o555)
        with cache_lock(cache):
            snapshot.parent.mkdir(parents=True, exist_ok=True)
            if not snapshot.exists():
                for directory, _, _ in os.walk(staging):
                    os.chmod(directory, 0o700)
                dedupe_managed(staging, hashes, modes, staging_root)
                for directory, _, _ in os.walk(staging, topdown=False):
                    os.chmod(directory, 0o555)
                os.rename(staging, snapshot)
            else:
                validate_snapshot(snapshot, hashes, modes)
            return register(snapshot, destination, cache)
    finally:
        if staging.exists():
            remove_snapshot(staging)


def pin_main(runtime, repo, tree, *, previous_tree=None, cache=None):
    """Retain the runtime of a successful main gate until main advances to another tree."""
    cache = Path(cache or cache_root()).resolve()
    repo = Path(repo).resolve()
    snapshot = Path(runtime).resolve() if runtime is not None else None
    if snapshot is not None and snapshot.parent != (cache/'snapshots').resolve():
        raise ValueError(f'not a shared snapshot: {snapshot}')
    common = subprocess.run(['git', '-C', str(repo), 'rev-parse', '--path-format=absolute', '--git-common-dir'],
                            check=True, capture_output=True, text=True).stdout.strip()
    key = hashlib.sha256(os.fsencode(common)).hexdigest()
    with cache_lock(cache):
        pin = cache/'pins'/key
        if snapshot is None:
            # A docs-only landing keeps the previous main build current.
            if not pin.is_file():
                return
            previous = json.loads(pin.read_text())
            if previous['tree'] != previous_tree:
                return
            snapshot = cache/'snapshots'/previous['snapshot']
        atomic_json(pin, {'git_dir': common, 'tree': tree, 'snapshot': snapshot.name})


def cleanup(*, days=14, cache=None, now=None, repo=None, remove=None, dry_run=False):
    """Remove old, unreferenced snapshots; retained run links and current main pins protect them."""
    if days < 0:
        raise ValueError('days must be nonnegative')
    cache = Path(cache or cache_root()).resolve()
    cutoff = (time.time() if now is None else now)-days*86400
    removed, protected = [], set()

    def discard(path):
        if remove is not None:
            return remove(path)
        if not dry_run:
            if path.is_dir() and not path.is_symlink():
                remove_snapshot(path)
            else:
                path.unlink()
        return True

    with cache_lock(cache):
        active = active_snapshots(cache)
        protected.update(active)
        # Upgrade protection for baselines written before retained references existed.
        agents = {cache.parent/'agents', Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound/agents'}
        for root in agents:
            summary = root/'baseline/summary.json'
            if summary.is_file():
                value = json.loads(summary.read_text())
                for candidate in [value.get('cli', ''), value.get('runtime_snapshot', ''),
                                  str(Path(value.get('work', ''))/'runtime')]:
                    target = Path(candidate).resolve()
                    storage = (cache/'snapshots').resolve()
                    if storage in target.parents:
                        protected.add(target.relative_to(storage).parts[0])
        landed_links = {}
        for root in agents:
            landed = root/'landed.jsonl'
            if landed.is_file():
                for line in landed.read_text().splitlines():
                    row = json.loads(line)
                    log = row.get('log')
                    if log and row.get('new'):
                        landed_links[str(Path(log).with_suffix('.runtime'))] = row['new']
        # A killed writer can leave a private JSON temporary behind. No writer holds
        # this lock now, so these are never live reference records.
        for directory in ('references', 'pins'):
            for temporary in (cache/directory).glob('*.tmp'):
                discard(temporary)
        for path in (cache/'references').glob('*'):
            if path.name.endswith('.tmp'):
                continue
            record = json.loads(path.read_text())
            reference = Path(record['path'])
            owner = record.get('owner', {})
            pid = owner.get('pid')
            live = pid != os.getpid() and owner.get('start') is not None and process_start(pid) == owner['start']
            if repo and str(reference) in landed_links and subprocess.run(
                    ['git', '-C', str(repo), 'merge-base', '--is-ancestor', landed_links[str(reference)], 'main'],
                    capture_output=True, timeout=2).returncode == 0:
                record['release_requested'] = True
            # Legacy suite links have no promotion history. Keep them until an
            # explicit retirement request; a completed summary alone proves nothing.
            if owner.get('start') and owner.get('branch') in ('', 'main') and not live:
                record['release_requested'] = True
            if record.get('release_requested') and not record.get('retained') and not live and record['snapshot'] not in protected:
                if reference.is_symlink() and reference.resolve() == (cache/'snapshots'/record['snapshot']).resolve():
                    if not discard(reference):
                        protected.add(record['snapshot'])
                        continue
                if discard(path):
                    continue
            if reference.is_symlink() and reference.resolve() == (cache/'snapshots'/record['snapshot']).resolve():
                protected.add(record['snapshot'])
            else:
                discard(path)
        # A main pin is replaced by the next successful build landing. Keep the
        # last validated main build across docs-only landings and their pin-update
        # interval, including when the checkout is unavailable during cleanup.
        for path in (cache/'pins').glob('*'):
            if path.name.endswith('.tmp'):
                continue
            protected.add(json.loads(path.read_text())['snapshot'])
        for snapshot in (cache/'snapshots').glob('*'):
            if snapshot.is_symlink() or not snapshot.is_dir():
                continue
            if snapshot.name.startswith('.tmp-'):
                if snapshot.stat().st_mtime >= cutoff:
                    continue
                try:
                    pid = int(snapshot.name.split('-')[1])
                    os.kill(pid, 0)
                except ProcessLookupError:
                    if discard(snapshot):
                        removed.append(snapshot.name)
                except (ValueError, PermissionError):
                    pass
                continue
            if len(snapshot.name) != 64 or any(c not in '0123456789abcdef' for c in snapshot.name):
                continue
            if snapshot.name not in protected and snapshot.stat().st_mtime < cutoff:
                if discard(snapshot):
                    removed.append(snapshot.name)
        for blob in (cache/'snapshots/.managed-blobs').glob('*'):
            if blob.is_file() and blob.stat().st_nlink == 1:
                discard(blob)
    return removed


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cache', type=Path)
    sub = parser.add_subparsers(dest='command', required=True)
    create = sub.add_parser('create')
    create.add_argument('--cli', type=Path, required=True)
    create.add_argument('--managed', type=Path)
    create.add_argument('--destination', type=Path, required=True)
    create.add_argument('--manifest', type=Path)
    gc = sub.add_parser('cleanup')
    gc.add_argument('--days', type=float, default=14)
    release = sub.add_parser('release')
    release.add_argument('destination', type=Path)
    args = parser.parse_args()
    if args.command == 'create':
        expected = json.loads(args.manifest.read_text()) if args.manifest else None
        runtime = snapshot_runtime(args.cli, args.managed or managed_for(args.cli, prefer_environment=False), args.destination,
                                   expected=expected, cache=args.cache)
        print(runtime)
    elif args.command == 'release':
        release_reference(args.destination, cache=args.cache)
    else:
        print(json.dumps({'removed': cleanup(days=args.days, cache=args.cache)}))


if __name__ == '__main__':
    main()
