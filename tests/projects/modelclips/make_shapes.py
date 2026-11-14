"""Blend shape weight curves for the float keyframe reduction (run with Blender 4+ in the background):

    blender -b --factory-startup --python tests/projects/modelclips/make_shapes.py

Writes Assets/Resources/ShapesOff.fbx, ShapesTight.fbx and ShapesLoose.fbx (the same file; their
.meta files choose Compression Off and Keyframe Reduction at Position Errors of 101% and 102%).
One take of frames 1..31 at 30 fps, so the clip starts one frame into the file's timeline; each
shape key eases from its first to its last value (Blender's automatic Bezier handles).
"""
import os
import shutil
import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
TARGET = os.path.join(HERE, 'Assets', 'Resources')
# Name, first and last value (the importer's weights are these times 100).
CURVES = [("DownZero", 1.0, 0.0), ("UpZero", 0.0, 1.0), ("HalfZero", 0.5, 0.0), ("DownMilli", 1.0, 0.001),
          ("DownTenth", 1.0, 0.1), ("DownNeg", 1.0, -0.2)]


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8, radius=1.0, location=(0, 0, 0))
    obj = bpy.context.active_object
    obj.name = "Shapes"
    obj.data.name = "ShapesMesh"
    obj.shape_key_add(name="Basis")
    shapes = []
    for j, (name, first, last) in enumerate(CURVES):
        shape = obj.shape_key_add(name=name, from_mix=False)
        shape.slider_min = -1.0
        for i, v in enumerate(obj.data.vertices):
            shape.data[i].co = v.co * (1.0 + 0.05 * (j + 1))
        shapes.append((shape, first, last))
    scene = bpy.context.scene
    scene.render.fps = 30
    scene.frame_start = 1
    scene.frame_end = 31
    for shape, first, last in shapes:
        shape.value = first
        shape.keyframe_insert("value", frame=1)
        shape.value = last
        shape.keyframe_insert("value", frame=31)
    obj.data.shape_keys.animation_data.action.name = "ShapesAction"
    for shape, _, _ in shapes:
        shape.value = 0.0
    path = os.path.join(TARGET, 'ShapesOff.fbx')
    bpy.ops.export_scene.fbx(filepath=path, add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=False,
                             bake_anim_use_nla_strips=False, object_types={'MESH'})
    for name in ('ShapesTight.fbx', 'ShapesLoose.fbx'):
        shutil.copyfile(path, os.path.join(TARGET, name))


main()
