"""Writes the probe's models with Blender: `blender -b --factory-startup --python make_models.py`.

Layers.fbx: a 2x2-quad grid with eight UV maps (map i holds the vertex's grid position scaled
and offset by i) and a byte color attribute per face corner. Plain.fbx: the same grid with one
UV map and no colors.
"""
import bpy
from pathlib import Path

OUT = Path(__file__).resolve().parent / 'Assets' / 'Resources'


def grid(name, uv_maps, colors):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    verts = [(x, y, 0.0) for y in range(3) for x in range(3)]
    faces = [(y * 3 + x, y * 3 + x + 1, (y + 1) * 3 + x + 1, (y + 1) * 3 + x) for y in range(2) for x in range(2)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    for i in range(uv_maps):
        layer = mesh.uv_layers.new(name=f'UV{i}')
        for loop in mesh.loops:
            x, y, _ = verts[loop.vertex_index]
            layer.data[loop.index].uv = (x * 0.25 + i, y * 0.125 + i * 0.5)
    if colors:
        attribute = mesh.color_attributes.new('Col', 'BYTE_COLOR', 'CORNER')
        for loop in mesh.loops:
            v = loop.vertex_index
            attribute.data[loop.index].color_srgb = (v / 8.0, 1.0 - v / 8.0, 0.25 + (v % 3) * 0.25, 0.5 + (v % 2) * 0.5)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    bpy.ops.export_scene.fbx(filepath=str(OUT / f'{name}.fbx'), use_selection=False, add_leaf_bones=False,
                             bake_anim=False, mesh_smooth_type='FACE', colors_type='SRGB')


grid('Layers', 8, True)
grid('Plain', 1, False)
