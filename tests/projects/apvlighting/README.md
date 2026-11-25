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
- `grid options`: the grid through a volume of its own (volume layer 12) that overrides five
  `ProbeVolumesOptions`: normal bias 0.4, view bias 0.6, no sampling noise, no leak reduction,
  intensity 0.5.
- `shaders forward`, `shaders forward plus`: Simple Lit, Complex Lit, Baked Lit, a smooth Lit
  sphere (smoothness 0.9), a Lit sphere with `lightProbeUsage = Off` and Unlit (control) in a row
  by the back wall, all six whole in the view.
- `shaders options`: that row through a volume (volume layer 13) that overrides the other options
  the shaders read: `worldOffset` (0.4, 0, 0.3), `scaleBiasWithMinProbeDistance` on,
  `occlusionOnlyReflectionNormalization` off, `animateSamplingNoise` off. The offset moves the
  lighting visibly; with this bake's probe spacing of 1 the scaled biases equal the plain ones,
  and without TAA URP passes frame index 0 for the noise, so those two leave the image as it is
  (unmeasured); no URP 17.0.4 shader reads the normalization clamp
  (`GetReflectionProbeNormalizationFactor` is not called), so it cannot change the image.
- `particles forward`, `particles forward plus`, `simple particles forward`: Particles/Lit and
  Particles/Simple Lit billboards where the grid's spheres are (z -1).
- `room`: the room alone from a corner.

Rows (`values.jsonl`): the probe volume's state at frame 4 (initialized, data loaded, SH bands,
video memory cost).

Unmeasured here: L2 (`ProbeVolumeSHBands.SphericalHarmonicsL2`), per-vertex and mixed SH
evaluation (`ShEvalMode`; Auto is per pixel on this desktop target), sky occlusion, rendering
layers, lighting scenarios and blending, streaming, Shader Graph and VFX Graph lit outputs,
Unbound's Vulkan device and other GPUs.

## Result

Measured in the Unity 6000.0.71f1 Editor (Linux, OpenGL Core, NVIDIA driver; two identical
captures): Unbound on its OpenGL Core device matches every row and draws 18 of the 20 images to
the Editor's pixel; in `room`, at both frames, one pixel of the floor's edge is one level apart
(`image_limits.json`). Drawn without the probe volume's data (a bake whose baking set does not
reference its cell files), every image of the first layout differed by 30 to 44 levels on
average: the images measure the probe volume lighting. `shaders options` differs from
`shaders forward` by 9 levels on average, in the Editor and in Unbound alike.

`bake.py bake` lets the Editor run 30 updates after `Lightmapping.Bake()` returns before it saves
and quits: the baking set gets the references to its cell files after Bake returns, and an Editor
that quits at once leaves them out of the saved set (nothing loads the cells then; measured). The
bake fails unless the saved set references its five cell files.

## Usage

```bash
python3 tests/projects/apvlighting/bake.py create          # once: the scene (commit it)
python3 tests/projects/apvlighting/bake.py bake --label b1 # the authored set (commit it)
python3 tests/projects/apvlighting/run.py record --label r1
python3 tests/projects/apvlighting/run.py record --label r2
python3 tests/projects/apvlighting/run.py publish r1 r2
python3 tests/projects/apvlighting/run.py gate
```
