"""Writes OrderFbx.fbx into Sources/Names (Blender 4+ in the background): one quad object per
name of Sources/Names/Order.obj's groups, in the same order, to record how the importer orders
a model's children.

    blender -b --factory-startup --python tests/projects/lightmapuvs/Tools/make_name_models.py -- tests/projects/lightmapuvs/Sources/Names
"""
import bpy, os, sys

out = sys.argv[sys.argv.index('--') + 1]
names = [line.split(None, 1)[1].strip() for line in open(os.path.join(out, 'Order.obj')) if line.startswith('g ')]
bpy.ops.wm.read_factory_settings(use_empty=True)
for i, name in enumerate(names):
    x = i * 1.5
    mesh = bpy.data.meshes.new(name + 'Mesh')
    mesh.from_pydata([(x, 0, 0), (x + 1, 0, 0), (x + 1, 1, 0), (x, 1, 0.1 * (i + 1))], [], [[0, 1, 2, 3]])
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
bpy.ops.export_scene.fbx(filepath=os.path.join(out, 'OrderFbx.fbx'), object_types={'MESH'},
                         use_selection=False, bake_anim=False, add_leaf_bones=False, mesh_smooth_type='OFF')
