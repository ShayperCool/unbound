#!/usr/bin/env python3
"""Compares a dropped-parameters capture with the Editor's: every value exactly (floats are the
hex of their bits; differing ones are shown decoded too), after one normalisation: CoreCLR's
" (Parameter 'x')" reads as Mono's "\nParameter name: x". Lists each missing, extra or differing
value and exits non-zero on any; a summary per section (dims, log, gpuproj, slice, load, unload,
shadow) follows.

    compare.py Reference/unity-6000.0.71f1.jsonl <capture>.jsonl
"""
import argparse
import json
import re
import struct
import sys

HEX = re.compile(r'\b[0-9a-f]{8}\b')
# CoreCLR words an argument exception's parameter as " (Parameter 'x')", Mono as "\nParameter name: x".
CORECLR_PARAMETER = re.compile(r" \(Parameter '([^']*)'\)")


def load(path):
    rows = [json.loads(line) for line in open(path, encoding='utf-8') if line.strip()]
    values = {row['k']: CORECLR_PARAMETER.sub(r'\nParameter name: \1', row['v']) for row in rows}
    if len(values) != len(rows) or not rows:
        sys.exit(f'{path}: empty or duplicate keys')
    return values


def decoded(value, key):
    if not key.startswith(('gpuproj.', 'shadow.', 'unload.')):
        return value
    return HEX.sub(lambda m: f'{struct.unpack("<f", bytes.fromhex(m.group(0))[::-1])[0]:.9g}', value)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('reference')
    parser.add_argument('actual')
    args = parser.parse_args()
    a, b = load(args.reference), load(args.actual)
    failures = 0
    stats = {}
    for key in sorted(a.keys() | b.keys()):
        section = key.split('.', 1)[0]
        s = stats.setdefault(section, [0, 0])
        s[0] += 1
        if key not in b:
            print('missing', key, a[key][:300])
        elif key not in a:
            print('extra', key, b[key][:300])
        elif a[key] == b[key]:
            s[1] += 1
            continue
        else:
            print('differs', key, '\n  unity  ', decoded(a[key], key)[:400], '\n  unbound', decoded(b[key], key)[:400])
        failures += 1
    for section in sorted(stats):
        n, exact = stats[section]
        print(f'{section}: {exact}/{n}')
    print(f'{failures} differences')
    return int(bool(failures))


if __name__ == '__main__':
    sys.exit(main())
