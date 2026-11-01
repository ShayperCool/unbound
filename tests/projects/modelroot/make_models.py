"""Generates the probe's FBX models with Blender's own FBX exporter.

    blender -b --factory-startup --python tests/projects/modelroot/make_models.py

24 frames per second, one take of frames 0..48, keys every 12 frames, linear in between, exported
with a key on every frame. Walker.fbx has two top-level nodes, so the importer keeps an explicit
root: Hips (walks forward, sways, bobs, turns and tilts; child Leg swings) and Prop. Mover.fbx has
the same motion on its single top-level node, which becomes the model's root. The copies differ
only in their .meta, which `run.py create` (ProbeRunner.Create in the Editor) writes. The models
are exported with the axis conversion and unit scale applied (identity rest rotations, unit scale),
except WalkerTilt, whose nodes keep Blender's -90 degree X rotation and scale 100.
"""
import os
import shutil

import bpy

PROBE = os.path.dirname(os.path.abspath(__file__))
STAGE = '/tmp/unbound-modelroot'
FRAMES = (0, 12, 24, 36, 48)
DEG = 3.141592653589793 / 180.0


def empty(name, location, parent=None):
    obj = bpy.data.objects.new(name, None)
    obj.location = location
    obj.parent = parent
    bpy.context.scene.collection.objects.link(obj)
    return obj


def key(obj, path, values):
    for frame, value in zip(FRAMES, values):
        setattr(obj, path, value)
        obj.keyframe_insert(path, frame=frame)


def set_linear():
    for action in bpy.data.actions:
        curves = getattr(action, 'fcurves', None)
        if curves is None:
            curves = [c for layer in action.layers for strip in layer.strips
                      for bag in strip.channelbags for c in bag.fcurves]
        for curve in curves:
            for point in curve.keyframe_points:
                point.interpolation = 'LINEAR'


def walk(obj):
    key(obj, 'location', [(0, 0, 1), (0.3, 1, 1.1), (0.2, 2, 1), (-0.2, 3, 1.15), (0, 4, 1)])
    key(obj, 'rotation_euler', [(0, 0, 0), (5 * DEG, 0, 10 * DEG), (0, 0, 30 * DEG),
                                (-5 * DEG, 0, 45 * DEG), (0, 0, 90 * DEG)])


def scene(name, single, clean=True):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    s = bpy.context.scene
    s.render.fps = 24
    s.render.fps_base = 1.0
    s.frame_start = FRAMES[0]
    s.frame_end = FRAMES[-1]
    s.frame_current = 0
    body = empty('Mover' if single else 'Hips', (0, 0, 1))
    walk(body)
    leg = empty('Leg', (0, 0, -0.5), body)
    key(leg, 'rotation_euler', [(0, 0, 0), (30 * DEG, 0, 0), (0, 0, 0), (-30 * DEG, 0, 0), (0, 0, 0)])
    if not single:
        prop = empty('Prop', (1, 0, 0))
        key(prop, 'location', [(1, 0, 0), (1, 0, 0.5), (1, 0, 0), (1, 0, 0.5), (1, 0, 0)])
    set_linear()
    path = os.path.join(STAGE, name)
    os.makedirs(STAGE, exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=False, object_types={'EMPTY'},
                             bake_anim=True, bake_anim_use_all_actions=False,
                             bake_anim_use_nla_strips=False, bake_anim_simplify_factor=0.0,
                             bake_anim_step=1.0, bake_anim_force_startend_keying=True,
                             bake_space_transform=clean,
                             apply_scale_options='FBX_SCALE_ALL' if clean else 'FBX_SCALE_NONE')
    return path


def main():
    shutil.rmtree(STAGE, ignore_errors=True)
    target = os.path.join(PROBE, 'Assets', 'Resources')
    os.makedirs(target, exist_ok=True)
    shutil.copyfile(scene('WalkerTilt.fbx', single=False, clean=False), os.path.join(target, 'WalkerTilt.fbx'))
    walker = scene('Walker.fbx', single=False)
    for name in ('Walker.fbx', 'WalkerNoNode.fbx', 'WalkerNoAvatar.fbx'):
        shutil.copyfile(walker, os.path.join(target, name))
    mover = scene('Mover.fbx', single=True)
    for name in ('Mover.fbx', 'MoverAvatar.fbx'):
        shutil.copyfile(mover, os.path.join(target, name))


main()
