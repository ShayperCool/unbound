# SpriteMask opaque dispatch preparation

Prepared from Root `d662d3023261dc34a7dbc751a8bc86758dd81765`. A matching mask-only
runtime was validated afterwards (see the end of this file); Root decides on acceptance.
NativeApi15/render7/shader16/cache13/model11/shim9 are unchanged. Exact generated
stub changes and removals: `[]`. No native API reservation or protocol change.

`RenderCamera` supplies `BuiltinForwardOpaque` only to its actual opaque-array
`EmitItems` call when neither deferred nor VertexLit is active. `EmitItems`,
`EmitForward` and `EmitDraw` pass this argument directly to `SpriteMasking.Before`.
Every omitted argument defaults to `Unknown`. There is no ambient stage flag or
inference from pipeline assets, camera pointers, item queues or shader names.
Callbacks, renderer lists, overlays, explicit draws, depth/shadow draws, deferred
and its forward fallback retain their previous dispatch context.

`Before` still computes range/group coverage for every mask and reconciles the
list keyed by the current color/depth target. Only the loop adding a new tracked
mask excludes an effective material queue above `RenderQueue.GeometryLast` in
the tagged stage. A covering mask already tracked on that target is retained,
including after another camera's `Begin`. Filtering the wanted list instead
would wrongly decrement that pre-existing contribution. Range/group exits,
depth/stencil clears, Inside/Outside tests and override restoration retain their
existing handling. An empty eligible set still installs reference 1 with
LessEqual for Inside or Greater for Outside, with stencil writes disabled.

The shared `FrameBuilder.cs` hunks contain only optional trailing arguments,
argument forwarding and the opaque call-site expression. The lighting/probe
body of `EmitDraw` and all camera hooks/lifecycle logic are untouched. Root must
combine these hunks with the concurrent LightProbes owner without replacing its
lighting changes. Geometry, runners and TransformZ preparation are separate.

The prepared managed tests decode state at actual recorded Draw commands through
`EmitItems`/`EmitForward`, and use the actual renderer-list entry point. Their 15
cases cover both interactions, default Unknown opaque/transparent emission,
effective material queues at the stage boundary, existing target contributions,
camera Begin, color/depth/stencil clears, range/group exits, override restoration,
an untagged callback and renderer list following tagged emission, and fixture
restoration. They isolate registries, mask tracking, stream bytes/state, scratch
lists, material handles, shader caches/IDs, callbacks and object IDs. Synthetic
pre-uploaded resources require no installed native engine. They start no scene,
coroutine, Invoke or player-loop step. Permanent engine static initialization is
retained; test-specific state is restored even on fixture-construction failure.
These tests do not exercise the complete camera lifecycle or nested Camera.Render.

Saved Unity `6000.0.71f1` research establishes the relevant material-queue order:
queue-2000 lines draw before the queue-3000 mask writes stencil. The opaque stage
contains queues through 2500; later queues draw in the transparent stage.
Inside/Outside keep reference 1 with LessEqual/Greater, respectively. The full
research report remains in the run directory linked below. This is source and
saved-measurement evidence, not a new runtime result.

Evidence directory:
`/mnt/data/unbound-cache-20261031/unbound-validation/runs/line-mask-dispatch-research-20261101-t3-e46c0814/`.
Its immutable `REPORT.md` and `image-algebra/algebra.json` establish that the saved
final inside plus outside arrays equal Native outside byte for byte, and Native
inside is all zero. The empty-stencil model resolves 224 pixels / 896 channels
in each image, including the 80 pixels / 320 channels introduced inside. That
projection uses the saved final geometry; this branch alone has no matching
runtime capture. It changes neither geometry nor comparator tolerances.

Original measured report and complete comparisons remain at
`/mnt/data/unbound-cache-20261031/unbound-validation/runs/line-trail-combined-20261101-t3-9d5972f4/`.
All 60 index, 36 tangent, 95 pixel / 388 channel introductions and 15 time
failures remain measured until a later capture. Root's sphere48 and zero-life21
also remain open. No original report, reference, comparator or manifest is edited.

This is a necessary stage exclusion, not a full SpriteMask scheduler. Custom
queues and same-stage boundary ordering, multipass shaders, override materials
and replacement shaders, picking/scene view, SRP, retained multi-camera targets
with arbitrary stencil writes, nested callbacks/cameras and unsupported callers
retain their explicit validation limits. No broader behavior is established.

Root's next gate is one authorized coherent build with geometry, runner and
LightProbes integration, then the managed/API checks and affected runtime gates.
Keep the full existing nine-image line union and all missing/extra/failing values.
Run resources mask controls, including basic/cutoff/source/range/group,
mask_other_renderers and mask_particles. Include relevant built-in forward opaque
and transparent controls, deferred/fallback/VertexLit exclusions, callback and
renderer-list/override controls, and depth-clear/retained-target controls.
Use the existing release-selected canonical references and unchanged tolerances.

## Mask-only runtime validation (2026-11-01)

One coherent Host/Core/test build of the integrated source, compared against the frozen Root
runtime (13b7dd2d) on the same fixtures. Full managed suite: 1488/1499, the 11 known failures
unchanged, all 15 mask tests pass. API check: 0 errors, 2314 types, stub changes `[]`.
Lines: VisibleInsideMask goes from 144 failing pixels to 0, VisibleOutsideMask stays at 590,
no pixels or channels are introduced, and the other seven images are byte-identical. In
VisibleOutsideMask, 246 raw channel errors increase inside pixels that were already failing;
they are retained. Resources: 539/539 values and 62/62 images, byte-identical. The
builtinrp/lighting/graphics/urp outputs are byte-identical; builtinfx differs only in an image
its comparator already lists as expected. None of these controls has masks. Masked VertexLit,
deferred, renderer-list, override, callback and depth-clear paths remain unmeasured at runtime.
Receipt: `~/.cache/unbound/validation/runs/mask-only-integration-20261101-t3-65ed30f3/HANDOFF.md`.
