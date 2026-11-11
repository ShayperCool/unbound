#!/usr/bin/env python3
"""Author the FBX node-transform (Pivots) and skinned-bounds (Skinned) controls.

Each Pivots control is one ASCII FBX whose middle node `Body` exercises one FBX transform
feature (pivots and offsets, pre/post rotation, rotation order, geometric transforms, inherit
types) between a plain parent `Top` and a plain child `Leaf`, so the capture shows whether the
importer bakes the feature into the node, its mesh or its children. Combinations follow Maya
(pivots, offsets, joint orient as pre-rotation, segment scale compensation as inherit type Rrs)
and 3ds Max (Z-up centimetres, object offset as geometric transform) conventions. Blender's
convention comes from Blender itself (`blender_models.py`, committed binary output).

The Skinned controls bind a three-bone column with mixed weights to measure the imported
`SkinnedMeshRenderer.localBounds` and `Mesh.bounds`: bind pose equal to or different from the
file's node pose, a mesh node away from the origin, and a bone with a pivot. The Rigged controls
add clips that bend the column under each animation type (and a Humanoid model with its
clips), a root bone with a zero scale at import or in a clip, a vertex without weights, and
pivots, offsets and a child on the skinned mesh's node.
"""
import math
from pathlib import Path
import re
import shutil

import make_project as axis

ROOT = axis.ROOT
PROJECTS = axis.PROJECTS
uv = axis.uv


def euler(r, order='xyz'):
    """FBX Euler rotation in degrees as a 3x3 matrix (column vectors; `xyz` applies X first)."""
    def rx(a): c, s = math.cos(a), math.sin(a); return [[1, 0, 0], [0, c, -s], [0, s, c]]
    def ry(a): c, s = math.cos(a), math.sin(a); return [[c, 0, s], [0, 1, 0], [-s, 0, c]]
    def rz(a): c, s = math.cos(a), math.sin(a); return [[c, -s, 0], [s, c, 0], [0, 0, 1]]
    axes = dict(x=rx, y=ry, z=rz)
    m = [[1, 0, 0], [0, 1, 0], [0, 0, 1]]
    for a in order:
        m = mul3(axes[a](math.radians(r['xyz'.index(a)])), m)
    return m


def mul3(a, b):
    return [[sum(a[i][k]*b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


def trs(t, r, s):
    """A 4x4 matrix (column vectors) T * R(xyz) * S."""
    m = euler(r)
    return [[m[i][0]*s[0], m[i][1]*s[1], m[i][2]*s[2], t[i]] for i in range(3)] + [[0, 0, 0, 1]]


def mul4(a, b):
    return [[sum(a[i][k]*b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def translate(t, sign=1):
    return [[1, 0, 0, sign*t[0]], [0, 1, 0, sign*t[1]], [0, 0, 1, sign*t[2]], [0, 0, 0, 1]]


def rotate(r, order='xyz', inverse=False):
    m = euler(r, order)
    if inverse:
        m = [list(row) for row in zip(*m)]
    return [m[0] + [0], m[1] + [0], m[2] + [0], [0, 0, 0, 1]]


ORDERS = ['xyz', 'xzy', 'yzx', 'yxz', 'zxy', 'zyx']


def node_matrix(p):
    """The FBX node-to-parent matrix: T Roff Rp Rpre R Rpost^-1 Rp^-1 Soff Sp S Sp^-1."""
    zero = (0, 0, 0)
    active = p.get('RotationActive', 0)
    order = ORDERS[p.get('RotationOrder', 0)] if active else 'xyz'
    chain = [translate(p.get('Lcl Translation', zero)), translate(p.get('RotationOffset', zero)),
             translate(p.get('RotationPivot', zero)),
             rotate(p.get('PreRotation', zero) if active else zero),
             rotate(p.get('Lcl Rotation', zero), order),
             rotate(p.get('PostRotation', zero) if active else zero, inverse=True),
             translate(p.get('RotationPivot', zero), -1), translate(p.get('ScalingOffset', zero)),
             translate(p.get('ScalingPivot', zero)), trs(zero, zero, p.get('Lcl Scaling', (1, 1, 1))),
             translate(p.get('ScalingPivot', zero), -1)]
    m = chain[0]
    for step in chain[1:]:
        m = mul4(m, step)
    return m


def inverse(m):
    """The inverse of an affine matrix (Gauss-Jordan on the 3x3 part)."""
    a = [row[:3] + [1 if i == j else 0 for j in range(3)] for i, row in enumerate(m[:3])]
    for c in range(3):
        pivot = max(range(c, 3), key=lambda r: abs(a[r][c]))
        a[c], a[pivot] = a[pivot], a[c]
        a[c] = [x / a[c][c] for x in a[c]]
        for r in range(3):
            if r != c:
                a[r] = [x - a[r][c]*y for x, y in zip(a[r], a[c])]
    linear = [row[3:] for row in a]
    t = [-sum(linear[i][k]*m[k][3] for k in range(3)) for i in range(3)]
    return [linear[i] + [t[i]] for i in range(3)] + [[0, 0, 0, 1]]


def fbx_matrix(m):
    """FBX writes a matrix column by column (translation last)."""
    return [float(m[r][c]) for c in range(4) for r in range(4)]


TETRA = ((0, 0, 0), (2, 0, 0), (0, 3, 0), (0, 0, 4))
TETRA_FACES = ((0, 2, 1), (0, 1, 3), (0, 3, 2), (1, 2, 3))


def tetra_geometry(gid, name):
    """The probe's tetrahedron with flat per-corner normals (four unequal axis extents)."""
    corners, normals = [], []
    for face in TETRA_FACES:
        a, b, c = (TETRA[i] for i in face)
        u = [b[i]-a[i] for i in range(3)]
        v = [c[i]-a[i] for i in range(3)]
        n = [u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0]]
        length = math.sqrt(sum(x*x for x in n))
        normals += [x/length for x in n]*3
        corners += [face[0], face[1], -face[2]-1]
    return geometry(gid, name, [x for p in TETRA for x in p], corners, normals)


def array(name, values, indent=' '):
    """An FBX array property; the FBX SDK's ASCII reader expects the contents on their own line."""
    return f'{indent}{name}: *{len(values)} {{\n{indent} a: {",".join(map(str, values))}\n{indent}}}\n'


def geometry(gid, name, vertices, corners, normals=None):
    text = (f'Geometry: {gid}, "Geometry::{name}", "Mesh" {{\n' +
            array('Vertices', [float(v) for v in vertices]) +
            array('PolygonVertexIndex', corners) + ' GeometryVersion: 124\n')
    if normals:
        text += (' LayerElementNormal: 0 {\n  Version: 102\n  Name: ""\n'
                 '  MappingInformationType: "ByPolygonVertex"\n  ReferenceInformationType: "Direct"\n' +
                 array('Normals', [round(x, 9) for x in normals], '  ') + ' }\n'
                 ' Layer: 0 {\n  Version: 100\n  LayerElement: {\n   Type: "LayerElementNormal"\n'
                 '   TypedIndex: 0\n  }\n }\n')
    return text + '}\n'


PROPERTY_TYPES = {
    'Lcl Translation': ('Lcl Translation', '', 'A'), 'Lcl Rotation': ('Lcl Rotation', '', 'A'),
    'Lcl Scaling': ('Lcl Scaling', '', 'A'),
    'RotationActive': ('bool', '', ''), 'InheritType': ('enum', '', ''),
    'RotationOrder': ('enum', '', ''), 'DefaultAttributeIndex': ('int', 'Integer', ''),
}


def model(mid, name, kind, props):
    # Exporters write the attribute index of every node with an attribute (mesh, bone). The
    # NoIndex controls leave it out: the Editor then bakes no geometric transform or pivot
    # offset into the mesh, while the node itself still takes its pivots.
    if kind != 'Null' and not props.pop('no_index', False):
        props = dict(props, DefaultAttributeIndex=0)
    text = f'Model: {mid}, "Model::{name}", "{kind}" {{\n Version: 232\n Properties70: {{\n'
    for prop, value in props.items():
        kind_name, label, flags = PROPERTY_TYPES.get(prop, ('Vector3D', 'Vector', ''))
        if isinstance(value, tuple):
            text += f'  P: "{prop}", "{kind_name}", "{label}", "{flags}",' + ','.join(map(repr, map(float, value))) + '\n'
        else:
            text += f'  P: "{prop}", "{kind_name}", "{label}", "{flags}",{value}\n'
    return text + ' }\n Shading: T\n Culling: "CullingOff"\n}\n'


def document(objects, connections, up=1, unit=1.0, front_sign=1, animated=False, time_mode=6):
    axes = dict(UpAxis=up, UpAxisSign=1, FrontAxis=2 if up == 1 else 1,
                FrontAxisSign=front_sign, CoordAxis=0, CoordAxisSign=1)
    header = ('; FBX 7.4.0 project file\nFBXHeaderExtension: {\n FBXHeaderVersion: 1003\n'
              ' FBXVersion: 7400\n}\nGlobalSettings: {\n Version: 1000\n Properties70: {\n')
    header += ''.join(f'  P: "{k}", "int", "Integer", "",{v}\n' for k, v in axes.items())
    header += f'  P: "UnitScaleFactor", "double", "Number", "",{unit}\n'
    if animated:
        # 6: 30 frames per second, 3: 60.
        header += f'  P: "TimeMode", "enum", "", "",{time_mode}\n'
    header += ' }\n}\n'
    # (child, parent) object links, (child, parent, property) property links.
    links = ''.join(f' C: "OO",{c[0]},{c[1]}\n' if len(c) == 2 else f' C: "OP",{c[0]},{c[1]}, "{c[2]}"\n'
                    for c in connections)
    counts = {}
    for text in objects:
        kind = text.split(':', 1)[0]
        counts[kind] = counts.get(kind, 0) + 1
    definitions = (f'Definitions: {{\n Version: 100\n Count: {sum(counts.values())}\n' +
                   ''.join(f' ObjectType: "{k}" {{\n  Count: {n}\n }}\n' for k, n in counts.items()) + '}\n')
    return header + definitions + 'Objects: {\n' + ''.join(objects) + '}\nConnections: {\n' + links + '}\n'


# The plain transforms around the feature node. Body's own transform has rotation and
# nonuniform scale so every pivot and offset changes the result.
TOP = {'Lcl Translation': (1, 2, 3), 'Lcl Rotation': (0, 15, 0), 'Lcl Scaling': (1, 1, 1)}
BODY = {'Lcl Translation': (4, 5, 6), 'Lcl Rotation': (10, 20, 30), 'Lcl Scaling': (1.5, 2, 0.5)}
LEAF = {'Lcl Translation': (0.5, 1, -1), 'Lcl Rotation': (5, 10, 15), 'Lcl Scaling': (1, 1, 1)}

PIVOT = (1, 2, 3)
GEO_ALL = {'GeometricTranslation': (0.5, -1, 2), 'GeometricRotation': (20, 40, 60),
           'GeometricScaling': (2, 0.5, 1.5)}
MAYA = {'RotationPivot': PIVOT, 'ScalingPivot': PIVOT, 'RotationOffset': (0.3, 0.2, 0.1),
        'ScalingOffset': (-0.1, 0.4, 0.2)}

# name: (Top extra, Body extra, Leaf extra, options)
PIVOTS = {
    'Plain': ({}, {}, {}, {}),
    'Rp': ({}, {'RotationPivot': PIVOT}, {}, {}),
    'Sp': ({}, {'ScalingPivot': (-1, 0.5, 2)}, {}, {}),
    'RpSp': ({}, {'RotationPivot': PIVOT, 'ScalingPivot': PIVOT}, {}, {}),
    'RpSpDiff': ({}, {'RotationPivot': PIVOT, 'ScalingPivot': (-1, 0.5, 2)}, {}, {}),
    'Roff': ({}, {'RotationOffset': (0.5, -1, 2)}, {}, {}),
    'Soff': ({}, {'ScalingOffset': (-2, 1, 0.25)}, {}, {}),
    'Maya': ({}, MAYA, {}, {}),
    'Pre': ({}, {'RotationActive': 1, 'PreRotation': (30, 0, 45)}, {}, {}),
    'Post': ({}, {'RotationActive': 1, 'PostRotation': (0, 60, 15)}, {}, {}),
    'PrePost': ({}, {'RotationActive': 1, 'PreRotation': (30, 0, 45), 'PostRotation': (0, 60, 15)}, {}, {}),
    'PreInactive': ({}, {'RotationActive': 0, 'PreRotation': (30, 0, 45)}, {}, {}),
    'OrderZXY': ({}, {'RotationActive': 1, 'RotationOrder': 4}, {}, {}),
    'GeoT': ({}, {'GeometricTranslation': (0.5, -1, 2)}, {}, {}),
    'GeoR': ({}, {'GeometricRotation': (20, 40, 60)}, {}, {}),
    'GeoS': ({}, {'GeometricScaling': (2, 0.5, 1.5)}, {}, {}),
    'GeoAll': ({}, GEO_ALL, {}, {}),
    'GeoNull': ({}, GEO_ALL, {}, {'body_mesh': False}),
    'GeoShared': ({}, GEO_ALL, {}, {'shared': True}),
    'GeoSharedLeaf': ({}, {}, GEO_ALL, {'shared': True}),
    'PivotShared': ({}, MAYA, {}, {'shared': True}),
    'AnimPivot': ({}, dict(MAYA, RotationActive=1, PreRotation=(30, 0, 45)), {'RotationPivot': (0.5, 0, 1)},
                  {'animated': True, 'compression': 0}),
    'GeoNegS': ({}, {'GeometricScaling': (-1, 1.5, 1)}, {}, {}),
    'PivotNull': ({}, {'RotationPivot': PIVOT, 'ScalingPivot': PIVOT}, {}, {'body_mesh': False}),
    'GeoAllNoIndex': ({}, dict(GEO_ALL, no_index=True), {}, {}),
    'MayaNoIndex': ({}, dict(MAYA, no_index=True), {}, {}),
    'Inherit0': ({}, {}, {'InheritType': 0}, {}),
    'Inherit1': ({}, {}, {'InheritType': 1}, {}),
    'Inherit2': ({}, {}, {'InheritType': 2}, {}),
    'RootPivot': ({'RotationPivot': (-2, 1, 0.5), 'ScalingPivot': (-2, 1, 0.5),
                   'Lcl Scaling': (2, 1, 0.5)}, {}, {}, {}),
    'RootPivotH': ({'RotationPivot': (-2, 1, 0.5), 'ScalingPivot': (-2, 1, 0.5),
                    'Lcl Scaling': (2, 1, 0.5)}, {}, {}, {'preserve': 1}),
    'MayaCombo': ({}, dict(MAYA, RotationActive=1, PreRotation=(30, 0, 45), RotationOrder=2),
                  dict(GEO_ALL, InheritType=2), {}),
    'MayaComboBake': ({}, dict(MAYA, RotationActive=1, PreRotation=(30, 0, 45), RotationOrder=2),
                      dict(GEO_ALL, InheritType=2), {'bake': 1}),
    'GeoAllBake': ({}, GEO_ALL, {}, {'bake': 1}),
    'MaxZup': ({}, {'GeometricTranslation': (0.5, -1, 2), 'GeometricRotation': (0, 0, 90)},
               {'InheritType': 0}, {'up': 2, 'unit': 100}),
    'MaxZupBake': ({}, {'GeometricTranslation': (0.5, -1, 2), 'GeometricRotation': (0, 0, 90)},
                   {'InheritType': 0}, {'up': 2, 'unit': 100, 'bake': 1}),
}


# FBX time units per second.
KTIME = 46186158000


def animation(node, prop, curves, seconds=1):
    """A take of `seconds` animating `node`'s `prop` (`Lcl Rotation`, ...): per axis (0..2)
    linear keys spread evenly over the take. Object ids from 600."""
    stop = KTIME * seconds
    objects = ['AnimationStack: 600, "AnimStack::Take 001", "" {\n Properties70: {\n'
               '  P: "LocalStart", "KTime", "Time", "",0\n'
               f'  P: "LocalStop", "KTime", "Time", "",{stop}\n'
               '  P: "ReferenceStart", "KTime", "Time", "",0\n'
               f'  P: "ReferenceStop", "KTime", "Time", "",{stop}\n }}\n}}\n',
               'AnimationLayer: 601, "AnimLayer::BaseLayer", "" {\n}\n']
    links = [(601, 600)]
    for i, (node_id, prop_name, axes) in enumerate(zip(node, prop, curves)):
        curve_node = 602 + 10 * i
        defaults = ''.join(f'  P: "d|{a}", "Number", "", "A",{values[0]}\n'
                           for a, values in zip('XYZ', axes))
        objects.append(f'AnimationCurveNode: {curve_node}, "AnimCurveNode::{prop_name[4]}", "" {{\n'
                       f' Properties70: {{\n{defaults} }}\n}}\n')
        links += [(curve_node, 601), (curve_node, node_id, prop_name)]
        for k, (axis, values) in enumerate(zip('XYZ', axes)):
            curve = curve_node + 1 + k
            # Linear interpolation (KeyAttrFlags 4) for both keys.
            times = [stop * k // (len(values) - 1) for k in range(len(values))]
            objects.append(f'AnimationCurve: {curve}, "AnimCurve::", "" {{\n Default: {values[0]}\n'
                           ' KeyVer: 4009\n' + array('KeyTime', times) +
                           array('KeyValueFloat', [float(v) for v in values]) +
                           array('KeyAttrFlags', [4]) + array('KeyAttrDataFloat', [0, 0, 0, 0]) +
                           array('KeyAttrRefCount', [len(values)]) + '}\n')
            links.append((curve, curve_node, f'd|{axis}'))
    return objects, links


def pivots_fbx(top, body, leaf, options):
    objects = [tetra_geometry(100, 'Tetra')]
    shared = options.get('shared')
    if not shared:
        objects.append(tetra_geometry(101, 'LeafTetra'))
    body_kind = 'Mesh' if options.get('body_mesh', True) else 'Null'
    objects += [model(300, 'Top', 'Null', dict(TOP, **top)),
                model(301, 'Body', body_kind, dict(BODY, **body)),
                model(302, 'Leaf', 'Mesh', dict(LEAF, **leaf))]
    links = [(300, 0), (301, 300), (302, 301), (100 if shared else 101, 302)]
    if body_kind == 'Mesh':
        links.append((100, 301))
    if options.get('animated'):
        # Body turns and moves; Leaf turns under it.
        more, more_links = animation((301, 301, 302), ('Lcl Rotation', 'Lcl Translation', 'Lcl Rotation'),
                                     (((10, 40), (20, 20), (30, 120)), ((4, 6), (5, 5), (6, 4)),
                                      ((5, 5), (10, 70), (15, 15))))
        objects += more
        links += more_links
    return document(objects, links, up=options.get('up', 1), unit=options.get('unit', 1.0),
                    animated=options.get('animated', False))


# A three-bone column: Hips at the base, Spine and Head above (bone-local translations in Y).
BONES = [('Hips', None, (0, 1, 0), (0, 0, 10)), ('Spine', 'Hips', (0, 2, 0), (0, 0, -20)),
         ('Head', 'Spine', (0, 2, 0), (15, 0, 0))]
# The file's node pose of `Posed` differs from the bind pose (bone rotations only).
POSED = {'Hips': (0, 0, 25), 'Spine': (0, 30, -40), 'Head': (45, 0, 0)}


def column():
    """Three rings of four vertices from y=0 to y=4 with the sides as quads."""
    rings = [(0, 0.6), (2, 0.5), (4, 0.3)]
    vertices = []
    for y, r in rings:
        vertices += [r, y, r, r, y, -r, -r, y, -r, -r, y, r]
    corners = []
    for ring in range(2):
        for side in range(4):
            a, b = ring*4+side, ring*4+(side+1) % 4
            corners += [a, b, b+4, -(a+4)-1]
    corners += [0, 3, 2, -2]
    corners += [8, 9, 10, -12]
    weights = {'Hips': {0: 1, 1: 1, 2: 1, 3: 1, 4: 0.5, 5: 0.5, 6: 0.5, 7: 0.5},
               'Spine': {4: 0.5, 5: 0.5, 6: 0.5, 7: 0.5, 8: 0.3, 9: 0.3, 10: 0.3, 11: 0.3},
               'Head': {8: 0.7, 9: 0.7, 10: 0.7, 11: 0.7}}
    return vertices, corners, weights


SKINNED = {
    'Bound': {},
    'Posed': {'posed': True},
    'MeshAway': {'mesh': {'Lcl Translation': (1.5, 0.5, -2), 'Lcl Rotation': (0, 0, 20),
                          'Lcl Scaling': (1, 1.5, 1)}},
    'BonePivot': {'bone': {'RotationPivot': (0.2, 0.3, 0), 'ScalingPivot': (0.2, 0.3, 0)}},
    'BonePre': {'bone': {'RotationActive': 1, 'PreRotation': (0, 0, 15)}},
    'MeshPivot': {'mesh': {'Lcl Translation': (1.5, 0.5, -2), 'Lcl Rotation': (0, 0, 20),
                           'RotationPivot': (0.5, 1, 0), 'ScalingPivot': (0.5, 1, 0)}},
    'MeshGeo': {'mesh': {'Lcl Translation': (1.5, 0.5, -2),
                         'GeometricTranslation': (0.5, -1, 0.25), 'GeometricRotation': (0, 30, 0)}},
}


def skinned_fbx(options):
    vertices, corners, weights = column()
    for vertex in options.get('unweighted', ()):
        # A vertex no cluster lists.
        for bone in weights.values():
            bone.pop(vertex, None)
    objects = [geometry(100, 'Column', vertices, corners)]
    mesh_props = dict({'Lcl Translation': (0, 0, 0), 'Lcl Rotation': (0, 0, 0),
                       'Lcl Scaling': (1, 1, 1)}, **options.get('mesh', {}))
    objects.append(model(300, 'Column', 'Mesh', mesh_props))
    objects.append(model(301, 'Rig', 'Null', {'Lcl Translation': (0, 0, 0)}))
    if 'child' in options:
        # A plain mesh node under the skinned mesh's node.
        objects += [tetra_geometry(101, 'Tetra'), model(302, 'Leaf', 'Mesh', dict(LEAF, **options['child']))]
    # Cluster matrices as exporters write them: Transform is the mesh node's bind matrix in
    # the bone's bind space, TransformLink the bone's bind matrix in world space.
    mesh_world = node_matrix(mesh_props)
    links = [(301, 0), (300, 0), (100, 300), (400, 100)]
    if 'child' in options:
        links += [(302, 300), (101, 302)]
    objects.append('Deformer: 400, "Deformer::Skin", "Skin" {\n Version: 101\n Link_DeformAcuracy: 50\n}\n')
    worlds, pose, ids = {}, [], {}
    for i, (name, parent, t, r) in enumerate(BONES):
        props = {'Lcl Translation': t, 'Lcl Rotation': r, 'Lcl Scaling': (1, 1, 1)}
        if name == 'Spine':
            props.update(options.get('bone', {}))
        bind_local = node_matrix(props)
        worlds[name] = mul4(worlds[parent], bind_local) if parent else bind_local
        if options.get('posed'):
            props['Lcl Rotation'] = POSED[name]
        if name == 'Hips' and 'root_scale' in options:
            props['Lcl Scaling'] = options['root_scale']
        objects.append(model(310+i, name, 'LimbNode', props))
        objects.append(f'NodeAttribute: {320+i}, "NodeAttribute::{name}", "LimbNode" {{\n TypeFlags: "Skeleton"\n}}\n')
        links += [(320+i, 310+i), (310+i, ids[parent] if parent else 301)]
        ids[name] = 310+i
        indices = sorted(weights[name])
        objects.append(f'Deformer: {410+i}, "SubDeformer::{name}", "Cluster" {{\n Version: 100\n' +
                       array('Indexes', indices) +
                       array('Weights', [float(weights[name][k]) for k in indices]) +
                       array('Transform', fbx_matrix(mul4(inverse(worlds[name]), mesh_world))) +
                       array('TransformLink', fbx_matrix(worlds[name])) + '}\n')
        links += [(410+i, 400), (310+i, 410+i)]
        pose.append((310+i, worlds[name]))
    pose.append((300, mesh_world))
    objects.append('Pose: 500, "Pose::BindPose", "BindPose" {\n Type: "BindPose"\n Version: 100\n'
                   f' NbPoseNodes: {len(pose)}\n' +
                   ''.join(f' PoseNode:  {{\n  Node: {node}\n' + array('Matrix', fbx_matrix(m), '  ') + ' }\n'
                           for node, m in pose) + '}\n')
    if 'animated' in options:
        more, more_links = animation(*options['animated'], seconds=options.get('seconds', 1))
        objects += more
        links += more_links
    return document(objects, links, animated='animated' in options, time_mode=options.get('time_mode', 6))


# The Root bone scales up from zero: its first frame has no inverse.
VANISH = {'animated': ((310,), ('Lcl Scaling',), (((0, 1), (0, 1), (0, 1)),))}
# The Rigged controls: the column rig's imported localBounds under each animation type with
# clips that bend it (the Editor bounds Legacy models over sampled clip frames), a degenerate
# root bone, a vertex without weights, and pivots, offsets and a child on the skinned mesh's
# node; a Humanoid rig with its clips (HumanoidSkin).
# Hips moves, Spine and Head bend (linear keys at 0 s and 1 s).
SWING = {'animated': ((310, 311, 312), ('Lcl Translation', 'Lcl Rotation', 'Lcl Rotation'),
                      (((0, 0.8), (1, 1), (0, 0.3)), ((0, 0), (0, 0), (-20, 70)), ((15, 100), (0, 0), (0, 0))))}
# Only the root bone moves and turns: the bounds are in its space.
ROOT_ONLY = {'animated': ((310, 310), ('Lcl Translation', 'Lcl Rotation'),
                          (((0, 0.8), (1, 1.5), (0, 0.3)), ((0, 40), (0, 0), (10, -30))))}
MESH_TRS = {'Lcl Translation': (1.5, 0.5, -2), 'Lcl Rotation': (10, 0, 20), 'Lcl Scaling': (1, 1.5, 0.8)}
RIGGED = {
    'LegacySwing': (SWING, dict(animationType=1, animationCompression=0)),
    'LegacySwing60': (dict(SWING, time_mode=3), dict(animationType=1, animationCompression=0)),
    'LegacyRootOnly': (ROOT_ONLY, dict(animationType=1, animationCompression=0)),
    'GenericSwing': (SWING, dict(animationType=2, avatarSetup=0, animationCompression=0)),
    'NoneSwing': (SWING, dict(animationType=0)),
    'GenericZeroRoot': ({'root_scale': (0, 0, 0)}, dict(animationType=2, avatarSetup=0)),
    'LegacyRootVanish': (VANISH, dict(animationType=1, animationCompression=0)),
    'LegacyUnweighted': (dict(SWING, unweighted=(11,)), dict(animationType=1, animationCompression=0)),
    'GenericUnweighted': (dict(SWING, unweighted=(11,)), dict(animationType=2, avatarSetup=0, animationCompression=0)),
    'NoneMeshRp': ({'mesh': dict(MESH_TRS, RotationPivot=(0.5, 1, 0.25))}, dict(animationType=0)),
    'MeshRp': ({'mesh': dict(MESH_TRS, RotationPivot=(0.5, 1, 0.25))}, {}),
    'MeshSp': ({'mesh': dict(MESH_TRS, ScalingPivot=(-0.5, 0.75, 0.5))}, {}),
    'MeshRpSp': ({'mesh': dict(MESH_TRS, RotationPivot=(0.5, 1, 0.25), ScalingPivot=(-0.5, 0.75, 0.5))}, {}),
    'MeshRoff': ({'mesh': dict(MESH_TRS, RotationOffset=(0.3, -0.2, 0.1))}, {}),
    'MeshSoff': ({'mesh': dict(MESH_TRS, ScalingOffset=(-0.1, 0.4, 0.2))}, {}),
    'MeshMaya': ({'mesh': dict(MESH_TRS, **MAYA)}, {}),
    'MeshChild': ({'mesh': dict(MESH_TRS, RotationPivot=PIVOT, ScalingPivot=PIVOT), 'child': {}}, {}),
    'MeshChildMaya': ({'mesh': dict(MESH_TRS, **MAYA), 'child': {'RotationPivot': (0.5, 0, 1)}}, {}),
    'MeshChildPlain': ({'mesh': MESH_TRS, 'child': {}}, {}),
}


# The Reduced controls: imported clips under the reducing animation compressions, with
# several keys per curve so that reduction keeps corners and drops straight runs.
BEND = {'animated': ((310, 311, 311, 312),
                     ('Lcl Translation', 'Lcl Rotation', 'Lcl Scaling', 'Lcl Rotation'),
                     (((0, 0.8, 0.2, 0.2), (1, 1.2, 1, 1.1), (0, 0.3, -0.4, 0)),
                      ((0, 0, 20, 20), (0, 30, -15, 0), (-20, 70, 10, 40)),
                      ((1, 1.5, 0.8, 1), (1, 1, 1.3, 1), (1, 1, 1, 1)),
                      ((15, 100, 40, 40), (0, 0, 0, 25), (0, 10, 0, 0))))}
# Three seconds of slow straight motion: reduced segments reach the 50-frame limit.
DRIFT = {'animated': ((310, 311), ('Lcl Translation', 'Lcl Rotation'),
                      (((0, 0.9), (1, 1.2), (0, -0.3)), ((0, 10), (0, 0), (-20, 40)))),
         'seconds': 3}
# One second through more than half a turn about two axes.
TURN = {'animated': ((310, 311), ('Lcl Translation', 'Lcl Rotation'),
                     (((0, 0.9), (1, 1.2), (0, -0.3)), ((0, 40), (0, 250), (-20, 300))))}
LEGACY_REDUCED = dict(animationType=1, animationCompression=1)
LEGACY_PACKED = dict(animationType=1, animationCompression=2)
GENERIC = dict(animationType=2, avatarSetup=0)
REDUCED = {
    'Default': (BEND, LEGACY_REDUCED),
    'Loose': (BEND, dict(LEGACY_REDUCED, animationRotationError=3, animationPositionError=2,
                         animationScaleError=2)),
    # Errors well above single precision (a 0.05 degree threshold is within a few ulps of a
    # unit dot product, where keep or drop is a rounding tie).
    'Tight': (BEND, dict(LEGACY_REDUCED, animationRotationError=0.2, animationPositionError=0.1,
                         animationScaleError=0.1)),
    'Rate60': (dict(BEND, time_mode=3), LEGACY_REDUCED),
    'Split': (BEND, dict(LEGACY_REDUCED, animationPositionError=3, animationScaleError=0.1)),
    'Drift': (DRIFT, LEGACY_REDUCED),
    'GenericReduced': (BEND, dict(GENERIC, animationCompression=1)),
    'GenericOptimal': (BEND, dict(GENERIC, animationCompression=3)),
    # Legacy under Keyframe Reduction and Compression: the rotation curves are stored packed.
    'Packed': (BEND, LEGACY_PACKED),
    'PackedLoose': (BEND, dict(LEGACY_PACKED, animationRotationError=3, animationPositionError=2,
                               animationScaleError=2)),
    'PackedRate60': (dict(BEND, time_mode=3), LEGACY_PACKED),
    # 120 frames a second: frames closer than the hundredth of a second key times are stored in.
    # The scale error stays off 0.5%, where dropping the scale corner at 1/3 s is a tie (README).
    'PackedRate120': (dict(BEND, time_mode=1), dict(LEGACY_PACKED, animationScaleError=1)),
    'PackedDrift': (DRIFT, LEGACY_PACKED),
    # Past half a turn: the rotation's largest component changes and turns negative.
    'PackedTurn': (TURN, LEGACY_PACKED),
    # Legacy with the setting the other rig types call Optimal.
    'LegacyOptimal': (BEND, dict(LEGACY_REDUCED, animationCompression=3)),
}


def write_meta(path, name, overrides):
    settings = dict(uv.DEFAULTS, **uv.RAW, normalImportMode=0)
    meta = uv.META.format(guid=axis.guid(name), **settings)
    for key, value in overrides.items():
        meta, count = re.subn(r'(?m)^(  (?:  )?' + key + r': ).*', lambda m: m[1] + str(value), meta)
        assert count == 1, key
    Path(str(path) + '.meta').write_text(meta)


def main():
    dest = ROOT/'Assets/Resources/Pivots'
    dest.mkdir(parents=True, exist_ok=True)
    for name, (top, body, leaf, options) in PIVOTS.items():
        path = dest/(name + '.fbx')
        path.write_text(pivots_fbx(top, body, leaf, options))
        settings = dict(bakeAxisConversion=options.get('bake', 0),
                        preserveHierarchy=options.get('preserve', 0))
        if options.get('animated'):
            # Legacy clips, sampled by the probe.
            settings.update(animationType=1, importAnimation=1,
                            animationCompression=options['compression'])
        write_meta(path, 'Pivots/' + name, settings)
    # Blender's output (blender_models.py) with the same settings.
    for name in ('BlenderTree', 'BlenderApply'):
        write_meta(dest/(name + '.fbx'), 'Pivots/' + name, {})
    dest = ROOT/'Assets/Resources/Skinned'
    dest.mkdir(parents=True, exist_ok=True)
    generic = dict(animationType=2, avatarSetup=0, normalImportMode=2)
    for name, options in SKINNED.items():
        path = dest/(name + '.fbx')
        path.write_text(skinned_fbx(options))
        write_meta(path, 'Skinned/' + name, generic)
    write_meta(dest/'BlenderRig.fbx', 'Skinned/BlenderRig', generic)
    # The Blender-exported skinned models of the other model probes, with their own settings.
    for source, name in ((PROJECTS/'modelimport/Assets/Models/Skinned.fbx', 'BlenderBone'),
                         (PROJECTS/'blendshapes/Assets/Resources/Blob.fbx', 'BlenderBlob')):
        shutil.copyfile(source, dest/(name + '.fbx'))
        meta = Path(str(source) + '.meta').read_text()
        meta = re.sub(r'(?m)^guid: .*', 'guid: ' + axis.guid('Skinned/' + name), meta)
        # Readable, unoptimized vertex data so the probe can compare vertices and bounds.
        meta = re.sub(r'(?m)^(    isReadable: ).*', r'\g<1>1', meta)
        meta = re.sub(r'(?m)^(    meshOptimizationFlags: ).*', r'\g<1>0', meta)
        Path(str(dest/(name + '.fbx')) + '.meta').write_text(meta)
    dest = ROOT/'Assets/Resources/Rigged'
    dest.mkdir(parents=True, exist_ok=True)
    for name, (options, settings) in RIGGED.items():
        path = dest/(name + '.fbx')
        path.write_text(skinned_fbx(options))
        write_meta(path, 'Rigged/' + name, dict(dict(animationType=2, avatarSetup=0, normalImportMode=2,
                                                     importAnimation=1), **settings))
    # HumanoidSkin.fbx (blender_models.py): the humanoid probe's HumanA with boxes skinned to its
    # bones, with HumanA's settings and bone mapping; the skeleton (T-pose) comes from the file.
    meta = Path(str(PROJECTS/'humanoid/Assets/Resources/HumanA.fbx') + '.meta').read_text()
    meta = re.sub(r'(?m)^guid: .*', 'guid: ' + axis.guid('Rigged/HumanoidSkin'), meta)
    meta = re.sub(r'(?m)^(    isReadable: ).*', r'\g<1>1', meta)
    meta = re.sub(r'(?m)^(    meshOptimizationFlags: ).*', r'\g<1>0', meta)
    meta = re.sub(r'(?ms)^    skeleton:\n.*?^    armTwist:', '    skeleton: []\n    armTwist:', meta)
    Path(str(dest/'HumanoidSkin.fbx') + '.meta').write_text(meta)
    dest = ROOT/'Assets/Resources/Reduced'
    dest.mkdir(parents=True, exist_ok=True)
    for name, (options, settings) in REDUCED.items():
        path = dest/(name + '.fbx')
        path.write_text(skinned_fbx(options))
        write_meta(path, 'Reduced/' + name, dict(dict(normalImportMode=2, importAnimation=1), **settings))
    # The pivoted legacy clip of the Pivots set, reduced.
    top, body, leaf, options = PIVOTS['AnimPivot']
    (dest/'AnimPivot.fbx').write_text(pivots_fbx(top, body, leaf, options))
    write_meta(dest/'AnimPivot.fbx', 'Reduced/AnimPivot', dict(LEGACY_REDUCED, importAnimation=1))
    axis.write_missing_metas()


if __name__ == '__main__':
    main()
