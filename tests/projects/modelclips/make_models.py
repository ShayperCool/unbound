"""Generates the probe's FBX model with Blender's own FBX exporter.

    blender -b --factory-startup --python tests/projects/modelclips/make_models.py

Rig.fbx: 24 frames per second, one take of frames 0..48. Several top-level nodes (so the importer
keeps an explicit root): Hips (child Spine, its child Head) and Prop; Still is keyed with its rest
pose only; Spin turns 270 degrees about one axis. Keys every 12 frames, linear in between,
exported with a key on every frame. The .meta (clip ranges, loop, masks, curves, events) is made
by `run.py create` (ProbeRunner.Create in the Editor);
RigDefault.fbx is the same file with the default clip settings (one clip per take), RigReduced.fbx
and RigOptimal.fbx too, with keyframe reduction and Optimal compression, RigStatic.fbx without
animation import. The file's current frame is 1, so its static node transforms are frame 1's.
"""
import os
import shutil

import bpy

PROBE = os.path.dirname(os.path.abspath(__file__))
STAGE = '/tmp/unbound-modelclips'
FRAMES = (0, 12, 24, 36, 48)


def empty(name, location, parent=None):
    obj = bpy.data.objects.new(name, None)
    obj.location = location
    obj.parent = parent
    bpy.context.scene.collection.objects.link(obj)
    return obj


def key(obj, path, values):
    """One key per FRAMES entry; values are full vectors."""
    for frame, value in zip(FRAMES, values):
        setattr(obj, path, value)
        obj.keyframe_insert(path, frame=frame)


def set_linear():
    for action in bpy.data.actions:
        curves = getattr(action, 'fcurves', None)
        if curves is None:
            # Layered actions (Blender 4.4+): channelbags per slot.
            curves = [c for layer in action.layers for strip in layer.strips
                      for bag in strip.channelbags for c in bag.fcurves]
        for curve in curves:
            for point in curve.keyframe_points:
                point.interpolation = 'LINEAR'


def rig():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.fps = 24
    scene.render.fps_base = 1.0
    scene.frame_start = FRAMES[0]
    scene.frame_end = FRAMES[-1]
    deg = 3.141592653589793 / 180.0
    hips = empty('Hips', (0, 0, 1))
    key(hips, 'location', [(0, 0, 1), (0.5, 0, 1.2), (1, 0.3, 1), (1.5, 0.3, 0.8), (2, 0, 1)])
    key(hips, 'rotation_euler', [(0, 0, 0), (10 * deg, 0, 30 * deg), (0, 0, 60 * deg),
                                 (-10 * deg, 0, 45 * deg), (20 * deg, 0, 90 * deg)])
    key(hips, 'scale', [(1, 1, 1), (1.2, 1, 1), (1, 1.3, 1), (1, 1, 0.8), (1.5, 1, 1)])
    spine = empty('Spine', (0, 0, 0.5), hips)
    key(spine, 'rotation_euler', [(0, 0, 0), (0, 20 * deg, 0), (0, -20 * deg, 0),
                                  (0, 40 * deg, 0), (0, 10 * deg, 0)])
    head = empty('Head', (0, 0, 0.4), spine)
    key(head, 'location', [(0, 0, 0.4), (0, 0, 0.5), (0, 0, 0.4), (0, 0, 0.6), (0, 0, 0.3)])
    prop = empty('Prop', (1, 0, 0))
    key(prop, 'location', [(1, 0, 0), (1, 1, 0), (1, 2, 0), (1, 1, 0), (1, 3, 0)])
    # Turns 270 degrees: the end-to-start rotation is longer than a half turn.
    spin = empty('Spin', (0, 1, 0))
    key(spin, 'rotation_euler', [(0, 0, 0), (0, 0, 90 * deg), (0, 0, 180 * deg), (0, 0, 225 * deg), (0, 0, 270 * deg)])
    still = empty('Still', (-1, 0, 0.5))
    key(still, 'location', [(-1, 0, 0.5)] * len(FRAMES))
    set_linear()
    path = os.path.join(STAGE, 'Rig.fbx')
    os.makedirs(STAGE, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=False, object_types={'EMPTY'},
                             bake_anim=True, bake_anim_use_all_actions=False,
                             bake_anim_use_nla_strips=False, bake_anim_simplify_factor=0.0,
                             bake_anim_step=1.0, bake_anim_force_startend_keying=True)
    return path


def main():
    shutil.rmtree(STAGE, ignore_errors=True)
    target = os.path.join(PROBE, 'Assets', 'Resources')
    os.makedirs(target, exist_ok=True)
    rig_path = rig()
    for name in ('Rig.fbx', 'RigDefault.fbx', 'RigReduced.fbx', 'RigOptimal.fbx', 'RigStatic.fbx'):
        shutil.copyfile(rig_path, os.path.join(target, name))


main()
