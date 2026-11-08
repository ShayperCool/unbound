#!/usr/bin/env python3
"""Measure clip data ownership across device reset without a listener-delivery timing claim."""
import argparse
import datetime
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

FIXTURE = Path(__file__).resolve().parent
REPO = FIXTURE.parents[2]
sys.path.insert(0, str(FIXTURE))
from compare import compare, header, raw
from pack import pack


def verify(root, release, rate):
    import array
    import math
    meta = header(root)
    expected = {'release', 'rate', 'complete', 'asset.write', 'clone.asset.write', 'clone.script.write',
                'reset', 'restart', 'rewrite.old'}
    fields = ['valid', 'frames', 'channels', 'frequency', 'lengthBits', 'type', 'background', 'load', 'read', 'loadAfterRead'] + ['data'+str(i) for i in range(8)]
    prefixes = [stage+'.'+str(i) for stage in ('before', 'after') for i in range(8)] + ['playedClone', 'rewritten', 'stream.preloaded', 'stream.deferred']
    expected |= {prefix+'.'+field for prefix in prefixes for field in fields}
    expected |= {'length.'+str(rate)+'.'+str(frames) for rate in (44100,48000) for frames in (1,43,44,45,47,48,49,4096,48000)}
    expected |= {key+'.'+field for key in ('stream.preloaded','stream.deferred') for field in ('preload','loadReturn','afterLoad','idleStop','afterPlay','afterRender','afterStop','ended','afterEnd','afterDestroy','shotPlaying','shotAfterStop','shotEnded','shotAfterEnd','forward.both','forward.firstStop','forward.lastStop','reverse.both','reverse.firstStop','reverse.lastStop')}
    expected |= {'phase'+str(i) for i in range(12)} | {'play.'+str(i) for i in range(12)}
    assert meta.keys() == expected, meta.keys() ^ expected
    assert meta['release'] == release and meta['rate'] == rate and meta['complete'] is True
    for key in expected - {'release', 'complete'} - {'phase'+str(i) for i in range(12)}:
        assert isinstance(meta[key], int), key
    for phase in range(12):
        key = 'phase'+str(phase); values = array.array('f'); values.frombytes(raw(root, meta[key]))
        assert meta[key] == dict(file=key+'.f32', frames=8192, channels=2)
        assert len(values) == 16384 and all(map(math.isfinite,values))
    print('Complete reset data capture:', root, flush=True)


def capture(args):
    work = args.work.resolve(); work.mkdir(parents=True, exist_ok=False)
    project = work/'project'
    if args.mode == 'native':
        if os.environ.get('UNBOUND_SUITE_NO_EDITOR'): raise SystemExit('Editor recording forbidden in suite')
        for part in ('Assets', 'Packages', 'ProjectSettings'): shutil.copytree(FIXTURE/part, project/part)
        shutil.copy2(FIXTURE/'Controls/ResetDataProbe.cs', project/'Assets/Scripts/ResetDataProbe.cs')
        shutil.copy2(FIXTURE/'Controls/ResetDataRunner.cs', project/'Assets/Editor/ResetDataRunner.cs')
        editor = Path.home()/'Unity/Hub/Editor'/args.release/'Editor/Unity'
        commands = [[str(editor), '-batchmode', '-nographics', '-projectPath', str(project), '-executeMethod', 'ResetDataRunner.'+stage,
                     '-logFile', str(work/(stage.lower()+'.log'))] for stage in ('Prepare', 'Record')]
    else:
        for name, part in [('ResetDataProbe.cs', 'Scripts'), ('ResetDataRunner.cs', 'Editor')]:
            if (FIXTURE/'Controls'/name).read_bytes() != (args.project/'Assets'/part/name).read_bytes():
                raise SystemExit('Reset data control sources changed; record a fresh authored state project')
        for part in ('Assets', 'Packages', 'ProjectSettings'): shutil.copytree(args.project/part, project/part)
        commands = [[str(args.runtime/'unbound'), 'run', '-p', str(project), '--scene', 'Assets/Scenes/ResetData.unity',
                     '--headless', '20000', '--no-watch', '--fixed-fps', '60']]
    env = dict(os.environ, UseSharedCompilation='false', MSBUILDDISABLENODEREUSE='1', DOTNET_CLI_USE_MSBUILD_SERVER='0',
               SUSPEND_PROBE_RATE=str(args.rate), SUSPEND_PROBE_OUT=str(work/'capture'), SUSPEND_RESETDATA_ENGINE='1' if args.mode == 'engine' else '0')
    if args.mode == 'engine':
        env.update(UNBOUND_MANAGED_DIR=str(args.runtime/'managed'), LD_LIBRARY_PATH=str(args.runtime/'managed'), UNBOUND_CACHE=str(work/'cache'))
    for i, command in enumerate(commands):
        with (work/f'run-{i}.log').open('w') as log:
            subprocess.run([str(Path.home()/'.cache/compat/with-heavy-job'), 'timeout', '900', *command],
                           env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
    verify(work/'capture', args.release, args.rate); pack(work/'capture')


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('mode', choices=('native', 'engine', 'compare', 'publish', 'gate'))
    p.add_argument('--release', required=True)
    p.add_argument('--rate', type=int, choices=(48000, 44100), default=48000)
    p.add_argument('--work', type=Path)
    p.add_argument('--project', type=Path)
    p.add_argument('--runtime', type=Path)
    p.add_argument('--reference', type=Path)
    p.add_argument('--candidate', type=Path)
    p.add_argument('--repeat', type=Path)
    p.add_argument('--report', type=Path)
    a = p.parse_args()
    reference = a.reference or FIXTURE/'Reference'/('unity-'+a.release)/f'reset-data-rate-{a.rate}'
    if a.mode in ('native', 'engine'): capture(a)
    elif a.mode == 'compare': return bool(compare(reference, a.candidate, a.report, batch=True))
    elif a.mode == 'publish':
        if not a.repeat or a.repeat.resolve() == a.candidate.resolve(): p.error('two independent captures required')
        for root in (a.candidate, a.repeat): verify(root, a.release, a.rate)
        if compare(a.candidate, a.repeat, a.report, exact=True, batch=True): raise SystemExit('Native repeats differ')
        shutil.copytree(a.candidate, reference)
    else:
        cache = Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound/q43'
        work = a.work or cache/('gate-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f'))
        work.mkdir(parents=True, exist_ok=False)
        subprocess.run([sys.executable, str(REPO/'tests/authored_project.py'), 'assemble', 'mixersuspend', 'reset-data',
                        '--release', a.release, '--out', str(work/'authored')], check=True)
        runtime = a.runtime or Path(os.environ['UNBOUND']).parent
        failed = False
        for rate in (48000, 44100):
            subprocess.run([sys.executable, str(__file__), 'engine', '--release', a.release, '--rate', str(rate),
                            '--runtime', str(runtime), '--project', str(work/'authored'), '--work', str(work/f'engine-{rate}')], check=True)
            report = work/f'report-{rate}.jsonl.gz'
            compare_result = compare(FIXTURE/'Reference'/('unity-'+a.release)/f'reset-data-rate-{rate}', work/f'engine-{rate}/capture', report, batch=True)
            failed |= bool(compare_result)
        return failed
    return 0


if __name__ == '__main__': raise SystemExit(main())
