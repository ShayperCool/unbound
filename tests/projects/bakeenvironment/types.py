#!/usr/bin/env python3
"""types.py [OUT]: the type trees of the binary serialized files a bake writes, as the Editor
wrote them (the reference's `EnvSky/written/LightingData.asset`: LightingDataAsset and
LightProbes), as the engine's table (crates/unbound-assets/data/serialized-types-<family>.json,
the default OUT): per class its flags, hashes, nodes (version, level, type flags, type and name
string offsets, byte size, index, meta flags, reference hash), own strings and dependencies."""
import json
from pathlib import Path
import struct
import sys

PROBE = Path(__file__).resolve().parent
REPO = PROBE.parents[2]
FAMILY = '6000.0'
SOURCE = PROBE/'Reference/unity-6000.0.71f1/EnvSky/written/LightingData.asset'


def types(data):
    """The type table of a serialized file of format 22 with type trees."""
    if struct.unpack_from('>I', data, 8)[0] != 22:
        raise ValueError('not a serialized file of format 22')
    at = data.index(b'\0', 48) + 1 + 4
    if data[at] != 1:
        raise ValueError('a serialized file without type trees')
    at += 1
    count, = struct.unpack_from('<i', data, at)
    at += 4
    out = []
    for _ in range(count):
        class_id, stripped, script_index = struct.unpack_from('<iBh', data, at)
        at += 7
        script_hash = None
        if class_id == 114 or class_id < 0:
            script_hash = data[at:at + 16].hex()
            at += 16
        hash_ = data[at:at + 16].hex()
        at += 16
        nodes_count, strings_size = struct.unpack_from('<ii', data, at)
        at += 8
        nodes = []
        for _ in range(nodes_count):
            nodes.append(list(struct.unpack_from('<HBBIIiiIQ', data, at)))
            at += 32
        strings = data[at:at + strings_size].hex()
        at += strings_size
        deps_count, = struct.unpack_from('<i', data, at)
        at += 4
        deps = list(struct.unpack_from(f'<{deps_count}i', data, at))
        at += 4 * deps_count
        out.append({'classId': class_id, 'stripped': stripped, 'scriptIndex': script_index, 'scriptHash': script_hash,
                    'hash': hash_, 'nodes': nodes, 'strings': strings, 'dependencies': deps})
    return out


def main():
    out = Path(sys.argv[1]) if len(sys.argv) > 1 else REPO/f'crates/unbound-assets/data/serialized-types-{FAMILY}.json'
    table = {'source': str(SOURCE.relative_to(REPO)), 'types': types(SOURCE.read_bytes())}
    out.parent.mkdir(parents=True, exist_ok=True)
    # One line a node.
    lines = ['{', f' "source": {json.dumps(table["source"])},', ' "types": [']
    for k, t in enumerate(table['types']):
        fields = [f'  "{key}": {json.dumps(t[key])}' for key in ('classId', 'stripped', 'scriptIndex', 'scriptHash', 'hash', 'strings', 'dependencies')]
        nodes = ',\n'.join('   ' + json.dumps(n) for n in t['nodes'])
        lines.append('  {\n' + ',\n'.join(fields) + ',\n  "nodes": [\n' + nodes + '\n  ]\n  }' + (',' if k + 1 < len(table['types']) else ''))
    lines += [' ]', '}']
    out.write_text('\n'.join(lines) + '\n')
    return 0


if __name__ == '__main__':
    sys.exit(main())
