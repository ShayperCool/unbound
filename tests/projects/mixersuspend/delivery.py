#!/usr/bin/env python3
"""Single-reset callback ownership control with complete source and listener buffers."""
import argparse
import datetime
import json
import math
import os
from pathlib import Path
import shutil
import subprocess
import sys

FIXTURE = Path(__file__).resolve().parent
REPO = FIXTURE.parents[2]
sys.path.insert(0, str(REPO/'tests'))
from editor_runner import EditorJob, run, summary, result_problem
from probe_paths import shared_runtime
from compare import compare, header, samples
from pack import pack


def verify(root, release, rate):
    meta = header(root)
    assert meta.keys() == {'release', 'rate', 'notified', 'source', 'listener'}, meta.keys()
    assert meta['release'] == release and meta['rate'] == rate and meta['notified'] is True
    for key in ('source', 'listener'):
        record = meta[key]
        assert record.keys() == {'file', 'samples', 'callbacks'}, record
        assert record['file'] == key+'.f32'
        assert record['samples'] == sum(length for _, length in record['callbacks'])
        assert all(phase in (0, 1, 2, 3, 4) and length == 2048 for phase, length in record['callbacks'])
        data = samples(root, record)
        assert len(data) == record['samples'] and all(map(math.isfinite, data))
        assert any(abs(value) > 0.01 for value in data), key
    print('Complete reset delivery capture:', root, flush=True)


def capture(args):
    work = args.work.resolve(); work.mkdir(parents=True, exist_ok=False)
    project = work/'project'
    if args.mode == 'native':
        for part in ('Assets', 'Packages', 'ProjectSettings'): shutil.copytree(FIXTURE/part, project/part)
        for name, part in [('ResetDeliveryProbe.cs', 'Scripts'), ('ResetDeliveryRunner.cs', 'Editor')]:
            (project/'Assets'/part).mkdir(parents=True, exist_ok=True)
            shutil.copy2(FIXTURE/'Controls'/name, project/'Assets'/part/name)
        result = run(EditorJob(project, work/'editor.log', work/'editor', batch=True,
                              args=['-executeMethod', 'ResetDeliveryRunner.Record'],
                              env={'SUSPEND_PROBE_RATE': str(args.rate), 'SUSPEND_PROBE_OUT': str(work/'capture')},
                              editor=Path.home()/'Unity/Hub/Editor'/args.release/'Editor/Unity', timeout=600))
        print(summary(result), flush=True)
        problem = result_problem(work/'editor', gpu=True)
        if problem: raise SystemExit(problem)
    else:
        for name, part in [('ResetDeliveryProbe.cs', 'Scripts'), ('ResetDeliveryRunner.cs', 'Editor')]:
            if (FIXTURE/'Controls'/name).read_bytes() != (args.project/'Assets'/part/name).read_bytes():
                raise SystemExit('Reset delivery source changed; record a fresh authored project')
        for part in ('Assets', 'Packages', 'ProjectSettings'): shutil.copytree(args.project/part, project/part)
        env = dict(os.environ, SUSPEND_PROBE_RATE=str(args.rate), SUSPEND_PROBE_OUT=str(work/'capture'),
                   UNBOUND_MANAGED_DIR=str(args.runtime/'managed'), LD_LIBRARY_PATH=str(args.runtime/'managed'),
                   UNBOUND_CACHE=str(work/'cache'))
        with (work/'engine.log').open('w') as log:
            subprocess.run([str(Path.home()/'.cache/compat/with-heavy-job'), '--kind', 'engine-cpu', 'timeout', '600',
                            str(args.runtime/'unbound'), 'run', '-p', str(project), '--scene', 'Assets/Scenes/ResetDelivery.unity',
                            '--headless', '30', '--no-watch', '--fixed-fps', '60'], env=env, stdout=log,
                           stderr=subprocess.STDOUT, check=True)
    verify(work/'capture', args.release, args.rate); pack(work/'capture')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('native', 'engine', 'publish', 'gate'))
    parser.add_argument('--release', required=True)
    parser.add_argument('--rate', type=int, choices=(48000, 44100), default=48000)
    parser.add_argument('--work', type=Path)
    parser.add_argument('--project', type=Path)
    parser.add_argument('--runtime', type=Path)
    parser.add_argument('--candidate', type=Path)
    parser.add_argument('--repeat', type=Path)
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    reference = FIXTURE/'Reference'/('unity-'+args.release)/f'delivery-rate-{args.rate}'
    if args.mode in ('native', 'engine'): capture(args)
    elif args.mode == 'publish':
        if args.candidate.resolve() == args.repeat.resolve(): parser.error('two independent captures required')
        for root in (args.candidate, args.repeat): verify(root, args.release, args.rate)
        if compare(args.candidate, args.repeat, args.report, exact=True): raise SystemExit('Native repeats differ')
        shutil.copytree(args.candidate, reference)
    else:
        work = args.work or Path.home()/'.cache/unbound'/('q158-delivery-gate-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f'))
        work.mkdir(parents=True, exist_ok=False)
        runtime = args.runtime or shared_runtime(Path(os.environ['UNBOUND']), work/'runtime')
        subprocess.run([sys.executable, str(REPO/'tests/authored_project.py'), 'assemble', 'mixersuspend', 'delivery',
                        '--release', args.release, '--out', str(work/'authored')], check=True)
        failed = 0
        for rate in (48000,):
            child = argparse.Namespace(mode='engine', work=work/f'engine-{rate}', project=work/'authored',
                                       runtime=runtime, release=args.release, rate=rate)
            capture(child)
            failed += compare(FIXTURE/'Reference'/('unity-'+args.release)/f'delivery-rate-{rate}',
                              child.work/'capture', work/f'report-{rate}.jsonl.gz')
        return bool(failed)
    return 0


if __name__ == '__main__': raise SystemExit(main())
