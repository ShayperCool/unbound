# GPU Resident Drawer probe

URP's GPU Resident Drawer (com.unity.render-pipelines.core's GPU-driven code on BatchRendererGroup)
drawing ordinary MeshRenderers, in the shader graph probe's URP scene (tests/projects/shadergraph,
staged with the renderer in Forward+, the drawer in Instanced Drawing and the BatchRendererGroup
variants kept, as the Manual's GPU Resident Drawer setup asks). The drawer needs raw buffers: on the
OpenGL Core device it refuses (tests/projects/brg records the warning), so both sides run Vulkan.

`Assets/Scripts/ResidentDrawerProbe.cs` builds the scene at run time: a ground, a row of cubes
with URP Lit, Unlit and Simple Lit materials, a renderer of two sub-meshes and two materials, a
moving and a turning cube, LOD groups at several distances (one moving away through its levels,
one cross-fading, one with animated cross-fading, which the drawer leaves out), a transparent
sphere, a wall with a cube behind it, and renderers the drawer leaves to the ordinary path (a
material property block, a shader without DOTS instancing, an OnWillRenderObject script, the
package's DisallowGPUDrivenRendering), three levels drawn without a group, and out of view one
renderer per rule the drawer may refuse a renderer by (an OnRenderObject or OnBecameVisible
script, an Animator culling or always animating, an Animation without clips and one playing a
clip, a sorting order, a light probe proxy volume, an anchored probe with blended probes or with
probes off, additional vertex streams, forced-off rendering, 129 materials) besides a renderer
with additional vertex streams the ordinary path draws until frame 16 (a property block). Between frames it
changes a material's colour (frame 12), a renderer's material and disables another (18), destroys a
renderer and edits a mesh (24), sets a property block and deactivates an object (30), reactivates it
and clears the first block (36), adds a renderer (40), stretches a cube through its filter's own
mesh instance (MeshFilter.mesh, 42), puts the three levels drawn since the start into a new LOD
group (46) and moves a cube to a layer the camera does not draw (52). The main camera renders into
a 320x180 texture; frames 8, 14, ..., 56 are recorded as `f<frame>.png`, and `values.jsonl` holds per frame the drawer's state, the renderers it
holds (its CPU instance data, by name) and every renderer's isVisible, the BatchRendererGroup
variants setting and a new ObjectDispatcher's history length.

Profiles (`--profile`): `direct` (occlusion culling off: the drawer draws with direct draw
commands) and `occlusion` (GPU occlusion culling in cameras on: the main camera's commands are
indirect ones the drawer's compute shaders fill from the previous frame's depth pyramid). The two
references are identical (occlusion culling hides only what is hidden).

    python3 tests/projects/residentdrawer/run.py native --label L [--profile occlusion]   # two Editor captures
    python3 tests/projects/residentdrawer/run.py publish L-1 L-2 [--profile occlusion]
    python3 tests/projects/residentdrawer/run.py check [--profile occlusion]              # the land gate

Recording: a windowed Editor with `-force-vulkan` on the GPU-backed display :0 (the drawer
dispatches its changes from the player loop, which a batch-mode Editor does not run between
frames). Shaders compile asynchronously, the Editor's default (the drawer's draws run from jobs,
which cannot compile a variant at once: with synchronous compilation they draw the error shader),
and the probe starts its scripted frames once no shader has compiled for ten frames and three
seconds. The drawer's culling statistics are not read (turning them on turns URP's debug display
on, which compiles other variants), and the probe has no ObjectDispatcher of its own tracking the
drawer's types (in the Editor, one of the probe's read every frame left the drawer holding none of
the renderers made before it; why is not studied). The references are two identical captures
each, measured on the NVIDIA driver only.

Every renderer the drawer holds is visible in the Editor, wherever it is (behind the camera, on
a layer the camera does not draw, casting no shadows).

Unbound: every value on both profiles; images within the comparator's bounds (mean 0.13, at most
175 of 57600 pixels over 8: the edges of shadows and of a sphere, and the transparent sphere's
highlight, which differs the same way with the drawer off, so it is not the drawer's). The transparent sphere is placed where it overlaps
little on purpose: its difference is an open gap row of its own (a transparent URP Lit sphere
darker and without its highlight); once that is fixed, its images here are checked again.
