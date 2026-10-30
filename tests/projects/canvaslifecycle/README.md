# Canvas creation and driven transforms

Unity 6000.0.71f1, Linux GPU Editor. Two independent Native recordings are byte-identical and agree on
8671/8671 keyed observations, including callback sequences. `Reference/values.jsonl` stores
that oracle. The comparator checks every key with the existing absolute/relative 2e-4
numeric tolerance, exact strings and exact callback order, with no exclusions.

The measured default of a newly added Canvas is WorldSpace. Adding it preserves authored
pose and anchors. Setting a different screen mode drives immediately if active; setting
that mode while inactive waits for activation. Re-enabling drives synchronously. Disabling
retains the driver. Changing to WorldSpace releases the driver and preserves the last driven
values. ForceUpdateCanvases does not overwrite authored edits to a previously driven rect.
The following early frame update drives screen-space roots again. planeDistance changes wait
for that update, while camera assignment and scaleFactor changes drive synchronously. A
camera-mode Canvas with no camera reports ScreenSpaceOverlay. A nested canvas ignores
scaleFactor writes; its renderMode and worldCamera setters target the root. planeDistance
writes stay on the nested Canvas and become visible when it detaches.

The fixture covers constructor and AddComponent creation, active/inactive setup, all three
render modes, authored local/world pose and layout, disable/re-enable, repeated forced
updates, individual rect setters, canvases under ordinary Transform parents, nested canvases,
SetParent with either worldPositionStays value, detach/reparent,
camera/viewport/scale/plane changes, render-target resize, frame progression, disabled-mode
setters, rotated and non-uniformly scaled parents, serialized scene roots, a Resources prefab and cloning. It records local/world corners as well as rect,
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
Evidence is under `~/.cache/compat/canvas-lifecycle-*`. Current recordings are `distance1` and
`distance2`; earlier matrix recordings are `expanded1` and `expanded2`, `native1` and `native2`; `before` and `before-compare.txt` retain the parent-code engine baseline.

## Reconciled ordering expectation

Native overlay mode resets world Z to zero, including under an ordinary Transform parent.
`transformParent.0.mode` records world Z 0 and local Z -100 under a parent at Z 100.
The old CanvasOrder test assumed inherited depth survived the mode setter. Parent replaced
that assumption with the recorded world/local-Z behavior and retained explicit sortingOrder
coverage. The test now passes without reverting the Native lifecycle fix.

## Validation and handoff

The final engine matches all 8671 keys across 293 snapshots and 401 callbacks. Independent
Native files and the committed reference share SHA-256
`6ac47ffc451e6232f7397532ed2e16e6004bf5c6601eeac90e8dadc018e34a24`.
The parent-code baseline matched 5197/7039 keys in the original, smaller matrix.

All paths below start at `~/.cache/compat/`:

| Check | Result | Evidence |
| --- | --- | --- |
| Native repeatability | 8671/8671, byte identical | `canvas-lifecycle-distance1/values.jsonl`, `canvas-lifecycle-distance2/values.jsonl` |
| Engine lifecycle | 8671/8671, no unexpected keys | `canvas-lifecycle-distance/values.jsonl`, `canvas-lifecycle-distance-compare.txt` |
| Managed build | 0 errors, 34 existing warnings | `canvas-lifecycle-build5.log` |
| Linear colors, final binary | 49201/49201 | `canvas-lifecycle-colors-linear-final/values.jsonl` |
| Gamma colors, final binary | 49201/49201 | `canvas-lifecycle-colors-gamma-final/values.jsonl` |
| Fresh Native Gamma colors | 49201/49201 against committed Gamma reference | `canvas-lifecycle-color-gamma-native/values.jsonl` |
| UI Toolkit | JSON and all three PNGs byte identical to parent `uicolors-uitk`; all 11 steps match | `canvas-lifecycle-uitk/`, `canvas-lifecycle-uitk-compare.txt` |
| Canvas/UIElements/Yoga | 260/261, only the depth assumption above fails | `canvas-lifecycle-tests-final.log` |
| Semantic index | Refreshed without errors | `canvas-lifecycle-index-final.log` |

The UI Toolkit comparison is against the parent engine record, so its inherited Native
one-pixel label-width difference remains visible in the original Native comparison.
No existing tests or references were changed. Parent integration passes the API check with zero errors in 2266 types. After reconciling
the CanvasOrder assumption, Canvas/UIElements/Yoga pass 261/261 and additional Core/RectTransform
coverage passes 14/14. Earlier Unity releases and standalone players were not measured here.

These were the final lifecycle commands, with the wrapper and isolated outputs defined in
`run.sh`:

```sh
tests/projects/canvaslifecycle/run.sh native distance1
tests/projects/canvaslifecycle/run.sh native distance2
tests/projects/canvaslifecycle/run.sh build > ~/.cache/compat/canvas-lifecycle-build5.log 2>&1
tests/projects/canvaslifecycle/run.sh engine distance
python3 tests/projects/canvaslifecycle/compare.py ~/.cache/compat/canvas-lifecycle-distance1 ~/.cache/compat/canvas-lifecycle-distance2
cmp ~/.cache/compat/canvas-lifecycle-distance1/values.jsonl ~/.cache/compat/canvas-lifecycle-distance2/values.jsonl
python3 tests/projects/canvaslifecycle/compare.py tests/projects/canvaslifecycle/Reference ~/.cache/compat/canvas-lifecycle-distance
```

Regression commands, run from this worktree, used the same final managed output. The private
Gamma project is a copy of uicolors under this fixture's ignored Library directory, so its
Editor settings can change without editing the existing color fixture. Both it and the
committed project were restored to Linear after recording.

```sh
export PATH="$HOME/.cargo/bin:$HOME/.dotnet:$PATH"
export UseSharedCompilation=false MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0
export DISPLAY=:0
export XAUTHORITY="$(ls -t /run/user/1000/.mutter-Xwaylandauth.* | head -1)"
export UNBOUND_MANAGED_DIR="$HOME/.cache/compat/canvas-lifecycle-managed"
canvas_u=/mnt/storage-hdd/node-b-caches-20261023/unialt-target-compat/release/unbound
canvas_heavy="$HOME/.cache/compat/with-heavy-job"

UI_COLORS_OUT="$HOME/.cache/compat/canvas-lifecycle-colors-linear-final" "$canvas_heavy" "$canvas_u" run -p "$PWD/tests/projects/uicolors" --frames 8 --size 128x128 --no-watch

python3 - <<'PY'
from pathlib import Path
import shutil
project = Path('tests/projects/canvaslifecycle/Library/ColorRegression')
for part in ['Assets', 'Packages', 'ProjectSettings']:
    shutil.copytree(Path('tests/projects/uicolors') / part, project / part, dirs_exist_ok=True)
PY
UI_COLORS_OUT="$HOME/.cache/compat/canvas-lifecycle-color-gamma-native" "$canvas_heavy" "$HOME/Unity/Hub/Editor/6000.0.71f1/Editor/Unity" -batchmode -projectPath "$PWD/tests/projects/canvaslifecycle/Library/ColorRegression" -executeMethod ProbeRunner.RecordGamma -logFile "$HOME/.cache/compat/canvas-lifecycle-color-gamma-native.log"
UI_COLORS_OUT="$HOME/.cache/compat/canvas-lifecycle-colors-gamma-final" "$canvas_heavy" "$canvas_u" run -p "$PWD/tests/projects/canvaslifecycle/Library/ColorRegression" --frames 8 --size 128x128 --no-watch
python3 - <<'PY'
from pathlib import Path
p = Path('tests/projects/canvaslifecycle/Library/ColorRegression/ProjectSettings/ProjectSettings.asset')
p.write_text(p.read_text().replace('m_ActiveColorSpace: 0', 'm_ActiveColorSpace: 1'))
PY
python3 tests/projects/uicolors/compare.py tests/projects/uicolors/Reference/Linear ~/.cache/compat/canvas-lifecycle-colors-linear-final -q
python3 tests/projects/uicolors/compare.py tests/projects/uicolors/Reference/Gamma ~/.cache/compat/canvas-lifecycle-colors-gamma-final -q

UITK_PROBE_OUT="$HOME/.cache/compat/canvas-lifecycle-uitk/unbound.jsonl" "$canvas_heavy" "$canvas_u" run -p "$PWD/tests/projects/uitk" --frames 35 --size 960x540 --no-watch
python3 tests/projects/uitk/compare.py ~/.cache/compat/uicolors-uitk/unbound.jsonl ~/.cache/compat/canvas-lifecycle-uitk/unbound.jsonl

"$canvas_heavy" dotnet test managed/UnityEngine.Tests/UnityEngine.Tests.csproj -c Release -p:UnboundTarget="$HOME/.cache/compat/canvas-lifecycle-test-build/" --disable-build-servers -m:1 -p:UseSharedCompilation=false /nodeReuse:false --filter 'FullyQualifiedName~CanvasOrder|FullyQualifiedName~UIElements|FullyQualifiedName~.YG'
```

Only the assigned managed files and this fixture changed. `Core/Transform.cs` makes
`KeepWorldPose` virtual so RectTransform can preserve its rect size during SetParent(true).
The real game project was never run or modified. Shared progress documents, CanvasRenderer
colors, UIElements, Particles, Terrain and Rust/native code were not edited.

Parent integrated evidence is under ~/.cache/compat/canvas-parent-*: build/api/tests/core-tests
logs, engine lifecycle, Linear/Gamma colors and uitk. The earlier 260/261 row above records the
delegate handoff before parent corrected the outdated test; it is not the final integrated result.
