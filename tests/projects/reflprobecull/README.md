# reflprobecull

Which reflection probes a scriptable render pipeline's culling lists (`CullingResults.visibleReflectionProbes`)
in Unity 6000.0.71f1 Play mode, and in which order (`Assets/Scripts/ReflProbeCullProbe.cs`, culled by
`ReflProbeCullPipeline`, a pipeline that only culls; Linux GPU Editor in batch mode, two identical
captures). A camera at the origin looks along +z (field of view 60, a square target, near 0.3, far 50).

- `grid.<set>`: one bit per probe (1 = listed) of 399 probes on a horizontal grid (z from -12 to 60, x
  from -40 to 40, step 4), each set one box: sizes, center offsets, rotations about one axis (0, 30,
  63, 90 degrees), a tilted one, a scaled transform and a long box at -45 degrees. The Editor lists a
  probe when its box, rotated with the probe (the center offset rotated too, the transform's scale
  ignored), is on the inner side of each culling plane, tested plane by plane; the unrotated
  `bounds`, the box around the rotated one and an exact box-frustum intersection each differ on some
  of the 2793 placements.
- `notexture`: a probe in view without a texture is listed.
- `layer.maskedOut`: the camera's culling mask does not leave out a probe on another layer.
- `anchor.*`: the probe a renderer is anchored to (`Renderer.probeAnchor`) is listed although its box is
  out of view, whether the renderer is in view, on a layer the camera leaves out or has reflection
  probes off; not when the renderer is disabled or inactive or the probe is disabled; an empty
  transform inside a probe's box as the anchor lists nothing; a probe anchored by two renderers is
  listed once and sorted like the others (`anchor.order.*`).
- `sort.*`: the sorting criteria (`ScriptableCullingParameters.reflectionProbeSortingCriteria`; its
  default is `None`): `None` keeps the probes' order (by enabling; a disabled probe's place goes to
  the last one: `sort.None.reenabled`), `Importance` puts the least important first, `Size` the
  largest box first by its extents' squared length (`sort.Size.metric`), `ImportanceThenSize` both;
  ties keep the probes' order. `sort.fields` holds the first listed probe's data.

Unmeasured: renderers with `forceRenderingOff` as anchors (Unbound does not count them), an anchor
with several probes (Unbound counts its first), an orthographic camera, and which order the renderers'
own probe choice uses after a probe is disabled (Unbound keeps the order of enabling there).

```sh
python3 tests/projects/reflprobecull/run.py record --label a
python3 tests/projects/reflprobecull/run.py record --label b
python3 tests/projects/reflprobecull/run.py publish a b
python3 tests/projects/reflprobecull/run.py gate
```
