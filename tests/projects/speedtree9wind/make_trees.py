#!/usr/bin/env python3
"""Writes the probe's SpeedTree 9 trees (Assets/Trees/*.st9) with tests/projects/speedtree9's
writer: the same small tree, two wind configurations.

    python3 tests/projects/speedtree9wind/make_trees.py

`Gusty` turns every wind part on and asks for a gust on almost every frame (a gust frequency of
3000 per 100 seconds); `Calm` keeps the shared bend, the second branch level and the ripple, with
other curves. The textures are speedtree9's.
"""
import importlib.util
from pathlib import Path

PROBE = Path(__file__).resolve().parent
TREES = PROBE/'Assets/Trees'

_spec = importlib.util.spec_from_file_location('speedtree9_trees', PROBE.parent/'speedtree9/make_trees.py')
st9 = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(st9)


def wind(common_values, curves, scalars, flags):
    """common_values: {index: value} of the common table; curves: (scale, phase, independence) of
    the shared part and the two branch levels, then (scale, shimmer, independence) of the ripple;
    scalars: the shared start height and the two branch stretch limits; flags: shared, branch 1,
    branch 2, ripple, shimmer."""
    common = [st9.f32(0.0)] * st9.COMMON_ENTRIES
    for i, v in common_values.items():
        common[i] = st9.f32(v)
    shared, branch1, branch2, ripple = curves
    entries = [st9.table(common), st9.branch(*shared), st9.branch(*branch1), st9.branch(*branch2), st9.ripple(*ripple)]
    entries += [st9.u32(0)] * 5
    entries += [st9.f32(v) for v in scalars]
    entries += [st9.u32(0)] * 2
    entries += [st9.u32(1 if f else 0) for f in flags]
    assert len(entries) == st9.WIND_ENTRIES
    return st9.table(entries)


def tree(wind_table):
    bark = st9.material('Bark', False, False, False, [
        st9.material_map(True, 'Bark_Color.png', (1, 1, 1, 1)),
        st9.material_map(True, 'Bark_Normal.png', (0.5, 0.5, 1, 1)),
        st9.material_map(False, '', (0.2, 0.0, 1, 1)),
        st9.material_map(False, '', (1, 1, 1, 1))])
    leaf = st9.material('Leaves', True, True, False, [
        st9.material_map(True, 'Leaf_Color.png', (1, 1, 1, 1)),
        st9.material_map(False, '', (0.5, 0.5, 1, 1)),
        st9.material_map(True, 'Leaf_Extra.png', (0.3, 0.1, 1, 1)),
        st9.material_map(True, '', (0.8, 0.9, 0.4, 1))])
    lods = [
        st9.lod([(0, False, st9.trunk(3.0, 0.3, 8, 0)), (1, True, st9.leaves(6, 3.0, True))]),
        st9.lod([(0, False, st9.trunk(3.0, 0.3, 5, 1))]),
    ]
    root = [st9.u32(0)] * st9.ROOT_ENTRIES
    root[st9.ROOT_VERSION_MAJOR] = st9.u32(9)
    root[st9.ROOT_VERSION_MINOR] = st9.u32(5)
    root[st9.ROOT_BOUNDS] = st9.floats(-1.2, 0.0, -1.2, 1.2, 4.5, 1.2)
    root[st9.ROOT_LODS] = st9.table_array(lods)
    root[st9.ROOT_BILLBOARD] = st9.table([st9.u32(0), st9.u32(0), st9.u32(0)])
    root[st9.ROOT_COLLISIONS] = st9.table_array([])
    root[st9.ROOT_MATERIALS] = st9.table_array([bark, leaf])
    root[st9.ROOT_WIND] = wind_table
    blob = st9.Blob()
    blob.data += st9.SIGNATURE
    blob.table([lambda e=e: e(blob) for e in root])
    return bytes(blob.data)


def main():
    TREES.mkdir(parents=True, exist_ok=True)
    gusty = wind({5: 3000.0, 6: 0.2, 7: 0.9, 8: 0.5, 9: 1.5, 10: 1.0, 11: 1.0},
                 ((0.2, 0.0, 0.1), (0.4, 1.0, 0.3), (0.6, 2.0, 0.5), (0.05, 0.7, 0.25)),
                 (0.25, 0.4, 0.6), (1, 1, 1, 1, 1))
    calm = wind({5: 0.3, 6: 0.1, 7: 0.6, 8: 1.0, 9: 3.0, 10: 0.8, 11: 0.4},
                ((0.5, 2.5, 0.2), (0.3, 0.5, 0.4), (0.9, 4.0, 0.15), (0.08, 0.3, 0.6)),
                (0.1, 0.7, 0.3), (1, 0, 1, 1, 0))
    (TREES/'Gusty.st9').write_bytes(tree(gusty))
    (TREES/'Calm.st9').write_bytes(tree(calm))
    print('wrote', ', '.join(sorted(p.name for p in TREES.glob('*.st9'))))


if __name__ == '__main__':
    main()
