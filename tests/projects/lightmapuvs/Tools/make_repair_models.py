"""Writes models that the lightmap unwrap has to repair into Sources/Repair (Blender 4+ in the
background):

    blender -b --factory-startup --python tests/projects/lightmapuvs/Tools/make_repair_models.py -- tests/projects/lightmapuvs/Sources/Repair

All FBX, UV0 a planar projection of the positions:

Fin (a quad with two more quads on its top edge: an edge of three faces), BowTie (two closed
square pyramids touching at their apexes: a vertex of two fans), Doubled (a box whose top face is
listed twice the same way round: edges used twice the same way), Flipped (a box with a second,
reversed copy of its top face: two faces back to back), Folded (a strip of quads whose last quad
turns back over the one before it with its winding flipped), Slivers (a grid of quads with a
needle triangle, a triangle of three collinear corners whose middle corner lies nearer one end,
one nearer the other end, and a triangle with two corners closer than 1e-6 of the mesh size),
SphereFin (a UV sphere of more than a hundred triangles with a fin quad on one of its edges).
"""
import bpy, math, os, sys

out = sys.argv[sys.argv.index('--') + 1]
os.makedirs(out, exist_ok=True)


def model(name, verts, faces):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mesh = bpy.data.meshes.new(name + 'Mesh')
    mesh.from_pydata(verts, [], faces)
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    layer = mesh.uv_layers.new(name='UVMap')
    for loop in mesh.loops:
        co = mesh.vertices[loop.vertex_index].co
        layer.data[loop.index].uv = (co.x * 0.3 + co.z * 0.05 + 0.5, co.y * 0.3 + co.z * 0.1 + 0.5)
    for poly in mesh.polygons:
        poly.use_smooth = False
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, name + '.fbx'), object_types={'MESH'},
                             use_selection=False, bake_anim=False, add_leaf_bones=False,
                             mesh_smooth_type='OFF')


def box(z0=0.0):
    verts = [(x, y, z0 + z) for z in (0, 1) for y in (0, 1) for x in (0, 1)]
    faces = [[0, 2, 3, 1], [4, 5, 7, 6], [0, 1, 5, 4], [1, 3, 7, 5], [3, 2, 6, 7], [2, 0, 4, 6]]
    return verts, faces


# Fin: the quad's top edge (2, 3) is shared by two more quads leaning forward and backward.
model('Fin', [(0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0), (1, 1.8, 0.6), (0, 1.8, 0.6),
              (1, 1.7, -0.7), (0, 1.7, -0.7)],
      [[0, 1, 2, 3], [3, 2, 4, 5], [2, 3, 7, 6]])

# BowTie: two square pyramids, apexes at the shared vertex 0.
base_a = [(-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1)]
base_b = [(x * 0.8, y * 0.8, 1.2) for x, y, _ in base_a]
verts = [(0, 0, 0)] + base_a + base_b
faces = [[1, 4, 3, 2], [0, 1, 2], [0, 2, 3], [0, 3, 4], [0, 4, 1],
         [5, 6, 7, 8], [0, 6, 5], [0, 7, 6], [0, 8, 7], [0, 5, 8]]
model('BowTie', verts, faces)

verts, faces = box()
model('Doubled', verts, faces + [list(faces[1])])

verts, faces = box()
model('Flipped', verts, faces + [list(reversed(faces[1]))])

# Folded: quads 0..3 along x, the last one folded back over the third with flipped winding.
strip = [(x, y, 0.1 * x * x) for x in range(5) for y in (0, 1)]
folded = strip[:8] + [(2.2, 0, 0.6), (2.2, 1, 0.6)]
model('Folded', folded, [[0, 2, 3, 1], [2, 4, 5, 3], [4, 6, 7, 5], [6, 7, 9, 8]])

# Slivers: a 4x3 grid of quads with degenerate triangles on its top border.
n = 4
grid = [(x / n, y / 3, 0.05 * math.sin(x + y)) for y in range(4) for x in range(n + 1)]
quads = [[y * (n + 1) + x, y * (n + 1) + x + 1, (y + 1) * (n + 1) + x + 1, (y + 1) * (n + 1) + x]
         for y in range(3) for x in range(n)]
top = [3 * (n + 1) + x for x in range(n + 1)]
extra = len(grid)
p = [grid[i] for i in top]
grid += [
    (p[0][0] * 0.8 + p[1][0] * 0.2, p[0][1], p[0][2] * 0.8 + p[1][2] * 0.2),  # nearer the first end
    (p[1][0] * 0.3 + p[2][0] * 0.7, p[1][1], p[1][2] * 0.3 + p[2][2] * 0.7),  # nearer the second end
    (p[2][0] * 0.5 + p[3][0] * 0.5, p[2][1] + 0.002, p[2][2] * 0.5 + p[3][2] * 0.5),  # a needle
    (p[3][0] + 1e-8, p[3][1] + 1e-8, p[3][2]),  # next to a grid corner
]
tris = [[top[1], top[0], extra], [top[2], top[1], extra + 1], [top[3], top[2], extra + 2],
        [top[4], top[3], extra + 3]]
model('Slivers', grid, quads + tris)

# SphereFin: a UV sphere (16 segments, 10 rings) and a fin quad on one of its edges.
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=10, radius=1)
sphere = bpy.context.active_object.data
verts = [tuple(v.co) for v in sphere.vertices]
faces = [list(p.vertices) for p in sphere.polygons]
a, b = faces[40][0], faces[40][1]
pa, pb = verts[a], verts[b]
verts += [tuple(c * 1.6 for c in pb), tuple(c * 1.6 for c in pa)]
faces.append([a, b, len(verts) - 2, len(verts) - 1])
model('SphereFin', verts, faces)
