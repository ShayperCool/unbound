# Material color transfer oracle

Unity 6000.0.71f1 on Linux, NVIDIA OpenGL Core. The shader returns one property
without arithmetic into a 4x4 single-sample ARGBFloat linear target. All uniforms,
fragment output, and CPU readback use full float precision. There are no cameras,
effects, blends, screen reads or half/fixed shader variables. A command-buffer mesh
draw tests material and property-block bindings through the same path.

CPU getters are recorded before and after each draw, independently of all GPU
channels. The probe covers Color, HDR Color, Vector, Gamma Color, Gamma HDR Color,
Gamma Vector, Gamma Float and ordinary Float declarations. Shader defaults,
Editor-created imported material and material variant assets, repeated SetColor
and SetVector assignments, material copies, property-block copies, shared material
identity and live parent/variant overrides are separate cases. Finite values
cover transfer-function boundaries, [0,1], HDR RGB/alpha and negative components.
Mathf conversions and Color.linear/gamma have separate CPU-only boundary probes.
Nonfinite inputs are API-only and do not certify any GPU behavior.

`compare.py` fixes absolute and relative tolerances at 2e-5 before recordings or
runtime edits. Every observation key, image key and all 64 float channels per
image are mandatory. There are no expected failures or exclusions.

The runner accepts MATERIAL_COLOR_TRANSFER_CLI, MATERIAL_COLOR_TRANSFER_NATIVE_DIR,
UNBOUND_MANAGED_DIR, MATERIAL_COLOR_TRANSFER_BUILD, MATERIAL_COLOR_TRANSFER_PROJECT
and MATERIAL_COLOR_TRANSFER_OUT. Defaults use the frozen native6 CLI/library and
private managed build directories. All heavy jobs use the global lock and disable
shared compilation/build servers. Gamma runs use a copy below this fixture's
Library, changing only that copy's m_ActiveColorSpace to 0.

Measurement commands, evidence and results will be recorded after capture.

## Native measurements

`oracle-v2-linear1` and `oracle-v2-linear2` agree byte-for-byte, as do
`oracle-v2-gamma1` and `oracle-v2-gamma2`. Each has 2,986 observations and 424
4x4 float buffers, 27,136 channels. Both Native pairs were captured before runtime
changes. Native logs identify Unity 6000.0.71f1, NVIDIA RTX 4060, driver 580.178.04,
OpenGL Core 4.5. Engine logs identify the same GPU with Vulkan.

Each color space has 53 captures of all eight properties:

| Cases | Captures | Buffers |
| --- | ---: | ---: |
| Shader defaults, imported material, imported variant | 3 | 24 |
| Two setters, six inputs, original/copy/block/copied block | 48 | 384 |
| Live parent change, then child override plus parent change | 2 | 16 |
| Total | 53 | 424 |

Each capture records 14 CPU getters before and after, eight GPU centers and eight
pixel counts, 44 observations. The remaining observations record 12 assigned input
vectors, 12 shared-material identity checks, 36 groups of 14 source/copied-block
getters, two target/profile observations, 22 math cases with five observations
each, and 14 nonfinite API getters. Buffers contain every pixel, rather than only
the center used in the observation file.

The baseline is an independent Release managed build from clean 84880006, paired
with the frozen `parity-baseline-native6/unbound` and native6 library. Linear
matches 2,364/2,986 observations, 190/424 buffers and 17,024/27,136 channels. Gamma
matches 2,986/2,986 observations, 424/424 buffers and 27,136/27,136 channels.
`Reference/Native*` and `Reference/Before*` preserve every key and gzip-compressed
raw float buffer. `Evidence/before-*.txt` records all baseline failures.

The controlled Native result distinguishes four behaviors in a Linear project:

* Material Color properties convert RGB to linear, unless marked HDR. Gamma
  explicitly requests conversion for Color, HDR Color, Vector and Float.
  SetColor and SetVector on Material follow the declaration, rather than the
  setter's name. Alpha stays unchanged.
* RGB above one uses `pow(x,2.2)`. For example, 2 becomes 4.594794, 4 becomes
  21.1121273 and 8 becomes 97.00587. The former engine extended the sRGB 2.4
  formula and converted HDR properties too. Vector declarations remain raw.
* A converted material getter converts back to gamma. Negative RGB therefore
  reads as zero, although the uniform retains its negative linear value. For
  an assignment of -0.5, the GPU receives -0.03869969 and the getter reads zero.
  Source assignments and shared-material identities are recorded independently.
* A property block SetColor converts at assignment, independently of shader
  declaration. GetVector reads its linear storage and GetColor converts it back.
  SetVector and its getters stay raw. Float block overrides stay raw even on a
  Gamma declaration. Block copying retains this distinction.

Shader defaults clamp negative converted RGB before their first GPU use. Imported
material negative RGB is retained for GPU use and clamps only on the getter's
inverse transfer. HDR alpha is unchanged on every tested path. The CPU math probes
agree with the pre-existing Mathf and Color implementations, including the tested
nonfinite APIs, so no general math functions were changed.

## Implementation boundary

Material packets and per-draw overrides now carry working-space values. The
material keeps assigned source values internally; getters reproduce Native's
round trip without replacing them. Synthetic renderer material copies mark block
values as already converted. This prevents a second declaration-based transfer.
Property blocks retain Color versus Vector assignment types. The actual selected
ShaderLab source supplies binding flags, including built-ins whose managed Shader
objects have only a short default property list. Runtime ShaderLab parsing handles
HDR and Gamma defaults with the measured transfer function.

The matching new CLI and managed assembly must be used together for final runs.
Packet layout, kind numbers, entry points and native ABI are unchanged. ShaderLab
binding metadata is parsed from source in memory on each process; it has no disk
cache entry. WGSL, shader compiler output and its content keys are unchanged, so
shader cache version 13 remains valid. No camera, RenderTexture, readback, UI,
physics, authored game project, generated stub or shared progress file was edited.

The imported-asset live-change probe intentionally mutates loaded fixture assets.
The runner saves and restores their exact authored bytes. Initial Native captures
`native-linear1`, `native-linear2`, `native-gamma1`, `native-gamma2` and initial
`before-linear`/`before-gamma` are retained privately, but are not the accepted
pair or baseline because the first runner did not restore those dirty assets.
Their failed repeat comparison exposed this fixture error. `oracle-v2-*` and
`before-v2-*` use the corrected input preservation. Nothing was removed from their
strict comparisons.

Nonfinite GPU inputs, other Unity releases, shader-switch reinterpretation,
array setters and color properties absent from declarations are unmeasured by this
oracle. Existing regression captures cover their existing consumers, but do not
establish general engine parity. Parent owns integrated API-check and integration.

## Commands and private outputs

All raw outputs are under `~/.cache/compat/materialcolortransfer-6239018d/`.
The private Cargo target is a symlink there named `cargo`, pointing to
`/mnt/data/materialcolortransfer-6239018d-cargo`, to avoid filling the home volume.
No mutable parent CLI/library paths are used. The helper's root override allows a
fresh directory for repetitions; do not overwrite the retained captures.

```sh
export PATH="$HOME/.dotnet:$HOME/.cargo/bin:$PATH"
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
export DISPLAY=:0 XAUTHORITY="$(ls -t /run/user/1000/.mutter-Xwaylandauth.* | head -1)"
export MATERIAL_COLOR_TRANSFER_ROOT="$HOME/.cache/compat/materialcolortransfer-6239018d"
f=tests/projects/materialcolortransfer
w="$MATERIAL_COLOR_TRANSFER_ROOT"
# Independently built before runtime edits, at 84880006:
~/.cache/compat/with-heavy-job timeout 600 dotnet build managed/Unbound.Host/Unbound.Host.csproj \
  -c Release -o "$w/before-managed" -p:UnboundTarget="$w/before-build/" \
  --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false
MATERIAL_COLOR_TRANSFER_OUT="$w/create" "$f/run.sh" create
# Gamma copy is Assets/Packages/ProjectSettings below the fixture's Library/Gamma.
# Only the copied m_ActiveColorSpace changes from 1 to 0.
for space in linear gamma; do
  if [ "$space" = gamma ]; then
    export MATERIAL_COLOR_TRANSFER_PROJECT="$PWD/$f/Library/Gamma"
  else
    unset MATERIAL_COLOR_TRANSFER_PROJECT
  fi
  for n in 1 2; do
    MATERIAL_COLOR_TRANSFER_OUT="$w/oracle-v2-$space$n" "$f/run.sh" native
  done
  MATERIAL_COLOR_TRANSFER_OUT="$w/before-v2-$space" "$f/run.sh" engine
  python3 "$f/compare.py" "$w/oracle-v2-${space}1" "$w/before-v2-$space"
done
unset MATERIAL_COLOR_TRANSFER_PROJECT
# Private Rust/native build, skipping cargo's nested default managed output:
CARGO_TARGET_DIR=/mnt/data/materialcolortransfer-6239018d-cargo CARGO_BUILD_JOBS=2 \
UNBOUND_SKIP_MANAGED_BUILD=1 ~/.cache/compat/with-heavy-job timeout 2400 \
  cargo build --release -p unbound -j2
UNBOUND_MANAGED_DIR="$w/final-managed" MATERIAL_COLOR_TRANSFER_BUILD="$w/final-build/" \
  "$f/run.sh" build
```
