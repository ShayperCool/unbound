# Saved line geometry attribution, 2026-11-01

The measured package remains **REJECTED**. The source preparation below is
**UNBUILT / UNACCEPTED**. No measured failure has been closed by this work.
Source base is the authorized ordinary adoption of `ba658956`, compiled earlier
as `78ab4730`, from Root `870e4dfb`. Root's later platform integration `d662d302`
does not change these geometry inputs and was not merged here.

Original evidence:
`/mnt/data/unbound-cache-20261031/unbound-validation/runs/line-trail-combined-20261101-t3-9d5972f4/REPORT.md`.
This task's complete witnesses, diagnostic arrays and projections:
`/mnt/data/unbound-cache-20261031/unbound-validation/runs/line-trail-geometry-attribution-20261101-t3-dd198b0b/`.
The original full raw union, missing/extra values, changed retained values and
error increases remain untouched. Runtime manifests and verified receipts are
reused, not re-audited. No reference or comparator changes.

## Component introductions and causal split

Counts below are disjoint within their columns. An already-failing enclosing
mesh does not excuse a new component failure.

| Source hunk | New numeric/index paths vs Root | New numeric paths vs blend | New pixels vs Root | New channels vs Root |
| --- | ---: | ---: | ---: | ---: |
| Base rewrite, through `1df3e6cd`: start-cap indices | 60 | 0 | 0 | 0 |
| Base rewrite: world TransformZ render plane | 0 | 0 | 15 | 60 |
| Base rewrite: wider, Native-matching zigzag under mask handling | 0 | 0 | 80 | 320 |
| Base rewrite: trail cap/interpolation and texture overlap | 0 | 0 | 0 | 8 |
| Measured blend, `1012ca29` / `8e8c762b` | 0 | 0 | 0 | 0 |
| Cross-product tangent, `89b9141f` | 0 | 36 | 0 | 0 |
| Retained negative-cap turnback, `f0d31e78` / `704f3627` | 0 | 0 | 0 | 0 |
| Root prefix removal / `LoopDirections`, `49f152fd` | 0 | 0 | 0 | 0 |
| Queue / lifetime additions | 0 | 0 | 0 | 0 |

All 388 introduced image channels already fail in saved pre-blend `lr-cand3`.
The two affected image line meshes are byte-identical there and in blend cand1. Trail meshes
change with blending, but all eight channel introductions predate that change.
Final images equal blend cand1, so the later tangent, negative-cap and queue
hunks cannot explain their introduction. This is attribution of introductions,
not a claim that those later hunks leave every other quantity unchanged.

`introduced-paths-and-image-witnesses.json` retains every one of the 60 + 36
numeric witnesses and every affected pixel/channel, including Native, Root,
pre-blend, blend and final values. `cap-controls.json` includes all 171 applicable
saved capped meshes; `tangent-controls.json` includes all 37 lit meshes.

## Start caps: 60 introduced index components remain

For every `trail.f{1..15}.transformZ.{mid,end}.mesh`, the two paths are
`.idx[0].i[1]` and `.idx[0].i[2]`. Native and Root are respectively `1,2`;
pre-blend, blend and final are `2,1`. Thus there are 30 meshes times two paths.
Root's first triangle belongs to its differently tessellated strip; Native's
and final's first triangle belongs to the start cap. The numerical matches in
Root do not establish matching topology, but the 60 introductions still count.

The base rewrite's `Cap` always emits start `Tri(at, at+2+k, at+1+k)` and end
`Tri(at, at+1+k, at+2+k)`, and chooses cap displacement along the segment. All
30 Native TransformZ trail meshes instead bow their cap interiors over the
strip and reverse both cap fans. The smallest Native interior vertex is
`[-2.39944267,-0.486442268,0]`, versus final
`[-2.20055723,-0.2618438,0]`, at vertex 2 of the frame-1 end mesh. Their centres
and rim endpoints agree. These are geometry differences as well as indices.

Every one of those fixtures has exactly one cap vertex. The other 141 capped
meshes use two or three cap vertices; all keep the current fan orientation.
They include TransformZ lines. Alignment, cap count and trail origin are
confounded. No general winding or inward-cap rule is proven, so none is added.
The smallest discriminating question is whether the one-vertex inward cap
persists for a positive-width LineRenderer in View alignment. A future bounded
cap-count/alignment control, with a matching trail, would distinguish the
shared cap algorithm from a trail-only convention. No such capture is authorized
or performed here. Blanket TransformZ winding flips contradict existing controls.

## Tangents: 36 introduced components remain

All introductions against blend are `.t` components of
`trail.f{1,11,12}.round.{mid,end}.mesh`:

| Frame | Flat tangent-array indices per phase | Count per phase |
| --- | --- | ---: |
| 1 | 36,37,40,41,44,45,48,49 | 8 |
| 11 | 157,161,164,165,168,169 | 6 |
| 12 | 157,161,165,169 | 4 |

The smallest tangent witness is frame-1 `.t[36]`: Native and blend
`-0.198885649`, final `-0.1989244`. The position arrays are unchanged between
blend and final. The old tangent arc uses the angle between the directions;
the new tangent follows the side arc through `width * Cross(faceNormal,r)`.
Nearly coincident directions can round to an angle of zero while normalized
sides leave a small nonzero `Acos` angle. The later hunk therefore makes tangents
follow a pre-existing small position arc that Native does not have.

For all 30 lit round-trail meshes, reconstructing the new tangent from saved
corner `B-A` gives 510 pair checks with maximum error `2.06e-6`. All affected
components, not just the smallest one, are retained in the JSON report.
The complete cross-product hunk causes 480 numerical error increases against
blend, including six tolerated line components, and 136 changed retained paths.
It also closes all 78 previously failing tangent components of
`line.light.persp.spatial.c2cap2`. The other six lit line controls pass both ways.
Root has no tangent stream, which explains why these are not in its list of
new numeric paths; the raw missing fields are still preserved.

A complete tangent revert would restore the 36 components but lose those 78
measured spatial fixes. A corner-only split would also discard spatial corner
fixes. No floating-point/general interpolation rule has been proven across both
families. Both remain source-owned options for adjudication, not prepared changes.
The rejected reversal floor, signed miter clamp and corner-pivot rules are absent.

## Images and offline raster checks

The diagnostic CPU raster uses the authored orthographic camera, saved raw
positions, indices, vertex RGBA or UVs, and the authored shaders' interpolation.
It checks every pixel at the existing `2/255` threshold. It does not mask
boundaries or replace the comparator. All remaining mismatches are recorded.

For the TransformZ and inside-mask images, the saved Root, blend and final
meshes reproduce their corresponding engine images with zero failing pixels.
For the full trail image, composing all 19 saved meshes in fixture order, with
the five UV materials handled as authored, also reproduces Root/blend/final
with zero failing pixels. The Native trail replay retains 88 differing pixels;
it is not claimed as a full Native raster reconstruction. All eight introduced
trail channels individually reconstruct within the original threshold.

* `img.TransformZ.L.c2cap2.color`: all 15 new pixels are preserved. Pixel 2429
  is transparent in Native/Root and final RGBA is
  `[1,0.0313725508749485,0.0117647061124444,1]`. The saved Native bake mesh and
  final bake mesh match, but rasterizing that bake leaves 212 Native image
  failures. Replaying the existing strip algorithm with the authored object's
  forward, from `Quaternion.Euler(30,20,0)`, instead of build-space Z reproduces
  the Native image with zero failing pixels, maximum channel error `1/255`.
  No Native image values enter the construction. This distinguishes rendering
  from the already measured BakeMesh rule.
* `img.mask.VisibleInsideMask`: all 80 new pixels / 320 channels are transparent
  in Native and Root. The final baked zigzag matches Native geometry, including
  the miter widths. The Native inside-mask image is entirely transparent.
  Widening the Root strip to those Native-matching vertices under the engine's
  existing square clip reproduces all 80 introductions. Reverting the geometry
  would sacrifice the measured mesh fix; the masking/shader dispatch question
  lies outside this ownership and is not resolved by changing triangle widths.
* `img.trails.f10`: no newly failing pixels; eight newly failing channels are
  alpha at pixels 1665,1667,1668,1864,1957,1959, and U/red at 3102,3672. The six
  alpha values come from the TransformZ trail's cap/interpolation triangles.
  At the last two pixels, Native/Root's last covering trail is `tex.Tile`;
  final additionally covers them with the later `tex.DistributePerSegment`
  strip. Each raw contributing layer and color/UV interpolation is recorded in
  `trail-image-raster.json`. Neither a tangent-only change nor queue timing
  explains these saved geometry/color differences. No new per-mode width rule
  is fitted to them.

## Prepared source and full-union prediction

The only prepared production correction adds an explicit TransformZ forward
to `LineFacing`. BakeMesh and local-space construction keep build-space Z;
world-space LineRenderer collection supplies `transform.forward`. `Side`, the
corner face and the cap plane use that same explicit forward. Trail collection,
aging, queue, PlayerLoop, Gradient, shared particles and public API are unchanged.
The focused managed test checks the collection vertices against the offline
replay and verifies that a subsequent bake retains its original XY vertices.
That test has **not been built or run**.

`offline-projection.json` covers the full nine-image union and retains all
numeric paths by reference to the immutable 1887-key raw union. The prediction
is conditional on the prepared source implementing this replay:

| Quantity | Measured final | Offline prepared prediction |
| --- | ---: | ---: |
| Required / full key union | 1870 / 1887 | 1870 / 1887 |
| Failing whole keys | 215 | 214 |
| New numeric paths vs Root / blend | 60 / 36 | 60 / 36 |
| Changed retained numeric paths vs Root / blend | 4968 / 136 | 4968 / 136 |
| Numeric error increases vs Root / blend | 1279 / 480 | 1279 / 480 |
| New pixels vs Root / blend | 95 / 0 | 80 / 0 |
| New channels vs Root / blend | 388 / 0 | 328 / 0 |
| Failing pixels across nine images | 864 | 652 |
| Failing channels across nine images | 2630 | 2101 |

The CPU interpolation projection has 140 raw channel error increases versus
blend/final, all within tolerance; these are not omitted. Its Root-relative
raw image channel increases change from 572 to 516. This is a raster prediction,
not an assertion of exact future GPU RGBA bytes. The 15 timing failures, original
EOF resolution, 17 Root-only keys, all missing/extra values, and sphere48 /
zero-life21 dispositions remain as measured. No new runtime or suite result.

`attribute_geometry.py` is a standalone, read-only analysis client for those saved
inputs. It starts no build, engine, Editor or worker. All output is cache-only.
The one CCC refresh timed out after 75 seconds; its worktree registration was
removed and tracked ignore entry restored. No search/index clients remain.

Root's separate runner preparation `f60fe149` fixes the missing tracked shader
metadata and graphical frame count. It was not imported or investigated here.
Root can combine that runner, platform `d662d302`, and this source preparation
for the one later build only when authorized. That future runtime still needs
adjudication of the remaining cap, tangent and mask blockers. No PR, history
rewrite, force push, Native capture, Rust/Host/test build or GPU run in this task.
