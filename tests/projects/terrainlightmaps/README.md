# Terrain lightmap oracle

Unity 6000.0.71f1, built-in pipeline. `ProbeRunner.Create` authored the terrain,
layer, scene, prefab and CPU bake in `Assets/Scenes/Probe/`. Runtime cases supply
floating-point lightmaps to two terrains with distinct bindings. Diagnostic
shaders report UVs, lightmap scale/offset, color sampling and directional-map
behavior. Lighting cases use the built-in Diffuse and Standard terrain shaders.
Ordinary surface meshes have constant UV0 and nontrivial UV2 plus scale/offset.
Cases 29 to 35 give terrains and meshes a realtime lightmap index (5, and 0 beside a
baked lightmap) with no realtime lightmap behind it: the diagnostic shader reads the
bound `unity_DynamicLightmap`, `unity_DynamicDirectionality` and `unity_DynamicLightmap_HDR`,
and the Diffuse terrain shader and a surface shader draw with them in both lightmaps modes.

`Reference/unity-6000.0.71f1/{Linear,Gamma}` holds one canonical Native recording
per color space. Each contains 1,559 records and 48 PNGs (the first 1,335 records
and 41 PNGs as first recorded, cases 29 to 35 added). Two independent Editor
processes produced byte-identical recordings per space. `compare.py` checks every
field, sampled pixel and PNG. Field tolerance is 1e-5; readback samples allow one
byte; whole-image limits remain max 2 bytes and mean .25 bytes, including alpha.
Missing records or images fail. No values or images are excluded.

The engine matches 1,559/1,559 records and 48/48 PNGs per space. A realtime index
with no realtime lightmap behind it binds a grey texture of half alpha (128 in every
channel, sRGB) for both realtime maps, decoded (1, 1) in linear and (1, 1/2.2) in
gamma, with `DYNAMICLIGHTMAP_ON` and, in the directional mode, `DIRLIGHTMAP_COMBINED`
(cases 12, 13, 14, 19, 29 to 35). Baked import, UV2/ST, gradients, invalid bindings,
clones and property blocks pass.
The ordinary terrain regression retains its two inherited steepness differences,
214/216 records, with unchanged parent/candidate captures.

`run.sh` and `regression.sh` acquire `~/.cache/compat/with-heavy-job`. Builds and
outputs default to
`${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/terrainlightmaps/`.
`TERRAIN_LIGHTMAP_ROOT`, `TERRAIN_LIGHTMAP_OUT`, `TERRAIN_LIGHTMAP_CLI`,
`UNBOUND_MANAGED_DIR` and `UNBOUND_NATIVE_DIR` override the paths.
Both Native and engine runs use generated color-space project copies under
`Library/Unbound`. Authored settings are unchanged. `UNBOUND_PROBE_UNITY_VERSION`
selects the Editor and reference release; `UNBOUND_PROBE_UNITY` overrides its executable.

```sh
f=tests/projects/terrainlightmaps
cache="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/terrainlightmaps"
bash "$f/run.sh" rust-build
bash "$f/run.sh" rust-test
bash "$f/run.sh" build
bash "$f/run.sh" native native1/Linear Linear
bash "$f/run.sh" native native2/Linear Linear
bash "$f/run.sh" native native1/Gamma Gamma
bash "$f/run.sh" native native2/Gamma Gamma
bash "$f/run.sh" engine current/Linear Linear
bash "$f/run.sh" engine current/Gamma Gamma
ref="$f/Reference/unity-${UNBOUND_PROBE_UNITY_VERSION:-6000.0.71f1}"
python3 "$f/compare.py" "$ref/Linear" "$cache/current/Linear"
python3 "$f/compare.py" "$ref/Gamma" "$cache/current/Gamma"
python3 "$f/verify.py" --repeat-linear "$cache/native2/Linear" --repeat-gamma "$cache/native2/Gamma"
bash "$f/regression.sh"
```

For a new release, `verify.py --record-reference` requires `--native-linear`,
`--repeat-linear`, `--native-gamma` and `--repeat-gamma`. It verifies exact Native
repetition and refuses to overwrite the versioned oracle. Otherwise `verify.py`
compares the canonical references with the selected captures and writes reports
and manifests to the run cache. `--linear`, `--gamma` and `--out` select existing
captures and report destinations. Rebuilding the authored fixture with `create`
is an explicit Unity-compatible source edit and is unnecessary for normal runs.
