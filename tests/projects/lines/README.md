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
