"""Writes imperfect but ordinary models into Sources/Normals (Blender 4+ in the background):

    blender -b --factory-startup --python tests/projects/lightmapuvs/Tools/make_imperfect_models.py -- tests/projects/lightmapuvs/Sources/Normals

Every degenerate face is attached to a regular quad, as in exported real content.

FBX: Bridge (a square with a square hole, bridged into one 10-gon whose bridge corners repeat
positions as separate vertices), Pinch (an hourglass 6-gon whose waist corners coincide, and a
7-gon with a zero-length edge), FlatPoly (a pentagon whose corners lie on one line), FlatTri
(a triangle of three collinear corners), FlatQuad (a quad of four collinear corners), WeldTri (a
triangle with two corners at one position), WeldQuad (a quad with two adjacent corners at one
position), WeldDiag (a quad whose second and fourth corners share a position), Cross (a
self-intersecting pentagram), Slit (a smooth waved grid with sharp edges that end inside the
surface, as edge smoothing), SlitGroups (the same as smoothing groups), Seam (a smooth cylinder
with sharp caps and one sharp side edge, as edge smoothing). OBJ: BridgeObj (the bridged 10-gon
reusing its bridge vertices).
"""
import bpy, math, os, sys

out = sys.argv[sys.argv.index('--') + 1]
os.makedirs(out, exist_ok=True)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def export_fbx(name, smooth='OFF'):
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, name + '.fbx'), object_types={'MESH'},
                             use_selection=False, bake_anim=False, add_leaf_bones=False,
                             mesh_smooth_type=smooth)


def from_polygons(name, verts, faces, smooth=True):
    reset()
    mesh = bpy.data.meshes.new(name + 'Mesh')
    mesh.from_pydata(verts, [], faces)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    layer = mesh.uv_layers.new(name='UVMap')
    # A vertex repeating an earlier vertex's position gets its own U (a UV seam), so the
    # reference shows which corner a merged or welded position keeps.
    repeats = [sum(1 for w in verts[:i] if tuple(w) == tuple(v)) for i, v in enumerate(verts)]
    for loop in mesh.loops:
        co = mesh.vertices[loop.vertex_index].co
        layer.data[loop.index].uv = (co.x * 0.3 + 0.5 + 0.05 * repeats[loop.vertex_index],
                                     co.y * 0.3 + co.z * 0.1 + 0.5)
    for poly in mesh.polygons:
        poly.use_smooth = smooth
    return obj


def with_quad(verts, faces):
    """A regular quad below y = 1 whose top edge the degenerate faces share."""
    base = [(0, 0, 0), (1, 0, 0.1), (1, 1, 0), (0, 1, 0)]
    shift = len(base)
    moved = [[base_index(i, shift) for i in face] for face in faces]
    return base + verts, [[0, 1, 2, 3]] + moved


def base_index(i, shift):
    # Negative indices name the quad's corners: -1 is (1, 1), -2 is (0, 1).
    return {-1: 2, -2: 3}.get(i, i + shift if i >= 0 else i)


def model(name, verts, faces, smooth='OFF'):
    from_polygons(name, *with_quad(verts, faces))
    export_fbx(name, smooth)


# The hole's corners repeat at the bridge: outer 0..3, bridge back to outer 0, inner 0..3, back.
outer = [(0, 0, 0), (2, 0, 0), (2, 2, 0), (0, 2, 0)]
inner = [(0.5, 0.5, 0), (0.5, 1.5, 0), (1.5, 1.5, 0), (1.5, 0.5, 0)]
from_polygons('Bridge', outer + [outer[0]] + inner + [inner[0]], [list(range(10))])
export_fbx('Bridge')

with open(os.path.join(out, 'BridgeObj.obj'), 'w') as f:
    f.write('# A square with a square hole, bridged into one polygon that reuses two vertices.\n')
    f.write('o BridgeObj\n')
    for x, y, z in outer + inner:
        f.write(f'v {x} {y} {z}\n')
    # The repeated corners have their own texture coordinates (9 and 10), as a UV seam.
    for k, (x, y, z) in enumerate(outer + inner + [outer[0], inner[0]]):
        f.write(f'vt {x * 0.3 + 0.5 + (0.05 if k >= 8 else 0)} {y * 0.3 + 0.5}\n')
    f.write('vn 0 0 1\ns off\n')
    face = [(1, 1), (2, 2), (3, 3), (4, 4), (1, 9), (5, 5), (6, 6), (7, 7), (8, 8), (5, 10)]
    f.write('f ' + ' '.join(f'{v}/{t}/1' for v, t in face) + '\n')

# Waist corners 2 and 5 coincide (separate vertices); the 7-gon's corners 2 and 3 coincide.
model('Pinch', [(0.5, 1.5, 0), (1.0, 2.0, 0), (0.0, 2.0, 0), (0.5, 1.5, 0),
                (1.5, 1.0, 0), (2.0, 1.0, 0), (2.0, 1.0, 0), (2.4, 1.5, 0), (2.0, 2.0, 0), (1.5, 1.8, 0)],
      [[-2, -1, 0, 1, 2, 3], [-1, 4, 5, 6, 7, 8, 9]])
model('FlatPoly', [(0.25, 1, 0), (0.5, 1, 0), (0.75, 1, 0)], [[-2, 0, 1, 2, -1]])
model('FlatTri', [(0.5, 1, 0)], [[-2, 0, -1]])
model('FlatQuad', [(0.3, 1, 0), (0.6, 1, 0)], [[-2, 0, 1, -1]])
model('WeldTri', [(1, 1, 0)], [[-2, -1, 0]])
model('WeldQuad', [(0.5, 2, 0), (0.5, 2, 0)], [[-2, -1, 0, 1]])
model('WeldDiag', [(1, 2, 0), (1, 1, 0)], [[-2, -1, 0, 1]])
# A pentagram's edges cross each other.
star = [(0.5 + 0.5 * math.sin(4 * math.pi * k / 5), 1.5 + 0.5 * math.cos(4 * math.pi * k / 5), 0)
        for k in range(5)]
model('Cross', star, [[0, 1, 2, 3, 4]])


def slit():
    n = 6
    verts = [(x * 0.4, y * 0.4, 0.15 * math.sin(x * 1.1) * math.cos(y * 0.9))
             for y in range(n + 1) for x in range(n + 1)]
    faces = [[y * (n + 1) + x, y * (n + 1) + x + 1, (y + 1) * (n + 1) + x + 1, (y + 1) * (n + 1) + x]
             for y in range(n) for x in range(n)]
    obj = from_polygons('Slit', verts, faces)
    # Interior chain (2,3)-(3,3)-(4,3) and a chain from the border (0,1)-(1,1)-(2,1).
    sharp = {((2, 3), (3, 3)), ((3, 3), (4, 3)), ((0, 1), (1, 1)), ((1, 1), (2, 1))}
    index = lambda p: p[1] * (n + 1) + p[0]
    sharp = {frozenset((index(a), index(b))) for a, b in sharp}
    for edge in obj.data.edges:
        edge.use_edge_sharp = frozenset(edge.vertices) in sharp
    return obj


slit()
export_fbx('Slit', 'EDGE')
slit().name = 'SlitGroups'
export_fbx('SlitGroups', 'SMOOTH_GROUP')

reset()
bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.6, depth=1.5)
obj = bpy.context.active_object
obj.name, obj.data.name = 'Seam', 'SeamMesh'
bpy.ops.object.shade_smooth()
for edge in obj.data.edges:
    a, b = (obj.data.vertices[i].co for i in edge.vertices)
    cap = abs(a.z - b.z) < 1e-6 and abs(abs(a.z) - 0.75) < 1e-6
    vertical = abs(a.x - b.x) < 1e-6 and abs(a.y - b.y) < 1e-6 and a.x > 0.59
    edge.use_edge_sharp = cap or vertical
export_fbx('Seam', 'EDGE')
print('WROTE', out)
