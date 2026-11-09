"""Export the Blender-convention controls of the Pivots, Skinned and Rigged sets.

Run with `blender -b --factory-startup -P tests/projects/modelaxis/Tools/blender_models.py`
(append `-- <name>...` for some of them: tree, apply, rig, humanoid), then
`python3 tests/projects/modelaxis/Tools/make_pivots.py` for the metas. The binary FBX output is
committed; the gate never runs Blender.
"""
import math
import os
import sys

import bpy

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PIVOTS = os.path.join(ROOT, 'Assets', 'Resources', 'Pivots')
SKINNED = os.path.join(ROOT, 'Assets', 'Resources', 'Skinned')
RIGGED = os.path.join(ROOT, 'Assets', 'Resources', 'Rigged')
HUMAN_A = os.path.join(os.path.dirname(ROOT), 'humanoid', 'Assets', 'Resources', 'HumanA.fbx')


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def cube(name, location, rotation, scale, parent=None):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0))
    obj = bpy.context.active_object
    obj.name = obj.data.name = name
    # Unequal extents so every axis permutation is visible.
    for v in obj.data.vertices:
        v.co = (v.co.x * 2.0 + 0.25, v.co.y * 3.0, v.co.z * 4.0 - 0.5)
    obj.parent = parent
    obj.location, obj.rotation_euler, obj.scale = location, rotation, scale
    return obj


def tree():
    """An empty with a rotated, nonuniformly scaled mesh child and a grandchild."""
    reset()
    bpy.ops.object.empty_add(location=(1, 2, 3))
    top = bpy.context.active_object
    top.name = 'Top'
    top.rotation_euler = (0, 0, math.radians(15))
    body = cube('Body', (4, 5, 6), tuple(map(math.radians, (10, 20, 30))), (1.5, 2, 0.5), top)
    cube('Leaf', (0.5, 1, -1), tuple(map(math.radians, (5, 10, 15))), (1, 1, 1), body)


def export(path, **options):
    bpy.ops.export_scene.fbx(filepath=path, object_types={'MESH', 'ARMATURE', 'EMPTY'},
                             bake_anim=False, **options)


def rig():
    """A three-bone chain with mixed weights, posed away from its rest pose."""
    reset()
    bpy.ops.object.armature_add(location=(0, 0, 0))
    armature = bpy.context.active_object
    armature.name = 'Rig'
    bpy.ops.object.mode_set(mode='EDIT')
    bones = armature.data.edit_bones
    hips = bones[0]
    hips.name = 'Hips'
    hips.head, hips.tail = (0, 0, 0.2), (0.1, 0, 1.5)
    spine = bones.new('Spine')
    spine.head, spine.tail, spine.parent, spine.use_connect = (0.1, 0, 1.5), (0, 0.2, 3), hips, True
    head = bones.new('Head')
    head.head, head.tail, head.parent, head.use_connect = (0, 0.2, 3), (0, 0, 4.2), spine, True
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=0.5, depth=4.0, location=(0, 0, 2))
    mesh = bpy.context.active_object
    mesh.name = mesh.data.name = 'Column'
    for name in ('Hips', 'Spine', 'Head'):
        mesh.vertex_groups.new(name=name)
    for v in mesh.data.vertices:
        z = v.co.z + 2.0
        mesh.vertex_groups['Hips'].add([v.index], max(0.0, 1.0 - z / 2.5), 'REPLACE')
        mesh.vertex_groups['Spine'].add([v.index], max(0.0, 1.0 - abs(z - 2.0) / 2.0), 'REPLACE')
        mesh.vertex_groups['Head'].add([v.index], max(0.0, (z - 1.5) / 2.5), 'REPLACE')
    mesh.parent = armature
    mesh.modifiers.new('Armature', 'ARMATURE').object = armature
    pose = armature.pose.bones
    pose['Spine'].rotation_mode = pose['Head'].rotation_mode = 'XYZ'
    pose['Spine'].rotation_euler = (math.radians(30), 0, math.radians(20))
    pose['Head'].rotation_euler = (0, math.radians(-40), 0)


# Bones of the humanoid probe's HumanA that get a box of their own.
BOXED = ('Hips', 'Spine', 'Head', 'LeftUpperLeg', 'LeftLowerLeg', 'RightUpperLeg',
         'RightLowerLeg', 'LeftUpperArm', 'LeftLowerArm', 'RightUpperArm', 'RightLowerArm')


def humanoid():
    """HumanA's skeleton and clips with a box rigidly skinned to each of its main bones."""
    reset()
    bpy.ops.import_scene.fbx(filepath=HUMAN_A)
    armature = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    boxes = []
    for name in BOXED:
        bone = armature.data.bones.get(name)
        if bone is None:
            continue
        centre = armature.matrix_world @ ((bone.head_local + bone.tail_local) * 0.5)
        size = max(bone.length, 0.05) * 0.5
        bpy.ops.mesh.primitive_cube_add(size=size, location=centre)
        box = bpy.context.active_object
        box.vertex_groups.new(name=name).add([v.index for v in box.data.vertices], 1.0, 'REPLACE')
        boxes.append(box)
    bpy.ops.object.select_all(action='DESELECT')
    for box in boxes:
        box.select_set(True)
    bpy.context.view_layer.objects.active = boxes[0]
    bpy.ops.object.join()
    body = bpy.context.active_object
    body.name = body.data.name = 'Body'
    body.parent = armature
    body.modifiers.new('Armature', 'ARMATURE').object = armature
    bpy.ops.export_scene.fbx(filepath=os.path.join(RIGGED, 'HumanoidSkin.fbx'),
                             object_types={'MESH', 'ARMATURE'}, bake_anim=True,
                             add_leaf_bones=False)


EXPORTS = {
    'tree': lambda: (tree(), export(os.path.join(PIVOTS, 'BlenderTree.fbx'))),
    'apply': lambda: (tree(), export(os.path.join(PIVOTS, 'BlenderApply.fbx'), bake_space_transform=True)),
    'rig': lambda: (rig(), export(os.path.join(SKINNED, 'BlenderRig.fbx'))),
    'humanoid': humanoid,
}


def main():
    names = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else list(EXPORTS)
    for name in names:
        EXPORTS[name]()


main()
