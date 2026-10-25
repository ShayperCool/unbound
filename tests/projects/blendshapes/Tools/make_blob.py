import bpy, sys
out = sys.argv[sys.argv.index('--') + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8, radius=1.0, location=(0, 0, 0))
obj = bpy.context.active_object
obj.name = "Blob"; obj.data.name = "BlobMesh"
bpy.ops.object.shade_smooth()
basis = obj.shape_key_add(name="Basis")
puff = obj.shape_key_add(name="Puff", from_mix=False)
for i, v in enumerate(obj.data.vertices):
    co = v.co
    puff.data[i].co = co * 1.3 + (co.z > 0) * co * 0.2
squash = obj.shape_key_add(name="Squash", from_mix=False)
for i, v in enumerate(obj.data.vertices):
    co = v.co
    squash.data[i].co = (co.x * 1.2, co.y * 1.2, co.z * 0.5)
# Two bones; lower and upper halves.
bpy.ops.object.armature_add(location=(0, 0, -1))
arm = bpy.context.active_object
arm.name = "Rig"
bpy.ops.object.mode_set(mode='EDIT')
eb = arm.data.edit_bones
lower = eb[0]; lower.name = "Lower"; lower.head = (0, 0, -1); lower.tail = (0, 0, 0)
upper = eb.new("Upper"); upper.head = (0, 0, 0); upper.tail = (0, 0, 1); upper.parent = lower
bpy.ops.object.mode_set(mode='OBJECT')
gl = obj.vertex_groups.new(name="Lower"); gu = obj.vertex_groups.new(name="Upper")
for v in obj.data.vertices:
    if v.co.z < -0.01: gl.add([v.index], 1.0, 'REPLACE')
    elif v.co.z > 0.01: gu.add([v.index], 1.0, 'REPLACE')
    else:
        gl.add([v.index], 0.5, 'REPLACE'); gu.add([v.index], 0.5, 'REPLACE')
obj.parent = arm
mod = obj.modifiers.new("Armature", 'ARMATURE'); mod.object = arm
# Puff animated 0 -> 1 over frames 1..31, Squash 1 -> 0.
scene = bpy.context.scene
scene.render.fps = 30; scene.frame_start = 1; scene.frame_end = 31
keys = obj.data.shape_keys
puff.value = 0.0; puff.keyframe_insert("value", frame=1)
puff.value = 1.0; puff.keyframe_insert("value", frame=31)
squash.value = 1.0; squash.keyframe_insert("value", frame=1)
squash.value = 0.0; squash.keyframe_insert("value", frame=31)
keys.animation_data.action.name = "BlobAction"
puff.value = 0.0; squash.value = 0.0
bpy.ops.export_scene.fbx(filepath=out, add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=False,
                         bake_anim_use_nla_strips=False, object_types={'ARMATURE', 'MESH'})
print("WROTE", out)
