# Parity continuation, 2026-10-30

The interrupted Claude work lives on `compat/close-gaps`, originally checked out in
`.claude/worktrees/compat-gaps`. Its last commit was `982c93b2`. The root checkout was still on
`main` at `4724b839`, so continuing from that checkout alone would have missed 58 commits.

`backup/claude-parity-20261030` preserves those commits and all 35 changed or new files from the
interrupted working tree, including probe assets and reference images. Snapshot commit:
`b184aada`. The old `compat/close-gaps` branch still points at its original commit. The snapshot
branch is checked out in the old worktree; leave it intact.

`compat/parity-continue-20261030` starts at that snapshot. Subsequent implementation and test
commits belong here. The root checkout uses this branch; `main` has not been moved.

## Recovered state

Claude's compatibility audit session stopped on an Anthropic account verification error during
phase 2c, render resources. Its plan was only in `~/.cache/compat/PLAN.md`; this file records the
handoff in the repository.

The earlier session reported:

- Phase 0 tools implemented, including silent-member checks, stub-event warnings, unsupported
  message warnings and unread serialization tracing. Its API check reported zero errors.
- Phase 1 messages and player loop implemented, with 316/316 Editor probe records matching.
- Most of phases 2a and 2b implemented. These are not complete rendering parity: halo/flare
  drawing, per-pixel LPPV, shadowmask mixing, skinned motion vectors and several other details
  remain. Consult `COMPATIBILITY_GAPS.md` and the probe READMEs for each limitation.
- Phase 2c partially implemented: SpriteMask stencil ranges, sorting groups, tiled sprites and
  secondary textures. Secondary-texture API, vertex attributes and URP 2D probes were unfinished.
- Animation/audio, physics, UI/input and assets/networking still have open rows.

The other recent sessions worked on material/shader declarations and on obtaining Editor data
without an installation. Material changes are already ancestors of the parity branch. Editor
archive/package changes were merged into `origin/main` via PR #3, after the parity branch split.
The continuation also merges that version of `origin/main`.

## Validated continuation

Reference Editor: Unity 6000.0.71f1, Linux, NVIDIA GeForce RTX 4060, gamma color space. Run the
Editor with a GPU display. Do not replace it with software GL for reference images.

- Resource probe: imported and created secondary textures, copying/query semantics, texture
  precedence, default texels and sprite vertex frames for every flip in Simple/Sliced/Tiled.
  Sprite flips now change vertex positions and tangent frames without changing the object's
  matrix. The resource comparison also fails for missing images and mismatches; six failure-case
  tests cover the checker itself.
- Unassigned samplers use their own neutral placeholder. The interrupted changes used the
  declared gray default for both, which broke 240 built-in camera-event values. The Editor probe
  measures 127 for the declared default and 128 for an unassigned sampler.
- Command buffer renderers: moving bones, blend shapes, renderer/slot property blocks and
  changes after recording. Four Editor images cover BeforeForwardAlpha and AfterEverything.
  Geometry and property blocks are taken at execution. Late commands keep scene depth during
  MSAA rendering, with the edge antialiasing limitation recorded in the resource probe README.
- URP 2D: 13 scenarios, including normal maps, mask textures, all flip states and SpriteMask.
  Every image is within a mean largest-channel difference of 0.02/255 from the Editor.
- Two inherited ShaderLab test fixtures omitted the new `vertex_lit` field and prevented the
  test target from compiling. They now use the default selection for fields outside their LOD
  checks. All 16 `unbound-app` library tests pass, as do all six resource-comparator tests.
- After merging `origin/main`, the release build succeeded. `api-check` reported zero errors in
  2221 hand-written types against Unity 6000.0.71f1.

## Texture2D mip chains, continued

Runtime and serialized Texture2D chains now retain every mip on the CPU and GPU. The resource
probe has 269/269 matching values and 62/62 images within its mean-difference threshold. The new
182 values cover six byte/float formats, manual levels, `tex2Dlod`, generated NPOT levels, raw
chains, imported CPU levels, `.asset` mips, invalid parameters and readability behavior.
The API check still reports zero errors in 2221 types.

## RenderTexture mip chains, continued

The additional 144 Editor values cover manual render levels, explicit and automatic filtering,
command-buffer generation, cube faces, array slices and MSAA resolve. RFloat and ARGBFloat render
targets now retain 32-bit precision. The compiler also uses the emitted WGSL entry point name when
Naga renames a reserved HLSL identifier, in both graphics and compute programs; all 34 shader
compiler tests pass. The complete resource reference now contains 413 values.

The asset test suite had an outdated baked-cubemap size: its EXR header and scene generator both
specify 64-pixel faces, while the test expected 128. The test now checks the actual fixture.

All 14 related graphics groups passed their regression with the same existing differences:
`~/.cache/compat/rtmips-render-reg-results.txt`. The resource probe matches 413/413 values and
62/62 images, worst mean 0.83/255. Rust library tests passed: app 16, runtime 38, assets 59.
The API check reports zero errors in 2221 types.

## Compressed bundle mip chains, continued

Bundle loading now decodes every BC/ETC/ASTC mip instead of dropping the lower levels. It also
retains encoded source data for raw-data queries and the original graphics format. Editor-built
DXT1 and DXT5 bundles match all 26 new CPU/GPU, format and raw-data values. Missing compressed
levels and invalid mip counts fail with a diagnostic. The complete resource reference now has
439 values.

## SRP shader overrides, continued

Renderer lists now apply `DrawingSettings.overrideShader` while retaining each source material's
properties. Four URP render-graph scenarios check both passes, -1 selecting the first pass, and
the simultaneous material override's precedence and error diagnostic. Every new image matches
the Editor within 1/255 at the worst pixel. The existing URP scenarios retain their prior results.

## Scissor and MRT clears, continued

Command buffers now clip draws, blits and clears with the active scissor. MRT clears accept
different colors per attachment, copy recorded color arrays, and restore every color attachment
for subsequent draws. The Editor's null/empty/oversized color-array errors and fixed dynamic
resolution factors are measured too. All 22 new values match, bringing the resource reference
to 461 values and 62 images. The API check remains at zero errors in 2221 types.

## Float texture component formats, continued

R/RG/RGBA half and full float now retain their native component count and precision on the GPU,
including arrays, volumes, cubes and cube arrays. Layered CPU setters quantize half values and
return the Editor's missing-channel values. All 78 new checks match exact float bits, including
sampler updates on unreadable textures, raw render-target reads and shader samples. The complete
resource reference now has 539 values and 62 images.

## Generic Animator root motion, continued

`tests/projects/animator` now measures 658 records against the Editor. Runtime root motion matches
the callbacks' order, disabled handlers requesting deltas, the apply flag, one builtin application
per evaluation, world/angular velocities, zero and backward time and several crossed loops.
Nested nonuniform and negative scale are included: animation uses the parent's decomposed TRS,
so the API root pose and actual Transform can differ. The root-pose setters retain an independent
API value, and a setter during a movement callback does not move a generic root.

This closes the runtime generic-motion part of the audit row. Imported root-node selection,
Bake Into Pose, matching, humanoids and body/controller integration remain separate work.

The API check reports zero errors in 2221 types. Animation-from-bundle matches 8/8 records,
lifecycle matches exactly, and messages match 316/316. Playables retain their two existing
scenario differences, with no new ones.

## Blend tree interpolation, continued

Simple Directional uses triangle fans with the Editor's central/default contribution, including
trees without idle and degenerate layouts. Freeform Directional uses polar gradient bands rather
than Cartesian ones. Direct nodes honor their normalization flag and preserve raw sums above
one when it is disabled; missing weight comes from bound defaults rather than previous frames.
The same eight tree configurations are measured as text assets and as compiled AssetBundle
controllers. All 1232 new records match; the Animator reference now has 1890 records in total.

## Building and checking

Use a separate Cargo target directory to avoid replacing another checkout's executable. The
recovered target is `/mnt/storage-hdd/node-b-caches-20261023/unialt-target-compat`; use at most
six build jobs and run heavy builds/probes sequentially.

When reusing that target from a different checkout, explicitly rebuild managed code from the
current sources too. Cached Cargo build-script dependencies can still point into the old
worktree. Example from the root checkout:

```bash
export PATH="$HOME/.cargo/bin:$HOME/.dotnet:$PATH"
export CARGO_TARGET_DIR=/mnt/storage-hdd/node-b-caches-20261023/unialt-target-compat
export CARGO_BUILD_JOBS=6
cargo build --release
dotnet build -c Release -o "$CARGO_TARGET_DIR/managed" managed/Unbound.Host/Unbound.Host.csproj
export UNBOUND_MANAGED_DIR="$CARGO_TARGET_DIR/managed"
```

Probe commands are in `tests/projects/resources/README.md` and `tests/projects/urp2d/README.md`.
The recovered broader regression script and its previous results are in
`~/.cache/compat/regress.sh` and `regress-baseline.txt`. That script hard-codes the old worktree;
use the current checkout and a new output directory when running it again. Several earlier
physics, navigation, particle and image probes already differ from the Editor in that baseline.
Do not report them as newly introduced failures, or as full parity.

The continuation completed 25 groups of the broader script before stopping its long LOD sweep.
This was not a complete regression run. Results are in `~/.cache/compat/codex-regress-results.txt`.
After correcting the sampler placeholder, all 14 related graphics groups were rerun via
`~/.cache/compat/codex-render-final.sh`, with results in `codex-render-final-results.txt`.
The 12 unchanged groups reproduce their earlier summaries exactly. The other two are the
expanded resources and URP 2D probes. Built-in RP is back to zero unexpected value differences.
Resources match 87/87 values and 62/62 images within a mean difference of 2/255, worst mean 0.83.
URP 2D has 13 scenarios within a mean difference of 0.02/255. Known baseline differences in
particles, lighting and terrain remain. These results do not establish full engine parity.

## Remaining work

Continue phase 2c with remaining texture/RT formats, sub-elements and shapes, custom render
textures, SRP shader overrides and the remaining low-level graphics calls. Then continue the
animation/audio, physics, UI/input and import/networking sections of `COMPATIBILITY_GAPS.md`.
Keep an Editor probe for each behavioral change and leave the snapshot branch unchanged. A full
parity PR is not ready while those rows remain open.
