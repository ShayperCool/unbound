# apvbake

Measures `Lightmapping.Bake()` on a URP 17 scene (`Assets/Scenes/Apv.unity`) containing a global
Adaptive Probe Volume (`ProbeVolume`), its `ProbeVolumeBakingSet` (`Assets/Scenes/Apv Baking Set.asset`),
a ground plane, three GI-contributing cubes, and a baked directional light under the procedural sky
with the Progressive CPU lightmapper.

## Scene geometry

- `Ground` sits at `y = 0.85` (strictly inside brick cell `y = 0`, `[0, 81]`), with its edges at `±5.0`
  and all cube faces placed inside the voxel-center safe bands of `VoxelizeScene.shader`'s `64³` cell
  grid (`[(k + 0.5) * 81/64, (k + 1) * 81/64)`). `VoxelizeScene` uses SubShader 0 (`Conservative True`
  geometry shader) on Linux Vulkan and falls back to SubShader 1 (`VoxelizeMeshFallback`, standard
  pixel-center rasterization) on platforms without geometry shaders (macOS Metal and Unbound);
  keeping mesh faces inside the safe bands ensures both subshaders select identical cells and bricks.
  (On geometry placed directly on a cell or voxel boundary, SubShader 0's conservative dilation can
  activate an adjacent voxel/cell that SubShader 1 does not.)
- Cubes are embedded 5 cm into `Ground` (`y_min = 0.80` vs `Ground` `y = 0.85`) to avoid coincident
  opposite-plane `t` ties.
- `Tall` (`x ∈ [1, 2]`, `y ∈ [0.80, 3.60]`, `z ∈ [-2.1, -0.7]`) places 12 probes directly on the
  interior of its vertical faces (`x ∈ {1, 2}`, `y ∈ {1, 2, 3}`, `z ∈ {-1, -2}`), verifying
  Unity 6000.0's inclusive `t >= 0.0` probe ray intersection (`validity = 0.5` and coplanar direct
  sun shadowing / indirect bounces). `Red` and `Flat` enclose strictly interior probes (`validity = 1.0`).

## Reference

`Reference/unity-6000.0.71f1/` is published from two agreeing serial Linux GPU Unity 6000.0.71f1
Editor captures (`apv_bake.py native` using Vulkan so OpenCL / RadeonRays run without VirtualGL;
`log.txt`, `baking_set.json`, `probes.jsonl`, and `lighting_data.json` are byte-identical across
repeats, and `lightmap0.exr` agrees within `1e-5` across repeats):

- `log.txt`: bake event order (`prv initialized=True`, `bakeStarted`, `lightingDataUpdated`,
  `bakeCompleted`, `bake True`) and all 18 files written under `Assets/` (sorted ordinally).
- `baking_set.json`: normalized `ProbeVolumeBakingSet` layout metadata (4 cells, 30 bricks,
  1,920 probes, chunk sizes, and per-cell brick coordinates/subdivision levels).
- `probes.jsonl`: all 1,920 decoded probes across the 4 cells (world position, validity, neighbor
  validity mask, L0/L1, and L2 spherical harmonics coefficients decoded from the five `.bytes` streams).
- `lighting_data.json`: normalized `LightingData.asset` fields (`m_BakedAmbientProbeInLinear`,
  `m_BakedReflectionProbeCubemaps`, `m_LightmappedRendererData`, `m_Lightmaps`, `m_Lights`,
  `m_LightBakingOutputs`, and empty `m_LightProbes`).
- `lightmap0.exr`: baked URP `Lightmap-0_comp_light.exr` (occupied texels `A > 0.5` verified).

## Usage

```bash
python3 tests/projects/apvbake/apv_bake.py create
python3 tests/projects/apvbake/apv_bake.py native --label run1
python3 tests/projects/apvbake/apv_bake.py native --label run2
python3 tests/projects/apvbake/apv_bake.py publish run1 run2
python3 tests/projects/apvbake/apv_bake.py check
```
