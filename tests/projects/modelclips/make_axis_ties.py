#!/usr/bin/env python3
"""Writes Assets/Resources/AxisTies: one static ASCII FBX per axis system an FBX file can name (the
up, front and coordinate axes a permutation of x, y and z, each with either sign: 48 files), each
a Null `Root` with an unrotated Mesh child, imported without Bake Axis Conversion, so that the
root's rotation is the axis conversion itself: a signed permutation of the axes, exact in single
precision. Among them are half turns about a diagonal of two axes (two equal diagonal elements of
the rotation's matrix, x among them) and turns of a third about a diagonal of all three (a trace
of exactly 0), which decide the sign a rotation takes from its matrix (ClipProbe's `nodes.*`).

    python3 tests/projects/modelclips/make_axis_ties.py
"""
import hashlib
import importlib.util
from itertools import permutations, product
from pathlib import Path
import re

PROBE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('uv', PROBE.parent/'lightmapuvs/Tools/make_project.py')
uv = importlib.util.module_from_spec(spec)
spec.loader.exec_module(uv)


def name(up, up_sign, front, front_sign, coord, coord_sign):
    s = lambda v: 'p' if v > 0 else 'n'
    return f'U{up}{s(up_sign)}F{front}{s(front_sign)}C{coord}{s(coord_sign)}'


def fbx(up, up_sign, front, front_sign, coord, coord_sign):
    axes = dict(UpAxis=up, UpAxisSign=up_sign, FrontAxis=front, FrontAxisSign=front_sign,
                CoordAxis=coord, CoordAxisSign=coord_sign)
    text = '; FBX 7.4.0 project file\nFBXHeaderExtension: {\n FBXHeaderVersion: 1003\n FBXVersion: 7400\n}\n'
    text += 'GlobalSettings: {\n Version: 1000\n Properties70: {\n'
    text += ''.join(f'  P: "{k}", "int", "Integer", "",{v}\n' for k, v in axes.items())
    text += '  P: "UnitScaleFactor", "double", "Number", "",100\n }\n}\n'
    text += ('Definitions: {\n Version: 100\n Count: 3\n ObjectType: "Model" {\n Count: 2\n }\n'
             ' ObjectType: "Geometry" {\n Count: 1\n }\n}\n')
    text += 'Objects: {\nGeometry: 100, "Geometry::Wedge", "Mesh" {\n'
    text += ' Vertices: *12 {\n a: 0,0,0, 2,0,0, 0,3,0, 0,0,4\n }\n'
    text += ' PolygonVertexIndex: *12 {\n a: 0,2,-2, 0,1,-4, 0,3,-3, 1,2,-4\n }\n GeometryVersion: 124\n}\n'
    for i, (node, kind, position) in enumerate([('Root', 'Null', (5, 7, 11)), ('Tip', 'Mesh', (1, 2, 3))]):
        text += f'Model: {300 + i}, "Model::{node}", "{kind}" {{\n Version: 232\n Properties70: {{\n'
        for prop, value in (('Lcl Translation', position), ('Lcl Rotation', (0, 0, 0)), ('Lcl Scaling', (1, 1, 1))):
            text += f'  P: "{prop}", "{prop}", "", "A",' + ','.join(map(str, value)) + '\n'
        text += ' }\n Shading: T\n Culling: "CullingOff"\n}\n'
    text += '}\nConnections: {\n C: "OO",300,0\n C: "OO",301,300\n C: "OO",100,301\n}\n'
    return text


def systems():
    for up, front, coord in permutations(range(3)):
        for up_sign, front_sign, coord_sign in product((1, -1), repeat=3):
            yield up, up_sign, front, front_sign, coord, coord_sign


def main():
    dest = PROBE/'Assets/Resources/AxisTies'
    dest.mkdir(parents=True, exist_ok=True)
    for system in systems():
        model = name(*system)
        path = dest/(model + '.fbx')
        path.write_text(fbx(*system))
        meta = uv.META.format(guid=hashlib.md5(('modelclips/AxisTies/' + model).encode()).hexdigest(),
                              **dict(uv.DEFAULTS, **uv.RAW, normalImportMode=2))
        for key, value in dict(bakeAxisConversion=0, preserveHierarchy=0).items():
            meta = re.sub(r'(?m)^(    ' + key + r': ).*', lambda m: m[1] + str(value), meta)
        Path(str(path) + '.meta').write_text(meta)
    folder = PROBE/'Assets/Resources/AxisTies.meta'
    if not folder.exists():
        folder.write_text(uv.FOLDER.format(guid=hashlib.md5(b'modelclips/AxisTies').hexdigest()))
    print('wrote', len(list(systems())), 'models to', dest.relative_to(PROBE))


if __name__ == '__main__':
    main()
