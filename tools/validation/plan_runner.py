#!/usr/bin/env python3
"""Execute a reviewed finite plan through the existing job pool, once.

This is transport/accounting for trusted Root plans, not a sandbox or a parity
verifier. Package-specific checks are explicit jobs, with their original exits.
No shell, inherited environment, discovery, retries, or implicit continuation.
"""
import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import re
import signal
import subprocess
import sys
import time
import uuid


KINDS = {'build', 'tool', 'engine-cpu', 'engine-gpu', 'editor-cpu', 'editor-gpu'}
TOKEN = '{run}'


class Refused(ValueError):
    pass


def require(ok, reason):
    if not ok:
        raise Refused(reason)


def digest(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def parse_json(data):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result, 'duplicate JSON key')
            result[key] = value
        return result
    return json.loads(data, object_pairs_hook=unique)


def keys(value, required, optional=()):
    require(isinstance(value, dict), 'expected object')
    require(set(required) <= value.keys(), 'missing required field')
    require(value.keys() <= set(required) | set(optional), 'unknown field')


def physical(value):
    require(isinstance(value, str) and Path(value).is_absolute(), 'absolute path required')
    path = Path(value)
    require(path == path.resolve(), 'physical path required')
    return path


def stamp():
    return datetime.now(timezone.utc).isoformat()


def save(path, value):
    # All output paths are in a newly claimed private namespace.
    tmp = path.with_suffix(path.suffix + '.' + uuid.uuid4().hex + '.tmp')
    try:
        with tmp.open('x') as handle:
            json.dump(value, handle, indent=2, allow_nan=False)
            handle.write('\n')
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(tmp, path)
    finally:
        tmp.unlink(missing_ok=True)


def expand(value, run):
    require(isinstance(value, str) and '\0' not in value, 'invalid string')
    return value.replace(TOKEN, str(run))


def check(plan_path, approval_path):
    """Read-only preflight; does not create the run directory or launch a process."""
    plan_path, approval_path = physical(str(plan_path)), physical(str(approval_path))
    plan_bytes, approval_bytes = plan_path.read_bytes(), approval_path.read_bytes()
    plan, approval = parse_json(plan_bytes), parse_json(approval_bytes)
    keys(plan, ['version', 'run_dir', 'launcher', 'timeout', 'systemctl', 'env', 'inputs', 'jobs'])
    keys(approval, ['version', 'issued_by', 'plan_sha256', 'runner_sha256', 'run_dir', 'job_ids'])
    require(type(plan['version']) is int and plan['version'] == 1, 'unsupported plan version')
    require(type(approval['version']) is int and approval['version'] == 1, 'unsupported approval version')
    require(approval['issued_by'] == 'Root', 'Root approval required')
    plan_sha = hashlib.sha256(plan_bytes).hexdigest()
    require(approval['plan_sha256'] == plan_sha, 'plan hash differs from approval')
    require(approval['runner_sha256'] == digest(__file__), 'runner hash differs from approval')
    run = physical(plan['run_dir'])
    require(str(run) == approval['run_dir'], 'output differs from approval')
    require(run.parent.is_dir() and not run.exists(), 'output must be new under an existing parent')
    require(not run.is_symlink(), 'output must not be a symlink')
    require(isinstance(plan['inputs'], dict) and plan['inputs'], 'pinned inputs required')
    for name, sha in plan['inputs'].items():
        path = physical(name)
        require(path.is_file() and not path.is_symlink(), 'input must be a regular physical file')
        require(not path.is_relative_to(run) and not run.is_relative_to(path), 'output overlaps input')
        require(isinstance(sha, str) and re.fullmatch('[a-f0-9]{64}', sha), 'invalid input hash')
        require(digest(path) == sha, 'input hash differs: ' + str(path))
    for name in ['launcher', 'timeout', 'systemctl']:
        path = physical(plan[name])
        require(str(path) in plan['inputs'] and os.access(path, os.X_OK), 'tool must be executable and pinned')
    require(isinstance(plan['env'], dict), 'explicit environment required')
    for name, value in plan['env'].items():
        require(re.fullmatch('[A-Za-z_][A-Za-z0-9_]*', name), 'invalid environment key')
        expand(value, run)
    if Path(plan['launcher']).name == 'with-heavy-job':
        # The stock installed shell wrapper resolves its Python entry via HOME.
        # Check this declared dependency before spending a lease, not after spawn.
        home = physical(plan['env'].get('HOME'))
        cache = physical(plan['env'].get('UNBOUND_JOB_RUNNER_STATE', str(home/'.cache/compat')))
        for dependency in [home/'.cache/compat/job_runner.py', cache/'job-limits.json']:
            require(str(dependency) in plan['inputs'], 'installed launcher dependency must be pinned: ' + str(dependency))
    require(isinstance(plan['jobs'], list) and plan['jobs'], 'nonempty jobs required')
    ids = []
    for job in plan['jobs']:
        keys(job, ['id', 'kind', 'argv', 'cwd', 'timeout_s'])
        require(isinstance(job['id'], str) and re.fullmatch('[a-zA-Z0-9_-]+', job['id']), 'invalid job id')
        ids.append(job['id'])
        require(job['kind'] in KINDS, 'invalid job kind')
        duration = job['timeout_s']
        require(type(duration) in (int, float) and math.isfinite(duration) and 0 < duration <= 86400,
                'invalid job timeout')
        require(isinstance(job['argv'], list) and job['argv'], 'nonempty argv required')
        for arg in job['argv']:
            expand(arg, run)
        exe = physical(job['argv'][0])
        require(str(exe) in plan['inputs'] and os.access(exe, os.X_OK), 'executable must be pinned')
        cwd = expand(job['cwd'], run)
        require(cwd == str(run) or (TOKEN not in job['cwd'] and physical(cwd).is_dir()), 'invalid cwd')
    require(len(set(ids)) == len(ids), 'duplicate job ids')
    require(ids == approval['job_ids'], 'job sequence differs from approval')
    return plan, approval, plan_sha, hashlib.sha256(approval_bytes).hexdigest()


def verify_unchanged(plan, approval, plan_path, approval_path, plan_sha, approval_sha):
    require(digest(plan_path) == plan_sha, 'plan changed')
    require(digest(approval_path) == approval_sha, 'approval changed')
    require(digest(__file__) == approval['runner_sha256'], 'runner changed')
    for path, sha in plan['inputs'].items():
        require(physical(path).is_file() and digest(path) == sha, 'input changed: ' + path)


@contextmanager
def interruptions():
    def stop(number, frame):
        raise KeyboardInterrupt(number)
    prior = {s: signal.signal(s, stop) for s in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP)}
    try:
        yield
    finally:
        for sig, handler in prior.items():
            signal.signal(sig, handler)


def stop_owned(plan, scope, process, env):
    """Address only this job's UUID scope; never list or kill unrelated scopes."""
    # The installed launcher creates this scope. It may be absent before admission.
    try:
        subprocess.run([plan['systemctl'], '--user', 'kill', '--signal=SIGKILL',
                        '--kill-whom=all', scope], env=env, stdout=subprocess.DEVNULL,
                       stderr=subprocess.DEVNULL, timeout=5)
    finally:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        process.wait(timeout=5)


def execute(plan_path, approval_path):
    prepared_at, prepared_clock = stamp(), time.monotonic()
    plan, approval, plan_sha, approval_sha = check(plan_path, approval_path)
    run = Path(plan['run_dir'])
    env = {name: expand(value, run) for name, value in plan['env'].items()}
    state = dict(version=1, state='running', plan_sha256=plan_sha,
                 approval_sha256=approval_sha, prepared_at=prepared_at,
                 started_at=stamp(), readiness_s=time.monotonic() - prepared_clock,
                 completed=[], attempts=[], admission=False)
    with interruptions():
        run.mkdir(mode=0o700)  # Atomic one-time claim. An old/incomplete run is never resumed.
        save(run/'result.json', state)
        try:
            for job in plan['jobs']:
                verify_unchanged(plan, approval, plan_path, approval_path, plan_sha, approval_sha)
                uid = uuid.uuid4().hex
                prefix = 'editor' if job['kind'].startswith('editor-') else (
                    'engine' if job['kind'].startswith('engine-') else 'build')
                scope = f'unbound-{prefix}-{uid}.scope'
                command = [plan['launcher'], '--kind', job['kind'], '--scope-id', uid,
                           plan['timeout'], '--kill-after=5', str(job['timeout_s']),
                           *[expand(arg, run) for arg in job['argv']]]
                row = dict(id=job['id'], kind=job['kind'], scope=scope,
                           started_at=stamp(), state='attempted', raw_exit=None)
                state['attempts'].append(row)
                save(run/'result.json', state)  # Charge before spawn, including spawn failure.
                start = time.monotonic()
                with (run/(job['id']+'.log')).open('xb') as log:
                    process = subprocess.Popen(command, cwd=expand(job['cwd'], run), env=env,
                                               stdout=log, stderr=subprocess.STDOUT,
                                               start_new_session=True, close_fds=True)
                    try:
                        # Includes admission wait. The inner timeout covers the payload.
                        row['raw_exit'] = process.wait(timeout=job['timeout_s'] + 30)
                    except BaseException:
                        stop_owned(plan, scope, process, env)
                        row['raw_exit'] = process.returncode
                        raise
                    finally:
                        row['wall_s'] = time.monotonic() - start
                        row['ended_at'] = stamp()
                        row['state'] = 'returned' if row['raw_exit'] is not None else 'incomplete'
                row['log_sha256'] = digest(run/(job['id']+'.log'))
                save(run/'result.json', state)
                require(row['raw_exit'] == 0, 'nonzero exit: ' + job['id'])
                verify_unchanged(plan, approval, plan_path, approval_path, plan_sha, approval_sha)
                state['completed'].append(job['id'])
                save(run/'result.json', state)
            state['state'] = 'complete'
        except (Exception, KeyboardInterrupt) as error:
            state['state'] = 'stopped'
            # Never include exception text/argv/environment: logs are private evidence.
            state['failure_type'] = type(error).__name__
            if isinstance(error, Refused):
                state['reason'] = str(error)  # Only our fixed diagnostics and pinned paths.
            raise
        finally:
            state['ended_at'] = stamp()
            state['wall_s'] = time.monotonic() - prepared_clock
            save(run/'result.json', state)
    return state


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['check', 'run'])
    parser.add_argument('--plan', required=True, type=Path)
    parser.add_argument('--approval', required=True, type=Path)
    args = parser.parse_args()
    old_umask = os.umask(0o077)
    try:
        if args.action == 'check':
            plan, _, sha, _ = check(args.plan, args.approval)
            print(json.dumps(dict(ready=True, plan_sha256=sha, jobs=len(plan['jobs']))))
        else:
            state = execute(args.plan, args.approval)
            print(json.dumps(dict(state=state['state'], jobs=len(state['completed']), admission=False)))
        return 0
    except (Exception, KeyboardInterrupt) as error:
        result = dict(ready=False, error_type=type(error).__name__)
        if isinstance(error, Refused):
            result['reason'] = str(error)
        print(json.dumps(result), file=sys.stderr)
        return 1
    finally:
        os.umask(old_umask)


if __name__ == '__main__':
    raise SystemExit(main())
