# Terrain lightmap oracle

Unity 6000.0.71f1, built-in pipeline. `ProbeRunner.Create` authored the TerrainData,
layer, scene, prefab and a real CPU bake in `Assets/Scenes/Probe/`. The scene has
one flat 4 by 4 terrain. The runtime probe adds a second terrain using the same
data and material, then supplies deterministic floating-point lightmaps. The
custom diagnostic shader reports native vertex UV channels, transformed lightmap
UVs, raw color sampling and directional-map keyword/sampling. The rendered
lighting cases use the installed `Nature/Terrain/Diffuse` and
`Nature/Terrain/Standard` shaders. The baked scene uses the installed default
terrain material. An ordinary Lambert surface-shader mesh has constant UV0,
different UV2 coordinates, and nontrivial, distinct lightmap STs on two renderers.
Its directional and unbound cases test the shared surface compiler too.

The prefab was saved by Unity after assigning lightmap fields. Its YAML omits
those fields, as does the scene YAML. Baked scene bindings live in LightingData.
Runtime clones and imported prefabs are recorded separately.

All Editor, cargo, managed build and engine commands in `run.sh` acquire the
shared heavy-job lock. Managed outputs are isolated under
`target/terrain-lightmaps/{build,managed}`. Cargo uses the private
`target/terrain-lightmaps/rust` target and two build jobs, with the host's nested
managed build disabled. Engine runs select that private CLI and the private
managed output through UNBOUND_MANAGED_DIR. No protocol or native ABI changed.
The private dependency cache was seeded with independent copies from the shared
release cache, including the unchanged vendored native SDK archives; no shared
output was overwritten or hard-linked into the private cache.

Run from the repository root, in this order so the engine sees each color space:

```sh
bash tests/projects/terrainlightmaps/run.sh rust-build
bash tests/projects/terrainlightmaps/run.sh rust-test
bash tests/projects/terrainlightmaps/run.sh build
# Assets and bake are already committed. Create is only for rebuilding the fixture.
# bash tests/projects/terrainlightmaps/run.sh create
bash tests/projects/terrainlightmaps/run.sh native gamma-a Gamma
bash tests/projects/terrainlightmaps/run.sh native gamma-b Gamma
bash tests/projects/terrainlightmaps/run.sh engine gamma
bash tests/projects/terrainlightmaps/run.sh native linear-a Linear
bash tests/projects/terrainlightmaps/run.sh native linear-b Linear
bash tests/projects/terrainlightmaps/run.sh engine linear
bash tests/projects/terrainlightmaps/regression.sh
python3 tests/projects/terrainlightmaps/compare.py tests/projects/terrainlightmaps/Reference/linear-a tests/projects/terrainlightmaps/Reference/linear-b
python3 tests/projects/terrainlightmaps/compare.py tests/projects/terrainlightmaps/Reference/gamma-a tests/projects/terrainlightmaps/Reference/gamma-b
python3 tests/projects/terrainlightmaps/compare.py tests/projects/terrainlightmaps/Reference/linear-a tests/projects/terrainlightmaps/Library/linear
python3 tests/projects/terrainlightmaps/compare.py tests/projects/terrainlightmaps/Reference/gamma-a tests/projects/terrainlightmaps/Library/gamma
python3 tests/projects/terrain/compare.py tests/projects/terrain/Reference tests/projects/terrainlightmaps/Library/terrain-regression
```

`compare.py` checks every field and sampled pixel, and every pixel in every PNG.
Field tolerance is 1e-5; readback samples allow one byte; whole-image limits are
max 2 bytes and mean .25 bytes, including alpha. Missing records or images fail.
No defined values are excluded. Native repetition must also be byte-identical.

Two fresh Editor processes in each color space produce 1,335 records and 41
PNGs each, all byte-identical within that color space. The four final recordings
are in `Reference/{linear,gamma}-{a,b}`. `Evidence/native-repetition.json` records
the counts and SHA256 hashes. No real game project was run or modified.

Initial oracle at be73c737, before implementation: two fresh Editor processes produced
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

The shared surface compiler previously set `UnityGIInput.lightmapUV` to zero.
It now interpolates UV2 after the vertex modifier, applies unity_LightmapST, and
passes the result to GI in the forward base pass. Forward add stays free of baked
GI. Shader cache FORMAT_VERSION changes from 12 to 13. The full unbound-shaders
Rust suite passes 34/34 tests, including installed-Unity-include compilation.

The explicit realtime index of 5 is retained in cases 12, 13, 14 and 19. Unity
enables DYNAMICLIGHTMAP_ON and changes the lit result despite this fixture having
no authored realtime bake. Unbound reports unsupported realtime GI and draws the
baked contribution. These cases remain in all totals and comparisons. This work
does not implement realtime chart allocation, dynamic textures or their shader
bindings. Baked mixed shadowmask combination and multi-layer terrain add-pass
lightmaps are not certified by this fixture. Other Unity release families have
not been measured.

Final private-CLI comparisons on the NVIDIA RTX 4060, driver 580.178.04:

| Comparison | Records | PNGs | Largest difference in passing images |
| --- | --- | --- | --- |
| Native Linear a vs b | 1335/1335, byte-identical | 41/41, byte-identical | 0 |
| Native Gamma a vs b | 1335/1335, byte-identical | 41/41, byte-identical | 0 |
| Unbound vs Native Linear | 1271/1335 | 37/41 | 1 byte, worst mean .019531 bytes |
| Unbound vs Native Gamma | 1271/1335 | 37/41 | 1 byte, worst mean .005859 bytes |

All 23 non-pixel field records match in each color space. The 64 failing sampled
pixel records per color space belong to cases 12, 13, 14 and 19, exactly 16 samples
on the terrain with realtime index 5 in each case. The other terrain in each of
those captures agrees. No values or images are excluded from the totals.

The whole-image baked-only Diffuse gradients are cases 27 and 28, and the Standard
gradients are cases 22 and 23. Ordinary surface meshes with UV0 fixed to zero and
nontrivial UV2 plus ST are cases 24 and 25; case 26 is their unbound regression.
Those cases pass in both color spaces. The actual baked-scene import, missing
slots, sentinel indices, live map/ST/index changes, clone reset and material
property-block cases pass too.

Committed evidence:

- `Evidence/native-repetition.json`: strict repetition counts and hashes.
- `Evidence/linear-comparison.txt` and `Evidence/gamma-comparison.txt`: every PNG,
  full record totals and all failures.
- `Evidence/terrain-regression.txt`: 214/216 Native records and parent byte checks.
- `Evidence/rust-tests.txt`: 34 passing Rust tests, none ignored or filtered.

Private build logs are `/tmp/terrain-lightmaps-rust-build.log` and
`/tmp/terrain-lightmaps-build-final.log`; final GPU logs are
`/tmp/terrain-lightmaps-{linear,gamma}-final.log` and
`/tmp/terrain-lightmaps-terrain-final.log`. The terrain regression and final
lightmap runs report no errors. Lightmap runs report the explicit realtime-GI
warning once. Engine captures remain in this fixture's ignored `Library/`
directory. Shared parity documents, original terrain/baked references, API stubs,
protocol and native ABI are unchanged. Parent handles the integrated API check.
