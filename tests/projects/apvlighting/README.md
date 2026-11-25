# apvlighting

Objects lit by a baked Adaptive Probe Volume (URP 17, `PROBE_VOLUMES_L1`), drawn by the Unity
Editor and by Unbound from the same bake.

## Scene and bake

`Assets/Scenes/Lighting.unity` (made by `bake.py create`, `Assets/Editor/ApvLightingSetup.cs`):
a room of static boxes lit by the probe volume (`ReceiveGI.LightProbes`, no lightmaps): a grey
floor, a red wall at `x = -4.1`, a green one at `x = 4.1`, a white back wall and a roof over the red
half, under the procedural sky with a baked directional light (`Sun`, rotation `(45, 30, 0)`,
intensity 1.5, hard shadows), a global `ProbeVolume` and its `ProbeVolumeBakingSet`.

`bake.py bake` bakes it with `Lightmapping.Bake()` in the Vulkan Editor (batch mode; the probe
volume bake runs on the GPU there, as in `tests/projects/apvbake`) and keeps every file the Editor
wrote as the authored set `Authored/unity-<release>/baked` (`tests/authored_project.py`): the
probe volume's cell data, the scene with its per-scene data, the baking set and the lighting data.
The Editor's own bake is the input of both sides, so the probe measures drawing with probe
volume data, not baking it (`tests/projects/apvbake` measures the bake).

## Capture

`Capture/` (`ApvLightingRunner.cs`, `ApvLightingProbe.cs`) is copied into the assembled project
at `Assets/ApvLightingCapture/` for `record` and `gate` only: the bake does not depend on it. In
Play mode the probe adds dynamic objects (none takes part in the bake) and cameras that render
96x96 textures, saved at frames 4 and 20 (`<stage>-<frame>.png`):

- `grid forward`, `grid forward plus`: 15 matte Lit spheres across the room (x -3..3, y 0.5..2.5,
  z 0), under the roof by the red wall and in the sun by the green one, through the forward and
  the Forward+ renderer.
- `grid options`: the grid through a volume of its own (volume layer 12) that overrides the
  `ProbeVolumesOptions` the shaders read: normal bias 0.4, view bias 0.6, no sampling noise, no
  leak reduction, intensity 0.5.
- `shaders forward`, `shaders forward plus`: Simple Lit, Complex Lit, Baked Lit, a Lit sphere with
  `lightProbeUsage = Off` and Unlit (control) in a row by the back wall.
- `particles forward`, `particles forward plus`, `simple particles forward`: Particles/Lit and
  Particles/Simple Lit billboards where the grid's spheres are (z -1).
- `room`: the room alone from a corner.

Rows (`values.jsonl`): the probe volume's state at frame 4 (initialized, data loaded, SH bands,
minimum brick size, maximum subdivision).

Unmeasured here: L2 (`ProbeVolumeSHBands.SphericalHarmonicsL2`), per-vertex and mixed SH
evaluation (`ShEvalMode`; Auto is per pixel on this desktop target), sky occlusion, rendering
layers, lighting scenarios and blending, streaming, Shader Graph and VFX Graph lit outputs.

## Usage

```bash
python3 tests/projects/apvlighting/bake.py create          # once: the scene (commit it)
python3 tests/projects/apvlighting/bake.py bake --label b1 # the authored set (commit it)
python3 tests/projects/apvlighting/run.py record --label r1
python3 tests/projects/apvlighting/run.py record --label r2
python3 tests/projects/apvlighting/run.py publish r1 r2
python3 tests/projects/apvlighting/run.py gate
```
