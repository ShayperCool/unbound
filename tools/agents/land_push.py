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


def enqueue(repo, state, *, launch=True):
    path, lock_path = paths(repo)
    remote = 'origin' if git(repo, 'remote', 'get-url', 'origin', check=False).returncode == 0 else ''
    status = 'pending' if remote and not os.environ.get('UNBOUND_LAND_NO_PUSH') else 'disabled'
    head = git(repo, 'rev-parse', 'main').stdout.strip()
    # Enqueue uses its own short lock; a network attempt never holds this lock.
    with path.with_suffix('.queue.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        previous = json.loads(path.read_text()) if path.exists() else {}
        value = dict(previous, target=head, remote=remote, status=status, state=str(state), repo=str(repo))
        atomic_json(path, value)
    def pending(rows):
        for row in rows:
            if row.get('push', {}).get('status') != 'pushed' and git(repo, 'merge-base', '--is-ancestor', row['new'], head, check=False).returncode == 0:
                row['push'] = {'status': status, 'target': head, 'remote': remote}
    receipts(state, pending)
    if launch and status == 'pending':
        try:
            with (path.parent/'auto-push.log').open('a') as out:
                subprocess.Popen([sys.executable, str(Path(__file__).resolve()), '--repo', str(repo)],
                                 stdout=out, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL,
                                 start_new_session=True, close_fds=True)
        except OSError as error:
            # Intent is already durable. A later landing or explicit retry can deliver it.
            value['launch_error'] = str(error)
            print(f'land: push worker could not start; queued for retry: {error}', file=sys.stderr)
    return value


def deliver(repo, *, attempts=3, delay=time.sleep):
    path, lock_path = paths(repo)
    restart = None
    with lock_path.open('a') as worker:
        try:
            fcntl.flock(worker, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            return
        for attempt in range(attempts):
            restart = None
            with path.with_suffix('.queue.lock').open('a') as lock:
                fcntl.flock(lock, fcntl.LOCK_EX)
                value = json.loads(path.read_text())
            if value['status'] in ('pushed', 'disabled'):
                return
            target = value['target']
            try:
                result = git(repo, 'push', '--porcelain', value['remote'], target+':refs/heads/main', check=False)
                success, error = result.returncode == 0, (result.stderr+result.stdout)[-2000:]
            except (OSError, subprocess.TimeoutExpired) as failure:
                success, error = False, str(failure)
            state = Path(value['state'])
            def update(rows):
                for row in rows:
                    if row.get('push', {}).get('status') == 'pushed':
                        continue
                    if git(repo, 'merge-base', '--is-ancestor', row['new'], target, check=False).returncode == 0:
                        row['push'] = {'status': 'pushed' if success else 'failed', 'target': target,
                                       'remote': value['remote'], 'attempts': attempt+1,
                                       'error': None if success else error}
            receipts(state, update, serialize_land=True)
            with path.with_suffix('.queue.lock').open('a') as lock:
                fcntl.flock(lock, fcntl.LOCK_EX)
                latest = json.loads(path.read_text())
                if latest['target'] != target:
                    restart = Path(latest['state'])
                    continue  # A newer train coalesced while this attempt ran.
                latest.update(status='pushed' if success else 'failed', attempts=attempt+1,
                              error=None if success else error)
                atomic_json(path, latest)
            if success:
                return
            print(f'land: push failed, local main retained: {error}', flush=True)
            if attempt+1 < attempts:
                delay(min(2**attempt, 10))
    if restart is not None:
        enqueue(repo, restart)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, required=True)
    args = parser.parse_args()
    deliver(args.repo)
