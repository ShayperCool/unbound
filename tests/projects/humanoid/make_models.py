"""Generates the humanoid probe's rigs with Blender's own FBX exporter.

    blender -b --factory-startup --python tests/projects/humanoid/make_models.py

HumanA: every body bone Unity maps (upper chest, neck, shoulders, toes, eyes, jaw) and three
fingers per hand, in an A pose (arms down), with rolled bones so that the bone axes differ from
Unity's muscle axes; it carries a walk-like clip on all of them, hips translation included.
HumanB: only the required bones under extra non-human nodes (a root, a spine helper between Hips
and Spine, twist leaves), other proportions and other rolls; it carries a short clip too.
HumanT: HumanA's rig and clip with translations of the spine, neck, a leg and an arm added (for an
avatar with translation DoF).
HumanLong: HumanB's rig with the same clip over 31 frames, a length whose last frame time in
single precision lies past the clip's end; it goes to tests/projects/modelclips and is made
only when named.
HumanLate: HumanLong's clip one frame later, on frames 1..31 of a take that starts at frame 1;
it goes to tests/projects/modelclips and is made only when named.
HumanTwist: HumanB's rig and clip with its two twist bones, which are not human bones, animated
too (the left one turns, the right one moves); it goes to tests/projects/modelclips as
HumanTwistNone.fbx (copied there under its other clip masks) and is made only when named.
The character faces Blender's -Y, which is Unity's +Z.

Names after `--` generate only those rigs (HumanA, HumanB, HumanT, HumanLong, HumanLate, HumanTwist).
"""
import math
import os
import shutil
import sys

import bpy
from mathutils import Euler, Vector

PROBE = os.path.dirname(os.path.abspath(__file__))
STAGE = '/tmp/unbound-humanoid-models'


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.render.fps = 30
    scene.frame_start = 0


def armature(name, bones):
    """bones: (name, parent, head, tail, roll in degrees)."""
    data = bpy.data.armatures.new(name)
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT')
    for bone, parent, head, tail, roll in bones:
        b = data.edit_bones.new(bone)
        b.head = Vector(head)
        b.tail = Vector(tail)
        b.roll = math.radians(roll)
        if parent:
            b.parent = data.edit_bones[parent]
            b.use_connect = False
    bpy.ops.object.mode_set(mode='OBJECT')
    return obj


def side(bones):
    """Left bones (+X in Blender) mirrored to the right (-X), rolls negated."""
    out = []
    for bone, parent, head, tail, roll in bones:
        out.append((bone, parent, head, tail, roll))
        mirror = lambda p: (-p[0], p[1], p[2])
        out.append((bone.replace('Left', 'Right'), parent.replace('Left', 'Right') if parent else parent,
                    mirror(head), mirror(tail), -roll))
    return out


def a_bones():
    body = [
        ('Hips', None, (0, 0, 1.0), (0, 0, 1.1), 0),
        ('Spine', 'Hips', (0, 0.01, 1.1), (0, 0, 1.22), 5),
        ('Chest', 'Spine', (0, 0, 1.22), (0, 0, 1.33), 0),
        ('UpperChest', 'Chest', (0, 0, 1.33), (0, 0.01, 1.44), -5),
        ('Neck', 'UpperChest', (0, 0.01, 1.47), (0, 0, 1.56), 0),
        ('Head', 'Neck', (0, 0, 1.56), (0, 0, 1.76), 0),
        ('Jaw', 'Head', (0, -0.02, 1.6), (0, -0.09, 1.58), 0),
    ]
    limbs = side([
        ('LeftEye', 'Head', (0.03, -0.08, 1.67), (0.03, -0.11, 1.67), 0),
        ('LeftShoulder', 'UpperChest', (0.03, 0, 1.43), (0.16, 0.01, 1.43), 20),
        ('LeftUpperArm', 'LeftShoulder', (0.17, 0.01, 1.42), (0.37, 0.02, 1.22), 30),
        ('LeftLowerArm', 'LeftUpperArm', (0.37, 0.02, 1.22), (0.54, -0.02, 1.04), 45),
        ('LeftHand', 'LeftLowerArm', (0.54, -0.02, 1.04), (0.6, -0.03, 0.98), 60),
        ('LeftThumbProximal', 'LeftHand', (0.55, -0.05, 1.02), (0.57, -0.08, 1.0), 10),
        ('LeftThumbIntermediate', 'LeftThumbProximal', (0.57, -0.08, 1.0), (0.585, -0.1, 0.985), 10),
        ('LeftThumbDistal', 'LeftThumbIntermediate', (0.585, -0.1, 0.985), (0.595, -0.115, 0.975), 10),
        ('LeftIndexProximal', 'LeftHand', (0.6, -0.04, 0.98), (0.625, -0.045, 0.955), 60),
        ('LeftIndexIntermediate', 'LeftIndexProximal', (0.625, -0.045, 0.955), (0.64, -0.047, 0.94), 60),
        ('LeftIndexDistal', 'LeftIndexIntermediate', (0.64, -0.047, 0.94), (0.65, -0.048, 0.93), 60),
        ('LeftMiddleProximal', 'LeftHand', (0.605, -0.02, 0.975), (0.632, -0.021, 0.948), 60),
        ('LeftMiddleIntermediate', 'LeftMiddleProximal', (0.632, -0.021, 0.948), (0.65, -0.022, 0.93), 60),
        ('LeftMiddleDistal', 'LeftMiddleIntermediate', (0.65, -0.022, 0.93), (0.66, -0.022, 0.92), 60),
        ('LeftUpperLeg', 'Hips', (0.09, 0, 0.98), (0.1, -0.01, 0.55), 0),
        ('LeftLowerLeg', 'LeftUpperLeg', (0.1, -0.01, 0.55), (0.1, 0.01, 0.1), 0),
        ('LeftFoot', 'LeftLowerLeg', (0.1, 0.01, 0.1), (0.11, -0.11, 0.03), 0),
        ('LeftToes', 'LeftFoot', (0.11, -0.11, 0.03), (0.11, -0.17, 0.03), 0),
    ])
    return body + limbs


def human_a():
    rig = armature('HumanA', a_bones())
    animate(rig, 'Walk', 30, a_keys())
    export(rig, 'HumanA.fbx')


def human_t():
    """HumanA with bone translations (Blender's pose locations, in each bone's rest frame) on the
    bones that can carry a translation DoF."""
    rig = armature('HumanT', a_bones())
    keys = a_keys()
    for f, s in ((0, 1), (15, -1), (30, 1)):
        keys += [
            ('Spine', f, None, (0.01 * s, 0.02, -0.01 * s)),
            ('Neck', f, None, (0, 0.015 * s, 0.01)),
            ('LeftUpperLeg', f, None, (0.02 * s, -0.01, 0.015)),
            ('RightUpperArm', f, None, (-0.01, 0.025 * s, 0.005 * s)),
            ('LeftShoulder', f, None, (0.01, 0, -0.015 * s)),
        ]
    animate(rig, 'Walk', 30, keys)
    export(rig, 'HumanT.fbx')


def a_keys():
    """(bone, frame, euler degrees or None, location or None)."""
    keys = []
    for f, s in ((0, 1), (15, -1), (30, 1)):
        keys += [
            ('Hips', f, (5 * s, 3 * s, 4 * s), (0.02 * s, -0.4 * f / 30, 0.03 * abs(s))),
            ('Spine', f, (-3 * s, 0, -5 * s), None),
            ('Chest', f, (2, 4 * s, 0), None),
            ('UpperChest', f, (0, -2 * s, 1 * s), None),
            ('Neck', f, (4 * s, 3 * s, 0), None),
            ('Head', f, (-6 * s, -5 * s, 2 * s), None),
            ('Jaw', f, (10 + 5 * s, 0, 0), None),
            ('LeftEye', f, (5 * s, 0, 8 * s), None),
            ('RightEye', f, (5 * s, 0, 8 * s), None),
            ('LeftShoulder', f, (0, 5 * s, 10), None),
            ('RightShoulder', f, (0, -5 * s, -10), None),
            ('LeftUpperArm', f, (30 * s, 10, 20), None),
            ('RightUpperArm', f, (-30 * s, -10, -20), None),
            ('LeftLowerArm', f, (35 + 15 * s, 0, 0), None),
            ('RightLowerArm', f, (35 - 15 * s, 0, 0), None),
            ('LeftHand', f, (10 * s, 20, 5), None),
            ('RightHand', f, (-10 * s, -20, -5), None),
            ('LeftUpperLeg', f, (-30 * s, 5, 3 * s), None),
            ('RightUpperLeg', f, (30 * s, -5, -3 * s), None),
            ('LeftLowerLeg', f, (40 + 20 * s, 0, 0), None),
            ('RightLowerLeg', f, (40 - 20 * s, 0, 0), None),
            ('LeftFoot', f, (-10 * s, 3, 0), None),
            ('RightFoot', f, (10 * s, -3, 0), None),
            ('LeftToes', f, (15 + 10 * s, 0, 0), None),
            ('RightToes', f, (15 - 10 * s, 0, 0), None),
        ]
        for hand in ('Left', 'Right'):
            for finger, curl in (('Thumb', 15), ('Index', 30), ('Middle', 40)):
                for part in ('Proximal', 'Intermediate', 'Distal'):
                    keys.append((hand + finger + part, f, (curl + 10 * s, 0, 5 if part == 'Proximal' else 0), None))
    return keys


def human_b(end=20, file='HumanB.fbx', probe=PROBE, twist=False, start=0):
    body = [
        ('Root', None, (0, 0, 0), (0, 0, 0.2), 0),
        ('Hips', 'Root', (0, 0.02, 1.12), (0, 0.02, 1.22), 90),
        ('SpineHelper', 'Hips', (0, 0.02, 1.22), (0, 0.03, 1.28), 0),
        ('Spine', 'SpineHelper', (0, 0.03, 1.28), (0, 0.02, 1.6), -40),
        ('Head', 'Spine', (0, 0.0, 1.66), (0, 0.0, 1.9), 180),
    ]
    limbs = side([
        ('LeftUpperArm', 'Spine', (0.2, 0.02, 1.58), (0.48, 0.02, 1.58), -90),
        ('LeftUpperArmTwist', 'LeftUpperArm', (0.34, 0.02, 1.58), (0.4, 0.02, 1.58), 0),
        ('LeftLowerArm', 'LeftUpperArm', (0.48, 0.02, 1.58), (0.7, 0.0, 1.58), 135),
        ('LeftHand', 'LeftLowerArm', (0.7, 0.0, 1.58), (0.78, 0.0, 1.58), 0),
        ('LeftUpperLeg', 'Hips', (0.12, 0.02, 1.1), (0.12, 0.0, 0.62), 70),
        ('LeftLowerLeg', 'LeftUpperLeg', (0.12, 0.0, 0.62), (0.12, 0.03, 0.12), -30),
        ('LeftFoot', 'LeftLowerLeg', (0.12, 0.03, 0.12), (0.12, -0.14, 0.02), 15),
    ])
    rig = armature('HumanB', body + limbs)
    keys = []
    for f, s in ((start, 1), (start + (end - start) // 2, -1), (end, 1)):
        keys += [
            ('Hips', f, (0, 10 * s, 0), (0, 0, 0.05 * s)),
            ('Spine', f, (8 * s, 0, 0), None),
            ('LeftUpperArm', f, (0, 0, 40 * s), None),
            ('LeftLowerLeg', f, (30 + 20 * s, 0, 0), None),
            ('RightUpperLeg', f, (-20 * s, 0, 0), None),
        ]
        if twist:
            keys += [
                ('LeftUpperArmTwist', f, (25 * s, 0, 0), None),
                ('RightUpperArmTwist', f, None, (0, 0.03 * s, 0)),
            ]
    bpy.context.scene.frame_start = start
    animate(rig, 'Wave', end, keys)
    export(rig, file, probe)


def human_long():
    human_b(31, 'HumanLong.fbx', os.path.join(os.path.dirname(PROBE), 'modelclips'))


def human_late():
    """HumanLong's clip on frames 1..31 of a take that starts at frame 1 (Blender's default start)."""
    human_b(31, 'HumanLate.fbx', os.path.join(os.path.dirname(PROBE), 'modelclips'), start=1)


def human_twist():
    human_b(20, 'HumanTwistNone.fbx', os.path.join(os.path.dirname(PROBE), 'modelclips'), twist=True)


def animate(rig, name, end, keys):
    action = bpy.data.actions.new(name)
    rig.animation_data_create()
    rig.animation_data.action = action
    bpy.context.scene.frame_end = end
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='POSE')
    for bone, frame, euler, location in keys:
        pb = rig.pose.bones[bone]
        pb.rotation_mode = 'XYZ'
        if euler is not None:
            pb.rotation_euler = Euler([math.radians(a) for a in euler])
            pb.keyframe_insert('rotation_euler', frame=frame)
        if location is not None:
            pb.location = Vector(location)
            pb.keyframe_insert('location', frame=frame)
    bpy.ops.object.mode_set(mode='OBJECT')


def export(rig, file, probe=PROBE):
    os.makedirs(STAGE, exist_ok=True)
    path = os.path.join(STAGE, file)
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, add_leaf_bones=False,
                             bake_anim=True, bake_anim_use_all_actions=False,
                             bake_anim_use_nla_strips=False)
    dest = os.path.join(probe, 'Assets', 'Resources')
    os.makedirs(dest, exist_ok=True)
    shutil.copyfile(path, os.path.join(dest, file))


only = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else ['HumanA', 'HumanB', 'HumanT']
for name, make in (('HumanA', human_a), ('HumanB', human_b), ('HumanT', human_t), ('HumanLong', human_long),
                   ('HumanLate', human_late),
                   ('HumanTwist', human_twist)):
    if name in only:
        reset()
        make()
