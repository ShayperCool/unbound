#!/usr/bin/env python3
"""Compare an engine capture of the OnRenderObject order witness with the Editor reference.

    compare.py REFERENCE.jsonl CAPTURE.jsonl [--report OUT.json]

The record is compared line by line, in order, with no tolerance (as the core API probe whose
project this is): every missing (reference only) and extra (capture only) line is printed and
counted. The order window is also read with analyze.py, unchanged: for every frame o0..o16 and
every camera render, the camera and its OnRenderObject handler list must equal the reference's,
and the capture must pass analyze.py's structural guards. The reference is read as it is.

Exit 0 when every line matches; 1 otherwise. The last line is a JSON summary.
"""
import argparse
import difflib
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
import analyze  # noqa: E402


def lines(path):
    out = [line for line in Path(path).read_text().splitlines() if line.strip()]
    for line in out:
        json.loads(line)
    return out


def sequence(reference, actual):
    rows = []
    matcher = difflib.SequenceMatcher(a=reference, b=actual, autojunk=False)
    for tag, i1, i2, j1, j2 in matcher.get_opcodes():
        if tag == 'equal':
            rows += [{'status': 'pass', 'line': reference[i], 'reference_index': i, 'actual_index': j1 + k}
                     for k, i in enumerate(range(i1, i2))]
            continue
        rows += [{'status': 'missing', 'line': reference[i], 'reference_index': i} for i in range(i1, i2)]
        rows += [{'status': 'extra', 'line': actual[j], 'actual_index': j} for j in range(j1, j2)]
    return rows


def renders(path):
    """(o, render index) -> (camera, handlers) of the order window, in record order."""
    out = {}
    for o, frame in analyze.frames(analyze.rows(path)).items():
        for index, (cam, handlers) in enumerate(zip(frame['oro_cams'], frame['renders'])):
            out[(o, index)] = (cam, handlers)
    return out


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('reference')
    parser.add_argument('capture')
    parser.add_argument('--report')
    args = parser.parse_args()
    if not Path(args.capture).is_file():
        print(f'MISSING capture {args.capture}')
        return 1
    reference, actual = lines(args.reference), lines(args.capture)
    rows = sequence(reference, actual)
    summary = {s: sum(r['status'] == s for r in rows) for s in ('pass', 'missing', 'extra')}
    summary.update(reference_lines=len(reference), actual_lines=len(actual))

    expected, observed = renders(args.reference), renders(args.capture)
    order = []
    for key in sorted(expected.keys() | observed.keys()):
        if expected.get(key) != observed.get(key):
            order.append({'o': key[0], 'render': key[1], 'reference': expected.get(key), 'capture': observed.get(key)})
    summary.update(order_renders=len(expected), order_renders_differ=len(order),
                   guard=analyze.guard_problem(args.capture))
    if args.report:
        Path(args.report).write_text(json.dumps({'summary': summary, 'order_window': order, 'sequence': rows},
                                                indent=1) + '\n')
    for r in rows:
        if r['status'] != 'pass':
            print(('-' if r['status'] == 'missing' else '+'), r['line'])
    for row in order:
        print(f"order window o{row['o']} render {row['render']}: reference {row['reference']}, capture {row['capture']}")
    print(f"{summary['pass']}/{len(reference)} lines match; {summary['missing']} missing, {summary['extra']} extra; "
          f"order window {len(expected) - sum(r['reference'] is not None for r in order)}/{len(expected)} renders match; "
          f"guard: {summary['guard'] or 'passes'}")
    print(json.dumps(summary, sort_keys=True))
    return int(bool(summary['missing'] or summary['extra'] or order or summary['guard']))


if __name__ == '__main__':
    sys.exit(main())
