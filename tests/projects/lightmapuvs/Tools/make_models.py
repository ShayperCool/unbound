"""Writes the probe's source models (Blender 4+ in the background):

    blender -b --factory-startup --python tests/projects/lightmapuvs/Tools/make_models.py -- tests/projects/lightmapuvs/Sources

Cube (six quads), Cylinder (16 sides, smooth sides, hard caps), Organic (Suzanne, smooth: more
than a hundred triangles per connected part), Multi (three materials),
TwoUV (an authored second UV channel), Pieces (two disconnected boxes in one mesh) as FBX, and
Torus as OBJ.
"""
import bpy, bmesh, math, os, sys

out = sys.argv[sys.argv.index('--') + 1]
os.makedirs(out, exist_ok=True)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def export_fbx(name):
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, name + '.fbx'), object_types={'MESH'},
                             use_selection=False, bake_anim=False, add_leaf_bones=False)


def finish(obj, name):
    obj.name = name
    obj.data.name = name + 'Mesh'


reset()
bpy.ops.mesh.primitive_cube_add(size=2)
finish(bpy.context.active_object, 'Cube')
export_fbx('Cube')

reset()
bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.5, depth=2)
obj = bpy.context.active_object
finish(obj, 'Cylinder')
bpy.ops.object.shade_smooth()
for edge in obj.data.edges:
    a, b = (obj.data.vertices[i].co for i in edge.vertices)
    edge.use_edge_sharp = abs(a.z - b.z) < 1e-6
bpy.ops.object.modifier_add(type='EDGE_SPLIT')
obj.modifiers[-1].use_edge_angle = False
bpy.ops.object.modifier_apply(modifier=obj.modifiers[-1].name)
export_fbx('Cylinder')

reset()
bpy.ops.mesh.primitive_monkey_add(size=2)
obj = bpy.context.active_object
finish(obj, 'Organic')
bpy.ops.object.shade_smooth()
export_fbx('Organic')

reset()
bpy.ops.mesh.primitive_cube_add(size=2)
obj = bpy.context.active_object
finish(obj, 'Multi')
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.subdivide(number_cuts=1)
bpy.ops.object.mode_set(mode='OBJECT')
for i, colour in enumerate([(1, 0, 0, 1), (0, 1, 0, 1), (0, 0, 1, 1)]):
    material = bpy.data.materials.new('Slot%d' % i)
    material.diffuse_color = colour
    obj.data.materials.append(material)
for poly in obj.data.polygons:
    n = poly.normal
    poly.material_index = 0 if abs(n.z) > 0.5 else (1 if abs(n.x) > 0.5 else 2)
export_fbx('Multi')

reset()
bpy.ops.mesh.primitive_plane_add(size=2)
obj = bpy.context.active_object
finish(obj, 'TwoUV')
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.subdivide(number_cuts=2)
bpy.ops.object.mode_set(mode='OBJECT')
second = obj.data.uv_layers.new(name='Lightmap')
for loop in obj.data.loops:
    co = obj.data.vertices[loop.vertex_index].co
    second.data[loop.index].uv = (co.x * 0.25 + 0.5, co.y * 0.125 + 0.25)
export_fbx('TwoUV')

reset()
bpy.ops.mesh.primitive_cube_add(size=1, location=(-1, 0, 0))
a = bpy.context.active_object
bpy.ops.mesh.primitive_cube_add(size=1, location=(1.5, 0, 0.25), rotation=(0.3, 0.2, 0.1))
b = bpy.context.active_object
b.scale = (1, 2, 0.5)
bpy.ops.object.select_all(action='SELECT')
bpy.context.view_layer.objects.active = a
bpy.ops.object.join()
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
finish(bpy.context.active_object, 'Pieces')
export_fbx('Pieces')

reset()
bpy.ops.mesh.primitive_torus_add(major_segments=8, minor_segments=6, major_radius=1, minor_radius=0.3)
obj = bpy.context.active_object
finish(obj, 'Torus')
bpy.ops.object.shade_smooth()
bpy.ops.wm.obj_export(filepath=os.path.join(out, 'Torus.obj'), export_materials=False,
                      export_selected_objects=False)
print('WROTE', out)
