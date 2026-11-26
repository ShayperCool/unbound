# Mesh UV distribution metrics and the Editor's Collab query

What `Mesh.GetUVDistributionMetric` answers for meshes built by script, before and after
`RecalculateUVDistributionMetrics` / `RecalculateUVDistributionMetric`, and what the Editor's
internal `UnityEditor.Collaboration.Collab.IsCollabEnabledForCurrentProject` answers (called by
reflection, as packages call it). Unity 6000.0.71f1, recorded with the macOS Editor in batch mode
(two identical captures): the values depend on neither the OS nor the graphics device.

`Assets/Scripts/UvMetricsProbe.cs` records in Play mode into values.jsonl in `$UVMETRICS_OUT`
(tests/value_probe.py), floats with `"R"`:

* `unit`, `big`, `halfUv`, `odd`, `rotated`: quads of other sizes, UV ranges and orientation;
  `unit.before` before any recalculation; `unit.afterVertices`, `unit.recalculated` after the
  vertices change, before and after recalculating.
* `mixed`, `weighted`: two triangles of ratios 1 and 4 (world area over UV area), of equal and of
  different areas; `agg.*`: sets of right triangles of chosen ratios and areas; `seq.sameUv.n`,
  `seq.sameWorld.n`: ratios 1..n (n 2 to 10) of the same UV area, then of the same world area;
  `outlier.*`: twenty triangles of one ratio and one of another.
* `flatUv`, `allFlat`: triangles whose UVs have no area; `threshold.*`: the area threshold below and
  above the triangles' UV areas.
* `twoSets.*`: a second UV set, each recalculated alone; indices 8 and -1.
* `noUv`: a mesh without UVs.
* `collab.*`: the Collab class, its instance, and `IsCollabEnabledForCurrentProject`.

What it shows: a new mesh answers 1 for every UV set (also for an index out of range, which
throws nothing); a mesh whose triangles all have the same ratio answers that ratio for the set
recalculated (a quad twice as large 4, UVs half as large 4, both 144 at 3x and a quarter), the
others keep theirs; triangles whose UV area is under the threshold are skipped, and with none left
the metric is 1; changing the vertices keeps the metric until it is recalculated. Ratios that
differ do not give their average: 1 and 4 give 4 (whatever their areas); 1..n give values near the
mean plus a standard deviation of the ratios kept after a cut that depends on the triangles' areas
(Unbound does not compute the metric: residuals). `IsCollabEnabledForCurrentProject` answers false.

Record and gate:

    python3 tests/projects/uvmetrics/run.py record --label a   # twice, then
    python3 tests/projects/uvmetrics/run.py publish a b
    python3 tests/projects/uvmetrics/run.py gate
