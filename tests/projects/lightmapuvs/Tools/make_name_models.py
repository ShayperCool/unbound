"""Writes OrderFbx.fbx and Ties.fbx into Sources/Names (Blender 4+ in the background): one quad
object per name, to record how the importer orders a model's children. OrderFbx has the names of
Sources/Names/Order.obj's groups; Ties has 28 names, many of which differ only in letter case or
in where a number's leading zeros are (`x01y2`, `x1y02`), so that the order compares them as
equal (more siblings than the shortest ranges a sort finishes by insertion).

    blender -b --factory-startup --python tests/projects/lightmapuvs/Tools/make_name_models.py -- tests/projects/lightmapuvs/Sources/Names
"""
import bpy, os, sys

TIES = ['Gamma', 'gamma', 'GAMMA', 'Delta', 'delta', 'e1', 'E01', 'e001', 'x01y2', 'x1y02', 'K', 'k', 'm0',
        'M00', 'p10', 'P10', 'Q', 'q', 'R', 'r', 's', 'S', 'u0', 'U0', 'v', 'V', 'w', 'W']

out = sys.argv[sys.argv.index('--') + 1]
order = [line.split(None, 1)[1].strip() for line in open(os.path.join(out, 'Order.obj')) if line.startswith('g ')]
for file, names in (('OrderFbx.fbx', order), ('Ties.fbx', TIES)):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for i, name in enumerate(names):
        x = i * 1.5
        mesh = bpy.data.meshes.new(name + 'Mesh')
        mesh.from_pydata([(x, 0, 0), (x + 1, 0, 0), (x + 1, 1, 0), (x, 1, 0.1 * (i + 1))], [], [[0, 1, 2, 3]])
        obj = bpy.data.objects.new(name, mesh)
        bpy.context.scene.collection.objects.link(obj)
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, file), object_types={'MESH'},
                             use_selection=False, bake_anim=False, add_leaf_bones=False, mesh_smooth_type='OFF')
