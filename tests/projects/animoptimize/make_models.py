"""Writes the probe's ASCII FBX 7.4 rig into Assets/Resources/Models under every variant's name.

    python3 tests/projects/animoptimize/make_models.py

One file, copied once per import variant (the variants differ only in their `.meta`, which
`run.py create` writes): a top-level `Rig` null holding a bone chain `Hips/Spine/Hand` and
`Hips/Leg`, a rigid `Prop` mesh under `Hand`, and a top-level `Body` mesh skinned to the four
bones; `Turned*` copies hold both under a single top-level null turned -90° about X and scaled
by 2, which becomes the model's root. The take "Take 001" (one second) moves `Hips` along X, turns `Spine` about Z and raises
`Hand`, so every bone's pose changes over the take. Y up, metres (UnitScaleFactor 100), so the
importer keeps positions as written. Bind pose = rest pose.
"""
import math
import os

PROBE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(PROBE, 'Assets', 'Resources', 'Models')
KTIME = 46186158000  # FBX time units per second
VARIANTS = ('Rig', 'RigOpt', 'RigExposed', 'RigExposedRoot')
# The same rig and body under a single top-level null turned and scaled: that null is the root.
TURNED_VARIANTS = ('TurnedOpt',)


def euler_xyz(rx, ry, rz):
    """FBX's default rotation order: X, then Y, then Z (R = Rz * Ry * Rx), as a 3x3 row list."""
    cx, sx = math.cos(math.radians(rx)), math.sin(math.radians(rx))
    cy, sy = math.cos(math.radians(ry)), math.sin(math.radians(ry))
    cz, sz = math.cos(math.radians(rz)), math.sin(math.radians(rz))
    x = [[1, 0, 0], [0, cx, -sx], [0, sx, cx]]
    y = [[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]]
    z = [[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]]
    return mul3(z, mul3(y, x))


def mul3(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]


def affine(translation, rotation):
    """A 4x4 column-vector matrix (rows) from a translation and XYZ Euler angles."""
    r = euler_xyz(*rotation)
    return [r[0] + [translation[0]], r[1] + [translation[1]], r[2] + [translation[2]], [0, 0, 0, 1]]


def mul4(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def inverse_affine(m):
    a = [row[:3] for row in m[:3]]
    det = (a[0][0] * (a[1][1] * a[2][2] - a[1][2] * a[2][1]) - a[0][1] * (a[1][0] * a[2][2] - a[1][2] * a[2][0])
           + a[0][2] * (a[1][0] * a[2][1] - a[1][1] * a[2][0]))
    r = [[(a[(j + 1) % 3][(i + 1) % 3] * a[(j + 2) % 3][(i + 2) % 3] - a[(j + 1) % 3][(i + 2) % 3] * a[(j + 2) % 3][(i + 1) % 3]) / det
          for j in range(3)] for i in range(3)]
    t = [-sum(r[i][k] * m[k][3] for k in range(3)) for i in range(3)]
    return [r[0] + [t[0]], r[1] + [t[1]], r[2] + [t[2]], [0, 0, 0, 1]]


def fbx_matrix(m):
    """FBX stores matrices column-major (the translation is the last four values)."""
    return ','.join(repr(float(m[r][c])) for c in range(4) for r in range(4))


def model_file(top=None):
    ids = iter(range(1000, 100000))
    objects, conns = [], []
    counts = {}
    globals_ = {}

    def count(kind):
        counts[kind] = counts.get(kind, 0) + 1

    def node(name, parent, translation, rotation, kind, attr=None):
        mid = next(ids)
        objects.append(f'''	Model: {mid}, "Model::{name}", "{kind}" {{
		Version: 232
		Properties70:  {{
			P: "Lcl Translation", "Lcl Translation", "", "A",{translation[0]},{translation[1]},{translation[2]}
			P: "Lcl Rotation", "Lcl Rotation", "", "A",{rotation[0]},{rotation[1]},{rotation[2]}
			P: "Lcl Scaling", "Lcl Scaling", "", "A",1,1,1
			P: "DefaultAttributeIndex", "int", "Integer", "",0
		}}
		Shading: Y
		Culling: "CullingOff"
	}}''')
        count('Model')
        conns.append(f'	C: "OO",{mid},{parent}')
        parent_global = globals_.get(parent, affine((0, 0, 0), (0, 0, 0)))
        globals_[mid] = mul4(parent_global, affine(translation, rotation))
        if attr is not None:
            aid = next(ids)
            objects.append(f'''	NodeAttribute: {aid}, "NodeAttribute::{name}", "{kind}" {{
		TypeFlags: "{attr}"
	}}''')
            count('NodeAttribute')
            conns.append(f'	C: "OO",{aid},{mid}')
        return mid

    def geometry(name, vertices, polygons):
        gid = next(ids)
        flat = ','.join(repr(float(c)) for v in vertices for c in v)
        index = []
        for poly in polygons:
            index += list(poly[:-1]) + [-poly[-1] - 1]
        objects.append(f'''	Geometry: {gid}, "Geometry::{name}", "Mesh" {{
		Vertices: *{len(vertices) * 3} {{
			a: {flat}
		}}
		PolygonVertexIndex: *{len(index)} {{
			a: {','.join(map(str, index))}
		}}
		GeometryVersion: 124
	}}''')
        count('Geometry')
        return gid

    holder = 0
    if top is not None:
        holder = node('Top', 0, (0, 0, 0), top, 'Null', 'Null')
        objects[-2] = objects[-2].replace('"Lcl Scaling", "", "A",1,1,1', '"Lcl Scaling", "", "A",2,2,2')
        globals_[holder] = mul4(globals_[holder], [[2, 0, 0, 0], [0, 2, 0, 0], [0, 0, 2, 0], [0, 0, 0, 1]])
    rig = node('Rig', holder, (0, 0, 0), (0, 0, 0), 'Null', 'Null')
    hips = node('Hips', rig, (0, 1, 0), (0, 0, 10), 'LimbNode', 'Skeleton')
    spine = node('Spine', hips, (0, 2, 0), (0, 20, -30), 'LimbNode', 'Skeleton')
    hand = node('Hand', spine, (0, 2, 0), (40, 0, 0), 'LimbNode', 'Skeleton')
    leg = node('Leg', hips, (0.5, -1, 0), (0, 0, 15), 'LimbNode', 'Skeleton')
    prop = node('Prop', hand, (0.5, 0, 0), (0, 0, 0), 'Mesh')
    conns.append(f'	C: "OO",{geometry("Prop", [(0, 0, 0), (0.2, 0, 0), (0.2, 0.2, 0), (0, 0.2, 0)], [(0, 1, 2, 3)])},{prop}')
    body = node('Body', holder, (0, 0, 0), (0, 0, 0), 'Mesh')

    # Three rings of four vertices (at the hips, the spine and the hand) and a foot ring.
    rings = [(0.0, 0.5), (1.0, 0.6), (3.0, 0.5), (5.0, 0.3)]
    vertices = [(s * x, y, s * z) for y, s in rings for x, z in ((1, 1), (1, -1), (-1, -1), (-1, 1))]
    polygons = []
    for r in range(len(rings) - 1):
        a, b = r * 4, r * 4 + 4
        for i in range(4):
            j = (i + 1) % 4
            polygons.append((a + i, a + j, b + j, b + i))
    polygons.append((0, 3, 2, 1))
    polygons.append((12, 13, 14, 15))
    geo = geometry('Body', vertices, polygons)
    conns.append(f'	C: "OO",{geo},{body}')
    skin = next(ids)
    objects.append(f'	Deformer: {skin}, "Deformer::Skin", "Skin" {{\n\t\tVersion: 101\n\t\tLink_DeformAcuracy: 50\n\t}}')
    count('Deformer')
    conns.append(f'	C: "OO",{skin},{geo}')
    weights = {
        leg: {0: 1.0, 1: 1.0, 2: 0.6, 3: 0.6},
        hips: {2: 0.4, 3: 0.4, 4: 0.7, 5: 0.7, 6: 0.7, 7: 0.7},
        spine: {4: 0.3, 5: 0.3, 6: 0.3, 7: 0.3, 8: 0.6, 9: 0.6, 10: 0.6, 11: 0.6, 12: 0.2, 13: 0.2, 14: 0.2, 15: 0.2},
        hand: {8: 0.4, 9: 0.4, 10: 0.4, 11: 0.4, 12: 0.8, 13: 0.8, 14: 0.8, 15: 0.8},
    }
    names = {leg: 'Leg', hips: 'Hips', spine: 'Spine', hand: 'Hand'}
    mesh_global = globals_[body]
    for bone in (hips, spine, hand, leg):
        cluster = next(ids)
        indexes = sorted(weights[bone])
        link = globals_[bone]
        objects.append(f'''	Deformer: {cluster}, "SubDeformer::{names[bone]}", "Cluster" {{
		Version: 100
		Indexes: *{len(indexes)} {{
			a: {','.join(map(str, indexes))}
		}}
		Weights: *{len(indexes)} {{
			a: {','.join(repr(weights[bone][i]) for i in indexes)}
		}}
		Transform: *16 {{
			a: {fbx_matrix(mul4(inverse_affine(link), mesh_global))}
		}}
		TransformLink: *16 {{
			a: {fbx_matrix(link)}
		}}
	}}''')
        count('Deformer')
        conns.append(f'	C: "OO",{cluster},{skin}')
        conns.append(f'	C: "OO",{bone},{cluster}')
    pose = next(ids)
    pose_nodes = '\n'.join(f'''		PoseNode:  {{
			Node: {n}
			Matrix: *16 {{
				a: {fbx_matrix(globals_[n])}
			}}
		}}''' for n in (hips, spine, hand, leg, body))
    objects.append(f'''	Pose: {pose}, "Pose::BindPose", "BindPose" {{
		Type: "BindPose"
		Version: 100
		NbPoseNodes: 5
{pose_nodes}
	}}''')
    count('Pose')

    stack, layer = next(ids), next(ids)
    objects.append(f'''	AnimationStack: {stack}, "AnimStack::Take 001", "" {{
		Properties70:  {{
			P: "LocalStart", "KTime", "Time", "",0
			P: "LocalStop", "KTime", "Time", "",{KTIME}
			P: "ReferenceStart", "KTime", "Time", "",0
			P: "ReferenceStop", "KTime", "Time", "",{KTIME}
		}}
	}}''')
    objects.append(f'	AnimationLayer: {layer}, "AnimLayer::BaseLayer", "" {{\n\t}}')
    count('AnimationStack')
    count('AnimationLayer')
    conns.append(f'	C: "OO",{layer},{stack}')

    def curve(value_a, value_b):
        cid = next(ids)
        objects.append(f'''	AnimationCurve: {cid}, "AnimCurve::", "" {{
		Default: {value_a}
		KeyVer: 4009
		KeyTime: *2 {{
			a: 0,{KTIME}
		}}
		KeyValueFloat: *2 {{
			a: {value_a},{value_b}
		}}
		KeyAttrFlags: *1 {{
			a: 8456
		}}
		KeyAttrDataFloat: *4 {{
			a: 0,0,218434821,0
		}}
		KeyAttrRefCount: *1 {{
			a: 2
		}}
	}}''')
        count('AnimationCurve')
        return cid

    def animate(mid, prop, a, b):
        cn = next(ids)
        values = '\n'.join(f'\t\t\tP: "d|{c}", "Number", "", "A",{v}' for c, v in zip('XYZ', a))
        objects.append(f'''	AnimationCurveNode: {cn}, "AnimCurveNode::{prop}", "" {{
		Properties70:  {{
{values}
		}}
	}}''')
        count('AnimationCurveNode')
        conns.append(f'	C: "OO",{cn},{layer}')
        conns.append(f'	C: "OP",{cn},{mid}, "{prop}"')
        for channel, va, vb in zip('XYZ', a, b):
            conns.append(f'	C: "OP",{curve(va, vb)},{cn}, "d|{channel}"')

    animate(hips, 'Lcl Translation', (0, 1, 0), (1, 1, 0))
    animate(spine, 'Lcl Rotation', (0, 20, -30), (0, 20, 30))
    animate(hand, 'Lcl Translation', (0, 2, 0), (0, 3, 0.5))

    definitions = '\n'.join(f'\tObjectType: "{k}" {{\n\t\tCount: {v}\n\t}}' for k, v in counts.items())
    return f'''; FBX 7.4.0 project file
FBXHeaderExtension:  {{
	FBXHeaderVersion: 1003
	FBXVersion: 7400
	Creator: "Unbound animoptimize probe"
}}
GlobalSettings:  {{
	Version: 1000
	Properties70:  {{
		P: "UpAxis", "int", "Integer", "",1
		P: "UpAxisSign", "int", "Integer", "",1
		P: "FrontAxis", "int", "Integer", "",2
		P: "FrontAxisSign", "int", "Integer", "",1
		P: "CoordAxis", "int", "Integer", "",0
		P: "CoordAxisSign", "int", "Integer", "",1
		P: "OriginalUpAxis", "int", "Integer", "",1
		P: "OriginalUpAxisSign", "int", "Integer", "",1
		P: "UnitScaleFactor", "double", "Number", "",100
		P: "OriginalUnitScaleFactor", "double", "Number", "",100
		P: "TimeMode", "enum", "", "",6
		P: "TimeSpanStart", "KTime", "Time", "",0
		P: "TimeSpanStop", "KTime", "Time", "",{KTIME}
		P: "CustomFrameRate", "double", "Number", "",-1
	}}
}}
Definitions:  {{
	Version: 100
	Count: {sum(counts.values())}
{definitions}
}}
Objects:  {{
{chr(10).join(objects)}
}}
Connections:  {{
{chr(10).join(conns)}
}}
Takes:  {{
	Current: "Take 001"
	Take: "Take 001" {{
		FileName: "Take_001.tak"
		LocalTime: 0,{KTIME}
		ReferenceTime: 0,{KTIME}
	}}
}}
'''


def main():
    os.makedirs(OUT, exist_ok=True)
    for names, text in ((VARIANTS, model_file()), (TURNED_VARIANTS, model_file(top=(-90, 0, 0)))):
        for name in names:
            with open(os.path.join(OUT, name + '.fbx'), 'w', newline='\n') as f:
                f.write(text)
    print('wrote', len(VARIANTS) + len(TURNED_VARIANTS), 'models to', OUT)


if __name__ == '__main__':
    main()
