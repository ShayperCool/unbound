# vfxlit probe

Visual Effect Graph outputs that the pipeline lights, outputs that draw each particle as a mesh,
the shadows of both, and outputs drawn by a Shader Graph, through the forward renderer and the 2D
renderer (com.unity.visualeffectgraph 17.0.4 under URP 17, the Editor's built-in
packages, unmodified): small graphs of the probe's own, each drawn in front of a camera of its
own, recorded by the same script in the Unity Editor and in Unbound. The outputs here draw
themselves, in the order their system keeps its particles (opaque, or added to what is behind):
outputs that are sorted, culled per camera or drawn through an indirect buffer belong to
`tests/projects/vfxoutputs`, the import, the API and the simulation to `tests/projects/vfx`.

The Editor records in a window (not in batch mode), after letting five editor updates pass
(`Assets/Editor/ProbeRunner.cs`): see `tests/projects/vfx/README.md`.

## Graphs, objects and scene

`Assets/Editor/Authoring/LitAuthoring.cs` builds everything in the Editor through the packages'
own models; its assembly carries the name of a friend assembly of both packages
(`Unity.VisualEffectGraph.EditorTests`). Every graph is one burst of nine particles at random
places and turned at random angles, in a system of sixteen, unless said otherwise.

- `LitMatte`, `LitSmooth`, `LitMetal`: opaque lit quads (`Output Particle URP Lit Quad`), rough,
  smooth and metallic. `LitAdditive`: lit quads added to what is behind.
- `LitMapped`: lit quads with a base colour map (an image whose four quarters differ) and an
  emissive colour.
- `LitOutputs`: one system with three outputs, in this order in the graph: lit quads, smaller
  unlit quads moved towards the camera, and still smaller unlit meshes moved further. The sort
  priority of the lit output and of the mesh output is 1, the unlit quads' is 0.
- `LitCaster`: lit quads that cast shadows. `UnlitCaster`: unlit quads that do.
- `LitMoving`: a lit output that writes motion vectors (drawn, not shot; what its asset asks of
  the renderer is recorded). `LitReceiving`: `LitMatte` saved with renderer settings of its own:
  receiving shadows, and, for the two settings the graph's compilation decides, values it does
  not decide (shadow casting two sided, motion vectors forced off).
- `MeshUnlit`, `MeshLit`: each particle drawn as a mesh (`Output Particle Mesh` and
  `Output Particle URP Lit Mesh`): `Assets/Objects/Wedge.asset`, a wedge of two submeshes (its two
  triangles, its three sides) with normals and texture coordinates, without tangents or colours.
- `MeshExposed`: the unlit mesh output with its mesh and its submesh mask as exposed properties
  (`Shape`, `Mask`). `MeshNone`: its mesh slot left empty. `MeshCaster`: lit meshes that cast
  shadows.
- `MeshDying`: ten particles a second that rise and live two fifths of a second, drawn as the
  wedge: a system that draws every particle it has room for, with dead ones between live ones.
- `LitSorted`: nine lit quads blended over what is behind them, half transparent, each of a
  colour, a turn and a depth of its own (a depth of three units in all): such an output sorts
  its particles by default and draws them through a sorted indirect buffer.
- `CurveOutput`: unlit quads, twenty a second living four fifths of a second, whose output
  context sets each one's size from a curve and its colour from a gradient over its life
  (`Set Size over Life`, `Set Color over Life`): the output's own program samples both.

- `SpriteLit`, `SpriteUnlit`: quads of `Output Particle ShaderGraph Quad` (the composed output)
  drawn by the render pipeline package's own graphs for particles, `VFXSpriteLit` (lit by 2D
  lights) and `VFXSpriteUnlit`, whose texture is the image whose quarters differ; spread over a
  depth of one unit, each of a colour and a turn of its own.
- `GraphUnlit`: the same output drawn by `Assets/Graphs/Unlit.shadergraph`, the graph package's
  default graph (URP Unlit, opaque) written with its support for particles on, as the authoring
  copies it.
- `SpriteClassic`: `VFXSpriteLit` given to `Output Particle Quad` instead, through that
  output's own Shader Graph setting.
- `PlainUnlit`: such quads drawn by `Output Particle Quad` without a Shader Graph, blended over
  what is behind.
- `MeshSorted`, `MeshLitSorted`: the wedges of `MeshUnlit` and `MeshLit` blended over what is
  behind them, half transparent, spread over a depth of three units: such an output sorts its
  particles by default and draws them through a sorted indirect buffer. `MeshSorted`'s mesh is
  the exposed `Shape`. `MeshListed`: opaque wedges of an output told to draw through an indirect
  buffer, from the list its system's update makes (not sorted).
- `LitListed`: opaque lit quads told to draw through an indirect buffer, from a list that is not
  sorted. `SpriteListed`: the lit sprite graph's quads (`Output Particle ShaderGraph Quad`)
  blended over what is behind them, their sort turned off and culled by each camera's frustum:
  each camera draws a list of its own, not sorted.
- `MeshPair`: an `Output Particle Mesh` of two meshes, the wedge (its first submesh alone) and the
  spike, each particle's mesh index drawn between 0 and 2. `MeshLod`: the same two meshes chosen
  by the size each particle takes on screen (`lodValues` 30, 0, 0, 0; sizes between 0.3 and 1.2).
- `GraphLegacy`: quads of `Output Particle Quad` drawn by `Assets/Graphs/Legacy.shadergraph`, a
  graph of the Visual Effect target Shader Graph keeps for older graphs (shown as deprecated),
  written through Shader Graph's own model: unlit, its colour the probe's image sampled through
  a texture property of its own. `GraphLegacyMesh`: wedges of `Output Particle Mesh` drawn by
  `Assets/Graphs/LegacyDefault.shadergraph`, the same graph sampling the image as its sampling
  node's default texture (a texture slot nothing feeds).
- `GraphLit`: quads of `Output Particle ShaderGraph Quad` drawn by `Assets/Graphs/Lit.shadergraph`,
  the render pipeline's lit graph (URP Lit, opaque, its default surface) written with its
  support for particles on, its base colour the probe's image through a texture property of its
  own. Such a graph has the lightmapper's pass (`Meta`) besides the passes cameras draw.
- `PointStill`, `PointList`, `LineStill`, `LineList`, `LineSoft`: sixty-four opaque points or
  lines (from each particle's position to its target position) at random places, each a colour
  of its own, drawn by the output itself or through an indirect buffer (`*List`: the list the
  update makes); lines as native lines, `LineSoft` as quads a pixel wide (the output's default).
- `MovingLit`, `MovingMeshes`: twenty opaque particles moving sideways fast (6 units a second),
  writing motion vectors: quads lit by the render pipeline (no base colour map), and the
  package's default mesh turned each its own way (unlit).
- `SingleMesh`: two single mesh outputs (`Output Single Mesh`) of the probe's wedge, turned
  and scaled by their transforms: one through the render pipeline's unlit shader (both
  submeshes, its colour an exposed property of the output), one through its lit shader (the
  first submesh only, another colour and a smoothness of the output's).
- `LitDecal`: five decals of the render pipeline (`Output Particle URP Lit Decal`), boxes 0.9
  across centred on a wall's plane, each turned about the axis it projects along and of a
  colour of its own, the probe's image as their base colour map (with its alpha).

`Assets/Objects/Spike.asset` is a second mesh, of one submesh. The project names its own URP
global settings (`Assets/UniversalRenderPipelineGlobalSettings.asset`, with its default volume
profile, mapped in `ProjectSettings/GraphicsSettings.asset`), so the Editor makes none of its own. The pipeline asset
(`Assets/Settings/ProbeURP.asset`) has four renderers: a forward one, a deferred one
(`ProbeDeferred.asset`), a 2D one (`Probe2DRenderer.asset`, the 2D renderer's defaults) and a
forward one with the render pipeline's decal feature (`ProbeDecal.asset`, the feature's
defaults: its technique chosen for the device, screen space on OpenGL Core); main light shadows are on (one cascade, hard), additional lights are
per pixel and cast no shadows. The scene has one flat ambient colour and no reflection, and 2D
lights, which only the 2D renderer draws by: a global one (white, 0.4) and, at the places of
`sprite point`, `sprite normals`, `sprite classic` and `sprite listed`, a point light of radius two, the one of
`sprite normals` lighting by normals (accurate, at a distance of one).

## What is recorded

`Assets/Scripts/LitProbe.cs` makes a directional light that casts hard shadows and, per control,
a stage far from the others: the effect, an orthographic camera rendering into a 64x64 texture
and, where said, a point or spot light of the stage's own. The frames named are saved as
`draw-<control>-f<frame>.png` at the end of the frame, with a row `draw/<control>/f<frame>` (the
effect's alive count, whether it is culled and awake, its renderer's state).

- `matte`, `additive`: the directional light and the ambient colour alone. `smooth`, `mapped`,
  `outputs`, `mesh lit`: with a point light. `metal`: with a spot light.
- `shadowed`: `LitMatte` under a box of the scene's that the camera does not draw: its shadow
  falls across the quads. The effect's renderer says it receives no shadows (`renderer/*`) and
  the quads are shadowed all the same: the generated shader samples the shadow map itself.
- `caster`, `mesh caster`, `unlit caster`: lit quads, lit meshes and unlit quads that cast
  shadows, on each other (the lit ones) and on a lit wall of the scene's behind them.
- `offscreen caster`: `LitCaster` moved up the sun's rays, out of the camera's view: its
  shadows fall on the wall in view. Rows at frames 5 and 12 say whether the effect is culled.
- `hidden caster`, `hidden` (rows alone, frames 5 and 12): `LitCaster` and `LitMatte` far from
  every camera and from what any camera's shadows cover. "Regardless of the mode, Unity always
  uses the bounding box to perform culling of the effect" (Visual Effect Graph 17.0 manual, the
  asset's Culling Flags): an effect that casts shadows is culled like one that does not.
- `plain shadow`: no effect at all: a box of the scene's before such a wall under such a box.
  What differs in this image differs without any effect (see the image limits).
- `deferred`, `mesh deferred`: `LitMatte` and `MeshLit` through the deferred renderer (the
  outputs' `UniversalGBuffer` pass), with a point light.
- `mesh unlit`: the wedge textured with the image whose quarters differ.
- `mesh exposed`: both submeshes (frame 5), then what the probe sets (`act/mesh exposed/*` rows
  say what was set and what the effect then answers): mask 1 (frame 8: the first submesh), mask
  2 (11: the second), mask `0xfffffffe` (14: the second, the mask's other bits name submeshes the
  mesh does not have), the mesh `Spike` under that mask (17: nothing, its one submesh is not
  named), mask `0xffffffff` (20: the spike), `SetMesh(name, null)` (it throws
  `ArgumentNullException` for `m` and the effect keeps its mesh), the wedge again (23).
- `mesh none`: nothing is drawn of an output whose mesh is none.
- `mesh dying`: frames 12 and 24.
- `lit sorted`: `LitSorted` with a point light: where two quads overlap, the one drawn later
  shows through less of the other.
- `curve output`: frames 6, 12 and 24: quads of every age, each of the size and colour of its
  age.
- `renderer/<control>`: what the effect's renderer is set to (shadow casting, receiving, motion
  vectors, probes) and the names of its materials: one per output, by the outputs' sort priority
  and, among outputs of one priority, in the graph's order (`outputs`: the unlit quads' first,
  which the graph lists second; then the lit output's and the meshes', which the graph lists in
  that order). `renderer/no asset`, `asset given`, `asset taken`: an effect without an asset,
  given one whose graph casts shadows, and with none again. `renderer/motion vectors`: an asset
  with an output that writes motion vectors. `renderer/saved settings`: `LitReceiving`.
- `null/*`: what `SetMesh`, `SetTexture`, `SetAnimationCurve` and `SetGradient` throw when given
  null (`ArgumentNullException` naming the parameter: `SetMesh` for a property the effect has
  and for a name it does not have, the three others for a name it does not have), and `SetMesh`
  and `SetTexture` when given an object that was destroyed (`MissingReferenceException`);
  `null/Shape after`: the mesh the effect then answers, the one it had.
- Through the 2D renderer, each at a place of a row of their own where the scene's 2D lights
  stand: `sprite` (`SpriteLit` under the global light), `sprite point`, `sprite normals` (with
  the point light that lights by normals: the outputs' normals pass is drawn), `sprite unlit`,
  `sprite classic` (nothing is drawn: the output given the graph through its own setting has no
  material, `renderer/sprite classic`), `plain 2d` (`PlainUnlit`).
- `sprite sorting`: `SpriteUnlit` between two unlit sprites of the image whose quarters differ
  (untinted), one in sorting order -1 nearer the camera than the effect, one in order 1 further
  away: the 2D renderer draws them by their order, not by their distance (frame 5). At frame 8
  the effect's renderer is set to order 2 (`act/sprite sorting/f008`) and is drawn after both
  (frame 10). "Unity renders sublayers in numerical order, so lower values render behind
  higher values", and only "within a sorting layer, a sublayer, and a render queue" by the
  distance to the camera (Unity 6000.0 manual, 2D renderer sorting).
- `mesh sorted` (frames 5 and 10: at frame 8 `Shape` is set to the spike, of one submesh,
  `act/mesh sorted/f008`), `mesh paused` (the same, paused at frame 6 before the mesh is set: a
  paused effect still takes steps of no time, `tests/projects/vfx`, and draws the new mesh), `mesh lit sorted` (with a point
  light), `mesh listed`: meshes drawn
  through an indirect buffer, one draw of each submesh the mask names, of as many instances as
  the list counts.
- `graph unlit`: `GraphUnlit` through the forward renderer; its renderer casts shadows
  (`renderer/graph unlit`).
- `lit listed` (frames 5 and 12, with a point light), `sprite listed` (through the 2D renderer,
  under a 2D point light): lit outputs drawn from a list that is not sorted, pixel for pixel.
- `mesh pair`: each particle drawn as the mesh its index names; the system's output update puts
  each particle into the list of its mesh, and each mesh's task draws its own list. `mesh lod`:
  its renderer's row alone; which mesh the Editor draws differs from one run to the next (two
  captures differed), so no image of it is kept, and Unbound does not draw such an output.
- `graph legacy`: the older target's graph, through the forward renderer. `graph legacy mesh`:
  the older target's graph of a default texture on meshes.
- `graph lit`: `GraphLit` under a point light through the forward renderer: the output is drawn
  as its lit graph has it; its lightmapper pass is drawn by no camera. Six pixels where two
  quads or two quarters of the image meet differ by at most 3 levels (`image_limits.json`).
- `lit 2d`, `mesh 2d`: `LitMatte` and `MeshUnlit` through the 2D renderer: the lit quads draw
  nothing there (no pass of theirs is one the 2D renderer draws), the meshes are drawn.
- `single mesh`: `SingleMesh` under the sun and a point light; the effect has no particle
  system (its alive count is 0 and no system is awake), and its renderer lists both outputs'
  materials.
- `moving lit`, `moving meshes`: `MovingLit` and `MovingMeshes` under a point light, their
  cameras with post-processing and a volume of object motion blur (camera and objects, high
  quality, intensity 1) only they read, at frames 9 and 10.
- `points`, `points listed`, `lines`, `lines listed`, `lines soft`: those graphs at frame 5.
- `decal`: `LitDecal` projected onto a lit wall through the renderer with the decal feature,
  under the sun and a point light (frames 5 and 12): the decal feature draws the output's
  screen space pass; its DBuffer and GBuffer passes exclude OpenGL Core.
- `capture/graphs imported while playing` must be empty (a capture where it is not stays
  incomplete).

## Image limits

Every image is the Editor's pixel for pixel on the machine both ran on (an NVIDIA driver, the
Linux Editor on OpenGL Core), except where a shadow's edge is drawn: the images with shadows
(`shadowed`, the `caster` controls) and the control without any effect, `plain shadow`, differ
in a few pixels on shadow edges. `image_limits.json` records the residuals as measured. Their
cause is not resolved: the control without an effect has them too, so the pipeline's shadows
of the scene's own objects differ at caster edges already; whether the effects' shadow pass
adds to that is not separated.
`graph lit` differs in six pixels where two quads or two quarters of the image meet, by at most
3 levels (cause not resolved; the unlit graph's and the lit output's own quads match exactly).

## Not covered

- Motion vectors of lit mesh, transparent, strip, Shader Graph and cube outputs, and of particles
  that turn or change size between frames (lit quads and unlit meshes moving sideways are
  measured here, `moving lit`, `moving meshes`; unlit quads in `tests/projects/vfxoutputs`).
  Strips lit by 2D lights and meshes chosen among several by
  their size on screen: not drawn; each says so once for its asset (strips lit by the
  pipeline's lights are drawn: `tests/projects/vfxstrips` `lit`). One mesh
  culled by its size on screen is drawn, not measured. Outputs of three or four
  meshes, several meshes through a sorted list or by a lit output: drawn as `mesh pair`, not
  measured.
- A sorted lit output with another sort criterion than the default, of quads that cut
  through each other, of other primitives than quads, that casts shadows, or that is not
  blended over what is behind it (an opaque or additive one made to sort: its depth and
  deferred passes then draw the sorted list too); a curve or a
  gradient sampled by a lit, mesh or strip output, or by other blocks than the two here.
- Shadows cast by an output drawn through an indirect buffer (the casters here draw
  themselves): such an output's shadow pass is drawn from the list its colour is drawn from, a
  frustum-culled one included, where the Editor may leave out of the shadows the particles its
  culling left out.
- A lit output that fades against the camera's depth (soft particles are measured for unlit
  outputs, `tests/projects/vfx`).
- Forward+, light cookies, light layers, reflection probes, light probes other than the ambient
  one, fog, additional lights' shadows, soft shadows, shadow cascades, screen space occlusion,
  the specular workflow, normal, occlusion, metallic and emissive maps, the six-way smoke lit
  material, double sided lighting, an output that receives no shadows: the generated shaders
  compile them like any shader, and none is measured here.
- A mesh with vertex colours or tangents, of 32-bit indices, or of other topologies than
  triangles (quads), or whose submeshes' indices are not in submesh order; a mesh or mask of an
  output drawn through an indirect buffer changed in a frame where its system takes no step at
  all (a frame shorter than the fixed step: the draw then describes its submeshes anew).
- The depth passes of an opaque output (a depth prepass, the camera's depth and normals
  textures), an additive lit output through the deferred renderer, the order in which several
  outputs of an effect are drawn where it shows.
- A script that sets the renderer's shadow casting itself; a mesh destroyed while an effect uses
  it (the Editor then answers with an object that is no longer there).
- Shader Graph outputs other than the composed quad output with the graphs here: lit Shader
  Graphs of the forward renderer, other topologies, strips, lit graphs of the deprecated Visual
  Effect target or ones through the 2D renderer, material overrides of a graph that allows
  them; sprites in sorting layers other than the default one, sprite masks, 2D shadows and the
  camera sorting layer texture with effects.
- Decals through the deferred renderer or the DBuffer technique (whose passes OpenGL Core
  drops) or through a renderer without the decal feature, the order of overlapping decals (the
  output sorts by default; the five here do not overlap), decals with normal, mask or emissive
  maps, angle fade, decal layers, and decals over other effects. The decals stand still: frame
  12 shows what frame 5 does.
- Other devices than OpenGL Core in the Editor.

## Running

    python3 tests/projects/vfxlit/run.py record --label a     # one Editor capture (GPU display)
    python3 tests/projects/vfxlit/run.py record --label b
    python3 tests/projects/vfxlit/run.py publish a b          # identical captures -> Reference/ and Authored/
    python3 tests/projects/vfxlit/run.py gate                 # the engine on the authored project
