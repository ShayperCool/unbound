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
references are identical (occlusion culling hides only what is hidden). `controls` is `direct` with
more renderers out of view, for rules the others do not measure (the staged project gets a second
sorting layer, `Probe`): one with an OnBecameInvisible script, one with a sorting group, one under a
GameObject with a sorting group, one with a disabled sorting group, one on the `Probe` sorting layer
and one casting shadows only. The drawer refuses the first, second, third and fifth and holds the
disabled group's and the shadows-only one, which is visible though out of view, as every renderer it
holds.

`lightmaps` and `shadowmask` stage the URP probe's project (tests/projects/urp) instead and record
its baked `Subtractive` scene (non-directional lightmaps) and `Shadowmask` scene (directional
lightmaps, a shadow mask, a mixed sun casting realtime shadows) as they are (`RESIDENT_DRAWER_SCENE`): each renderer's lightmap index and scale
and offset, then at frames 8 and 14 the image, the renderers' isVisible and the renderers the drawer
holds. The drawer holds the lightmapped ground, wall and block with the probe-lit ones, and leaves out
the sphere with a light probe proxy volume; its commands carry each renderer's lightmap index with
the legacy lightmaps keyword flag, and the lightmapped renderers are drawn with their lightmaps
(with their directions and shadow mask in `shadowmask`).

`speedtree` stages `tests/projects/speedtree9wind`'s trees (and the Wind module) and records with
`Assets/SpeedTree/DrawerWindProbe.cs` instead: the SpeedTree 9 wind parameters the drawer gives tree
instances. Each tree is a copy of an asset's first level (its Tree keeps the asset's wind asset)
drawing a quad with `Assets/SpeedTree/DrawerWindDump.shader`, which writes the instance's
`DOTS_ST_WindParam0` to `10` and `DOTS_ST_WindHistoryParam0` to `10` into the tree's row of a 22x8
float target (-1 without DOTS instancing); one orthographic camera renders all trees once a frame.
Game time stands still until shaders have compiled, so the wind does not depend on the warm-up.
Over 470 frames at a captured 30 frames per second a directional zone comes (frame 10), strengthens
(50) and turns around (75); a tree leaves the camera's view (90) and comes back (95); two trees
appear (100), with camera and per-object motion vectors; the zone goes (130) and the wind settles;
a zone without wind comes (320), then a wind again (330); a spherical zone reaches every tree but
`Calm` (385), so each gets a local wind, and moves out of reach (420), so they return to their
asset's wind (to frame 470). The drawer holds every tree; floats are recorded as the hex of their
bits (values compare exactly).

`motion` is `occlusion` with the probe's other scene (`RESIDENT_DRAWER_MOTION=1`): a grey wall,
five Lit cubes in a row behind it and two farther away beside it, and a camera that stays behind the
wall (frames 1 to 10), jumps 6 to the left (11), back (16) and 6 to the right (21), moves sideways
from x -3 at 0.25 a frame (25 to 36) and zooms from a field of view of 60 to 20 (37 to 42) and back
(43 to 48). Every frame is captured, with every renderer's isVisible and the renderers the drawer
holds. Each frame's draws are tested against the previous frame's depth pyramid and the ones it
culls again against the frame's own, so the Editor draws every cube the camera sees in the frame it
shows (the scene's own Lit sphere stays where the URP probe's scene has it). The images catch
culling of what the camera sees (isVisible, set by the CPU culling, stays true); what the wall hides looks the same whether it is
culled or only fails the depth test.

The profile also records four depth controls once, before the camera moves (`depthRead.*`): a
draw gives a 16x16 depth-only render texture depth 0.25 and the command buffer moves to another
target, then `base` reads one texel of it with a compute kernel through a float texture slot
(`Shaders/Resources/ProbeDepthRead.compute`). The others first draw depth 0.75 into it
(`ProbeDepthQuad.shader`) and read it with the target still bound (`bound`), after a
`SetBufferData` (`afterBufferData`) and after a `SetBufferData` and a move to the other target
(`afterSwitch`). A kernel reads what the commands before it drew, as the drawer's occluder depth
pyramid does from the camera's depth after the opaque draws.

    python3 tests/projects/residentdrawer/run.py native --label L [--profile occlusion|motion|controls|lightmaps|shadowmask|speedtree]   # two Editor captures
    python3 tests/projects/residentdrawer/run.py publish L-1 L-2 [--profile occlusion|motion|controls|lightmaps|shadowmask|speedtree]
    python3 tests/projects/residentdrawer/run.py check [--profile occlusion|motion|controls|lightmaps|shadowmask|speedtree]              # the land gate

Recording: a windowed Editor with `-force-vulkan` on the GPU-backed display :0 (the drawer
dispatches its changes from the player loop, which a batch-mode Editor does not run between
frames). Shaders compile asynchronously, the Editor's default (the drawer's draws run from jobs,
which cannot compile a variant at once: with synchronous compilation they draw the error shader),
and the probe starts its scripted frames once no shader has compiled for ten frames and three
seconds. The runner enters Play mode only after the Editor's start-up work (300 Editor updates and
three seconds after opening the scene, with no compilation or import running), as a user pressing
Play in an open Editor does: entered at once, the first Play mode frames still see that work (the
switch to the project's gamma colour space, URP's re-import of every material), which reloads
every material and so has URP's material inspector validate the materials the probe makes at run
time (its transparent Lit sphere then blends premultiplied: source factor One with
_ALPHAPREMULTIPLY_ON). The drawer's culling statistics are not read (turning them on turns URP's debug display
on, which compiles other variants), and the probe has no ObjectDispatcher of its own tracking the
drawer's types (in the Editor, one of the probe's read every frame left the drawer holding none of
the renderers made before it; why is not studied). The references are two identical captures
each, measured on the NVIDIA driver only.

On macOS arm64 the runner records and checks Metal, the device of the macOS Editor and of Unbound
there (AGENTS.md Platforms: the graphics device's answers and rounding follow the macOS Editor):
`native` runs the macOS Editor windowed (tests/macos_editor.py) and `publish` writes
`Reference/unity-R/metal/<profile>` from two byte-identical captures; every macOS check compares
with it, with the same bounds, and reads `residuals-metal-<profile>.json` where one exists. The
Metal references of all seven profiles were recorded on an Apple M2 Pro (macOS 26). Their values
are the Vulkan references' but for the `device` row (the drawer holds the same renderers with the
same visibility); their images differ from them by the device's rounding (per profile a largest
image mean of 0.38 to 0.92), and `direct` and `controls` also in 83 pixels of each frame by up to
64/255. Those 83 pixels are why `occlusion` keeps `direct`'s values on Metal but not its images
(82 pixels differ, mean 0.05; on Vulkan the two are identical): its images are within 2 pixels over
8 of Vulkan's. Where those pixels are and why is not studied. Unbound on Metal matches every
profile's Metal reference: all values (508, 508, 497, 562, 31, 31 and 5405) and every image within
the comparator's bounds.

Every renderer the drawer holds is visible in the Editor, wherever it is (behind the camera, on
a layer the camera does not draw, casting no shadows).

Unbound: every value on both profiles; images within the comparator's bounds (mean 0.00, at most
3 of 57600 pixels over 8, at shadow edges). The transparent sphere is placed where it overlaps
little; it matches since the references are recorded after the Editor's start-up work (before,
the Editor's sphere was brighter, with a highlight, because of the material validation above).
On `speedtree` Unbound matches every value (5405 of 5405).
