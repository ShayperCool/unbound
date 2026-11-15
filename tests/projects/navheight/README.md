# NavMesh height meshes built at run time

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode; the two captures are
byte-identical and the canonical reference has 270 rows. Heights come from the build's geometry and
agent movement, not from the OS or the graphics device. The comparator compares text exactly and
numbers within 1e-5 m: heights interpolated over a sloped triangle differ in their last bits between
the Editor and Unbound, while a height mesh changes heights by centimetres (a 0.1 m bump against a
smoothed 0.16 to 0.21 m polygon surface).

`NavMeshBuilder.BuildNavMeshData` builds a 24 × 14 m floor with a 15° ramp, five 0.2 m stairs up to
a landing, a 0.1 m bump, a 0.15 m step, a 40° slope, a shelf and a table 2.5 m up. It builds them
once from cube meshes and once from box sources, with `buildHeightMesh` set.

- `NavMesh.SamplePosition` along five lines (`main`, `bump`, `step`, `table`, `under`), a raycast up
  the ramp, and agents walking up the ramp and over the bump take their heights from the build's
  walkable triangles. A point takes the highest part of a triangle within a voxel's square around
  it, below its own height plus the agent climb, as with baked height meshes. The step tops, the
  table and the ground under it keep their own heights. `mesh.none.main` is the same line built
  without a height mesh: the polygons' heights.
- `preserveTilesOutsideBounds` (documented: during an update the height mesh is not created, and an
  existing one is removed). Measured: a build or update preserving tiles rebuilds only the tiles
  wholly inside its bounds and keeps every other tile; it creates no height mesh and drops the
  data's existing one (`preserve.small.*`, 32-voxel tiles). With no tile wholly inside the bounds
  (`preserve.*` with the default tiles), the data is left exactly as it was, height mesh included,
  and a first build has no NavMesh at all. A preserving update keeps the data's source bounds and,
  as documented, its own settings: `preserve.changed` passes another tile size and agent radius,
  and the rebuilt tiles keep the data's (the ramp's side edge stays 1 m from its middle, not 0.6 m).
  `drop.*` updates the same data without preserving tiles: tiles outside the bounds go, and the
  source bounds become the update's.
- Documented, not recorded here: a preserving update also removes the links the data's bake
  generated. The runtime builder does not generate links (a source asking for them gets a warning),
  so the probe cannot make such data; a managed test covers the removal on data carrying baked
  links. An update that rebuilds no tile leaves them too (unmeasured; taken from "left exactly as it
  was"). Bounds within a thousandth of a voxel of a tile edge count as on it (unmeasured).

| Unity 6000.0 documentation | Discriminating control |
| --- | --- |
| [NavMeshBuildSettings.buildHeightMesh](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AI.NavMeshBuildSettings-buildHeightMesh.html) | `mesh.*` and `box.*` against `mesh.none.main`; `preserve.small.update` drops an existing height mesh. |
| [NavMeshBuildSettings.preserveTilesOutsideBounds](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AI.NavMeshBuildSettings-preserveTilesOutsideBounds.html) | `preserve.*` and `drop.*`: the stairs outside the update's bounds stay or go. |

Known residuals, listed in `residuals.json` and reported by every check without failing it: the
triangle counts with 32-voxel tiles, one triangle fewer in Unbound in every such build (the base
polygon build, with or without a height mesh); and, unexplained, an update without preserving tiles
at 32-voxel tiles, after which the Editor's height mesh no longer has the bump's top (0 where
Unbound has 0.1).

Not compared: an agent walking up the stairs, whose path along x follows the runtime polygons (their
detail heights and an agent's walk differ by up to 1 cm with or without a height mesh; the heights
it takes on the steps match); the lines next to the stairs' side edges and path corner heights,
which read the polygons. Terrain sources are not supported by the runtime builder (an error says
so), nor is link generation (a warning). Other releases are unmeasured.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/navheight/run.py native --label reference-i
python3 tests/projects/navheight/run.py native --label reference-j
python3 tests/projects/navheight/run.py publish reference-i reference-j
python3 tests/projects/navheight/run.py check
```

Captures, engine outputs and comparison reports stay in the run cache.
