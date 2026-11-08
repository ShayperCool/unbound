#!/usr/bin/env python3
"""Exercise concurrent facade generation against one disposable global cache."""
import argparse
import concurrent.futures
import hashlib
import json
import os
from pathlib import Path
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cli', type=Path, default=os.environ.get('UNBOUND'), required=False)
    parser.add_argument('--project', type=Path, default=Path('tests/projects/physics'))
    parser.add_argument('--work', type=Path, required=True)
    parser.add_argument('--workers', type=int, default=8)
    args = parser.parse_args()
    if not args.cli or args.workers < 2:
        parser.error('--cli/$UNBOUND and at least two workers are required')
    args.work.mkdir(parents=True, exist_ok=False)
    env = dict(os.environ, UNBOUND_CACHE=str(args.work.resolve()/'cache'), UNBOUND_OFFLINE='1')

    def run(index):
        result = subprocess.run([str(args.cli.resolve()), 'facades', '-p', str(args.project)],
                                env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
        (args.work/f'worker-{index}.log').write_text(result.stdout)
        if result.returncode:
            raise RuntimeError(f'worker {index}: exit {result.returncode}; see {args.work}')
        output = Path(next(line.split(None, 1)[1] for line in result.stdout.splitlines()
                           if line.startswith('facades ')))
        return output, {file.name: hashlib.sha256(file.read_bytes()).hexdigest()
                        for file in output.iterdir() if file.is_file()}

    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as pool:
        results = list(pool.map(run, range(args.workers)))
    assert all(result == results[0] for result in results), 'workers observed different facade sets'
    assert len(results[0][0].name) == 64, 'facade set is not content-addressed'
    assert not list((args.work/'cache/facades').rglob('*.tmp')), 'temporary files leaked'
    report = {'workers': args.workers, 'output': str(results[0][0]), 'sha256': results[0][1]}
    (args.work/'result.json').write_text(json.dumps(report, indent=2)+'\n')
    print(f'facade cache: {args.workers} concurrent processes observed one complete set')


if __name__ == '__main__':
    main()
