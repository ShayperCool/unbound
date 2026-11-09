#!/usr/bin/env python3
"""Settled streamed and decompressed completion clocks with complete rendered buffers."""
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
    assert meta['release'] == release and meta['rate'] == rate == 48000
    for name in ('decoded', 'streamed', 'decodedshort', 'streamedshort'):
        for loop in ('loop', 'once'):
            for timing in ('play', 'scheduled', 'end'):
                key = name+'.'+loop+'.'+timing
                assert meta[key] == dict(file=key+'.f32', frames=48*1024, channels=2)
                data = samples(root, meta[key])
                assert len(data) == 48*2048 and all(map(math.isfinite, data))
                assert any(abs(v) > 0.01 for v in data)
                assert meta[key+'.stopped']['playing'] == 0 and meta[key+'.stopped']['sample'] == 0
                for block in range(48):
                    for phase in ('before', 'render'):
                        assert meta[key+'.'+phase+'.'+str(block)].keys() == {'playing', 'sample', 'timeBits'}
                for block in (10, 40):
                    assert meta[key+'.wallAdvancesDsp.'+str(block)] is False
    print('Complete completion clock capture:', root, flush=True)


def capture(args):
    work = args.work.resolve(); work.mkdir(parents=True, exist_ok=False)
    project = work/'project'
    if args.mode == 'native':
        for part in ('Assets', 'Packages', 'ProjectSettings'): shutil.copytree(FIXTURE/part, project/part)
        for name, part in [('StreamClockProbe.cs', 'Scripts'), ('StreamClockRunner.cs', 'Editor')]:
            (project/'Assets'/part).mkdir(parents=True, exist_ok=True)
            shutil.copy2(FIXTURE/'Controls'/name, project/'Assets'/part/name)
        result = run(EditorJob(project, work/'editor.log', work/'editor', batch=True,
                              args=['-executeMethod', 'StreamClockRunner.Record'],
                              env={'Q158_RATE': str(args.rate), 'Q158_OUT': str(work/'capture'), 'Q158_VORBIS': '1' if args.vorbis else '0'},
                              editor=Path.home()/'Unity/Hub/Editor'/args.release/'Editor/Unity', timeout=600))
        print(summary(result), flush=True)
        problem = result_problem(work/'editor', gpu=True)
        if problem: raise SystemExit(problem)
    else:
        for name, part in [('StreamClockProbe.cs', 'Scripts')]:
            if (FIXTURE/'Controls'/name).read_bytes() != (args.project/'Assets'/part/name).read_bytes():
                raise SystemExit('Completion clock source changed; record a fresh authored project')
        for part in ('Assets', 'Packages', 'ProjectSettings'): shutil.copytree(args.project/part, project/part)
        env = dict(os.environ, Q158_RATE=str(args.rate), Q158_OUT=str(work/'capture'),
                   UNBOUND_MANAGED_DIR=str(args.runtime/'managed'), LD_LIBRARY_PATH=str(args.runtime/'managed'),
                   UNBOUND_CACHE=str(work/'cache'))
        with (work/'engine.log').open('w') as log:
            subprocess.run([str(Path.home()/'.cache/compat/with-heavy-job'), '--kind', 'engine-cpu', 'timeout', '600',
                            str(args.runtime/'unbound'), 'run', '-p', str(project), '--scene', 'Assets/Scenes/StreamClock.unity',
                            '--headless', '2000', '--no-watch', '--fixed-fps', '60'], env=env, stdout=log,
                           stderr=subprocess.STDOUT, check=True)
    verify(work/'capture', args.release, args.rate); pack(work/'capture')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('native', 'engine', 'publish', 'gate'))
    parser.add_argument('--release', required=True)
    parser.add_argument('--vorbis', action='store_true', help='Record the imported Vorbis compression control')
    parser.add_argument('--rate', type=int, choices=(48000, 44100), default=48000)
    parser.add_argument('--work', type=Path)
    parser.add_argument('--project', type=Path)
    parser.add_argument('--runtime', type=Path)
    parser.add_argument('--candidate', type=Path)
    parser.add_argument('--repeat', type=Path)
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    if args.vorbis and args.mode in ('publish', 'gate'):
        parser.error('Vorbis is a diagnostic capture; independent repeats do not yet agree')
    reference = FIXTURE/'Reference'/('unity-'+args.release)/f'stream-clocks-rate-{args.rate}'
    if args.mode in ('native', 'engine'): capture(args)
    elif args.mode == 'publish':
        if args.candidate.resolve() == args.repeat.resolve(): parser.error('two independent captures required')
        for root in (args.candidate, args.repeat): verify(root, args.release, args.rate)
        if compare(args.candidate, args.repeat, args.report, exact=True): raise SystemExit('Native repeats differ')
        shutil.copytree(args.candidate, reference)
        pack(reference)
    else:
        work = args.work or Path.home()/'.cache/unbound'/('q158-stream-clocks-gate-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f'))
        work.mkdir(parents=True, exist_ok=False)
        runtime = args.runtime or shared_runtime(Path(os.environ['UNBOUND']), work/'runtime')
        subprocess.run([sys.executable, str(REPO/'tests/authored_project.py'), 'assemble', 'mixersuspend', 'stream-clocks',
                        '--release', args.release, '--out', str(work/'authored')], check=True)
        failed = 0
        for rate in (48000,):
            child = argparse.Namespace(mode='engine', work=work/f'engine-{rate}', project=work/'authored',
                                       runtime=runtime, release=args.release, rate=rate, vorbis=False)
            capture(child)
            failed += compare(FIXTURE/'Reference'/('unity-'+args.release)/f'stream-clocks-rate-{rate}',
                              child.work/'capture', work/f'report-{rate}.jsonl.gz')
        return bool(failed)
    return 0


if __name__ == '__main__': raise SystemExit(main())
