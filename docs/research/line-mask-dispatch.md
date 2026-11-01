# Line mask dispatch diagnosis

Read-only research against source `d662d302`, Unity `6000.0.71f1`, 2026-11-01.
No production source edits, merge, build, test, Native capture or engine run.
The measured line package remains rejected. This document establishes a cause
and a narrow correction for Root's ownership decision, not runtime acceptance.

The 80 introduced pixels and 320 channels come from drawing a transparent-queue
SpriteMask before an opaque-queue line. Native draws the line's pass and applies
its mask-interaction stencil test while the target stencil is still zero.
Unbound eagerly draws the mask before that pass and admits pixels Native rejects.
The corrected line geometry merely exposes more of this shared dispatch error.

## Authored inputs

`tests/projects/lines/Assets/Scripts/LineProbe.cs`, especially `Images`, `NewLine`,
`NewCamera` and `Image`, authors the following inputs:

- Both line and mask are active, on object layer 9. The disabled image camera
  renders synchronously through `Camera.Render`, culling only layer 9. The main
  camera excludes that layer. There is no sorting group or custom mask range.
- Both use Default sorting layer ID 0 and sorting order 0. The mask is created
  before the line. Neither object sets a depth offset or material queue override.
- The line is the world-space View-aligned zigzag at Z 0, width multiplier 0.4,
  constant width curve, no cap/corner vertices, and the authored three-key color
  gradient. Its identity transform does not move it. Native's two mask-case bake
  records contain the same mesh. The prior attribution established the final
  mesh matches Native; this task does not repeat that analysis.
- The material is `LineProbe/Color`, loaded from `Resources/LineColor.shader`.
  Its sole unnamed pass has `Queue=Geometry`, hence 2000, `Cull Off`, `ZWrite Off`,
  `ZTest Always`, default blend off, and no shader-authored stencil block or
  LightMode restriction. It returns interpolated vertex RGBA unchanged.
- The mask is at the origin with identity rotation and scale 1.5,1.5,1. Its
  sprite comes from a fully opaque white 4x4 RGBA32 texture, centered rect/pivot,
  4 pixels per unit. Its nominal full square spans -0.75..0.75 in X and Y.
  Alpha cutoff defaults to 0.2; mask source is Sprite; custom range is false.
  Canonical `resources/Reference/values.jsonl` records these defaults and
  `Sprites-Mask|Sprites/Mask` as the material.
- Unity's installed built-in shader source
  `~/.cache/unbound/builtin-shaders/v1/6000.0.71f1/DefaultResourcesExtra/Sprites-Mask.shader`
  declares `Queue=Transparent`, hence 3000, `ColorMask 0`, `ZWrite Off`,
  `Cull Off`, and alpha cutoff via `clip(c.a - _Cutoff)`. It has no shader-authored
  stencil block. Native installs the stencil write around its draw.
- The camera is orthographic at 0,0,-10 looking at the origin, size 2, aspect
  96/64, near/far 0.3/50. Each case creates a fresh 96x64 ARGBFloat linear target
  with depth 24 and one sample. SolidColor clear writes transparent black.
  HDR/MSAA are disabled. All four float channels are read, including alpha.
  `ProjectSettings/GraphicsSettings.asset` sets no custom pipeline and default
  desktop/mobile rendering path 1, Forward, with no tier overrides.

Queue membership is independent of blend and ZWrite. The line remains in the
opaque stage even though its gradient has partial alpha and it disables depth
writes. Its `ZTest Always` cannot manufacture a mask stencil write.

## Local Native proof

The local Editor and its full-symbol companion are
`~/Unity/Hub/Editor/6000.0.71f1/Editor/{Unity,Unity_s.debug}`. Both have ELF build ID
`9a4f6559dbeb9b8aa47d7c970b8858ddda4f717c`. These were read, never executed.
The machine-code observations below use symbol names from `nm -C` and bounded
`objdump -dC` ranges. They are a binary-backed reconstruction, not unavailable
C++ source or a dynamic GPU trace.

- `SpriteMaskJobs::SpriteMaskPrepareRendererNodes<false>`, `0x2edab80`, prepares
  mask boundary nodes. At `0x2edb00c` and `0x2edac12` it calls the same
  `BaseRenderer::FlattenSharedMaterialData<false>` used for renderer material
  data, then `PrepareNodeForMaskSource` with the two boundary node types.
  `SpriteMaskSetupRenderNode`, `0x2eda750`, assigns the mask draw callback at
  `0x2eda83b`; the mask is a render node, not an unconditional pre-camera clip.
  `LineRenderer::PrepareRenderNodes<false>`, `0x41a0da0`, calls that same material
  flattener at `0x41a10a2`, without rewriting the line's queue for masking.
- `DoRenderLoop`, `0x1da4760`, obtains each effective material/shader queue at
  `0x1da4bb8..0x1da4bfb`. At `0x1da4c08` it compares against `0x9c5`, decimal
  2501, placing lower queues into the vector at context offset `0xda0` and
  higher queues into `0xdc0`. There is no SpriteMask exception in this split.
  `0x1da4cfb` stores that queue at RenderObjectData offset `0x10`.
- The same function calls `DoForwardShaderRenderLoop` on `0xda0` at `0x1da4f76`
  under `gRenderOpaque`, then on `0xdc0` at `0x1da509d` under
  `gRenderTransparent`, after the sky and opaque image filters. Thus this
  default mask's queue-3000 write cannot affect the queue-2000 line in the
  fresh cleared target. Sorting layer/order ties cannot reverse these stages.
- `DrawUtil::DrawLineOrTrailFromNodeQueue<(SpriteMaskInteraction)1>`,
  `0x1e9f410`, loads its inside stencil state at `0x1e9f473`, calls
  `SetupMaskingStencilState` at `0x1e9f47d`, and proceeds to the geometry draw.
  This callback does not inspect shader names or suppress custom materials.
  Corresponding None/Outside and multiple-draw callbacks exist.
- `CreateMaskingStencilStates`, `0x1ee8f40`, selects comparison enum 4,
  LessEqual, for Inside and 5, Greater, for Outside, with stencil writes off.
  `SetupMaskingStencilState`, `0x1ee8ff0`, submits the state with reference 1
  at `0x1ee9042`. A zero stencil therefore rejects Inside and admits Outside.
  The helper also has picking/device/scene-render-mode exclusions, which are
  outside this measured camera path.
- `DrawSpriteMaskRawFromNodeQueue`, `0x2eda360`, chooses mask toggle-on/off
  stencil state at `0x2eda649..0x2eda66b` and draws the mask mesh. This is the
  scheduled write the Unbound eager path moves across the stage boundary.

These instructions, the authored shader source, and both existing Native images
jointly support the cause. The transparent inside sample alone would not do so.

## Public contract and counterexamples

`maskInteraction` selects the renderer's stencil visibility test. It does not
promise that every active mask has already drawn when any arbitrary material
pass runs. Neither this API nor the observed callback requires a sprite-specific
shader name, `CanUseSpriteAtlas`, an authored stencil block, or a shader whitelist.
The built-in `Sprites/Default` shader itself has no stencil block.

The saved Native outside image has 590 visible pixels with the *same custom line
shader*. This rules out a blanket unsupported/custom-material-pass suppression.
Its exact relation to the engine pair below also rules out a smaller geometric
mask as the explanation needed for these pixels. This does not establish all
custom shaders, multipass or replacement-shader dispatch.

The independent resources controls use transparent `Sprites/Default` materials.
`mask_other_renderers` explicitly exercises inside/outside lines, an inside trail
and a tilemap; `mask_particles`, basic/cutoff/range/group/source controls exercise
other existing mask behavior. The saved combined resources union has all 539
values and 62 images passing at its unchanged tolerances. In particular,
`mask_other_renderers.png` has worst channel error 1 byte, mean 0.09625 bytes,
and no channel above 8 bytes. Disabling mask interaction for lines, custom
materials, or materials without authored stencil would contradict these controls.
Those controls share the transparent stage and do not test the opaque-line gap.
Their tolerated edge differences are not proof of this dispatch mechanism.

The older line mask failures *do* witness it. Matching Root already emits 144
inside pixels where Native emits zero, with 576 failing channels. Final emits
224 inside pixels and 896 failing channels. The 80/320 introductions and all
385 Root-relative raw channel error increases in that image remain counted.
An unchanged mask file does not waive an introduced image difference.

## Offline closure from the saved images

`tools/validation/analyze_line_mask_dispatch.py` reads only the already validated
eight inside/outside reference/Root/blend/final float arrays. It reconstructs no
geometry, invokes no renderer and does not alter any original report or oracle.
At all 6144 pixels and 24576 channels in each image:

- Final inside and outside images have no overlapping nonzero pixels.
- Their componentwise sum equals the complete Native outside image bit for bit.
  Maximum and mean channel errors are exactly zero, including all alpha values.
- Native inside is exactly zero everywhere. Empty-stencil prediction gives
  exactly that image and the full outside sum, resolving 224 pixels and 896
  channels in **each** mask image. It introduces zero failures and zero raw
  error increases against Root, blend or final for these two images.

This algebra preserves all source float colors. It is a prediction of the
identified dispatch correction, not a replacement capture or comparator result.
`algebra.json` retains every differing/failing component, all raw versions,
absolute errors and every introduction/error-increase index.
`all-raw-colors-and-errors.npz` also preserves every input/predicted channel,
including zeros, and all full error arrays. No boundary, color or test mask.

## Exact correction site and ownership

The shared defect is `managed/UnityEngine/Graphics/SpriteMask.cs`:
`Covers` line 282 returns true for every default-range mask; `Before` lines
312-328 uses that result to eagerly emit `DrawMask` regardless of the mask's
render stage. `FrameBuilder.EmitDraw` lines 776-785 installs this path for any
masked renderer in both built-in and renderer-list dispatch. Existing
`Material.renderQueue` and `DrawItem.Queue` already supply the needed queues.
Mask source here is byte-identical to the permitted prepared `4e700985` baseline;
there was no need to merge its separate unaccepted geometry work.

The smallest causal hunk is the `s_Wanted` population inside `Before`. For the
measured built-in forward camera emission, its necessary phase exclusion is:

```csharp
// Necessary phase exclusion, not a complete replacement for mask scheduling.
// Add this to eligibility before Covers(), retaining the existing stencil test.
foreach (var mask in Masks())
{
    if (!mask.Mask) continue;
    var material = mask.Mask.sharedMaterial ?? BuiltinObjects.SpritesMask;
    bool laterPhase = GraphicsSettings.currentRenderPipeline == null
        && s_Camera != null && s_Camera.actualRenderingPath == RenderingPath.Forward
        && item.Queue <= (int)RenderQueue.GeometryLast
        && material != null && material.renderQueue > (int)RenderQueue.GeometryLast;
    if (!laterPhase && Covers(mask, item, renderer)) s_Wanted.Add(mask.Mask);
}
```

This is a proposed hunk, deliberately not installed. Its
rule is that a mask scheduled in a later camera stage cannot contribute an
earlier stencil write. Both inside and outside tests stay active, so an empty
stencil rejects the former and admits the latter for every supported renderer.
No line-specific, shader-name, caller-name, square or epsilon condition is needed.

The snippet describes the captured branch. It is not a safe unconditional patch
for every caller of `Before`: a null current render-pipeline asset plus material
queue does not prove that a command-buffer or renderer-list invocation is in the
camera's opaque stage. A shared implementation must apply the rule using actual
dispatch context, preserving later explicit draws and pre-existing stencil.
The current helper does not expose that context. This task therefore does not
claim a complete production-ready mask-only patch for all callers, nor authorize
a global FrameBuilder change to obtain it. Root must adjudicate that ownership.

One shared mask owner must adjudicate this with the rendering-lifecycle owner
before editing. Geometry remains with its single owner. No change is indicated
to line widths, mesh generation, global FrameBuilder/executor/sampler/math,
public API, Native ABI or generated stubs. A later matching-runtime gate needs
the existing full nine-image line union and the affected resources controls,
preserving their original comparators and complete raw results. No new Native
measurement is necessary to distinguish the cause of these 80 pixels.

This condition is a necessary exclusion, not proof of a full mask scheduler.
It does not establish same-stage ordering with custom mask queues, custom
back/front boundaries in opaque queues, overlapping opaque mask materials,
multi-camera targets retaining stencil, command-buffer stencil writes or clears,
screen/backbuffer rendering, callback mutations, deferred/VertexLit rendering,
SRP renderer lists, override materials/shaders, or picking/scene-view behavior.
The current `Before` interface also has no effective override-material dispatch
context. Extending the guard to those paths would need separate proof and
ownership, not an ABI change inferred from this sample. The measured fresh
Camera.Render target avoids those ambiguities. No broader rewrite is proposed.

## Evidence and unchanged dispositions

Original full receipt, all comparisons, missing/extra rows and input/runtime
manifests remain immutable at:

`/mnt/data/unbound-cache-20261031/unbound-validation/runs/line-trail-combined-20261101-t3-9d5972f4/REPORT.md`

Prior attribution, full 80/320 witnesses with pre-blend values, and geometry
limitations remain immutable at:

`/mnt/data/unbound-cache-20261031/unbound-validation/runs/line-trail-geometry-attribution-20261101-t3-dd198b0b/REPORT.md`

New read-only proof excerpts, commands and image algebra:

`/mnt/data/unbound-cache-20261031/unbound-validation/runs/line-mask-dispatch-research-20261101-t3-e46c0814/`

Required/full unions remain 1870/1887. Measured final retains 215 failing whole
keys, 60 introduced Root index paths, 36 introduced blend tangent paths,
95 introduced pixels/388 channels, all 15 time failures, and the measured EOF
resolution. Original numerical error increases and all trail/TransformZ residuals
remain in their original reports. Root's sphere48 and zero-life21 remain open.
The prepared TransformZ correction is unbuilt/unaccepted and is not adopted here.
No original acceptance count is changed by the mask-only offline prediction.

CCC was initialized for this private worktree; its one bounded refresh timed out
at 75 seconds without search results. Exact-name searches continued with rg.
`ccc reset --all -f` completed, removing this worktree's registration and index;
the existing tracked ignore entry was restored. No shared daemon was stopped.
Two named-function GDB disassembly attempts could not resolve demangled names;
bounded objdump ranges against the full-symbol binary supplied the proof.
No search/index clients or payload jobs remain. No PR or history rewrite.
