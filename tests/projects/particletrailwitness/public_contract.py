#!/usr/bin/env python3
"""Audit an existing pair, keeping the rejected storage union outside the canonical contract."""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import struct
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
from run import rows, leaf_differences


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n')


def number(value):
    return struct.unpack('!f', bytes.fromhex(value['bits']))[0]


def audit(pair, layout, release, out):
    a, b = [rows(pair / side / 'capture/witness.jsonl') for side in ('native-a', 'native-b')]
    assert a['release'] == b['release'] == release
    fields = json.loads(layout.read_text())
    assert {f['name'] for f in fields} == set(a['trails.fieldNames'])
    assert all(f['isAssembly'] and not f['isPublic'] for f in fields)
    union = [dict(key=k, a_present=k in a, b_present=k in b, a=a.get(k), b=b.get(k),
                  equal=k in a and k in b and a[k] == b[k]) for k in sorted(a.keys() | b.keys())]
    save(out/'full-repeat-union.json', union)
    save(out/'full-repeat-leaves.json', leaf_differences(a, b))
    public = [{k: v for k, v in x.items() if not k.startswith('trails.')} for x in (a, b)]
    assert public[0] == public[1], 'Public fields must all repeat, including complete raw mesh buffers'
    metadata = {k: v for k, v in a.items() if k.startswith('trails.') and k != 'trails.positions'}
    assert all(b[k] == v for k, v in metadata.items())
    capacity = a['trails.maxPositionsPerTrailCount']
    slots = a['trails.maxTrailCount']
    assert len(a['trails.positions']) == len(b['trails.positions']) == slots * capacity == a['trails.returnCount']
    ranges = []
    for slot, count in enumerate(a['trails.positionCounts']):
        front, back = a['trails.frontPositions'][slot], a['trails.backPositions'][slot]
        assert 0 <= count <= capacity and front == (back + count - 1) % capacity
        indices = [slot * capacity + (back + j) % capacity for j in range(count)]
        ranges.append(dict(slot=slot, count=count, front=front, back=back, indices=indices))
    valid = {i for r in ranges for i in r['indices']}
    differences = [dict(index=i, slot=i//capacity, offset=i%capacity, a=x, b=y,
                        in_counted_range=i in valid) for i, (x, y) in enumerate(zip(a['trails.positions'], b['trails.positions'])) if x != y]
    save(out/'storage-differences.json', differences)
    assert not any(x['in_counted_range'] for x in differences)
    # Each known live particle contributes its head and its counted ring in reverse.
    # Do not call the extra count-described entry active, dead or padding. It is
    # preserved and compared above; here every actual baked center is accounted for.
    vertices = np.array([[number(x) for x in p] for p in a['mesh.vertices']], dtype=np.float64)
    membership = []
    expected_indices = []
    cursor = 0
    for particle in range(a['particles.count']):
        ring = ranges[particle]['indices']
        sources = [(None, a[f'particles.{particle}.position'])] + [(i, a['trails.positions'][i][:3]) for i in reversed(ring)]
        for index, source in sources:
            center = np.array([number(x) for x in source])
            left, right = vertices[cursor:cursor+2]
            # Addition/subtraction of the half-width each rounds once to float32.
            # This bound follows from the ULP of each result, not an empirical fit.
            ulps = [np.abs(np.spacing(v.astype(np.float32)).astype(np.float64)) for v in (left, right)]
            bound = (ulps[0] + ulps[1]) * .25
            delta = np.abs((left+right)*.5-center)
            assert np.all(delta <= bound), (particle, index, delta, bound)
            membership.append(dict(vertex_pair=cursor//2, particle=particle, raw_index=index,
                                   center=center.tolist(), rounding_error=delta.tolist(), rounding_bound=bound.tolist()))
            cursor += 2
        start = cursor - 2*len(sources)
        for i in range(len(sources)-1):
            v = start+2*i
            expected_indices += [v,v+2,v+1,v+1,v+2,v+3]
    assert cursor == a['mesh.vertexCount'] == len(vertices)
    assert expected_indices == a['mesh.submesh.0.indicesWithBase'] == a['mesh.submesh.0.indicesWithoutBase']
    assert len(expected_indices) == a['mesh.submesh.0.indexCount']
    # Cross-check typed views against every raw declared attribute and index byte.
    stride = a['mesh.buffer.0.stride']; raw = base64.b64decode(a['mesh.buffer.0.bytes'])
    assert len(raw) == len(vertices)*stride
    for i in range(len(vertices)):
        pos = b''.join(bytes.fromhex(x['bits'])[::-1] for x in a['mesh.vertices'][i])
        uv = b''.join(bytes.fromhex(x['bits'])[::-1] for x in a['mesh.uv0'][i][:2])
        assert raw[i*stride:(i+1)*stride] == pos + bytes(a['mesh.colors32'][i]) + uv
    assert base64.b64decode(a['mesh.indexBytes']) == struct.pack('<'+'H'*len(expected_indices), *expected_indices)
    image = Path(__file__).resolve().parent.parent/'particles/Reference/images/trails.png'
    for side in ('native-a', 'native-b'):
        capture = pair/side/'capture'
        assert (capture/'trails.png').read_bytes() == image.read_bytes()
        png = np.array(Image.open(capture/'trails.png').convert('RGBA'))
        rgba = np.frombuffer((capture/'trails.rgba').read_bytes(), dtype=np.uint8).reshape(png.shape)
        assert np.array_equal(rgba[::-1], png)
    assert (pair/'native-a/capture/trails.rgba').read_bytes() == (pair/'native-b/capture/trails.rgba').read_bytes()
    save(out/'mesh-center-membership.json', membership)
    save(out/'all-ring-ranges.json', ranges)
    summary = dict(release=release, public_repeat_exact=True, public_records=len(public[0]),
                   full_raw_snapshot='REJECTED' if differences else 'exact', storage_parity='UNCLAIMED / NOT IMPLEMENTED',
                   raw_vector_differences=len(differences), counted_entries=len(valid), counted_differences=0,
                   mesh_vertices=cursor, mesh_indices=len(expected_indices),
                   extra_counted_slots=[r for r in ranges[a['particles.count']:] if r['count']],
                   scope='All non-trails records: public particles, complete mesh bytes and typed attributes, camera, quality and target. Reflected Trails backing storage and GetTrails implementation are excluded.',
                   inputs={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in [layout, image, *[pair/s/'capture/witness.jsonl' for s in ('native-a','native-b')]]})
    save(out/'public-repeat-summary.json', summary)
    return public[0], summary


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--pair', required=True, type=Path)
    p.add_argument('--layout', required=True, type=Path)
    p.add_argument('--release', required=True)
    p.add_argument('--out', required=True, type=Path)
    p.add_argument('--publish', action='store_true')
    args = p.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    public, summary = audit(args.pair, args.layout, args.release, args.out)
    if args.publish:
        target = Path(__file__).resolve().parent/'Reference'/('unity-'+args.release)
        target.mkdir(parents=True, exist_ok=False)
        with (target/'public.jsonl').open('x') as f:
            for k, v in public.items():
                f.write(json.dumps(dict(k=k,v=v), separators=(',',':'))+'\n')
        # One canonical data file, with scope and content-bound provenance beside it.
        published = {k: v for k, v in summary.items() if k != 'inputs'}
        published['input_sha256'] = {f'input-{i}': v for i, v in enumerate(summary['inputs'].values())}
        published['field_layout'] = json.loads(args.layout.read_text())
        save(target/'contract.json', published)
    print(json.dumps(summary))


if __name__ == '__main__':
    main()
