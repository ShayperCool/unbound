"""Writes the models of the normals variants (Blender 4+ in the background):

    blender -b --factory-startup --python tests/projects/lightmapuvs/Tools/make_normal_models.py -- tests/projects/lightmapuvs/Sources/Normals

FBX: Prism (an octagonal prism, 45 degrees between sides and 90 at the caps: smooth or hard
depending on the smoothing angle), Tent (smooth, irregular triangles and quads around shared
corners: the weighting modes differ), Groups (smooth sides and hard caps as smoothing groups),
Edges (the same as sharp edges), Faces (flat and smooth faces as per-face smoothing), Star (a
prism whose caps are concave 10-gons), Polys (convex polygons of 5 to 9 corners, one not
planar) and Authored (custom normals that are not the shape's); OBJ: StarObj (Star with normals
and smoothing groups) and Bare (the Tent without normals).
"""
import bpy, bmesh, math, os, sys

out = sys.argv[sys.argv.index('--') + 1]
os.makedirs(out, exist_ok=True)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def export_fbx(name, smooth='OFF'):
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, name + '.fbx'), object_types={'MESH'},
                             use_selection=False, bake_anim=False, add_leaf_bones=False,
                             mesh_smooth_type=smooth)


def export_obj(name, normals=True):
    bpy.ops.wm.obj_export(filepath=os.path.join(out, name + '.obj'), export_materials=False,
                          export_selected_objects=False, export_normals=normals,
                          export_smooth_groups=True)


def finish(obj, name):
    obj.name = name
    obj.data.name = name + 'Mesh'


def from_polygons(name, verts, faces):
    mesh = bpy.data.meshes.new(name + 'Mesh')
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    return obj


def add_uvs(obj):
    layer = obj.data.uv_layers.new(name='UVMap')
    for loop in obj.data.loops:
        co = obj.data.vertices[loop.vertex_index].co
        layer.data[loop.index].uv = (co.x * 0.3 + 0.5, co.y * 0.3 + co.z * 0.1 + 0.5)


def sharpen_caps(obj, cap_z):
    for edge in obj.data.edges:
        a, b = (obj.data.vertices[i].co for i in edge.vertices)
        edge.use_edge_sharp = abs(a.z - b.z) < 1e-6 and abs(abs(a.z) - cap_z) < 1e-6


def smooth_with_sharp_caps(name, smooth):
    reset()
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.6, depth=1.5)
    obj = bpy.context.active_object
    finish(obj, name)
    bpy.ops.object.shade_smooth()
    sharpen_caps(obj, 0.75)
    export_fbx(name, smooth)


def star():
    corners = []
    for k in range(10):
        r = 1.0 if k % 2 == 0 else 0.45
        a = math.pi * 2 * k / 10 + 0.1
        corners.append((r * math.cos(a), r * math.sin(a)))
    verts = [(x, y, -0.3) for x, y in corners] + [(x, y, 0.3) for x, y in corners]
    faces = [list(range(9, -1, -1)), list(range(10, 20))]
    faces += [[k, (k + 1) % 10, 10 + (k + 1) % 10, 10 + k] for k in range(10)]
    return verts, faces


def tent():
    verts = [(0, 0, 0.8), (1, 0, 0), (0.3, 0.9, 0.1), (-0.8, 0.5, 0), (-0.6, -0.7, 0.2), (0.4, -0.9, 0),
             (2, 0.2, -0.3), (1.2, 1.4, -0.4), (-1.6, 1.2, -0.2), (-1.5, -1.3, -0.3), (1.3, -1.5, -0.4)]
    faces = [[0, 1, 2], [0, 2, 3], [0, 3, 4], [0, 4, 5], [0, 5, 1],
             [1, 6, 7, 2], [2, 7, 8, 3], [3, 8, 9, 4], [4, 9, 10, 5], [5, 10, 6, 1]]
    return verts, faces


reset()
bpy.ops.mesh.primitive_cylinder_add(vertices=8, radius=0.7, depth=1.2)
finish(bpy.context.active_object, 'Prism')
export_fbx('Prism')

reset()
obj = from_polygons('Tent', *tent())
add_uvs(obj)
bpy.ops.object.shade_smooth()
export_fbx('Tent')

smooth_with_sharp_caps('Groups', 'SMOOTH_GROUP')
smooth_with_sharp_caps('Edges', 'EDGE')

reset()
bpy.ops.mesh.primitive_uv_sphere_add(segments=10, ring_count=6, radius=0.8)
obj = bpy.context.active_object
finish(obj, 'Faces')
for poly in obj.data.polygons:
    poly.use_smooth = poly.center.x > 0
export_fbx('Faces', 'FACE')

reset()
obj = from_polygons('Star', *star())
add_uvs(obj)
export_fbx('Star')

reset()
verts, faces = [], []
for n in range(5, 10):
    base = len(verts)
    for k in range(n):
        a = math.pi * 2 * k / n
        r = 0.5 + 0.15 * math.sin(3 * a + n)
        z = 0.2 * math.sin(2 * a) if n == 7 else 0.0
        verts.append((1.5 * (n - 7) + r * math.cos(a), r * math.sin(a), z))
    faces.append(list(range(base, base + n)))
obj = from_polygons('Polys', verts, faces)
add_uvs(obj)
export_fbx('Polys')

reset()
bpy.ops.mesh.primitive_cube_add(size=1.4)
obj = bpy.context.active_object
finish(obj, 'Authored')
bpy.ops.object.shade_smooth()
mesh = obj.data
custom = []
for loop in mesh.loops:
    co = mesh.vertices[loop.vertex_index].co
    n = (co.x * 0.5, co.y * 0.5 + 0.4, co.z * 0.5 + 0.8)
    length = math.sqrt(sum(c * c for c in n))
    custom.append(tuple(c / length for c in n))
mesh.normals_split_custom_set(custom)
export_fbx('Authored')

reset()
obj = from_polygons('StarObj', *star())
add_uvs(obj)
for poly in obj.data.polygons:
    poly.use_smooth = abs(poly.normal.z) < 0.5
export_obj('StarObj')

reset()
obj = from_polygons('Bare', *tent())
add_uvs(obj)
export_obj('Bare', normals=False)
print('WROTE', out)
