"""Durable, bounded background delivery of landed main commits. Never changes local refs."""
import argparse
import contextlib
import fcntl
import json
import os
from pathlib import Path
import subprocess
import sys
import time

from runtime_snapshot import atomic_json


@contextlib.contextmanager
def journal(state):
    state.mkdir(parents=True, exist_ok=True)
    with (state/'landed.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        yield state/'landed.jsonl'


def receipts(state, callback, *, serialize_land=False):
    with contextlib.ExitStack() as stack:
        if serialize_land:
            # Older coordinators append without landed.lock, but all hold land.lock.
            # Only receipt publication takes this lock; network commands never do.
            state.mkdir(parents=True, exist_ok=True)
            land = stack.enter_context((state/'land.lock').open('a'))
            fcntl.flock(land, fcntl.LOCK_SH)
        path = stack.enter_context(journal(state))
        rows = [json.loads(line) for line in path.read_text().splitlines()] if path.exists() else []
        callback(rows)
        temporary = path.with_suffix('.jsonl.tmp')
        with temporary.open('w') as stream:
            for row in rows:
                stream.write(json.dumps(row)+'\n')
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)


def git(repo, *args, check=True):
    return subprocess.run(['git', '-C', str(repo), *args], check=check,
                          capture_output=True, text=True, timeout=30,
                          env=dict(os.environ, GIT_TERMINAL_PROMPT='0',
                                   GIT_SSH_COMMAND='ssh -oBatchMode=yes -oConnectTimeout=10'))


def paths(repo):
    common = Path(git(repo, 'rev-parse', '--path-format=absolute', '--git-common-dir').stdout.strip())
    return common/'land-push.json', common/'land-push.lock'


def successful_target(value):
    current = value.get('status') == 'pushed'
    target = value.get('target') if current else value.get('pushed_target')
    if not target:
        return None
    return {'status': 'pushed', 'target': target,
            'remote': value.get('remote') if current else value.get('pushed_remote'),
            'attempts': value.get('attempts', 1) if current else value.get('pushed_attempts', 1), 'error': None}


def enqueue(repo, state, *, launch=True, land_locked=False):
    path, lock_path = paths(repo)
    remote = 'origin' if git(repo, 'remote', 'get-url', 'origin', check=False).returncode == 0 else ''
    status = 'pending' if remote and not os.environ.get('UNBOUND_LAND_NO_PUSH') else 'disabled'
    # Enqueue uses its own short lock; a network attempt never holds this lock.
    with path.with_suffix('.queue.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        head = git(repo, 'rev-parse', 'main').stdout.strip()
        previous = json.loads(path.read_text()) if path.exists() else {}
        confirmed = successful_target(previous)
        value = dict(previous, target=head, remote=remote, status=status, state=str(state), repo=str(repo))
        if confirmed:
            value.update(pushed_target=confirmed['target'], pushed_remote=confirmed['remote'],
                         pushed_attempts=confirmed['attempts'])
        atomic_json(path, value)
    def pending(rows):
        for row in rows:
            if row.get('push', {}).get('status') == 'pushed':
                continue
            if confirmed and git(repo, 'merge-base', '--is-ancestor', row['new'], confirmed['target'], check=False).returncode == 0:
                row['push'] = dict(confirmed)
            elif git(repo, 'merge-base', '--is-ancestor', row['new'], head, check=False).returncode == 0:
                row['push'] = {'status': status, 'target': head, 'remote': remote}
    receipts(state, pending, serialize_land=not land_locked)
    if launch and status == 'pending':
        error = launch_worker(repo)
        if error:
            value['launch_error'] = error
    return value


def launch_worker(repo):
    path, _ = paths(repo)
    try:
        with (path.parent/'auto-push.log').open('a') as out:
            subprocess.Popen([sys.executable, str(Path(__file__).resolve()), '--repo', str(repo)],
                             stdout=out, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL,
                             start_new_session=True, close_fds=True)
    except OSError as error:
        print(f'land: push worker could not start; queued for retry: {error}', file=sys.stderr)
        return str(error)
    return None


def deliver(repo, *, attempts=3, delay=time.sleep):
    path, lock_path = paths(repo)
    restart, updates = False, []
    with lock_path.open('a') as worker:
        # Every enqueue launches a waiter. A waiter must survive the previous
        # worker's final queue check, so it acquires this lock in blocking mode.
        fcntl.flock(worker, fcntl.LOCK_EX)
        for attempt in range(attempts):
            restart = False
            with path.with_suffix('.queue.lock').open('a') as lock:
                fcntl.flock(lock, fcntl.LOCK_EX)
                value = json.loads(path.read_text())
            if attempt == 0:
                confirmed = successful_target(value)
                if confirmed:
                    updates.append((dict(value, remote=confirmed['remote']), confirmed['target'],
                                    True, None, confirmed['attempts']))
            if value['status'] in ('pushed', 'disabled'):
                break
            target = value['target']
            try:
                result = git(repo, 'push', '--porcelain', value['remote'], target+':refs/heads/main', check=False)
                success, error = result.returncode == 0, (result.stderr+result.stdout)[-2000:]
            except (OSError, subprocess.TimeoutExpired) as failure:
                success, error = False, str(failure)
            updates.append((value, target, success, error, attempt+1))
            with path.with_suffix('.queue.lock').open('a') as lock:
                fcntl.flock(lock, fcntl.LOCK_EX)
                latest = json.loads(path.read_text())
                if success:
                    latest.update(pushed_target=target, pushed_remote=value['remote'], pushed_attempts=attempt+1)
                if latest['target'] != target:
                    if success:
                        atomic_json(path, latest)
                    restart = True
                    continue
                latest.update(status='pushed' if success else 'failed', attempts=attempt+1,
                              error=None if success else error)
                atomic_json(path, latest)
            if success:
                break
            print(f'land: push failed, local main retained: {error}', flush=True)
            if attempt+1 < attempts:
                delay(min(2**attempt, 10))
    # Release admission before potentially waiting on an older coordinator's
    # receipt append. New trains can deliver while those receipts wait.
    if restart:
        launch_worker(repo)
    for value, target, success, error, attempt in updates:
        def update(rows):
            for row in rows:
                if row.get('push', {}).get('status') == 'pushed':
                    continue
                if git(repo, 'merge-base', '--is-ancestor', row['new'], target, check=False).returncode == 0:
                    row['push'] = {'status': 'pushed' if success else 'failed', 'target': target,
                                   'remote': value['remote'], 'attempts': attempt,
                                   'error': None if success else error}
        receipts(Path(value['state']), update, serialize_land=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, required=True)
    args = parser.parse_args()
    deliver(args.repo)
