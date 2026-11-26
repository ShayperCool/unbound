# Controls for creation defaults

Controls for rows `tests/projects/defaultstate` queued: each new value is set apart from the
context it explains. The probe sets a value after creation, uses another context or object, or
passes an argument that takes another path. Unity 6000.0.71f1, recorded with the macOS Editor
in batch mode (two identical captures). The values depend on neither the OS nor the graphics
device: the probe reads no device formats, only whether a depth format has stencil.

`Assets/Scripts/DefaultControlsProbe.cs` records in Play mode into values.jsonl in
`$DEFAULTCONTROLS_OUT` (tests/value_probe.py). Floats are written with `"R"`. An exception is written as
`throws <type>: <message>`, and the log lines a read writes are a `.log` row of their own.

At once:

* `cmd.*`: CommandBuffer.name new, set, set to empty, set to null, after Clear.
* `rtformat.<value>.<name>`: GraphicsFormatUtility.GetRenderTextureFormat of every graphics
  format.
* `attach.*`, `rtd.*`: AttachmentDescriptor.format and RenderTextureDescriptor.colorFormat (with
  the graphics format, sampling mode and kind of depth format) of default descriptors, of ones
  made from formats, and with the sampling mode, color format and depth bits set afterwards.
* `srf.*`: SupportedRenderingFeatures.terrainDetailUnsupported new and set.
* `bfs.*`, `go.*`, `editor.*`: BatchFilterSettings.sceneCullingMask default and set; a
  GameObject's mask in the scene, new, inactive, in DontDestroyOnLoad and in a created scene;
  EditorSceneManager.GetSceneCullingMask.
* `light.*`: Light.layerShadowCullDistances and whether the array is a copy.
* `lpg.*`: LightProbeGroup.probePositions added by AddComponent (active and inactive object),
  loaded empty and with two points (the scene's AuthoredEmpty and AuthoredTwo), instantiated
  from those and from an added group, set empty.
* `readback.*`, `culling.*`: a default AsyncGPUReadbackRequest and a default CullingResults.
* `animator.*`: feetPivotActive, target, recorder and playback values of a new Animator, on an
  inactive object, with a controller, activated later, disabled and deactivated; set values.

Then staged: objects made at once and read new, after the next physics step, after two more
frames, after a destination is set and two frames later (`<key>.<stage>`).

* `geom.*`: Collider.GeometryHolder of box, capsule (three directions, and every direction on
  non-uniformly scaled objects, one shorter than its width), sphere, controllers (default, larger,
  thicker skin, scaled), mesh (none, triangles, convex), terrain (empty and with data) and wheel
  colliders, scaled and mirrored objects, a disabled and an inactive collider.
* `agent.*`: NavMeshAgent on, off and disabled on a NavMesh built at run time, and with a
  destination set on and off it (the moving agent is not read two frames later: its position
  follows the frame times).
* `obstacle.*`: NavMeshObstacle height, radius, size and center of a box and a capsule, with
  the height, radius, size or center set, through shape changes and a shape set to itself, and
  added to objects with a sphere mesh and an offset, stretched cube mesh, then their shape changed.
* `trail.*`, `line.new`: bounds of trails without points (at, away from the origin, wider,
  scaled) and of a new line.
* `tilemap.*`: cell and local bounds of an empty tilemap and with a tile without and with a
  sprite.
* `canvas.*`: Canvas.renderingDisplaySize of overlay, camera without camera, world space, a
  resized world-space canvas and a new canvas (with its render mode).
* `physics2d.*`: PhysicsScene2D.subStepCount of the default and of local scenes (empty, with a
  dynamic body, a static collider alone, a kinematic body alone, an unsimulated body alone), after
  their simulations, in Script mode and with sub-stepping (with the lost time).
* `terrainData.*`: TerrainData.heightmapTexture and holesTexture.

What it shows: a command buffer is named "Unnamed command buffer" until named (empty and null
read empty). GetRenderTextureFormat answers 29 (past the last RenderTextureFormat) for every
format without a RenderTextureFormat. That covers None and the depth and stencil formats; YUV2
answers 21. A descriptor without a color format is Depth, or Shadowmap whenever it samples depth
(a default descriptor compares). An attachment of a depth format is Depth.
Scene culling masks of GameObjects and an unset filter are the Editor's default mask. A new
light's 32 layer distances are 0, and the array is a copy. A LightProbeGroup added in the
Editor starts with the corners of a cube of half-size 1, and a loaded or instantiated one keeps
its own (also none). A default readback is done; a default CullingResults throws. A new
Animator's feet pivot is 1 and takes any value, but only while its hierarchy is active (inactive:
0, sets ignored; each activation sets 1 again; a disabled component keeps its value). Without
recording, the recorder's times and the playback time read -1 and sets are ignored; reading the
playback time warns. An agent not placed on a NavMesh reads an infinite destination and remaining
distance and an invalid path, and reading the remaining distance logs an error, as a disabled
agent's does. A trail without points spans the world origin and the object, widened by the
width multiplier. A world-space canvas's display size is its rectangle, the others' the game
view. A NavMeshObstacle keeps one box of extents for both shapes: its height reads half the
size's height and setting it sets that height, its radius reads half the size's width and setting
it sets the width and depth, and a shape change fits it again as adding it does: around its
mesh's bounds (a capsule as wide as their wider side), else the new shape's default size (a
capsule's is 1 x 2 x 1) about the origin. A 2D physics world without bodies or colliders reports
0 sub-steps and no lost time after a step; with any it reports the step's.

`residuals.json` lists the differences still open: collider geometry holders (the measured rule is
in the entry) and terrain textures.

Record and gate:

    python3 tests/projects/defaultcontrols/run.py record --label a   # twice, then
    python3 tests/projects/defaultcontrols/run.py publish a b
    python3 tests/projects/defaultcontrols/run.py gate
