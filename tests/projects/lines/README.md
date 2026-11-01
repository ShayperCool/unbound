# LineRenderer and TrailRenderer geometry

A windowed GPU Editor probe for the meshes `LineRenderer` and `TrailRenderer`
generate, read back through `BakeMesh`, plus a few renders of them into float
targets (`Assets/Scripts/LineProbe.cs`).

- Lines, in one frame: sharp, reversed, repeated and degenerate points; corner
  and cap vertex counts; loops; every texture mode and texture scale; width
  curves and multipliers; color gradients; View alignment for orthographic and
  perspective cameras; TransformZ on rotated transforms; local-space lines with
  and without the transform applied; lighting data; shadow bias; component
  defaults and the start/end shortcuts.
- Trails, stepped at a 50 fps capture frame rate: moving objects with several
  settings, recorded after each move and at the end of each frame (positions,
  visible positions, the baked mesh); emitting toggles, autodestruct, `Clear`,
  `AddPosition(s)`/`SetPosition(s)`.
- Nine 96x64 float RGBA images (`img.*.rgba`): colors, UVs, masks and trails.
- Short-segment blend (`blend.*`, recorded last): sixteen predeclared lines at the default
  width whose segment lengths are ratios of that width (one corner with equal or unequal
  lengths, staircase/U/short-middle/collinear-run polylines, and five of them reversed).
  `blend_cases.py` defines them and generates `Assets/Scripts/BlendCases.cs`
  (`--check` verifies it, `--list` prints the points).

`Assets/Editor/ProbeRunner.cs` opens the scene at a 96x64 game view, enters
Play mode and quits when the record is written. Its own Play-mode watchdog
writes `watchdog.txt` and quits after 600 s.

## Running

Run from the repository root. The runner takes one installed launcher lease per
Editor through `tests/editor_runner.py`; do not wrap it in `with-heavy-job`.

```sh
python3 tests/projects/lines/run.py editor  --release 6000.0.71f1 --label L --dry-run
python3 tests/projects/lines/run.py editor  --release 6000.0.71f1 --label L
python3 tests/projects/lines/run.py check   --release 6000.0.71f1 --label L
python3 tests/projects/lines/run.py publish --release 6000.0.71f1 --label L
UNBOUND_CLI=/path/to/unbound UNBOUND_MANAGED_DIR=/path/to/managed \
  python3 tests/projects/lines/run.py unbound --release 6000.0.71f1 --label U
python3 tests/projects/lines/compare.py tests/projects/lines/Reference/unity-6000.0.71f1 \
  ~/.cache/unbound/validation/runs/lines/U/unbound \
  --report ~/.cache/unbound/validation/runs/lines/U/report.json [--before OLD_CAPTURE]
```

Projects, captures, logs, `editor-run.json` and reports go to
`$XDG_CACHE_HOME/unbound/validation/runs/lines/<label>/`. The Editor deadlines
are counted from admission into the lease. The hard limit is 1200 s. The
progress limit is 480 s without a write to `values.jsonl`: the probe writes
everything in one burst at the end of Play mode, so this budget covers cold
package resolution and import. See `run.py` for the measurements behind both.

`publish` refuses unless both repeats exited with 0 on the NVIDIA renderer
without a forced stop, end with exactly one `complete: true`, have no `fatal`
or duplicate key, have exactly the images they recorded at their pixel sizes,
and are byte-identical in `values.jsonl` and every image. It never overwrites
an existing reference. `publish --append PREFIX` adds keys to an existing reference
instead: after the same checks, the captures without their `PREFIX` keys must reproduce
the reference line for line and every reference image byte for byte, and the reference
must not hold a `PREFIX` key yet; the new lines are then appended to `values.jsonl`. Records
are compared and appended as raw bytes with their own terminators: a CRLF record on either
side, or a captured record without a newline, is refused, and a refusal leaves the reference
unchanged (`test_run.py`). Images are stored as gzip-compressed raw RGBA (`*.rgba.gz`, not
PNG) with a zero timestamp, so republishing the same capture gives the same bytes.

## Comparison and tolerances

`compare.py` keeps the whole key union and reports every missing, extra and
differing key. Numbers match within an absolute 1e-5 plus a relative 1e-5.
Strings, booleans, list lengths and object keys match exactly. An image matches
when no channel of any pixel differs by more than 2/255; the report counts the
differing pixels. With `--before`, failures are classified as retained,
introduced or resolved.

## Reference

`Reference/unity-6000.0.71f1/` was published with `run.py publish` from two
independent windowed GPU Editor captures (NVIDIA GeForce RTX 4060, OpenGLCore,
linear color space). Both exited with 0 after `Cleanup mono` and were
byte-identical: `values.jsonl` (1853 records) and all nine images. They ran
with the committed `Assets`, `Packages` and `ProjectSettings` (37 files,
byte-identical) and the runner from before `editor_runner`. The ported runner
was validated with `py_compile`, `--dry-run`, `check` and `publish` on those
saved captures and a publish refusal on an altered copy. No live capture has
used it yet.

The sixteen `blend.*` records were appended with `run.py publish --append blend.` from the
first live pair through `editor_runner` (label `lines-blend-20261101b`, cases committed in
002b7f76 before the capture): both repeats exited with 0 on the NVIDIA GeForce RTX 4060 after
`Cleanup mono` (36.8 s each), were complete (1869 records), byte-identical in `values.jsonl`
and all nine images, and reproduced all 1853 existing records and the nine images byte for
byte. The existing bytes of `values.jsonl` are unchanged; the new records follow them.

## Offline geometry attribution

`attribute_geometry.py` reads the saved combined raw union, image arrays and a
pre-blend capture. It writes full component witnesses, cap/tangent controls and
CPU raster diagnostics to an explicit cache directory. It starts no engine,
Editor or build, and does not change the reference, comparator or tolerances.
See [the attribution and remaining blockers](../../../docs/LINE_GEOMETRY_ATTRIBUTION.md).

```sh
~/.cache/compat/with-heavy-job --kind tool python3 tests/projects/lines/attribute_geometry.py \
  --batch /path/to/saved/combined-report \
  --preblend /path/to/saved/pre-blend/unbound \
  --out ~/.cache/unbound/validation/runs/geometry-attribution
```

## Predeclared cap-count control

`capcount.*` adds exactly eight synchronous public BakeMesh controls. Renderer
Line/Trail, alignment View/TransformZ and cap count 1/2 vary; all share points
`(-1,0,0), (1,0.5,0)`, identity object transform, the existing orthographic
camera at `(0,0,-10)` facing +Z, constant positive width 0.4 and LineColor.
Normals, tangents, positions, indices, UVs, colors and bounds are captured.
Public readbacks verify points, width, cap count and alignment. Trails use
Clear/AddPositions with emission and autodestruct disabled and time 10000,
without yielding. They run after all existing observations and add no images.

The authorized budget is one independent complete Native pair on 6000.0.71f1.
No replacement pair or tolerance changes. The unchanged append guard remains
mandatory: the current reference ends with blend rows after its original
complete marker, so a future complete capture may fail its raw-order check.
Such a refusal leaves the canonical reference unchanged.

The sole cap-count pair (`cap-count-20261101-t3-ce24a7cc`, cases precommitted
in `bf2ace6f`) exited 0 twice on NVIDIA with 1885 byte-identical records and
nine byte-identical images. Every old record, including its terminator, matches
by key. Publication was refused at record 1853 because the old append left
`complete` before `blend.*`; the unchanged guards require a final completion
marker and exact old record order. No reference was changed or replacement run.
Unity spells TransformZ as its enum alias `Local` in these new keys/readbacks.
Both counts reverse the fans only for TransformZ trails. Those non-emitting
manual trails have zero effective width despite positive configured width:
their indices discriminate topology, while the 30 old nonzero cap-one meshes
establish the inward displacement. Extending that plane to nonzero cap-two
trails is an inference from the shared cap/fan rule, not a new observation.

The prepared tangent rule computes a separate arc angle from the side dot
product divided by the product of their actual lengths. Positions retain the
original arc. The float32 replay matches all 37 saved lighting streams within
the original tolerance before the change; the prediction closes all 36 tangent
introductions and preserves all 78 spatial fixes. No runtime was built.

`project_geometry.py` reuses the saved raster and full 1887-row union, adding
all predicted values, component failures, numeric increases and nine image
arrays. `project_cap_controls.py` checks every component of all eight new
meshes. These are offline diagnostics and cannot establish GPU acceptance.
The full projection still has all eight original trail channel introductions,
and adds alpha at pixel 2055. The 80 mask pixels remain separately owned.

```sh
~/.cache/compat/with-heavy-job --kind tool python3 tests/projects/lines/project_geometry.py \
  --batch /path/to/combined-report --attribution /path/to/saved-attribution \
  --out ~/.cache/unbound/validation/runs/geometry-prediction --source-revision REV
~/.cache/compat/with-heavy-job --kind tool python3 tests/projects/lines/project_cap_controls.py \
  --controls /path/to/new-native-controls.json \
  --out ~/.cache/unbound/validation/runs/geometry-prediction
```

Full task receipt and immutable independent pair bindings:
`/mnt/data/unbound-cache-20261031/unbound-validation/runs/line-cap-tangent-20261101-t3-ce24a7cc/REPORT.md`.

### Corner sides, arc ends and per-segment widths (offline, unbuilt)

Three general strip rules, each predicted from saved engine meshes and authored
inputs, never fitted to a Native value. `strip_projection.py` replays the shared
strip in float32 and first reproduces every saved engine mesh with the unchanged
rules (zero baseline errors on 143 corner lines and every corner/per-segment trail):

* DistributePerSegment and RepeatPerSegment evaluate width and gradient at
  index / (n - 1); Stretch rejects it. Closes all 28 line and 3792 trail components.
* The corner arc, its inner side and tangents follow the un-eased outgoing side;
  only the miter eases. Positions use the length-normalized cosine, as tangents do.
* A TransformZ trail's opposite face also swaps each corner's side.

Together: 5746 component failures in the affected rows become 44, none introduced;
round-trail tangents 368 -> 0. All eight original trail channels and pixel 2055
pass. The trail image predicts 204 -> 88 failing pixels: exactly the 88 where the
saved Native meshes do not raster to the Native image. All are under `tex.Static`,
whose Native U is the bake U minus textureScale.x times the head's whole travelled
path (3.8299 = 1.5 x 2.553281, one witness). It is a render-versus-bake rule, not
geometry, and stays open. Eight zero-area round-trail join indices also stay open.

Non-emitting manual trails mark the head and every added point hidden
(`m_DrawHidden`), so the cap-count trails have zero half width at every point,
as Native shows: a cap-two displacement there is zero by construction and the
inward cap-two plane remains an inference. `capcount_layout.py prepare` proposes
`Reference/unity-R/capcount.values.jsonl` with only the 16 cap-count rows; it
writes nothing under Reference/ and leaves the refused append refused.

```sh
~/.cache/compat/with-heavy-job --kind tool python3 tests/projects/lines/capcount_layout.py prepare \
  --release 6000.0.71f1 --label cap-count-20261101-t3-ce24a7cc --out ~/.cache/unbound/validation/runs/capcount-layout
```

Receipt: `/mnt/data/unbound-cache-20261031/unbound-validation/runs/line-corner-alpha-20261101-t3-dae2fa72/REPORT.md`.
