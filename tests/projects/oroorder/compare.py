#!/usr/bin/env python3
"""Compare an engine capture of the OnRenderObject order witness with the Editor reference.

    compare.py REFERENCE.jsonl CAPTURE.jsonl [--report OUT.json]

The record is compared line by line, in order, with no tolerance (as the core API probe whose
project this is): every missing (reference only) and extra (capture only) line is printed and
counted. The capture must also pass analyze.py's structural guards. For diagnosis, the order
window is read with analyze.py, unchanged: every frame o0..o16 and camera render whose camera or
OnRenderObject handler list differs from the reference's is listed. Unity documents no handler
order, and a different order already shows as missing and extra lines. The reference is read as
it is.

Exit 0 when every line matches and the guards pass; 1 otherwise. The last line is a JSON summary.
"""
import argparse
import importlib.util
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
import analyze  # noqa: E402


spec = importlib.util.spec_from_file_location('core_api_compare', Path(__file__).resolve().parents[1]/'coreapis/compare.py')
core_api_compare = importlib.util.module_from_spec(spec)
spec.loader.exec_module(core_api_compare)
lines, sequence = core_api_compare.load, core_api_compare.compare


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
    return int(bool(summary['missing'] or summary['extra'] or summary['guard']))


if __name__ == '__main__':
    sys.exit(main())
