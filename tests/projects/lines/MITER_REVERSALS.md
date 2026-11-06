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

## Reference publication

`miter_reference.py` checks the independent GPU repeats through the existing runner,
their pre-launch source hashes, all old records and images, and the predeclared new
keys. It accepts only the already documented move of `complete` to the end. It
creates `miterrev.values.jsonl` and `miterrev.image.rgba.gz` exclusively. It never
writes `values.jsonl`, `capcount.values.jsonl` or their images. The cache receipt
records hashes for both repeats and the reference before and after publication.

```sh
python3 tests/projects/lines/run.py editor --release 6000.0.71f1 --label LABEL
python3 tests/projects/lines/miter_reference.py --release 6000.0.71f1 --label LABEL \
  --receipt ~/.cache/unbound/r8/reference-receipt.json
python3 tests/projects/lines/compare.py tests/projects/lines/Reference/unity-6000.0.71f1 \
  CAPTURE --mode miter --before BEFORE --report ~/.cache/unbound/r8/comparison.json
```

The `miter` comparison mode includes every old record, both control files and every
capture key. The numerical and image tolerances are unchanged. Negative widths,
view-axis degeneracy, perceptual gradients, trail frame count and autodestruct
remain separate questions. Reversing loops' corner inner sides are also separate;
the open-strip denominator correction does not change that rule.
