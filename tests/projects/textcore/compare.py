#!/usr/bin/env python3
"""Compare the TextCore probe's output under Unbound with Unity's reference.

    compare.py REFERENCE.jsonl[.gz] UNBOUND.jsonl[.gz] [--verbose]

Per font: the font asset's face info and settings, its glyphs (metrics and atlas rects, in the
order they were added), characters, kerning pairs and free/used atlas rects, and atlas pixels of
a few glyphs (alpha, within 1/255 counts as the same); then every case's preferred size,
characters, lines and mesh vertices. Floats must be equal; the first differences of each kind
are shown. A font the run could not load (the Editor resource without the Editor) is skipped.
"""
import base64
import gzip
import json
import struct
import sys


def f32(v):
    """A number as the float it was printed from (G9 prints may round differently in the last
    digit: Mono rounds half away from zero)."""
    if isinstance(v, float):
        return struct.unpack('f', struct.pack('f', v))[0]
    return v


def load(path):
    opener = gzip.open if path.endswith('.gz') else open
    with opener(path, 'rt') as f:
        return [json.loads(line) for line in f if line.strip()]


def by_type(lines, kind):
    return [l for l in lines if l['type'] == kind]


class Report:
    def __init__(self, verbose):
        self.verbose = verbose
        self.differ = 0
        self.compared = 0
        self.shown = {}

    def check(self, kind, where, want, have):
        self.compared += 1
        if want == have:
            return True
        self.differ += 1
        n = self.shown.get(kind, 0)
        if self.verbose or n < 8:
            print(f'!! {kind:10} {where}: unity {want} unbound {have}')
        self.shown[kind] = n + 1
        return False


def compare_values(report, kind, where, want, have):
    if isinstance(want, list) and isinstance(have, list):
        if len(want) != len(have):
            report.check(kind, where + ' length', len(want), len(have))
        for i, (a, b) in enumerate(zip(want, have)):
            compare_values(report, kind, f'{where}[{i}]', a, b)
    elif isinstance(want, dict) and isinstance(have, dict):
        for key in want:
            compare_values(report, kind, f'{where}.{key}', want[key], have.get(key))
    else:
        report.check(kind, where, f32(want), f32(have))


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    if len(args) != 2:
        sys.exit(__doc__)
    reference, unbound = load(args[0]), load(args[1])
    report = Report('--verbose' in sys.argv)
    fonts_ref = {l['font']: l for l in by_type(reference, 'font')}
    fonts_got = {l['font']: l for l in by_type(unbound, 'font')}
    loaded = set()
    for index, want in fonts_ref.items():
        have = fonts_got.get(index)
        if have is None or have.get('missing') or have.get('asset', 1) is None:
            print(f'-- font {index} ({want["name"]}) not loaded by the run: skipped')
            continue
        loaded.add(index)
        if want.get('sha256') != have.get('sha256') and have.get('sha256'):
            print(f'!! font {index}: a different font file ({have.get("sha256")})')
        for key in ('faceInfo', 'atlasWidth', 'atlasHeight', 'atlasPadding', 'renderMode', 'populationMode', 'multiAtlas',
                    'atlasFormat', 'atlasSize', 'gradientScale', 'styles', 'material'):
            compare_values(report, 'font', f'font {index} {key}', want.get(key), have.get(key))
    glyphs_got = {l['font']: l for l in by_type(unbound, 'glyphs')}
    for want in by_type(reference, 'glyphs'):
        index = want['font']
        have = glyphs_got.get(index)
        if index not in loaded or have is None:
            continue
        for key in ('atlasCount', 'glyphs', 'characters', 'pairCount', 'freeRects', 'usedRects'):
            compare_values(report, 'glyphs', f'font {index} {key}', want.get(key), have.get(key))
        # Unity's pair records come from native memory whose lookup flags are not initialized
        # (random bits, IgnoreSpacingAdjustments among them): the flags are not compared.
        # The order the font engine lists pairs in only matters for duplicates (the first is kept).
        compare_values(report, 'pairs', f'font {index} pairs', sorted(p[:10] for p in want['pairs']), sorted(p[:10] for p in have.get('pairs', [])))
        lookup_ref = {e[0]: e for e in want['lookup']}
        lookup_got = {e[0]: e for e in have['lookup']}
        compare_values(report, 'lookup', f'font {index} lookup keys', sorted(lookup_ref), sorted(lookup_got))
        for key in sorted(lookup_ref):
            if key in lookup_got:
                compare_values(report, 'lookup', f'font {index} lookup {key}', lookup_ref[key], lookup_got[key])
    atlas_got = {(l['font'], l['char']): l for l in by_type(unbound, 'atlas')}
    for want in by_type(reference, 'atlas'):
        have = atlas_got.get((want['font'], want['char']))
        if want['font'] not in loaded or have is None or 'alpha' not in want:
            continue
        where = f'font {want["font"]} {chr(want["char"])!r}'
        if (want['w'], want['h']) != (have.get('w'), have.get('h')) or 'alpha' not in have:
            report.check('atlas', where + ' size', (want['w'], want['h']), (have.get('w'), have.get('h')))
            continue
        a, b = base64.b64decode(want['alpha']), base64.b64decode(have['alpha'])
        worst = max(abs(x - y) for x, y in zip(a, b))
        report.check('atlas', where + ' max alpha error <= 1', True, worst <= 1)
        if worst > 1:
            print(f'   {where}: max alpha error {worst}, {sum(1 for x, y in zip(a, b) if abs(x - y) > 1)} of {len(a)} pixels')
    for want, have in zip(by_type(reference, 'cold'), by_type(unbound, 'cold')):
        for key in ('first', 'second', 'pairsBefore', 'pairsAfter', 'glyphs'):
            compare_values(report, 'cold', f'cold {key}', want.get(key), have.get(key))
    cases_got = {l['id']: l for l in by_type(unbound, 'case')}
    cases = 0
    for want in by_type(reference, 'case'):
        if want['font'] not in loaded:
            continue
        have = cases_got.get(want['id'])
        if have is None:
            report.check('case', f'case {want["id"]}', 'present', 'missing')
            continue
        cases += 1
        name = f'case {want["id"]} ({want["text"][:20]!r} {want["size"]}px {want["style"]})'
        for key in ('preferred', 'truncated', 'characterCount', 'lineCount', 'materialCount', 'chars', 'lines'):
            compare_values(report, 'case', f'{name} {key}', want.get(key), have.get(key))
        if 'meshes' in want:
            compare_values(report, 'mesh', f'{name} meshes', want['meshes'], have.get('meshes'))
    print(f'{cases} cases; {report.differ} of {report.compared} values differ' +
          (' (' + ', '.join(f'{k} {v}' for k, v in report.shown.items()) + ')' if report.shown else ''))
    sys.exit(1 if report.differ else 0)


if __name__ == '__main__':
    main()
