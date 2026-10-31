"""Record Native oracles and keep comparison reports in the validation cache."""
import argparse
import gzip
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[2]))
from probe_paths import reference_root, reference_file, run_root, unity_version

FIXTURE = Path(__file__).resolve().parent


def external_reference(project, profile=''):
    """Consume unchanged legacy regression oracles only for their measured release."""
    if profile.endswith('.jsonl'):
        single = reference_file(project, 'jsonl')
        if single.exists():
            return single
    versioned = reference_root(project)
    if versioned.exists():
        return versioned/profile
    assert unity_version() == '6000.0.71f1', f'no {unity_version()} reference for {project}'
    return Path(project)/'Reference'/profile


def hashes(source):
    result = {}
    for path in sorted(Path(source).iterdir()):
        name = path.name.removesuffix('.gz')
        if not name.endswith(('.rgba', '.raw', '.png', '.jsonl')):
            continue
        data = path.read_bytes()
        if path.suffix == '.gz':
            data = gzip.decompress(data)
        assert name not in result, f'duplicate capture file: {name}'
        result[name] = hashlib.sha256(data).hexdigest()
    assert 'values.jsonl' in result, f'no capture in {source}'
    return result


def verify_repeat(native, repeat, space):
    expected, actual = hashes(native), hashes(repeat)
    # Packed oracles keep float/raw buffers; fresh captures also have PNG previews.
    if not any(name.endswith('.png') for name in expected):
        actual = {name: digest for name, digest in actual.items() if not name.endswith('.png')}
    assert expected == actual, f'{space} Native repeats differ'


def pack(source, destination):
    """Keep observations and complete float/raw buffers; reports stay outside Reference."""
    source, destination = Path(source), Path(destination)
    assert not destination.exists(), f'refusing to overwrite {destination}'
    destination.mkdir(parents=True)
    shutil.copyfile(source/'values.jsonl', destination/'values.jsonl')
    for path in source.iterdir():
        name = path.name.removesuffix('.gz')
        if not name.endswith(('.rgba', '.raw')):
            continue
        data = path.read_bytes()
        if path.suffix == '.gz':
            data = gzip.decompress(data)
        (destination/(name+'.gz')).write_bytes(gzip.compress(data, mtime=0))
    return {'source': str(source), 'sha256': hashes(source)}


def compare(name, reference, actual, output):
    result = subprocess.run([sys.executable, str(FIXTURE/'compare.py'), str(reference), str(actual)], capture_output=True, text=True)
    (output/(name+'.txt')).write_text(result.stdout+result.stderr)
    print(name, 'exit', result.returncode, '\n'.join(result.stdout.splitlines()[-2:]), flush=True)
    return result.returncode


def main(focused=False):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('label', nargs='?', default='current')
    parser.add_argument('--linear', type=Path, help='Linear engine capture')
    parser.add_argument('--gamma', type=Path, help='Gamma engine capture')
    parser.add_argument('--native-linear', type=Path)
    parser.add_argument('--native-gamma', type=Path)
    parser.add_argument('--repeat-linear', type=Path)
    parser.add_argument('--repeat-gamma', type=Path)
    parser.add_argument('--out', type=Path)
    parser.add_argument('--record-reference', action='store_true', help='pack two byte-identical Native repeats into the selected release reference')
    args = parser.parse_args()
    output = args.out or run_root(FIXTURE, args.label)/('focused-reports' if focused else 'reports')
    output.mkdir(parents=True, exist_ok=True)
    if args.record_reference:
        for space in ('Linear', 'Gamma'):
            native = getattr(args, 'native_'+space.lower())
            repeat = getattr(args, 'repeat_'+space.lower())
            assert native and repeat, '--record-reference requires two explicit Native captures per color space'
            verify_repeat(native, repeat, space)
            profile = ('Focused' if focused else '')+space
            assert not (reference_root(FIXTURE)/profile).exists(), 'reference already exists: '+profile
    info, failures = {}, []
    for space in ('Linear', 'Gamma'):
        profile = ('Focused' if focused else '')+space
        native = getattr(args, 'native_'+space.lower()) or reference_root(FIXTURE)/profile
        actual = getattr(args, space.lower()) or run_root(FIXTURE, args.label)/profile
        repeat = getattr(args, 'repeat_'+space.lower())
        if repeat:
            verify_repeat(native, repeat, space)
            failures.append(compare('repeat-'+space.lower(), native, repeat, output))
        if args.record_reference:
            assert repeat, '--record-reference requires two Native recordings per color space'
            info[profile] = pack(native, reference_root(FIXTURE)/profile)
        else:
            info[profile] = {'native': str(native), 'actual': str(actual), 'sha256': hashes(actual)}
            failures.append(compare(args.label+'-'+space.lower(), native, actual, output))
    (output/'recordings.json').write_text(json.dumps(info, indent=2)+'\n')
    return int(any(failures))
