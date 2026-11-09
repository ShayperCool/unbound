# Reversal joints on Unity 6000.0.71f1

The `miterrev.*` controls extend the existing lines probe after its old observations.
They use synchronous public `BakeMesh` calls, positive constant widths and no caps.
Angles approach and cross 180 degrees, with zero or two corner vertices. Width,
segment lengths, neighboring joints, perspective facing and a rotated TransformZ
object distinguish join arithmetic from camera and transform behavior. Emitting
trails use `Clear` and `AddPositions` at a stationary head, without yielding, so
their effective widths are nonzero and their geometry is independent of frame count.
The 96 by 64 `miterrev.image` isolates the 179-degree unrounded line.

## Contract and cause table

The pages below were checked for Unity 6000.0 on 2026-11-06, including their
limitations. None specifies a numerical miter cutoff or an intersection algorithm.

| Kind | Source or context | Applicable statement |
| --- | --- | --- |
| Documented | [Line Renderer properties](https://docs.unity3d.com/6000.0/Documentation/Manual/class-LineRenderer-line-renderer-properties.html), [Trail Renderer properties](https://docs.unity3d.com/6000.0/Documentation/Manual/class-TrailRenderer.html) | Width curves are sampled at vertices, limiting their accuracy to the available vertices. Corner and cap counts round those regions. Alignment selects camera facing or Transform Z. A line's loop joins first and last positions. |
| Documented | [Line BakeMesh](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/LineRenderer.BakeMesh.html), [Trail BakeMesh](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TrailRenderer.BakeMesh.html) | BakeMesh takes a snapshot. The camera determines camera-space facing. `useTransform` includes rotation and scale. |
| Documented | [Line SetPositions](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/LineRenderer.SetPositions.html), [Trail SetPositions](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TrailRenderer.SetPositions.html), [Trail AddPositions](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TrailRenderer.AddPositions.html) | Set the line's position count first; surplus array entries are ignored. Trail SetPositions edits existing positions only; AddPositions appends points. The controls record position readbacks. |
| Observed | Two independent Linux GPU Editor captures, 6000.0.71f1, `r8-miter-native` | All 2038 records and ten float images are byte-identical. The 1885 existing canonical and cap-count records reproduce byte for byte, as do all nine old images. The new 153 records and one image are published separately beside those immutable files. |
| Observed | Planar 179-degree turn, width 0.4, unit segments | The joint offsets are opposite vectors with absolute XY components 0.3490491 and 0.199999988. Line and emitting-trail meshes agree. The 179.5- and 179.9-degree controls approach the incoming side instead of keeping a three-half-width spike. |
| Implementation hypothesis | Guarded intersection, checked against the added controls | For side directions `s1`, `s2` with cosine `c`, the uncapped offset per half width is `s1 + (s2 - c*s1) / max(1+c, 0.01)`. The existing three-half-width and segment-reach limits apply to its magnitude. This is a measured-release reconstruction, not a Unity guarantee or evidence of Unity's internal code. |

The later `miterchain.*` controls resolve the reach limit left by the first
correction. Fourteen paths include swapped incoming/outgoing lengths, constant
and tapered API2 paths, a short straight prefix before 10/90/140-degree turns,
and unequal consecutive reversals. Both renderer types and corner counts 0/2
are measured synchronously. Two independent final GPU captures,
`r8-chain-final-native`, reproduce every earlier record and image byte for byte
and add 112 byte-identical records in `miterchain.values.jsonl`.

Observed reach scales distinguish the segment two points back, or the first
segment at the first joint, from either adjacent lengths or their minimum.
For the measured positive-width open strips, the miter is limited to the local half width times
`clamp(3 * earlierSegmentLength / (2 * widestWidth), 1, 3)`, where the widest width is the
largest width curve key times the multiplier. The tapered controls retain the same
dimensionless scale as their constant-width counterparts. Lighting tangents follow the
segment's side even without corner vertices. One strip builder now covers these controls,
negative widths and loops alike (docs/COMPATIBILITY_GAPS.md, LineRenderer/TrailRenderer
geometry); other releases are unmeasured and use it too.

## Reference publication

`miter_reference.py` checks the independent GPU repeats through the existing runner,
their pre-launch source hashes, all old records and images, and the predeclared new
keys. It accepts only the already documented move of `complete` to the end. It
creates `miterrev.values.jsonl` and `miterrev.image.rgba.gz` exclusively. It never
writes `values.jsonl`, `capcount.values.jsonl` or their images. The cache receipt
records hashes for both repeats and the reference before and after publication.
`--chains` applies the same checks to all previous records and images, then
creates only `miterchain.values.jsonl`.

```sh
python3 tests/projects/lines/run.py editor --release 6000.0.71f1 --label LABEL
python3 tests/projects/lines/miter_reference.py --release 6000.0.71f1 --label LABEL \
  --receipt ~/.cache/unbound/r8/reference-receipt.json
python3 tests/projects/lines/compare.py tests/projects/lines/Reference/unity-6000.0.71f1 \
  CAPTURE --mode miter --before BEFORE --report ~/.cache/unbound/r8/comparison.json
```

The `miter` comparison mode includes every old record, all control files and every
capture key. The numerical and image tolerances are unchanged. Negative widths,
view-axis degeneracy, perceptual gradients, trail frame count and autodestruct
remain separate questions. Reversing loops' corner inner sides are also separate;
the open-strip denominator correction does not change that rule.

## Validation and retained gaps

`check_miters.py` runs the engine against the committed release reference, saves
the complete strict key report and every failing leaf/image channel under its
cache capture, and gates the reversal controls and old API2 cases. The suite
continues to use the full strict comparator, including all disjoint controls;
unrelated failures remain failures there.

The original set has 81 failing keys before R8. Large errors in `line.miter.179`,
the API2 copy and API2Emitting meshes/copies are resolved. The new 179-degree
image's 36 failing pixels are resolved. Four rotated TransformZ `useTransform=true`
meshes expose a separate BakeMesh plane/normal gap and stay in the strict report.
Their input records still gate. No camera/transform change is included here.

The two `trail.f6.api2Emitting.{mid,end}.asline` records retain x-component errors
of at most 4.42e-5 at vertices 2/3. Their native trail meshes pass. CopyAsLine's
pair-center readback differs in y by 4.8e-8 before being fed back through the
near-cancelling short-segment directions and reversal intersection. This float
amplification is the suspected cause. It affects only these synthetic copied
meshes, with no visible or gameplay effect. The gate admits only those two
components per key within 5e-5, and the strict comparator retains every failure
at its original 1e-5 absolute plus 1e-5 relative tolerance. No further Native
capture was spent on this residual.

Final strict comparison: 2099/2150 keys match, with 51 failing keys retained,
including the four rotated TransformZ meshes and the two float-copy keys above.
The original 1885-key set has 47 retained failures versus 81 before the change.
No previously passing original key or component becomes failing, and no failing
original component increases its error. All 261 new control records outside the
four TransformZ meshes pass. The unchanged command-buffer gate also passes with
exactly its two task-required invalid Append differences, 274/276 strict matches.

Land gate:

```sh
python3 tests/projects/lines/check_miters.py --release "$UNBOUND_PROBE_UNITY_VERSION"
```
