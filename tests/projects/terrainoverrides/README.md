# terrainoverrides

What Unity 6000.0.71f1 answers in Play mode for TerrainData's level-of-detail overrides and for
TerrainUtility's TerrainMap factories (`Assets/Scripts/TerrainOverridesProbe.cs`).

* `lod.*`: a 33 × 33 heightmap's `GetMaximumHeightError`, `GetPatchMinMaxHeights` and `bounds`
  before and after `OverrideMaximumHeightError` / `OverrideMinMaxPatchHeights` with the right
  number of values, too few, too many and null, then after `SetHeights`, and after
  `SetHeightsDelayLOD` and `SyncHeightmap`.
* `map.*`: `TerrainMap.CreateFromPlacement` on a 2 × 2 grid, with two terrains on one tile, a larger
  terrain, another heightmap resolution, an off-grid and a raised terrain, another grouping ID, a
  filter that keeps all or none, a grid origin between tiles; `CreateFromConnectedNeighbors` over
  links that disagree with the positions and links that reach a terrain twice; null origins. Each
  with `fullValidation` on and off.

```sh
python3 tests/projects/terrainoverrides/run.py record --label a
python3 tests/projects/terrainoverrides/run.py record --label b
python3 tests/projects/terrainoverrides/run.py publish a b
python3 tests/projects/terrainoverrides/run.py gate
```
