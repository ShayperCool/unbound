# Canvas rectangle clipping

Measured on Unity 6000.0.71f1 Linux Editor using the NVIDIA GPU on display :0. This is
rectangle-clipping evidence for that release, not a claim of full engine or UI parity.

## Results

| Matrix | Keyed observations | Complete images | Float channels |
| --- | --- | --- | --- |
| Native Linear repeat | 1211/1211 | 266/266 byte identical | 17432576/17432576 |
| Engine Linear, single sample | 1211/1211 | 266/266 | 17432576/17432576 |
| Native Gamma repeat | 1211/1211 | 266/266 byte identical | 17432576/17432576 |
| Engine Gamma, single sample | 1211/1211 | 266/266 | 17432576/17432576 |
| Independent pre-change engine, Linear | 1211/1211 | 54/266 | 13437916/17432576 |
| Native Linear quality-MSAA repeat | 1211/1211 | 266/266 byte identical | 17432576/17432576 |
| Engine Linear quality-MSAA | 1211/1211 | 151/266 | 17390866/17432576 |

The two passing engine matrices total 2422 observations, 532 images and 34865152 float
channels. Every image has 128x128 RGBA pixels. The comparator uses the existing numeric
absolute/relative 2e-4 tolerance, with the same maximum rule as Python math.isclose.
It checks every key, all float channels, the exact float-image manifest and the captured
PNG manifest. It rejects missing, duplicate and unexpected records/images. There are no
expected-failure keys, image exclusions or tolerance overrides. The MSAA comparison exits 1.
Matching signed infinities from the disabled rectangle diagnostic compare exactly; NaNs in
GPU images never pass. Invalid API readback uses explicit strings for NaN/infinity.

All compressed float images are committed in Reference/Linear, Reference/Gamma and
Reference/LinearMSAA. Each directory has the keyed record, SHA-256 manifest for every
uncompressed image, and nine PNG companions for visual inspection. The probe writes a PNG
for every capture; those complete sets remain with the raw GPU recordings in the cache.
`evidence.json` records the exact Native pairs, engine runs and record hashes.

### Retained MSAA failure

The engine applies quality MSAA to this single-sample ARGBFloat camera target while Native
does not. Rotated geometry therefore has engine half-coverage edge pixels where Native has
single-sample coverage. With quality MSAA enabled, 115/266 images and 41710/17432576
channels fail the unchanged comparator. This is an inherited rendering difference, still
reproducible with RECT_CLIP_MSAA=1. The unclipped rotated image p1.s9.rgba is byte-identical
in the independently built pre-change and final engines, and both disagree with Native.
The failing comparison and both builds' images are retained. No MSAA rendering code was
changed in this task. The default matrix explicitly disables camera and quality MSAA to
measure clipping independently. This does not establish parity with quality MSAA enabled.

## Measured behavior and coverage

The production shader is Unity's original built-in UI/Default. Neither the fixture nor the
engine substitutes a clipping shader for it. The engine source is the cached original
`~/.cache/unbound/builtin-shaders/v1/6000.0.71f1/DefaultResourcesExtra/UI/UI-Default.shader`,
SHA-256 `38d9865634d50ed7aaa43db043423f762ec2b91eb1607d468c03632fc7c4e2b1`.
rectclip-final.log records its clipped, unclipped and alpha-clip shader variants compiling. Coordinates.shader and NoVariant.shader are
separate diagnostic shaders; they expose vertex/world coordinates, _ClipRect,
_UIMaskSoftnessX/Y and UNITY_UI_CLIP_RECT.

* Native supplies shader vertices in root Canvas coordinates. Renderer position, rotation,
  scale and nested Canvas transforms are baked into the private batch. overrideSorting
  does not change that coordinate system. World coordinates retain the root model transform.
* Renderer clipping overrides material rectangle and softness uniforms. Disabling supplies
  (-infinity,-infinity,+infinity,+infinity), zero softness and the disabled clipping variant,
  including when the material or global keyword explicitly enables clipping. Global keyword
  state survives the draw. A custom shader omitting the clipping variant draws outside the
  rectangle; a screen scissor would incorrectly clip it.
* Softness reaches the shader unchanged, including negative and fractional values. The API
  preserves negative softness. Negative values can produce inverted edge ramps in the
  original shader; the engine does not clamp or replace that behavior.
* SetMesh retains a private snapshot. Mutating the caller's vertices does not affect the
  draw until SetMesh is called again. Transform, clipping, softness and shader-property
  changes take effect without reassigning the mesh. Source vertices and assigned material
  identity, properties and keyword state are observed separately from GPU output.
* Normal and valid pop draws receive the same clipping state. SetPopMaterial outside the
  current popMaterialCount reports an error and retains no material. The initial oracle
  also measured Clear/GetMaterial behavior, which the initial engine got wrong.

The seven direct-renderer poses cover WorldSpace translation, rotated roots, nonuniform
root scale, transformed renderers, transformed nested canvases with and without independent
sorting, and ScreenSpaceCamera. Each covers hard clipping, X/Y/both-axis softness, zero-width,
zero-area and negative-width rectangles, negative/fractional softness, disable/re-enable,
material and global keyword overrides, custom shaders with/without variants, source-mesh
mutation/reassignment, valid/invalid pop draws, and live renderer movement/rotation.

Four real uGUI paths cover WorldSpace and ScreenSpaceCamera with both nested sorting modes.
They exercise two intersecting RectMask2D components, rotated/nonuniformly scaled geometry,
softness precedence, disabling/re-enabling masks, padding changes, rect resizing, moving
content, inactive/reactivated hierarchy, and a stencil Mask with its pop draw. Eight Overlay
captures cover direct hard/soft/disabled clipping, coordinate and uniform diagnostics, and
real RectMask2D softness, rotated/resized masks and mask disabling.

Nonfinite rectangle/softness setters were measured only by API readback, then replaced with
finite values before rendering. They are not GPU parity successes. Rendering nonfinite input,
other Unity releases, standalone players, perspective/tilted 3D canvases, HDR vertex attributes,
custom normal/tangent shader inputs, and every Overlay nested overrideSorting combination
remain unmeasured. No crashing Native case was needed or excluded.

### Capture controls

Camera matrices, target size, sample policy, color space and explicit poses are deterministic.
The camera Canvas is given an explicit Z=-9 pose after camera/plane assignment so clipping
measurements do not depend on when a render call drives the Canvas. Canvas lifecycle code is
unchanged and its independent fixture is rerun below.

Overlay requires a real Game view, rather than Camera.Render, which omits Overlay canvases.
The Native runner temporarily selects a fixed 128x128 Game view size and removes that custom
size/restores selection on exit. The probe invokes the Editor's actual PlayModeView.RenderView
and reads its sRGB ARGB32 output in Linear, or linear ARGB32 in Gamma. The engine invokes its
existing overlay draw collector into an equivalent target. This explicitly tests overlay draw
preparation; it does not establish screen-capture API or EndOfFrame scheduling parity. Native
recordings log OVERLAY_VIEW with target format, sRGB, screen size and root position. Earlier
black captures from an incomplete Editor render call are retained as failed capture attempts
in rectclip-overlay1 and rectclip-view1 and are not used as an oracle.

The first oracle was recorded twice before implementation: 976 observations and 186 images,
all equal, in rectclip-native1/native2. Its original MSAA-enabled reference is also preserved
in commit 2d2fe30f. Before implementation, this worktree independently built the starting
38e9279b engine into rectclip-before-managed with separate intermediates. The initial baseline
matched 27/186 images and 9272624/12189696 channels. The expanded oracle preserves and
extends those scenarios, adds valid pop draws and Overlay, and makes the sample policy explicit.
The final MSAA matrix retains the original rendering failure rather than treating it as a pass.

## Reproduce

Run from the repository root. `run.sh` changes to that root itself, acquires the global
~/.cache/compat/with-heavy-job lock for every Editor/build/engine invocation, applies a
600-second timeout, configures NVIDIA display authentication, and disables build servers.
Do not wrap the script in another heavy-job lock. It uses ~/.dotnet/dotnet through PATH,
private rectclip-build/ intermediates, private rectclip-managed/ output, and the shared
read-only CLI plus its matching native libraries. It never touches the real game.

```sh
f=tests/projects/uirectclipping
"$f/run.sh" create create
"$f/run.sh" native linear1
"$f/run.sh" native linear2
"$f/run.sh" build > ~/.cache/compat/rectclip-build.log 2>&1
"$f/run.sh" engine final > ~/.cache/compat/rectclip-final.log 2>&1
python3 "$f/compare.py" "$f/Reference/Linear" ~/.cache/compat/rectclip-final
python3 "$f/compare.py" ~/.cache/compat/rectclip-linear1 ~/.cache/compat/rectclip-linear2

"$f/run.sh" native-gamma gamma1
"$f/run.sh" native-gamma gamma2
"$f/run.sh" engine gamma-final > ~/.cache/compat/rectclip-gamma-final.log 2>&1
python3 "$f/compare.py" "$f/Reference/Gamma" ~/.cache/compat/rectclip-gamma-final
python3 "$f/compare.py" ~/.cache/compat/rectclip-gamma1 ~/.cache/compat/rectclip-gamma2
"$f/run.sh" set-linear restore

RECT_CLIP_MSAA=1 "$f/run.sh" native msaa1
RECT_CLIP_MSAA=1 "$f/run.sh" native msaa2
RECT_CLIP_MSAA=1 "$f/run.sh" engine msaa-final > ~/.cache/compat/rectclip-msaa-final.log 2>&1
python3 "$f/compare.py" "$f/Reference/LinearMSAA" ~/.cache/compat/rectclip-msaa-final
# The preceding comparison currently fails; it is deliberately not suppressed by the comparator.
```

The build command inside run.sh is:

```sh
~/.cache/compat/with-heavy-job timeout 600 dotnet build managed/Unbound.Host/Unbound.Host.csproj \
  -c Release -o ~/.cache/compat/rectclip-managed \
  -p:UnboundTarget="$HOME/.cache/compat/rectclip-build/" \
  --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false
```

For the independent baseline, build before changing engine sources, copy the output to
rectclip-before-managed, then select it without rebuilding:

```sh
UNBOUND_MANAGED_DIR="$HOME/.cache/compat/rectclip-before-managed" "$f/run.sh" engine final-before
RECT_CLIP_MSAA=1 UNBOUND_MANAGED_DIR="$HOME/.cache/compat/rectclip-before-managed" "$f/run.sh" engine msaa-before
python3 "$f/compare.py" "$f/Reference/Linear" ~/.cache/compat/rectclip-final-before
cmp ~/.cache/compat/rectclip-msaa-before/p1.s9.rgba ~/.cache/compat/rectclip-msaa-final/p1.s9.rgba
```

Probe commands intentionally exercise rejected pop indices and undeclared built-in material
property getters; both runtimes log these errors and recording continues. Their measured
readbacks and resulting images remain in the oracle. An absent values.jsonl is a failed run,
not an empty success. Output directories should be fresh when changing the fixture matrix.

## Regression evidence

All paths below are under ~/.cache/compat/. The final managed build is rectclip-build3.log:
zero errors and 34 existing warnings. No api-check, stub generation, Rust/native/protocol
change, Canvas lifecycle change, or shared progress-document edit was made.

| Check | Result | Evidence |
| --- | --- | --- |
| Native Linear repeat | byte-identical record and 266 float images | rectclip-linear1, rectclip-linear2, rectclip-linear-repeat.txt |
| Native Gamma repeat | byte-identical record and 266 float images | rectclip-gamma1, rectclip-gamma2, rectclip-gamma-repeat.txt |
| Engine Linear/Gamma | all keys/images/channels pass | rectclip-final-compare.txt, rectclip-gamma-compare.txt |
| Pre-change clipping baseline | API readback passes, 212 GPU images fail | rectclip-final-before, rectclip-final-before-compare.txt |
| MSAA retained failure | 115 GPU images fail, no exceptions | rectclip-msaa-final-compare.txt, rectclip-msaa-before, rectclip-msaa-final |
| Linear colors | 49201/49201 | rectclip-final-regress/colors-linear |
| Gamma colors | 49201/49201, total colors 98402 | rectclip-final-regress/colors-gamma |
| Canvas lifecycle | 8671/8671 | rectclip-final-regress/lifecycle |
| USS transitions | 253 samples, 831 callbacks, zero differences | rectclip-final-regress/transitions |
| Canvas/UIElements/Yoga | 261/261 | rectclip-final-tests.log |
| Additional Core/RectTransform | 14/14 | rectclip-final-core-tests.log |
| UI Toolkit | JSON, all 11 interactions and 3 PNGs byte-identical to independent baseline | rectclip-before/uitk, rectclip-final-regress/uitk, rectclip-final-regress.log |

Native UI Toolkit still differs by one pixel in the final foldout label width, 53 versus 54.
Both independent pre-change and final engine Native-comparison reports are byte-identical:
rectclip-before-uitk-native-compare.txt and rectclip-final-uitk-native-compare.txt. Thus that
existing Native difference is preserved, not a new regression.

The regression runner contains the full engine commands and private Gamma-fixture setup:

```sh
"$f/regress.sh" before before-managed > ~/.cache/compat/rectclip-before-regress.log 2>&1
"$f/regress.sh" final-regress > ~/.cache/compat/rectclip-final-regress.log 2>&1
```

Final unit-test commands, after exporting PATH and the build-server controls as in run.sh:

```sh
~/.cache/compat/with-heavy-job timeout 300 dotnet test managed/UnityEngine.Tests/UnityEngine.Tests.csproj \
  -c Release -p:UnboundTarget="$HOME/.cache/compat/rectclip-test-build/" \
  -p:UnboundNativeDir=/mnt/storage-hdd/node-b-caches-20261023/unialt-target-compat/managed \
  --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false \
  --filter 'FullyQualifiedName~CanvasOrder|FullyQualifiedName~UIElements|FullyQualifiedName~.YG'
~/.cache/compat/with-heavy-job timeout 300 dotnet test managed/UnityEngine.Tests/UnityEngine.Tests.csproj \
  -c Release -p:UnboundTarget="$HOME/.cache/compat/rectclip-test-build/" \
  -p:UnboundNativeDir=/mnt/storage-hdd/node-b-caches-20261023/unialt-target-compat/managed \
  --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false \
  --filter 'FullyQualifiedName~CoreTests|FullyQualifiedName~RectTransform'
```

Only this fixture, CanvasRenderer draw preparation and the narrow FrameBuilder keyword override
changed. The semantic index was refreshed after implementation. No PR or merge is part of this task.

### Parent integration

The parent reviewed and cherry-picked the four commits as db43474b, fba80dd6,
f2e03994 and 72fa2a16. Follow-up 5ae8458b releases prepared clipping materials
when assigned normal/pop slots or their counts change, and on Clear. This bounds
the private material cache when a long-lived renderer cycles through materials;
assigned material identity and properties remain untouched.

The parent managed build and API check pass, zero errors in 2266 types. The final
275-test run combines the 261 Canvas/UIElements/Yoga and 14 Core/RectTransform
checks, with no failures or skips. Parent Linear and Gamma EACH match all 1211
observations, 266 images and 17432576 float channels. Gamma uses a private fixture
under Library/ParentGamma; authored fixture settings remain Linear.

Parent reruns also retain colors 49201 EACH, lifecycle 8671, transitions 253
samples/831 callbacks, and UI Toolkit metadata, eleven interactions and all three
PNG files byte-identical to the independently built baseline. The regression
runner now honors an explicit UNBOUND_MANAGED_DIR, so it can validate the parent's
actual integrated output without overwriting the delegate's build or evidence:

```sh
UNBOUND_MANAGED_DIR=/mnt/storage-hdd/node-b-caches-20261023/unialt-target-compat/managed \
  tests/projects/uirectclipping/regress.sh parent-regress
```

Parent quality-MSAA reproduces exactly the retained failure: 1211 observations
pass, 151/266 images and 17390866/17432576 channels pass, leaving 115 images and
41710 channels different. This remains an open rendering gap; no failing capture
was removed. Parent logs and outputs are ~/.cache/compat/rectclip-parent-*;
rectclip-parent-msaa-comparison.txt contains the complete failing comparison.

Unity's own Bee compiler still launches Roslyn servers despite the supplied build-server
controls. `run.sh` invokes `cleanup.py` on exit. It stops only VBCSCompiler processes whose
inherited PWD is this exact isolated fixture; it does not stop shared/system build servers.
The same cleanup was run at handoff. No Editor, probe, compiler, local server or tunnel from
this task remains running.
