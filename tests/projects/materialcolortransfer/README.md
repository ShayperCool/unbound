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

Measurement commands, evidence and results are recorded below.

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

Final capture and regression commands, with matching private artifacts:

```sh
source "$w/final-env.sh"
# This file sets CLI=$w/cargo/release/unbound, native directory=$w/cargo/managed,
# UNBOUND_MANAGED_DIR=$w/final-managed, LD_LIBRARY_PATH to that private native
# directory, DISPLAY=:0 and the newest Xwayland authorization file.
for space in linear gamma; do
  if [ "$space" = gamma ]; then
    export MATERIAL_COLOR_TRANSFER_PROJECT="$PWD/$f/Library/Gamma"
  else
    unset MATERIAL_COLOR_TRANSFER_PROJECT
  fi
  MATERIAL_COLOR_TRANSFER_OUT="$w/final-$space" "$f/run.sh" engine
  python3 "$f/compare.py" "$w/oracle-v2-${space}1" "$w/final-$space"
done
unset MATERIAL_COLOR_TRANSFER_PROJECT
MATERIAL_COLOR_REGRESS_OUT="$w/regress-final" python3 "$f/regress.py"
python3 "$f/summarize.py"
python3 "$f/measure-regress.py"
CARGO_TARGET_DIR=/mnt/data/materialcolortransfer-6239018d-cargo UNBOUND_SKIP_MANAGED_BUILD=1 \
  ~/.cache/compat/with-heavy-job timeout 600 cargo test --release -p unbound-app --lib -j2
~/.cache/compat/with-heavy-job timeout 600 dotnet test managed/UnityEngine.Tests/UnityEngine.Tests.csproj \
  -c Release -p:UnboundTarget="$w/unit-build/" \
  -p:UnboundNativeDir="$HOME/.cache/compat/parity-baseline-native6" \
  --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false \
  --filter 'FullyQualifiedName~ColorTests|FullyQualifiedName~MathfTests|FullyQualifiedName~Material|FullyQualifiedName~PropertyBlock|FullyQualifiedName~ShaderUtil|FullyQualifiedName~CoreTests|FullyQualifiedName~CanvasOrder|FullyQualifiedName~UIElements|FullyQualifiedName~.YG|FullyQualifiedName~RectTransform'
python3 tests/projects/builtinfx/test_compare.py
python3 tests/projects/resources/test_compare.py
```

The baseline regressions ran the same fixture helper with explicit
`MATERIAL_COLOR_TRANSFER_CLI=~/.cache/compat/parity-baseline-native6/unbound`,
`UNBOUND_MANAGED_DIR=$w/before-managed`, snapshot LD_LIBRARY_PATH and
`MATERIAL_COLOR_REGRESS_OUT=$w/regress-before`. The helper creates separate
Linear/Gamma project copies below this fixture's Library for clipping, colors and
focused camera controls. It uses no mutable camera helper default paths.

The first builtinrp regression used the CLI's default screen size, producing five
previous-view-projection matrix mismatches. Its original output remains in
`regress-before/builtinrp`. The corrected baseline uses this exact command and
is compared to the final 640x480 run; no comparator keys or tolerances change:

```sh
BUILTIN_PROBE_OUT="$w/regress-before/builtinrp-correct" \
UNBOUND_MANAGED_DIR="$w/before-managed" LD_LIBRARY_PATH="$HOME/.cache/compat/parity-baseline-native6" \
~/.cache/compat/with-heavy-job timeout 600 "$HOME/.cache/compat/parity-baseline-native6/unbound" \
  run -p "$PWD/tests/projects/builtinrp" --frames 30 --size 640x480 --no-watch
```

The .NET tests use the unchanged snapshot PhysX ABI 6, matching the unchanged
native source and private rebuilt PhysX ABI 6. All engine runs after the fix use
the matching private CLI and private native library. Parent's native version or
integrated managed artifacts may advance independently and are never selected.

The focused managed regression selection passes 306 tests, with zero failures and
zero skips. It includes Color, Mathf, Core, ShaderUtil, property blocks, UI, Yoga
and RectTransform. `Evidence/unit-tests.txt` preserves the complete output.

## Final oracle result

Both Linear and Gamma pass all 2,986 observations, 424/424 buffers and
27,136/27,136 channels. Across both profiles this is 5,972 observations, 848
buffers and 54,272 channels. There are no missing or unexpected observations or
buffers. The absolute/relative tolerance remains 2e-5. Both Native pairs remain
byte-identical. `Evidence/final-linear.txt`, `Evidence/final-gamma.txt` and
`Evidence/recordings.json` retain the complete comparisons and per-file hashes.
`Reference/Final*` preserves every final float buffer and observation.

All 18 Rust app tests pass, including the new default/binding tests and shader
validation. No test was ignored. The private Release build completes with zero
errors, and the private managed Release build has the same 34 existing warnings
as the independent baseline and zero errors. The PhysX library built by that
private Cargo target reports ABI 6. `Evidence/provenance.json` includes final CLI,
library and managed-assembly hashes. `Evidence/rust-tests.txt` retains the Rust
results.

`Evidence/conservation.json` verifies byte-identical before/final data for all
425 Gamma capture files, all 29 Linear material Vector buffers and all 53 Linear
ordinary Float buffers. The declaration-based Vector controls are unchanged.
Property-block SetColor applied to a Vector declaration is a measured conversion
case; its behavior is documented separately above.

### Final metadata-reader check

After the first final captures, the metadata reader changed from integer IDs to
property names for its internal flag set. Reading shader flags now allocates no
new Shader property IDs. This avoids introducing an ordering side effect. The
wire data and Rust code are unchanged. Initial final captures remain privately
under `final-*` and `regress-final`; the final source revision is independently
rebuilt under `final2-managed`/`final2-build` and captured under `final2-*` and
`regress-final2`. The selected managed tests are rebuilt under `unit2-build`.

```sh
UNBOUND_MANAGED_DIR="$w/final2-managed" MATERIAL_COLOR_TRANSFER_BUILD="$w/final2-build/" \
  "$f/run.sh" build
source "$w/final-env.sh"
export UNBOUND_MANAGED_DIR="$w/final2-managed" MATERIAL_COLOR_TRANSFER_FINAL_SUFFIX=2
for space in linear gamma; do
  if [ "$space" = gamma ]; then
    export MATERIAL_COLOR_TRANSFER_PROJECT="$PWD/$f/Library/Gamma"
  else
    unset MATERIAL_COLOR_TRANSFER_PROJECT
  fi
  MATERIAL_COLOR_TRANSFER_OUT="$w/final2-$space" "$f/run.sh" engine
  python3 "$f/compare.py" "$w/oracle-v2-${space}1" "$w/final2-$space"
done
unset MATERIAL_COLOR_TRANSFER_PROJECT
MATERIAL_COLOR_REGRESS_OUT="$w/regress-final2" python3 "$f/regress.py"
python3 "$f/summarize.py"
python3 "$f/measure-regress.py"
# Repeat the identical managed test command above, replacing unit-build/ with
# unit2-build/. Preserve unit-tests.log and unit2-tests.log separately.
```

The final metadata-reader rebuild again passes 306 selected managed tests, with no
failures or skips. Both 425-file oracle captures are byte-identical to the first
final captures, in addition to passing Native. `Evidence/unit2-tests.txt` and the
updated per-file provenance preserve this second build independently.

The first complete regression sweep also passes all strict numeric/image controls.
Its full reports are retained under `Evidence/InitialFinal`. The final sweep uses
the same checkers, keys, tolerances and inherited expected sets. The graphics probe
writes one JSON file, not a directory; it is counted as one capture file.

Only builtinfx's `material_variant.png` and three numeric readbacks in its JSON
change. All three readbacks now equal the Native recordings exactly:
`material.parent.metallic` and `material.variant.metallic` become 0.299999982,
and `material.made.metallic` becomes 0.0499999821. These are Gamma property getter
round trips. The variant image's Native mean maximum-channel error falls from
1.1788333 to 0.003, its worst error from 27 to 8, and pixels over 8 from 1,989 to
zero out of 30,000. `Evidence/builtinfx-values-changed.json` and
`Evidence/builtinfx-variant-metrics.json` preserve these measurements.

### Final regression results

The fresh final2 build passes the same regression matrix. The report is
`Evidence/regression-summary.txt`; every original comparator output is retained
alongside it. `Evidence/regression-recordings.json` gives every capture path and
per-file SHA-256. `Evidence/InitialFinal` keeps the first complete final sweep.

| Control | Final result |
| --- | --- |
| Graphics | 80/80 observations |
| Resources, including property blocks and float formats | 539/539 values, 62/62 images |
| Builtinrp | 830 matching +124 inherited expected values, 33 passing +4 inherited expected images |
| Builtinfx | 213 matching +2 inherited expected values, 92 normally checked passing +11 inherited expected images |
| Clipping Linear, Gamma, LinearMSAA, each | 1211/1211 observations, 266/266 images, 17432576/17432576 channels |
| Canvas colors | 49201 per profile, 98402 total |
| Focused camera HDR/vector layers, chains and clear, each profile | 481/481 observations, 54/54 images, 3268608/3268608 channels |
| UI Toolkit | JSON, all 11 interactions and all 3 PNGs byte-identical to the private baseline |
| Managed selection, each independent build | 306 passed, 0 failed, 0 skipped |
| Rust app tests | 18 passed, 0 failed, 0 ignored |
| Existing builtinfx/resources checker tests | 7 +6 passed |

Graphics, resources, builtinrp, every clipping/color/camera capture and UI Toolkit
are byte-identical to the private baseline. Only the documented builtinfx variant
image and three readbacks change. No existing expected sets or tolerances change.
Normally checked `hdr_half_target` remains included and passing. UI Toolkit's
Native comparison still reports the inherited 53-versus-54-pixel label width;
both complete reports retain that failure. Its independent before/final check
passes with zero changed capture bytes. This is not full engine parity.

### Parent integration

Parent reviewed and cherry-picked all sixteen commits in order, c34b4824 through
8c67b55b. Runtime fixes are 820ac0d5, 3190d697 and f5cd1e0b. The parent rebuilt
both the CLI and managed host together. API-check passes with zero errors in
2266 types. Main native ABI remains 5, PhysX remains 6 and shader-cache version
remains 13; the separate in-progress Physics branch is not integrated.

Parent verifies both Native pairs and committed references byte-identical again.
Its actual integrated artifacts pass all 2986 observations, 424 buffers and
27136 channels in EACH color space, 5972 observations and 848 buffers total,
without changed tolerances or exclusions. Managed tests pass 306/306, Rust app
tests 18/18 and existing checker tests 13/13. Authored settings remain Linear;
parent Gamma uses Library/ParentGamma.

Parent independently repeats graphics/resources/builtinrp/builtinfx, three clipping
matrices, 98402 colors, focused camera controls in both spaces and UI Toolkit.
Every existing comparator passes with its documented inherited differences.
Builtinfx normally checks 92 passing images with 11 inherited expected images;
hdr_half_target stays normally checked. UI Toolkit's metadata, eleven interactions
and three PNGs are byte-identical to the independent pre-change baseline. The
inherited Native label-width difference is retained separately.

Parent CLI/managed builds, API and unit logs are ~/.cache/compat/materialcolor-parent-*.log.
Raw captures, complete comparisons and the regression summary are in
~/.cache/compat/materialcolor-parent/. Evidence/parent-regression-summary.txt
retains that summary. No extra runtime change was needed during integration.

The first final2 focused Gamma camera attempt exited 101 during Vulkan device
creation with `RequestDeviceError: Device(Lost)`, before any rendering or captures.
The failure log and hardware check are retained under `Evidence/Failures`. A
single retry in a new directory passes all 481 observations and 54 images. No
renderer or driver setting changed. The final2 comparison selects that complete
retry explicitly, and preserves the aborted attempt. Exact remaining commands:

```sh
source "$w/final-env.sh"
export UNBOUND_MANAGED_DIR="$w/final2-managed" MATERIAL_COLOR_TRANSFER_FINAL_SUFFIX=2
CAMERA_MSAA_FOCUSED=1 CAMERA_MSAA_OUT="$w/regress-final2/camera-focused-gamma-retry1" \
  ~/.cache/compat/with-heavy-job timeout 600 "$MATERIAL_COLOR_TRANSFER_CLI" run \
  -p "$PWD/$f/Library/RegressioncameramsaaGamma" --frames 64 --size 128x128 --no-watch
RECT_CLIP_MSAA=1 RECT_CLIP_OUT="$w/regress-final2/rectclip-msaa" \
  ~/.cache/compat/with-heavy-job timeout 600 "$MATERIAL_COLOR_TRANSFER_CLI" run \
  -p "$PWD/$f/Library/RegressionuirectclippingLinear" --frames 24 --size 128x128 --no-watch
MATERIAL_COLOR_TRANSFER_GAMMA_CAMERA_FOLDER=camera-focused-gamma-retry1 \
  python3 "$f/measure-regress.py"
```

The fixture Gamma copy was prepared from restored authored inputs before the
accepted Native recordings with the following operation:

```sh
python3 - <<'PY'
from pathlib import Path
import shutil
f=Path('tests/projects/materialcolortransfer');g=f/'Library/Gamma'
for part in ['Assets','Packages','ProjectSettings']:
    shutil.copytree(f/part,g/part,dirs_exist_ok=True)
p=g/'ProjectSettings/ProjectSettings.asset'
p.write_text(p.read_text().replace('m_ActiveColorSpace: 1','m_ActiveColorSpace: 0'))
PY
```

All owned Editor/engine/compiler jobs finish before handoff. The frozen CLI and
native library hashes were rechecked unchanged against their manifest. The parent
still owns integrated API-check, review and cherry-picking. No PR or merge was made.
