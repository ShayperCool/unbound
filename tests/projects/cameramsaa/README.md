# Camera and RenderTexture MSAA

The retained uirectclipping quality-MSAA failure is fixed. Its unchanged comparator
now passes Linear, Gamma and LinearMSAA, each with 1211 observations, 266 images
and 17432576 float channels. This does not establish full camera or engine parity.

## Measurement and implementation

Unity 6000.0.71f1 Linux Editor, built-in RP, NVIDIA RTX 4060, driver 580.178.04,
OpenGL Core 4.5 on DISPLAY=:0. Unbound uses the same GPU through Vulkan. The
Editor executable is ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity. Authentication
uses the newest /run/user/1000/.mutter-Xwaylandauth.*. No software GL or real game
project was used.

Before changing runtime sources, two independent real Editor processes recorded
~/.cache/compat/cameramsaa-oracle1 and cameramsaa-oracle2. All 886 observations,
181 complete float images and 11862016 channels match byte for byte. The private
starting engine was independently built from f2063fce into
cameramsaa-before-managed with cameramsaa-before-build/ intermediates, then
captured in cameramsaa-oracle-before. Commit dfd79dc7 preserves that initial
oracle and baseline. Earlier native*/focused* captures remain in the cache. The
focused screen attempt was black and its engine recording threw on an unimplemented
Editor API. The final screen path uses a fresh camera and two player-loop frames
before reading an actual Game view.

The expanded final matrix adds explicit samples with allowMSAA off, upward and
downward HDR sample selection, and valid active-source resolve. Two independent
Editors recorded each final color space before its final comparisons. All 948
observations, 195 images and 12779520 channels are byte-identical within each
Native pair. Gamma lives in this fixture's Library/Gamma; authored settings stay
Linear. evidence.json names each recording and hashes its complete record. All
Native float buffers and independently built baseline buffers are compressed in
Reference/Linear, Gamma, BeforeLinear and BeforeGamma with per-image SHA-256.
The original smaller recordings and all intermediate builds/captures remain in
~/.cache/compat/cameramsaa-*.

The matrix covers ARGB32/Default, ARGBHalf/DefaultHDR, ARGBFloat, RFloat/RGFloat,
RHalf/RGHalf; target samples 1/2/4 and quality 0/2/4; allowMSAA off, HDR on/off,
explicit targets, render buffers, partial viewports, copy image effects, actual
screen rendering, live quality/allowMSAA changes, release/recreation and resolve.
The HDR/flag/buffer/effect combinations are focused pairs rather than a full
Cartesian product. Descriptor/API readbacks precede Create, follow Create, and
follow rendering. State arrays contain format, graphicsFormat, antiAliasing,
descriptor.msaaSamples, IsCreated, sRGB and supported sample count, in that order.
Sample capability requests 0/1/2/3/4/8 include invalid/non-power-of-two API queries;
no invalid sample-count render target is created.

The triangle has three slopes and subpixel vertices. Every capture contains
128x128 RGBA float pixels and a PNG companion. All channels, including alpha and
missing format channels, use the original 2e-4 absolute/relative maximum rule.
The comparator rejects missing/extra keys and images and checks the PNG manifest.
There are no expected failures, exclusions or per-case tolerance overrides.

The Native policy explains the older graphics counterexample:

* All nine tested formats report and render explicit 2x and 4x samples. Their
  supported sample query rounds 3 down to 2. It does not veto float MSAA.
* An explicit target supplies its own samples, independent of quality and
  allowMSAA, unless the camera requires an HDR intermediate for an LDR target.
* The HDR intermediate uses quality samples with allowMSAA enabled, or one sample
  otherwise. It can raise an explicit target's sample count or reduce it to one.
  Camera.Render leaves the final target descriptor unchanged.
* Already-HDR targets use their own samples with either allowHDR value. Explicit
  buffer bindings bypass the HDR/quality intermediate. Screen rendering uses
  quality samples with HDR on or off. Image effects preserve target samples when
  no HDR conversion is required.
* HDR samples resolve before LDR byte quantization. Quantizing individual samples
  first loses one alpha byte at partial coverage. FrameBuilder now allocates the
  HDR format for this intermediate.

For example, ARGBFloat pixel x=29,y=27 has red 0 with one sample, 0.375 with two,
and 0.1875 with four. Evidence/native-coverage.json contains complete red-value
histograms for diagnostic cases, including their pixel counts. Selection is based
on camera policy, not on a format blacklist. FrameBuilder.cs and SystemInfo.cs
are the only runtime files changed. RenderTexture storage, Rust, protocol/native
ABI and cache formats are unchanged. No release difference was established;
no new release-specific condition was introduced. Other releases remain unmeasured.

## Strict results and remaining failures

| Complete camera matrix | API observations | Images | Float channels |
| --- | --- | --- | --- |
| Native Linear repeat | 948/948 | 195/195 byte-identical | 12779520/12779520 |
| Native Gamma repeat | 948/948 | 195/195 byte-identical | 12779520/12779520 |
| Starting engine Linear | 939/948 | 128/195 | 12420733/12779520 |
| Final engine Linear | 948/948 | 174/195 | 12445868/12779520 |
| Starting engine Gamma | 939/948 | 134/195 | 12516160/12779520 |
| Final engine Gamma | 948/948 | 183/195 | 12541376/12779520 |

Both final camera comparisons exit 1. Complete failing keys, matched-channel
counts and maximum errors are committed in Evidence/final-policy-compare.txt
and final-policy-gamma-compare.txt. No image has been dropped to obtain a pass.

Linear has 19 valid failing images: 12 image-effect precision/color conversion
cases, six screen readbacks, and ARGB32.hdrViewport. Gamma has ten valid failing
images: nine image-effect cases and ARGB32.hdrViewport. These differences are
present in the independent pre-change build. The image effects differ even at
fully covered pixel 64,64: ARGBFloat.effect reads Native
[0.75,0.5,0.25,0.625] versus engine
[0.75390625,0.50390625,0.25,0.6235294342041016], unchanged before/after.
Linear screen.hTrue.q2 reads Native
[0.8823530078,0.7372549176,0.5372549295,0.6235294342] versus engine
[0.75,0.5,0.25,0.625]. The screen discrepancy is encoded-versus-linear readback,
while the HDR partial viewport differs in placement/filtering. Those remaining
paths need separate work; the current partial-viewport guard is preserved.

Each matrix also retains two invalid-input images, ARGB32.resolve and
ARGBFloat.resolve. They call ResolveAntiAliasedSurface after the source was
unbound. Native asserts "We should always resolve only current RT" and leaves
unrelated destination contents. The calls and images stay in the strict totals,
but are not valid rendering successes. Evidence/invalid-inputs.json identifies
them separately. The additional resolveActive cases explicitly bind the source
and pass for both formats and both color spaces. Native sample queries for 0
and 3 are API observations only, not claims of rendering invalid input.

## Regression evidence

Every full pre/post engine run uses the independently built private managed
outputs. Raw results are cameramsaa-regress-before and cameramsaa-regress-final.
Evidence/final-regression-summary.txt contains comparator exit codes and all
changed filenames. Existing graphics/resources/builtinrp/builtinfx comparators,
tolerances and their documented inherited differences remain unchanged.

| Check | Final result |
| --- | --- |
| uirectclipping Linear | 1211/1211 observations, 266/266 images, 17432576/17432576 channels |
| uirectclipping Gamma | 1211/1211 observations, 266/266 images, 17432576/17432576 channels |
| uirectclipping LinearMSAA | 1211/1211 observations, 266/266 images, 17432576/17432576 channels |
| graphics | 80/80 records, original per-pixel comparator passes |
| resources, including rtmips/resolve and command_early/late | 539/539 values, 62/62 images, comparator passes |
| builtinrp, including camera events | 830 values match, 124 inherited expected values, 33 images pass, 4 inherited expected images |
| builtinfx, including AA and alpha-to-mask | 213 values match, 2 inherited expected values, 91 images pass, 12 inherited expected images |
| colors Linear/Gamma | 49201 EACH, 98402 total, zero differences |
| Canvas lifecycle | 8671/8671 |
| USS transitions | 253 samples, 831 callbacks, zero differences |
| UI/Canvas/Yoga/Core tests | 275 passed, zero failed/skipped |
| UI Toolkit | JSON with all 11 interactions and all 3 PNGs byte-identical to the private pre-change build |

The full pre-fix clipping MSAA comparison and rotated unclipped p1.s9 float image
are preserved in Evidence/rectclip-before-msaa.txt and
rectclip-before-p1.s9.rgba.gz. They reproduce all 115 failing images and 41710
channels from the parent. The final unchanged comparator passes all of them.

Resolving HDR before quantization changes more regression edge bytes than the
initial sample-selection-only commit. All existing comparators still pass.
Evidence/regression-pixel-metrics.json compares every resources/builtinrp/
builtinfx PNG directly to Native before and after, including images their
comparators classify as inherited. Resources has 48 changed images, 38 with lower
Native mean error and none higher. Builtinrp has 11 changed images, all with lower
mean error. Builtinfx has 74 changed images, 72 with lower mean error. Two means
rise slightly: material_variant 1.1758667 to 1.1788333 byte units and inherited
unsupported flare_LensFlareObject 6.8685667 to 6.8686. Their worst errors stay 27
and 50 and fewer channels differ. These are recorded, not hidden by new tolerances.

Native UI Toolkit still has the inherited 53-versus-54-pixel final foldout label
width. Complete Native-comparison reports for pre/post are byte-identical.
No API-check/gen-stubs was run because the shared CLI embeds parent source paths.
Integrated API-check remains for the parent. Managed build2 has zero errors and
34 existing warnings. No shared progress document or probe outside cameramsaa
was edited.

## Parent integration

The parent reviewed and cherry-picked the four commits as 1c962e0c, 7f1d7021,
81b54879 and 89a50e33. The integrated managed build passes. API-check reports
zero errors in 2266 types; all 275 UI/Canvas/Yoga/Core tests pass without skips.
Both Native pairs and all 195 committed float references per color space were
verified byte-identical again by the parent.

Parent repeats the complete camera matrix, retaining exactly the reported
948/948 observations, 174/195 Linear images and 183/195 Gamma images. Full failed
comparisons are cameramsaa-parent-linear-compare.txt and
cameramsaa-parent-gamma-compare.txt under ~/.cache/compat. The 19/10 valid remaining
failures and two invalid resolve captures in each space are still present.

Parent also repeats all three clipping matrices, graphics/resources/builtinrp/
builtinfx, both color spaces, lifecycle, transitions and UI Toolkit through the
regression runner using the actual parent managed output. All existing comparator
results are retained, with no tolerance changes. The clipping quality-MSAA matrix
now passes all 266 images and 17432576 channels. UI Toolkit is byte-identical to
the independently built baseline, including all eleven interactions and three
PNGs. Its inherited Native label-width discrepancy remains.

Parent build/API/test logs, raw captures and the full regression summary are
~/.cache/compat/cameramsaa-parent-*. The final regression summary is also retained
in Evidence/parent-regression-summary.txt. Authored fixtures stay Linear; parent
Gamma uses Library/ParentGamma. No additional runtime change was needed during
this integration. Camera image-effect precision, screen-readback equivalence and
HDR partial viewport behavior remain open work rather than MSAA parity claims.

## Reproduce

Run from the repository root. Scripts apply the heavy-job flock wrapper to every
Unity, dotnet and engine job with a 600-second timeout. Do not wrap a complete
script in that lock again. They disable build servers and use private managed
intermediates/output. Shared CLI and native libraries are read-only:
/mnt/storage-hdd/node-b-caches-20261023/unialt-target-compat/release/unbound and
its sibling managed directory. Main ABI 5, PhysX shim 6, shader cache format 13.
The root target symlink is not used.


```sh
f=tests/projects/cameramsaa
"$f/run.sh" create create
"$f/run.sh" native boundary1
"$f/run.sh" native boundary2
# Before editing runtime, build into the independent baseline output.
UNBOUND_MANAGED_DIR="$HOME/.cache/compat/cameramsaa-before-managed" "$f/run.sh" build
UNBOUND_MANAGED_DIR="$HOME/.cache/compat/cameramsaa-before-managed" "$f/run.sh" engine boundary-before
UNBOUND_MANAGED_DIR="$HOME/.cache/compat/cameramsaa-before-managed" "$f/regress.sh" regress-before
# After implementation:
"$f/run.sh" build > ~/.cache/compat/cameramsaa-build2.log 2>&1
"$f/run.sh" engine final-policy > ~/.cache/compat/cameramsaa-final-policy.log 2>&1
"$f/regress.sh" regress-final > ~/.cache/compat/cameramsaa-regress-final.log 2>&1
python3 "$f/compare.py" ~/.cache/compat/cameramsaa-boundary1 ~/.cache/compat/cameramsaa-boundary2
python3 "$f/compare.py" "$f/Reference/Linear" ~/.cache/compat/cameramsaa-final-policy
# The preceding complete camera comparison intentionally reports remaining failures and exits 1.
python3 "$f/check-regress.py" > ~/.cache/compat/cameramsaa-final-regression-summary.txt
```

The actual initial baseline build used a distinct intermediate directory as well:

```sh
export PATH="$HOME/.dotnet:$HOME/.cargo/bin:$PATH"
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
~/.cache/compat/with-heavy-job timeout 600 dotnet build managed/Unbound.Host/Unbound.Host.csproj \
  -c Release -o "$HOME/.cache/compat/cameramsaa-before-managed" \
  -p:UnboundTarget="$HOME/.cache/compat/cameramsaa-before-build/" \
  --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false
```

Create the private Gamma fixture without copying any generated Library state:

```sh
python3 - <<'PY'
from pathlib import Path
import shutil
p=Path('tests/projects/cameramsaa')
for part in ['Assets','Packages','ProjectSettings']:
    shutil.copytree(p/part,p/'Library/Gamma'/part,dirs_exist_ok=True)
s=p/'Library/Gamma/ProjectSettings/ProjectSettings.asset'
s.write_text(s.read_text().replace('m_ActiveColorSpace: 1','m_ActiveColorSpace: 0'))
PY
export CAMERA_MSAA_PROJECT="$PWD/tests/projects/cameramsaa/Library/Gamma"
"$f/run.sh" native boundary-gamma1
"$f/run.sh" native boundary-gamma2
UNBOUND_MANAGED_DIR="$HOME/.cache/compat/cameramsaa-before-managed" "$f/run.sh" engine boundary-gamma-before
"$f/run.sh" engine final-policy-gamma
python3 "$f/compare.py" ~/.cache/compat/cameramsaa-boundary-gamma1 ~/.cache/compat/cameramsaa-boundary-gamma2
python3 "$f/compare.py" "$f/Reference/Gamma" ~/.cache/compat/cameramsaa-final-policy-gamma
unset CAMERA_MSAA_PROJECT
python3 "$f/save-evidence.py"
```

Exact final unit-test invocation, with the PATH/build-server controls above:

```sh
~/.cache/compat/with-heavy-job timeout 600 dotnet test managed/UnityEngine.Tests/UnityEngine.Tests.csproj \
  -c Release -p:UnboundTarget="$HOME/.cache/compat/cameramsaa-test-build/" \
  -p:UnboundNativeDir=/mnt/storage-hdd/node-b-caches-20261023/unialt-target-compat/managed \
  --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false \
  --filter 'FullyQualifiedName~CanvasOrder|FullyQualifiedName~UIElements|FullyQualifiedName~.YG|FullyQualifiedName~CoreTests|FullyQualifiedName~RectTransform'
```

Build logs, complete raw PNG sets and all pre/post Native comparisons are under
~/.cache/compat/cameramsaa-*. Evidence/ retains the complete strict failures,
regression summary, final unit-test summary and repeat reports. ccc was initialized
and refreshed after runtime changes. cleanup.py only stops Unity Roslyn servers
whose inherited PWD equals this fixture or the explicitly selected private copy.
No owned Editor, compiler or probe process remains at handoff. No PR or merge.

Branch t3/camera-msaa-sample-policy. Ordered implementation commits are dfd79dc7
for the initial independent oracle, 4e4811dd for sample policy and supported-query
rounding, and d814000e for downward HDR sample selection and resolve precision.
The final test commit contains the expanded Linear/Gamma references and evidence.

## Camera follow-up oracle, 2026-10-31

Branch `t3/camera-oracle-parity` starts independently at `1ac656a0`. Its private
baseline is `~/.cache/compat/camerafollowup-before-managed`, built with
`camerafollowup-before-build/` intermediates. Earlier outputs, reference matrices,
and parent evidence are read-only. All jobs use the existing heavy-job lock.

Before runtime changes, the extended fixture was recorded twice per color space
in real Unity 6000.0.71f1. The final recordings are
`camerafollowup-oracle-v3-{linear,gamma}{1,2}`. Each pair matches all 3261
observations, 433 complete images and 28106752 float channels. Sources with a
partial viewport have their actual dimensions, including 80x64, rather than a
128x128 read outside their storage. The comparator retains its original maximum
absolute/relative rule, key checks and PNG manifest checks; its buffer-size check
now uses each recorded pixel count. Original 128x128 images remain unchanged.
`Reference/Followup{Linear,Gamma}` and `FollowupBefore{Linear,Gamma}` retain
complete compressed GPU buffers. `Evidence/Followup/` retains reports and hashes.

The independently built baseline captures are `camerafollowup-before-v3-*`.
They reproduce the parent's original strict results exactly: Linear 948/948 API
observations and 174/195 images; Gamma 948/948 and 183/195. The extended baseline
passes 291/433 Linear images and 330/433 Gamma images. Additional metadata exposes
existing descriptor/binding differences as well as additional rendering cases.
No original input or reference was replaced.

Earlier `camerafollowup-native-*` and `camerafollowup-oracle-{,v2-}*` recordings
remain experimental evidence. The first screen read attempt occurred outside a
drawing frame and Native rejected it. The final fixture reads the public null
framebuffer inside OnPostRender and records its active attachment separately.
The first HDR layer shader used a Color property; Linear values above one exposed
a separate material color-conversion discrepancy. The final shader uses Vector
uniforms to measure camera precision and HDR blending without that conversion.
It still draws two overlapping layers with non-half-representable values above
one and fractional alpha. Earlier experimental buffers have not been overwritten.

Native effect sources retain the explicit target's graphics format and encoding.
HDR conversion into LDR uses ARGBHalf, including with quality zero and one sample.
Sources keep their rendering sample count. The active texture on entry to
OnRenderImage is its source. An explicit non-converting target uses another
zero-depth, one-sample default-format destination, sRGB ARGB32 for LDR or ARGBHalf
for HDR, and a final copy. The destination argument differs from camera.targetTexture.
HDR-to-LDR instead supplies the final target directly. Complete source buffers
and both descriptors distinguish these cases.

Native HDR partial viewports use camera pixel dimensions, 96x96 for the legacy
case and 80x64 for the asymmetric boundary. The intermediate viewport starts at
zero and the final filtered copy covers the entire explicit target. Its clear
therefore overwrites the sentinel outside camera.rect too. Explicit buffer
bindings instead render to the original 128x128 attachment and bypass HDR. These
measurements explain why merely deleting the old full-viewport guard is insufficient.

The first implementation fixes all original image-effect and HDR viewport images.
Original strict totals become 187/195 Linear and 193/195 Gamma, with all 948 API
observations still matching. The six original Linear screen captures compare the
Editor's sRGB byte GameView attachment against the engine's linear half-float
screen storage. They are retained as failures, rather than changing either read
path or reference. Public in-frame null-framebuffer reads prove a further
separation: with no intermediate, Native returns encoded RGB and alpha 1; with
MSAA/HDR active it reads the Editor system framebuffer, gray 40/255 with alpha 1,
while the actual active attachment contains the camera image. GameView retains
fractional alpha. Null-framebuffer reads therefore do not certify GameView source
identity. No global gamma or screen presentation change is justified by these
captures alone.

Initialized resolve sentinels also do not make the rejected inactive-source API
a defined no-op. Native asserts the same current-RT requirement and overwrites
the destination with the current framebuffer, not the rendered source or the
initialized sentinel. Both legacy uninitialized outputs and both new rejected
sentinel outputs stay in strict totals. The source-bound counterparts pass.

The second measured correction narrows `Texture2D.ReadPixels`: explicit float
attachments copy numeric values into byte textures, with either linear flag.
Only the existing internal screen-to-byte screenshot path keeps presentation
encoding. The RenderTexture format/read-write constructor ignores an sRGB request
in Gamma, as Native does; explicit graphics-format/descriptor APIs are unchanged.
The final extended totals are 3201/3261 observations and 381/433 images in Linear,
3219/3261 and 405/433 in Gamma. Original totals stay 948/948, 187/195 and 193/195.
All remaining offscreen metadata and GPU buffers match, including the nontrivial
HDR layers, effects, camera rects, buffer bindings and valid resolves. Strict
comparisons still exit 1. Complete per-key results, every center pixel, channel
counts and maximum errors are in `Evidence/Followup/complete-final-metrics.json`.
It identifies 52 Linear and 28 Gamma image failures without masking them: four
rejected resolves per space; 24 public null-framebuffer captures per space;
and 24 Linear GameView-versus-engine-screen captures, including six legacy ones.
The 60/42 observation differences concern those same screen sources and their
absent Editor attachment metadata. These results do not claim screen API or
full engine parity.

Native repeats are also verified byte-identical, not just within tolerance, for
all values.jsonl, .rgba, .raw and .png files. Their complete SHA-256 manifests are
in `native-byte-manifests.json`; `RawReadbacksLinear/` and `RawReadbacksGamma/`
retain the compressed raw Texture2D buffers for both destination formats/flags.

### Effect chains and preserving color without a clear

A further focused oracle covers two copy effects for ARGB32/ARGBHalf/ARGBFloat,
HDR on/off, quality 0/4, plus partial HDR rects with depth-only, no-clear and solid
clears, with/without an effect. Two independent Native processes per space
recorded `camerafollowup-focused-native-{linear,gamma}{1,2}` before the subsequent
runtime correction. Each pair is byte-identical across all 481 observations,
54 complete GPU images and 3268608 channels. The separate focused run does not
replace or subset the full camera matrix. `CAMERA_MSAA_FOCUSED=1` selects it.
The fixture spells the SolidColor key explicitly because the equivalent Color
and SolidColor enum aliases stringify differently in Native and Unbound.

Native seeds the HDR temporary with the existing target when color is not
cleared. Its full-target sentinel survives outside the geometry for Depth and
Nothing, unlike a solid clear. FrameBuilder now copies that color before drawing;
this is restricted to the measured forward HDR intermediate for explicit targets.
Native's first effect destination follows the source's HDR classification. A
later, final effect writes directly to camera.targetTexture rather than introducing
another default-format conversion. The focused private baseline and final runs
are `camerafollowup-focused-before2-*` and `camerafollowup-focused-final3-*`.
Both final spaces pass 481/481 observations, 54/54 images and 3268608/3268608
channels with the same strict comparator. `Reference/FollowupFocused*` retains
all Native, independent baseline and final buffers; complete byte manifests and
reports are in `Evidence/Followup/focused-*`.

The final implementation changes only FrameBuilder.cs, RenderTexture.cs and
Texture2D.cs relative to the parent. It adds no protocol, native ABI, shader cache
format or release-specific switch. Rust and SystemInfo remain unchanged. The
parent's explicit-target MSAA selection and HDR-resolve-before-quantization policy
remain enabled. No authored project outside cameramsaa was changed.

### Final verification and commands

The final private output is `~/.cache/compat/camerafollowup-managed3`, with
`camerafollowup-build3/` intermediates. Final full captures are
`camerafollowup-final3-{linear,gamma}`. Their original and expanded strict totals
are unchanged from the second correction. Complete final GPU buffers are packed
in `Reference/FollowupFinal{Linear,Gamma}`. The separate focused oracle passes
in full. `complete-final3-metrics.json` lists every remaining unequal observation
and image, including all centers and maximum errors. No tolerance, expected-image
list, input, key or PNG manifest was relaxed. `legacy-native-preservation.json`
verifies all original 948 observations and 195 Native buffers against the extended
Native recordings in both spaces, including the retained invalid outputs.

| Final check | Result |
| --- | --- |
| Camera original Linear / Gamma | 948/948 API each; 187/195 and 193/195 images |
| Camera expanded Linear / Gamma | 3201/3261 and 3219/3261 observations; 381/433 and 405/433 images |
| Focused chains/clear preservation, each space | 481/481 observations, 54/54 images, 3268608/3268608 channels |
| Clipping Linear, Gamma, LinearMSAA, each | 1211/1211 observations, 266/266 images, 17432576/17432576 channels |
| Graphics | 80/80, original image comparator passes |
| Resources including rtmips/resolve and command early/late | 539/539, 62/62 images |
| Builtinrp | 830 matching +124 inherited expected values, 33 passing +4 inherited expected images |
| Builtinfx | 213 matching +2 inherited expected values, 91 passing +12 inherited expected images |
| Colors | 49201 per space, 98402 total |
| Lifecycle | 8671/8671 |
| Transitions | 253 samples, 831 callbacks, zero differences |
| UI/Canvas/Yoga/Core tests | 275 passed, zero failed/skipped |
| UI Toolkit | JSON, all 11 interactions, all 3 PNGs byte-identical to the independent baseline |

The complete final regression summary and unit log are `final3-regression-summary.txt`
and `unit-tests3.txt`. The earlier second-build summaries remain separate.
Graphics, resources, builtinrp and UI Toolkit have no changed capture files.
Only three builtinfx PNGs change; all now match Native byte-for-byte:
alpha_to_mask_no_msaa, hdr_half_target and hdr_on_no_msaa. Their Native mean
maximum-channel errors fall from 0.0191667, 9.4181333 and 0.5272 to zero, and
worst errors from 1, 73 and 11 to zero. The inherited expected-image lists remain
unchanged. `regression-final3-pixel-metrics.json` includes complete measurements.
The before/after Native UI Toolkit comparison reports also have identical hashes;
the inherited 53-versus-54-pixel label discrepancy remains.

Actual commands used for the independent baseline and final managed builds:

```sh
export PATH="$HOME/.dotnet:$HOME/.cargo/bin:$PATH"
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
# Executed at 1ac656a0, before runtime edits:
~/.cache/compat/with-heavy-job timeout 600 dotnet build managed/Unbound.Host/Unbound.Host.csproj \
  -c Release -o "$HOME/.cache/compat/camerafollowup-before-managed" \
  -p:UnboundTarget="$HOME/.cache/compat/camerafollowup-before-build/" \
  --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false
# Executed after the final runtime correction:
CAMERA_MSAA_BUILD="$HOME/.cache/compat/camerafollowup-build3/" \
UNBOUND_MANAGED_DIR="$HOME/.cache/compat/camerafollowup-managed3" \
  tests/projects/cameramsaa/run.sh build
```

The helper now accepts explicit output/intermediate paths while retaining its
original defaults. The commands below describe the recorded runs. All listed
outputs are retained evidence; choose fresh suffixes to repeat them.

```sh
f=tests/projects/cameramsaa
# Linear Native, twice; Gamma repeats used CAMERA_MSAA_PROJECT as below.
for n in 1 2; do
  CAMERA_MSAA_OUTPUT="$HOME/.cache/compat/camerafollowup-oracle-v3-linear$n" "$f/run.sh" native
done
# Gamma full copy: Assets, Packages and ProjectSettings only, with
# m_ActiveColorSpace: 0, under cameramsaa/Library/FollowupGammaV3.
# Authored settings remain Linear throughout.
CAMERA_MSAA_PROJECT="$PWD/$f/Library/FollowupGammaV3" \
CAMERA_MSAA_OUTPUT="$HOME/.cache/compat/camerafollowup-oracle-v3-gamma1" "$f/run.sh" native
CAMERA_MSAA_PROJECT="$PWD/$f/Library/FollowupGammaV3" \
CAMERA_MSAA_OUTPUT="$HOME/.cache/compat/camerafollowup-oracle-v3-gamma2" "$f/run.sh" native
# Both spaces were also captured with camerafollowup-before-managed before edits.
for space in linear gamma; do
  if [ "$space" = gamma ]; then
    export CAMERA_MSAA_PROJECT="$PWD/$f/Library/FollowupGammaV3"
  else
    unset CAMERA_MSAA_PROJECT
  fi
  CAMERA_MSAA_OUTPUT="$HOME/.cache/compat/camerafollowup-final3-$space" \
  UNBOUND_MANAGED_DIR="$HOME/.cache/compat/camerafollowup-managed3" "$f/run.sh" engine
done
unset CAMERA_MSAA_PROJECT
# Focused Native repeated twice per space before the chain/clear correction.
# Its Gamma copy is Library/FollowupFocusedGamma, made the same way.
CAMERA_MSAA_FOCUSED=1 \
CAMERA_MSAA_OUTPUT="$HOME/.cache/compat/camerafollowup-focused-native-linear1" "$f/run.sh" native
# Focused captures used both before-managed and managed3, for both spaces:
CAMERA_MSAA_FOCUSED=1 \
CAMERA_MSAA_OUTPUT="$HOME/.cache/compat/camerafollowup-focused-final3-linear" \
UNBOUND_MANAGED_DIR="$HOME/.cache/compat/camerafollowup-managed3" "$f/run.sh" engine
# Complete baseline and final regression runners:
CAMERA_MSAA_REGRESS_OUT="$HOME/.cache/compat/camerafollowup-regress-before" \
UNBOUND_MANAGED_DIR="$HOME/.cache/compat/camerafollowup-before-managed" "$f/regress.sh" followup-before
CAMERA_MSAA_REGRESS_OUT="$HOME/.cache/compat/camerafollowup-regress-final3" \
UNBOUND_MANAGED_DIR="$HOME/.cache/compat/camerafollowup-managed3" "$f/regress.sh" followup-final3
CAMERA_MSAA_BEFORE="$HOME/.cache/compat/camerafollowup-regress-before" \
CAMERA_MSAA_AFTER="$HOME/.cache/compat/camerafollowup-regress-final3" \
CAMERA_MSAA_REPORT_PREFIX=camerafollowup-final3 python3 "$f/check-regress.py"
python3 "$f/followup-evidence.py" final3
python3 "$f/followup-focused-evidence.py"
python3 "$f/followup-metrics.py" final3
~/.cache/compat/with-heavy-job timeout 600 dotnet test managed/UnityEngine.Tests/UnityEngine.Tests.csproj \
  -c Release -p:UnboundTarget="$HOME/.cache/compat/camerafollowup-test-build3/" \
  -p:UnboundNativeDir=/mnt/storage-hdd/node-b-caches-20261023/unialt-target-compat/managed \
  --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false \
  --filter 'FullyQualifiedName~CanvasOrder|FullyQualifiedName~UIElements|FullyQualifiedName~.YG|FullyQualifiedName~CoreTests|FullyQualifiedName~RectTransform'
```

Build3 has zero errors and 34 existing warnings. All engine/Editor/.NET jobs use
one global heavy-job lock; nested builds disable shared compilation/build servers.
No shared Cargo, managed or previous delegate output was built into. No real game
project, parent/backup branch, generated stub, ABI or shared progress document was
touched. Integrated API-check remains for the parent. No PR or merge was made.
