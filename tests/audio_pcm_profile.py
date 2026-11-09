"""Record and gate an additional complete PCM profile of an existing audio fixture."""
import argparse
import datetime
import gzip
import json
import math
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
from editor_runner import EditorJob, run, summary, result_problem
from probe_paths import shared_runtime


def main(fixture, name, prefix, script, array, blocks, acceptance=None):
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
    parser.add_argument('--extend', action='store_true', help='Add controls while preserving every existing reference value and sample')
    args = parser.parse_args()
    if (fixture/'ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0] != 'm_EditorVersion: '+args.release:
        parser.error('release does not match fixture')
    # Each wrapper runs in its fixture, so the existing full comparator and its tolerances apply.
    from compare import compare
    from common import header, samples
    body = (fixture/'Assets/Scripts'/script).read_text().split(array+' = {', 1)[1].split('};', 1)[0]
    keys = dict.fromkeys(re.findall(r'new\[\] \{ "([a-z0-9_]+)"', body))
    assert keys, 'empty profile'

    def verify(root, rate):
        meta = header(root)
        assert meta['release'] == args.release and meta['rate'] == rate
        for key in keys:
            for suffix in ('', '.master'):
                record = meta[key+suffix]
                assert record == dict(file=key+suffix+'.f32', frames=blocks*1024, channels=2), key+suffix
        declared = set()
        for key, record in meta.items():
            if not isinstance(record, dict) or 'file' not in record:
                continue
            assert key.split('.')[0] in keys, key
            data = samples(root, record)
            assert len(data) == record['frames'] * record['channels'] and all(map(math.isfinite, data)), key
            declared.add(record['file'])
        actual = {p.name.removesuffix('.gz') for p in root.iterdir() if p.name.endswith(('.f32', '.f32.gz'))}
        assert actual == declared, (actual-declared, declared-actual)
        print('Complete PCM profile:', root, flush=True)

    def capture(mode, work, project, runtime, rate):
        work.mkdir(parents=True, exist_ok=False)
        private = work/'project'
        for part in ('Assets', 'Packages', 'ProjectSettings'):
            shutil.copytree((fixture if mode == 'native' else project)/part, private/part)
        env = {prefix+'OUT': str(work/'capture'), prefix+'PROFILE': name, prefix+'RATE': str(rate)}
        if mode == 'native':
            result = run(EditorJob(private, work/'editor.log', work/'editor', batch=True,
                                   args=['-executeMethod', 'ProbeRunner.Record'], env=env,
                                   editor=Path.home()/'Unity/Hub/Editor'/args.release/'Editor/Unity', timeout=900))
            print(summary(result), flush=True)
            problem = result_problem(work/'editor', gpu=True)
            if problem: raise SystemExit(problem)
        else:
            env.update({prefix+'ENGINE': '1', 'UNBOUND_MANAGED_DIR': str(runtime/'managed'),
                        'LD_LIBRARY_PATH': str(runtime/'managed'), 'UNBOUND_CACHE': str(work/'cache')})
            command = [str(Path.home()/'.cache/compat/with-heavy-job'), '--kind', 'engine-cpu', 'timeout', '900',
                       str(runtime/'unbound'), 'run', '-p', str(private), '--scene', 'Assets/Scenes/Probe.unity',
                       '--headless', '60', '--no-watch', '--fixed-fps', '60']
            (work/'command.json').write_text(json.dumps(command, indent=2)+'\n')
            with (work/'engine.log').open('w') as log:
                subprocess.run(command, env=dict(os.environ, **env), stdout=log, stderr=subprocess.STDOUT, check=True)
        verify(work/'capture', rate)

    def reference(rate):
        return fixture/'Reference'/('unity-'+args.release)/(name+'-rate-'+str(rate))

    if args.mode in ('native', 'engine'):
        capture(args.mode, args.work.resolve(), args.project, args.runtime, args.rate)
    elif args.mode == 'publish':
        if args.candidate.resolve() == args.repeat.resolve(): parser.error('two independent captures required')
        for root in (args.candidate, args.repeat): verify(root, args.rate)
        if compare(args.candidate, args.repeat, args.report, keys, exact=True):
            raise SystemExit('Native repeats differ')
        target = reference(args.rate)
        if target.exists():
            assert args.extend, 'reference already exists'
            existing, incoming = header(target), header(args.candidate)
            assert all(incoming.get(key) == value for key, value in existing.items()), 'existing metadata changed'
            for record in existing.values():
                if isinstance(record, dict) and 'file' in record:
                    assert samples(target, record).tobytes() == samples(args.candidate, record).tobytes(), record['file']
        else:
            target.mkdir(parents=True)
        shutil.copy2(args.candidate/'capture.json', target/'capture.json')
        for source in args.candidate.glob('*.f32'):
            if (target/(source.name+'.gz')).exists(): continue
            with gzip.GzipFile(target/(source.name+'.gz'), 'wb', mtime=0) as output:
                output.write(source.read_bytes())
        verify(target, args.rate)
    else:
        work = args.work or Path.home()/'.cache/unbound'/('audio-'+name+'-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S-%f'))
        work.mkdir(parents=True, exist_ok=False)
        runtime = args.runtime or shared_runtime(Path(os.environ['UNBOUND']), work/'runtime')
        subprocess.run([sys.executable, str(fixture.parents[1]/'authored_project.py'), 'assemble', fixture.name,
                        name, '--release', args.release, '--out', str(work/'authored')], check=True)
        failed = 0
        for rate in (48000, 44100):
            child = work/f'engine-{rate}'
            capture('engine', child, work/'authored', runtime, rate)
            compare(reference(rate), child/'capture', work/f'strict-{rate}.jsonl.gz', keys)
            if acceptance is None:
                failed += compare(reference(rate), child/'capture', work/f'gate-{rate}.jsonl.gz', keys, pcm_steps=1)
            else:
                failed += acceptance(reference(rate), child/'capture', work/f'gate-{rate}.jsonl.gz', keys, child/'engine.log')
            diagnostics = (child/'engine.log').read_text()
            assert 'are not applied' not in diagnostics and 'activating zones during playback is not supported' not in diagnostics
        return bool(failed)
    return 0
