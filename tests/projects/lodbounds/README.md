# LODGroup bounds

What `LODGroup.RecalculateBounds` (and `SetLODs`) give a group's local reference point and size.
Unity 6000.0.71f1, recorded with the macOS Editor in batch mode (two identical captures).
`Assets/Scripts/LodBoundsProbe.cs` writes values.jsonl in `$LODBOUNDS_OUT` (tests/value_probe.py):
numbers, compared within a tolerance of 4e-6 (the last bits of the group-space matrices: about one
unit in the last place of the probe's largest size, 26).

The groups are of three cube renderers placed with fractional positions, rotations and scales
(whole-number ratios, the same floats in every runtime), one per level:

* `case/<name>`: the group's own transform `identity`, `translated`, `rotated`, `scaled`, `placed`
  (all three, uniform scale) and `skewed` (non-uniform scale), with axis-aligned children and with
  rotated ones (`placed`, `skewed`, `children`); each also with its renderers in the other order
  (`/reversed`) and all in one level (`/onelevel`).
* `counted/disabled`, `counted/inactive`: a renderer disabled, one of an inactive object;
  `counted/null_and_twice`: a null entry and a renderer in two levels; `counted/empty_level`: a
  level without renderers; `counted/none`: no renderers at all, after the values were set;
  `counted/setlods`: the values set, then `SetLODs` with renderers, without `RecalculateBounds`.

Each case's rows: `/before` and `/result` (the reference point and size before and after
`RecalculateBounds`), `/group` (the group's local-to-world and world-to-local matrices) and
`/renderer<i>` (each renderer's bounds, local bounds and local-to-world matrix).

What it shows: each renderer's local bounds are carried into the group's space (their corners
through the group's world-to-local matrix times the renderer's local-to-world matrix), the boxes
joined one by one; the reference point is the box's center, the size its largest side (in the
group's own space: a rotated or scaled group differs from a box of the renderers' world bounds).
Disabled renderers and those of inactive objects count, null entries do not; without renderers the
point is zero and the size 1; `SetLODs` recalculates.

Not covered: renderers other than mesh renderers (skinned, sprite, particle), meshes whose bounds
change after `SetLODs`.

Record and gate:

    python3 tests/projects/lodbounds/run.py record --label A   # also B; macOS batch is fine
    python3 tests/projects/lodbounds/run.py publish A B
    python3 tests/projects/lodbounds/run.py gate
