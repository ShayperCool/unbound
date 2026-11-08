#!/usr/bin/env python3
"""Publish additive audio cases only after two independent captures agree byte for byte.

Existing metadata and samples are immutable. A partial capture may add new cases to a
reference; the combined reference must pass the probe's full completeness verifier.
"""
import argparse
import gzip
import importlib.util
import json
import os
from pathlib import Path
import shutil
import sys
import tempfile


def publish(fixture, capture, repeat, release, rate, case_prefix=None):
    if capture.resolve() == repeat.resolve():
        raise ValueError('two independent captures are required')
    first, second = [json.loads((root/'capture.json').read_text()) for root in (capture, repeat)]
    if case_prefix:
        select = lambda data: {key: value for key, value in data.items()
                               if key in ('release', 'rate') or key.split('.')[0].startswith(case_prefix)}
        first, second = select(first), select(second)
        if len(first) <= 2: raise ValueError('case prefix selects no fields')
    if first != second:
        raise ValueError('repeat metadata differs')
    if first['release'] != release or first['rate'] != rate:
        raise ValueError('wrong release or rate')
    target = fixture/'Reference'/('unity-'+release)/('rate-'+str(rate))
    existing = json.loads((target/'capture.json').read_text())
    for key in existing.keys() & first.keys():
        if existing[key] != first[key]:
            raise ValueError('existing metadata changed: '+key)
    files = [value['file'] for value in first.values() if isinstance(value, dict) and 'file' in value]
    for filename in files:
        if Path(filename).name != filename or not filename.endswith('.f32'):
            raise ValueError('invalid sample filename: '+filename)
        raw = (capture/filename).read_bytes()
        if raw != (repeat/filename).read_bytes():
            raise ValueError('repeat samples differ: '+filename)
        committed = target/(filename+'.gz')
        if committed.exists() and raw != gzip.decompress(committed.read_bytes()):
            raise ValueError('existing samples changed: '+filename)
    merged = existing | first
    cache = Path(os.environ.get('XDG_CACHE_HOME') or Path.home()/'.cache')/'unbound'
    cache.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='publish-audio-', dir=cache) as temporary:
        staging = Path(temporary)
        for src in target.glob('*.gz'):
            (staging/src.name).symlink_to(src.resolve())
        for filename in files:
            (staging/filename).symlink_to((capture/filename).resolve())
        (staging/'capture.json').write_text(json.dumps(merged)+'\n')
        sys.path.insert(0, str(fixture))
        spec = importlib.util.spec_from_file_location('audio_verify', fixture/'verify.py')
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        module.verify(staging, release, rate)
        for filename in files:
            if not (target/(filename+'.gz')).exists():
                with gzip.GzipFile(target/(filename+'.gz'), 'wb', mtime=0) as output:
                    output.write((capture/filename).read_bytes())
        shutil.copyfile(staging/'capture.json', target/'capture.json')
    print(f'{fixture.name} {rate}: repeats identical; {len(first)} captured fields; existing values unchanged')


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('probe')
    p.add_argument('--release', required=True)
    p.add_argument('--rate', required=True, type=int, choices=(48000, 44100))
    p.add_argument('--capture', required=True, type=Path)
    p.add_argument('--repeat', required=True, type=Path)
    p.add_argument('--case-prefix', help='Publish only this additive case prefix from a full capture')
    args = p.parse_args()
    publish(Path(__file__).resolve().parent/'projects'/args.probe, args.capture, args.repeat, args.release, args.rate, args.case_prefix)
