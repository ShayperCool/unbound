# Canvas creation and driven transforms

Unity 6000.0.71f1, Linux GPU Editor. The first two independent Native recordings agree on
7039/7039 keyed observations, including callback sequences. `Reference/values.jsonl` stores
that oracle. The comparator checks every key with the existing absolute/relative 2e-4
numeric tolerance, exact strings and exact callback order, with no exclusions.

The measured default of a newly added Canvas is WorldSpace. Adding it preserves authored
pose and anchors. Setting a different screen mode drives immediately if active; setting
that mode while inactive waits for activation. Re-enabling drives synchronously. Disabling
retains the driver. Changing to WorldSpace releases the driver and preserves the last driven
values. ForceUpdateCanvases does not overwrite authored edits to a previously driven rect.

The fixture covers constructor and AddComponent creation, active/inactive setup, all three
render modes, authored local/world pose and layout, disable/re-enable, repeated forced
updates, nested canvases, SetParent with either worldPositionStays value, detach/reparent,
camera/viewport/scale/plane changes, render-target resize, frame progression, serialized
scene roots, a Resources prefab and cloning. It records local/world corners as well as rect,
anchors, pivot, offsets, driver, Canvas properties and callbacks after each operation.

```sh
tests/projects/canvaslifecycle/run.sh create create
tests/projects/canvaslifecycle/run.sh native native1
tests/projects/canvaslifecycle/run.sh native native2
python3 tests/projects/canvaslifecycle/compare.py ~/.cache/compat/canvas-lifecycle-native1 ~/.cache/compat/canvas-lifecycle-native2
tests/projects/canvaslifecycle/run.sh build > ~/.cache/compat/canvas-lifecycle-build.log 2>&1
tests/projects/canvaslifecycle/run.sh engine after
python3 tests/projects/canvaslifecycle/compare.py tests/projects/canvaslifecycle/Reference ~/.cache/compat/canvas-lifecycle-after
```

`run.sh` acquires the shared heavy-job lock for every build, Editor and engine invocation;
do not wrap it in another lock. It uses isolated managed outputs and absolute project paths.
Evidence is under `~/.cache/compat/canvas-lifecycle-*`. Initial recordings are `native1` and
`native2`; `before` and `before-compare.txt` retain the parent-code engine baseline.
