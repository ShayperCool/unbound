# Terrain lightmap oracle

Unity 6000.0.71f1, built-in pipeline. `ProbeRunner.Create` authored the TerrainData,
layer, scene, prefab and a real CPU bake in `Assets/Scenes/Probe/`. The scene has
one flat 4 by 4 terrain. The runtime probe adds a second terrain using the same
data and material, then supplies deterministic floating-point lightmaps. The
custom diagnostic shader reports native vertex UV channels, transformed lightmap
UVs, raw color sampling and directional-map keyword/sampling. The rendered
lighting cases use the installed `Nature/Terrain/Diffuse` shader and the baked
scene uses the installed default terrain material.

The prefab was saved by Unity after assigning lightmap fields. Its YAML omits
those fields, as does the scene YAML. Baked scene bindings live in LightingData.
Runtime clones and imported prefabs are recorded separately.

All Editor, managed build and engine commands in `run.sh` acquire the shared
heavy-job lock. Managed outputs are isolated under `target/terrain-lightmaps/`.
Run from the repository root:

```sh
tests/projects/terrainlightmaps/run.sh create
tests/projects/terrainlightmaps/run.sh native linear-a Linear
tests/projects/terrainlightmaps/run.sh native linear-b Linear
tests/projects/terrainlightmaps/run.sh build
tests/projects/terrainlightmaps/run.sh engine linear
python3 tests/projects/terrainlightmaps/compare.py tests/projects/terrainlightmaps/Reference/linear-a tests/projects/terrainlightmaps/Reference/linear-b
python3 tests/projects/terrainlightmaps/compare.py tests/projects/terrainlightmaps/Reference/linear-a tests/projects/terrainlightmaps/Library/linear
```

`compare.py` checks every field and sampled pixel, and every pixel in every PNG.
Field tolerance is 1e-5; readback samples allow one byte; whole-image limits are
max 2 bytes and mean .25 bytes, including alpha. Missing records or images fail.
No defined values are excluded. Native repetition must also be byte-identical.

Validation results and remaining limits will be recorded after the oracle runs.

Initial oracle, before implementation: two fresh Editor processes produced
byte-identical JSONL and all 27 PNGs in `Reference/linear-a` and `linear-b`.
There are 879 records per run. Parent cde799cb managed code matched 363/879
records and 9/27 images. Its isolated capture is `Library/before`; the build
and run logs are `/tmp/terrain-lightmaps-build.log` and
`/tmp/terrain-lightmaps-before.log`. That first run lacked the PhysX library;
the probe completed, but collider enable emitted loader errors. Subsequent
runs must use the native library search path.

The managed binding fix reads Terrain entries from LightingData, forwards per-draw
indices and STs, supplies normalized XZ UVs, and applies splat property blocks.
Both indices normalize out-of-range assignments to -1 while retaining 65534.
Native Instantiate resets all four lightmap properties; the implementation keeps
that reset, rather than copying runtime or baked bindings. Missing color slots
bind black; absent slots bind the default grey texture. Directional mode keeps
its keyword even if an individual slot lacks a direction texture.

After the managed fix, the baked scene PNG and diagnostic UV, ST, gradient,
missing/invalid bindings, directional sampling, disabled terrain, property-block
and clone PNGs match Native exactly. Realtime chart rendering is unsupported and
now reports that limitation when a terrain with a realtime index is drawn.

Terrain regression with the same CLI and separate cde799cb managed output:
214/216 records against Native, with the two inherited steepness differences.
Candidate and cde799cb have byte-identical records and all six PNGs. Outputs are
`Library/terrain-baseline` and `Library/terrain-regression`. Baseline build uses
`target/terrain-lightmaps/build-baseline.sh` and separate `baseline-build/` and
`baseline-managed/` directories. It temporarily restores only the three changed
managed files from cde799cb, builds, then restores the implementation with an EXIT
trap. No second checkout is created.
