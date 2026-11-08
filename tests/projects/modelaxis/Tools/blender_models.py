"""Export the Blender-convention controls of the Pivots and Skinned sets.

Run with `blender -b --factory-startup -P tests/projects/modelaxis/Tools/blender_models.py`,
then `python3 tests/projects/modelaxis/Tools/make_pivots.py` for the metas. The binary FBX
output is committed; the gate never runs Blender.
"""
import math
import os

import bpy

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PIVOTS = os.path.join(ROOT, 'Assets', 'Resources', 'Pivots')
SKINNED = os.path.join(ROOT, 'Assets', 'Resources', 'Skinned')


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


def main():
    tree()
    export(os.path.join(PIVOTS, 'BlenderTree.fbx'))
    tree()
    export(os.path.join(PIVOTS, 'BlenderApply.fbx'), bake_space_transform=True)
    rig()
    export(os.path.join(SKINNED, 'BlenderRig.fbx'))


main()
