#!/usr/bin/env python3
"""Run explicit independent build batches without changing machine-wide limits.

Plan: {"cases": [{"name": "serial", "parallelism": 1, "jobs": [
  {"name": "host-a", "cwd": "/private/source", "command": ["dotnet", "build", "..."],
   "resource_key": "/private/target", "env": {}}]}]}.
Prepare/restore equivalent sources first. Each case needs fresh independent outputs.
The driver takes no lease: each worker acquires exactly one installed build lease.
"""
import argparse
from concurrent.futures import ThreadPoolExecutor
import json
import os
from pathlib import Path
import resource
import signal
import subprocess
import sys
import threading
import time
import uuid

LAUNCHER = Path.home()/'.cache/compat/with-heavy-job'


def write(path, value):
    path.write_text(json.dumps(value, indent=2)+'\n')


def worker(path):
    job = json.loads(path.read_text())
    started = time.time()
    before = resource.getrusage(resource.RUSAGE_CHILDREN)
    result = subprocess.run(['timeout', '--kill-after=10', str(job.get('timeout_s', 600)), *job['command']], cwd=job['cwd'],
                            env=dict(os.environ, **job.get('env', {})))
    usage = resource.getrusage(resource.RUSAGE_CHILDREN)
    write(path.with_suffix('.result.json'), dict(
        exit=result.returncode, start=started, end=time.time(),
        run_s=time.time()-started, cpu_s=usage.ru_utime+usage.ru_stime-before.ru_utime-before.ru_stime))
    return result.returncode


def validate(case):
    assert case['parallelism'] in (1, 2), 'benchmark supports one or two simultaneous builds'
    assert case['jobs'], 'empty batch'
    names = [job['name'] for job in case['jobs']]
    assert len(set(names)) == len(names), 'duplicate job names'
    keys = [str(Path(job['resource_key']).resolve()) for job in case['jobs']]
    assert len(set(keys)) == len(keys), 'jobs must not share build outputs'
    for name in [case['name'], *names]:
        assert Path(name).name == name and name not in ('.', '..'), 'invalid label'
    for job in case['jobs']:
        assert isinstance(job['command'], list) and job['command'], 'command must be an argv array'
        assert Path(job['cwd']).is_dir(), 'missing source directory'
        assert job.get('timeout_s', 600) > 0, 'timeout must be positive'
        for check in job.get('restore_checks', []):
            assets = json.loads(Path(check['assets']).read_text())
            refs = assets['project']['restore']['frameworks'][check['framework']]['projectReferences']
            assert len(refs) == check['project_references'], 'incomplete restored project graph'


def run_case(case, out):
    validate(case)
    out.mkdir()
    paths = []
    active = {}
    guard = threading.Lock()
    cancelled = threading.Event()
    for job in case['jobs']:
        path = out/(job['name']+'.json')
        write(path, job)
        paths.append(path)

    def one(path):
        scope = 'unbound-build-'+uuid.uuid4().hex+'.scope'
        command = [str(LAUNCHER), '--kind', 'build', '--scope-id', scope[len('unbound-build-'):-len('.scope')], sys.executable,
                   str(Path(__file__).resolve()), '--worker', str(path)]
        started = time.time()
        source = json.loads(path.read_text())
        with path.with_suffix('.log').open('x') as log:
            process = subprocess.Popen(command, cwd=source['cwd'], stdout=log, stderr=subprocess.STDOUT)
            with guard:
                active[scope] = process
            if cancelled.is_set():
                stop(scope, process)
            try:
                code = process.wait()
            finally:
                with guard:
                    active.pop(scope, None)
        report = json.loads(path.with_suffix('.result.json').read_text()) \
            if path.with_suffix('.result.json').exists() else {'run_s': None}
        report.update(name=path.stem, command=command, scope=scope, launcher_exit=code,
                      total_s=time.time()-started)
        return report

    def stop(scope, process):
        subprocess.run(['systemctl', '--user', 'kill', '--signal=SIGTERM', '--kill-whom=all', scope],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        try:
            process.terminate()
        except ProcessLookupError:
            pass

    started = time.time()
    pool = ThreadPoolExecutor(max_workers=case['parallelism'])
    try:
        if case['parallelism'] == 1:
            jobs = []
            for path in paths:
                jobs.append(one(path))
                if jobs[-1]['launcher_exit'] != 0:
                    break
        else:
            jobs = list(pool.map(one, paths))
    except BaseException:
        cancelled.set()
        with guard:
            running = list(active.items())
        for scope, process in running:
            stop(scope, process)
        write(out/'cancelled.json', {'scopes': [s for s, p in running], 'time': time.time()})
        raise
    finally:
        pool.shutdown(wait=True, cancel_futures=True)
    report = dict(name=case['name'], parallelism=case['parallelism'],
                  start=started, end=time.time(), wall_s=time.time()-started, jobs=jobs)
    # Exact admission wait and peak memory are recorded by the installed runner.
    timing_file = Path.home()/'.cache/compat/job-timings.jsonl'
    if timing_file.exists():
        entries = []
        for line in timing_file.read_text().splitlines():
            try:
                entries.append(json.loads(line))
            except json.JSONDecodeError:
                continue
        for path, job in zip(paths, jobs):
            source = json.loads(path.read_text())
            job['lease'] = next((r for r in reversed(entries)
                                 if r.get('kind') == 'build' and r.get('start', 0) >= started-1
                                 and (str(path) in r.get('cmd', '') or r.get('cwd') == source['cwd'])), None)
    write(out/'result.json', report)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--worker', type=Path)
    parser.add_argument('--plan', type=Path)
    parser.add_argument('--out', type=Path)
    args = parser.parse_args()
    if args.worker:
        return worker(args.worker)
    def interrupt(number, frame):
        raise KeyboardInterrupt('benchmark cancelled')
    for number in (signal.SIGTERM, signal.SIGINT):
        signal.signal(number, interrupt)
    if not args.plan or not args.out:
        parser.error('--plan and --out are required')
    plan = json.loads(args.plan.read_text())
    for case in plan['cases']:
        validate(case)
    assert len({c['name'] for c in plan['cases']}) == len(plan['cases']), 'duplicate case labels'
    args.out.mkdir(parents=True, exist_ok=False)
    write(args.out/'plan.json', plan)
    results = []
    for case in plan['cases']:
        result = run_case(case, args.out/case['name'])
        results.append(result)
        write(args.out/'results.json', results)
        print(case['name'], round(result['wall_s'], 2), 's', flush=True)
        if any(job['launcher_exit'] != 0 for job in result['jobs']):
            return 1
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
