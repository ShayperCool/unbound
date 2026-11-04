# Core API probe

`Pose`, `LazyLoadReference<T>`, `GameObject.SetGameObjectsActive`, `GameObject.InstantiateGameObjects`
and `OnDemandRendering`. `ProbeRunner.Create` made the scene and the assets (`Assets/Data`: a
material, a ScriptableObject and a prefab referenced only by `LazyHolder`'s lazy fields, plus one
lazy reference to a deleted material and one texture field that names the material;
`Resources/Item`: a prefab with a child). `CoreProbe` records one JSON line per event: the frame
(from its Start), the scenario, the object and the event, with instance ids written as the names
of their objects. Messages of the scene's own load are not recorded. Floats of `Pose` are their
IEEE-754 bits. In the OnDemandRendering window each line has the frame in the window (`w`),
`Time.frameCount % 6`, the interval, `Time.renderedFrameCount` since the window began and
`willCurrentFrameRender`.

```
python3 tests/projects/coreapis/run.py create --release 6000.0.71f1 --label <new-label>   # once, writes Assets
python3 tests/projects/coreapis/preflight.py --copy <capture>/projects/native1 --launch <capture>/native1 --out <cache>/preflight
python3 tests/projects/coreapis/run.py editor --release 6000.0.71f1 --label <new-label> --repeat native1 --preflight <cache>/preflight/preflight.json
python3 tests/projects/coreapis/run.py check  --release 6000.0.71f1 --label <new-label> --repeat native1
python3 tests/projects/coreapis/run.py editor --release 6000.0.71f1 --label <new-label> --repeat native2 --preflight <cache>/preflight/preflight.json
python3 tests/projects/coreapis/run.py check  --release 6000.0.71f1 --label <new-label> --repeat native2
python3 tests/projects/coreapis/run.py publish --release 6000.0.71f1 --label <new-label>
UNBOUND_CLI=/path/to/unbound python3 tests/projects/coreapis/run.py unbound --release 6000.0.71f1 --label <new-label>
python3 tests/projects/coreapis/compare.py tests/projects/coreapis/Reference/unity-6000.0.71f1.jsonl \
    ~/.cache/unbound/validation/runs/coreapis/<label>/unbound/values.jsonl --report <cache>/compare.json
```

The Editor captures run in windowed Editors (a private Xvfb, the GPU through VirtualGL): in batch
mode the Game view does not render, so the screen camera, OnGUI and WaitForEndOfFrame never run
and rendering is never skipped. The runner therefore has no batch capture. The comparator aligns
the two event sequences and keeps every missing and extra line, with no tolerance.

## Native capture path

`run.py editor` starts one repeat through `tests/editor_runner.py`: its own GPU lease (do not wrap
the driver in `with-heavy-job`), a private Xvfb inside the lease, `vglrun -d egl` and a windowed
Editor without `-batchmode`, `-nographics`, `-quit` or `-executeMethod`. Deadlines: 1200 s hard,
and 300 s without record progress. The probe writes its record once, at the end, so the progress
budget is the time from admission to that write, cold import included. The previous Native pair
wrote it after 53 s and 90 s (an earlier pair: 76 s and 86 s), two concurrent cold Editors; 300 s is
more than three times the slowest. Editor log output never counts as progress. ProbeRunner's 600 s
Play mode watchdog remains inside the Editor. The repeats run one after the other:

* native1 writes the label's manifest once (checkout and a clean probe/runner tree, fixture,
  scripts, runner and shared helpers, profile, existing reference, preflight record, deadlines);
* `check --repeat native1` runs every guard and admits a passing capture (`admission.json`, written
  once over all its output hashes); native2 starts only after that admission, with an identical
  manifest;
* a label and each repeat directory are write-once; a launch is never retried in a label.

`guards.py` checks validity and completeness only, never an answer: both line kinds in the probe's
own writer format with exact keys and types, every scheduled scenario at its frame, all 17 window
frames with one probe Update and LateUpdate each, the fixed window members (ScreenCam, RtCam, Cube,
Canvas, the probe's OnGUI and WaitForEndOfFrame rows, cameras only the probe's), one completion line
that is the last line, the `... threw T: message` rows as expected data but a step that threw or an
error/exception/assert log as a failure, no absolute path, device name or raw instance id in a
compared line. Callback order, which frames render, counter values, API results and exception texts
stay raw. The run guards require a strict exit 0 without any forced stop (not even after the
shutdown line), the logged shutdown, a windowed NVIDIA OpenGL Core Editor of the release, the launch
receipt's bytes, no ProbeRunner watchdog, a record closed before the Editor exit, the compile graph
the Editor used being the preflight's, and no change to the copy's Assets/Packages/ProjectSettings.
The Editor writes `ProjectSettings/PackageManagerSettings.asset` on first open; the copy's one
deliberate setting (ProjectVersion.txt naming only the release, as for the existing reference's
captures) may only be written back to the fixture's bytes, which the Editor does.

`publish` needs two admitted, byte-identical repeats. A new release's reference is written once.
The existing reference is never rewritten: the pair is compared with it in full
(`publish-comparison.json`, once per label). Equal bytes leave it as it is; any difference prints
every missing and extra line and stops (exit 3) for adjudication.

`preflight.py` compiles both assemblies once, offline, with the build graph that a previous windowed
Native capture's Editor wrote for this fixture. That copy's Packages, settings and assets must be
the fixture's bytes except the declared CoreProbe.cs base. Its launch must have recorded the
existing reference's bytes. Every graph input must be unchanged since that compile. The response
files are used byte for byte; only the location and the generators' additional file are rebound.
No compiler server is used. `controls/formatter.py` runs the recorder and its id helpers, cut
verbatim from the probe and from the base blob, in a fake-API harness. The 23 saved corruption
witnesses (`controls/witnesses.json`), ordinary numeric and escaped payloads with colliding ids,
the Id/JsonIds forms and every reference event line are checked there. `controls/driver.py`
replaces the supervised run with a fake Editor, one defect per case, and runs the real
editor/check/publish steps. Neither control is Native evidence.

## Measured behavior in 6000.0.71f1

`Reference/unity-6000.0.71f1.jsonl` has 682 events from two byte-identical GPU captures.

* **Pose**: `GetTransformedBy(Pose)` is `lhs.position + lhs.rotation * position` and
  `lhs.rotation * rotation`, `GetTransformedBy(Transform)` uses `TransformPoint` and the
  transform's rotation; `forward`/`right`/`up` rotate the unit axes. `==` compares the positions
  approximately (`Vector3 ==`) and the rotations exactly; `Equals` is exact on both;
  `GetHashCode` is `position.GetHashCode() ^ (rotation.GetHashCode() << 1)`. `ToString` is
  `(<position>, <rotation>)` with their own formats. `Pose.identity != default`.
* **LazyLoadReference**: scenes store it as an object reference (`{fileID, guid, type}`); it
  holds the instance id. A reference to a deleted asset is set and broken (a positive id no
  object has); one to an asset of another type is set and not broken, and reading `asset`
  throws InvalidCastException. Assigning a non-persistent object through the constructor, the
  implicit conversion or `asset` throws ArgumentException ("Object that does not belong to a
  persisted asset cannot be set as the target of a LazyLoadReference."); any id, a scene
  object's or a runtime object's, can be set through `instanceID` and resolves. A destroyed
  object's id is broken. Instantiate remaps an id inside the copied hierarchy to the copy;
  JsonUtility writes `{"instanceID": id}` and reads it back.
* **SetGameObjectsActive** (both overloads): processes each id in order, each object's
  messages before the next one's (a reentrant change from OnDisable takes effect at once).
  Already-awake hierarchies notify the child first, even with one valid id; first wake is
  parent-first with Awake/OnEnable interleaved. This does not establish the order of Native
  plain `SetActive`, nor a general sibling/depth traversal. Ids
  of no live GameObject (0, unknown, destroyed, a Transform's or another component's) are
  skipped without an error. An uninitialized or disposed NativeArray throws ArgumentException
  ("NativeArray is uninitialized", `instanceIDs`); an empty one does nothing.
* **InstantiateGameObjects**: copies named `<name>(Clone)` at the root of `destinationScene`
  (the active scene for a default or unloaded one), with the original's local position, rotation
  and scale even when the original has a parent; references inside the copy point to the copy.
  Each copy gets Awake and OnEnable through its hierarchy before the next one is made; Start
  follows in the same frame. An inactive original makes inactive copies that wake when
  activated. A source id of no live GameObject (0, unknown, destroyed, a component's, an
  asset that is not a GameObject) makes nothing and writes 0 into both arrays, without an
  error. An uninitialized array throws first (even for count 0); then count 0 returns; then a
  count different from either array's length throws ArgumentException ("Size mismatch! Both
  arrays must already be the size of count.").
* **OnDemandRendering**: `renderFrameInterval` is at least 1. `willCurrentFrameRender` is
  `Time.frameCount % renderFrameInterval == 0`. `effectiveRenderFrameRate` is
  `targetFrameRate / renderFrameInterval` rounded, -1 when `targetFrameRate` is 0 or less;
  `vSyncCount` does not enter it. Whether a frame renders is decided with the interval in force
  when the frame begins: a change in Update or at the end of the frame before applies from the
  next frame. A frame that does not render still runs Update, LateUpdate,
  `Canvas.willRenderCanvases` and every camera that renders into a texture with all its
  callbacks (OnPreCull, OnWillRenderObject, OnPreRender, OnPostRender, OnRenderObject,
  `Camera.onPre*`/`onPost*`); the screen camera, OnGUI (Layout and Repaint) and
  WaitForEndOfFrame do not run, and `Time.renderedFrameCount` still advances.

## Measured scope admitted on 2026-11-02

Validated source `0d49b10d` implements Pose, LazyLoadReference and the batch GameObject
APIs. At that checkpoint OnDemandRendering was not included; its later measured
admission is recorded below. The committed w4 reference and probe are unchanged.

The strict reference comparison improves from 9 matching / 673 missing / 557 extra
to 289 matching / 393 missing / 523 extra lines. No previously matching reference
line is lost: 280 resolve. The raw union also preserves 80 newly unmatched actual
lines and 114 removed extras. These are classified below, not filtered out or
converted into passes. The held OnDemand area accounts for 329 missing and 459
extra lines, including 16 settings lines newly reaching its stubs; its 443 window
lines are unchanged. The remaining 64 missing / 64 extra lines comprise:

* **Probe defects:** lines 158, 166, 172, 177, 182, 187, 195, 199, 204, 209, 214,
  219, 225, 237, 243, 248, 256, 261, 263, 278, 279, 280 and 349. The probe's Names
  function replaces ordinary numeric text `10` with `#LazyData` after an instance-id
  collision. These 23 pairs provide no API-parity evidence and are not waived.
* **Significant activation order:** lines 155, 162, 170, 175, 180, 185, 212, 217,
  235, 254 and 259. Native batch activation notifies the child first for an
  already-awake hierarchy. Unbound notifies its parent first. A separate four-run
  control proves frozen plain SetActive = candidate plain SetActive = candidate
  batch span = candidate batch NativeArray for the recorded cases. This identifies
  Unbound's unchanged shared path, without proving Native plain SetActive order.
  A source/evidence audit at `b7d69af5` confirms the activation path is unchanged;
  the later RunUpdate correction is separate. No independently discriminating plain
  parent/child toggle was found in the existing Native references. Each measured
  hierarchy has one child/one level, so post-order and reverse pre-order still fit.
  The eleven pairs remain significant; no ID reversal or shared lifecycle sort is
  applied. The isolated activationorder fixture now has two siblings and a
  grandchild, both one-id batch overloads and seven distinct predicted orders.
  Root verified its preparation and released one strict Native pair. No new
  measurement, reference or shared lifecycle implementation is admitted yet.
* **Other open API/text gaps:** instance-id sign at 139/143; JSON double digits at
  152/107; large-float formatting at 72; exception text at 249/262/313/96.
* **Precision ledger:** the 21 GetTransformedBy rotation keys listed in
  `docs/COMPATIBILITY_GAPS.md`. Positions are exact; maximum absolute rotation
  error is `2.384185791015625e-7`. Raw hex remains in the strict failing records.

YAML save of a broken nonzero LazyLoadReference is explicitly unsupported. It throws
NotSupportedException before writing instead of discarding the reference. Retaining
the original GUID/fileID remains open; no Native rejection is claimed. Batch
instantiation resolves its destination scene once for all copies; per-copy behavior
in Unity is unmeasured.

The matching runtime passes 1576/1587 managed tests with the same 11 inherited
failures, API 0 errors / 2317 types, and all 108 generated files equal. Lifecycle,
graphics and assetclones outputs are byte-identical to the frozen baseline;
messages pass 316/316, preserving their timing invariants. Detailed receipts,
full unions and the activation control remain under the validation cache at
`validation/runs/claude-fresh-20261101b/core6-validation/`.

## OnDemand measured scope admitted on 2026-11-02

Validated source `d0378bce` adds OnDemandRendering and coroutine EOF gating. The
interval latched at frame start controls automatic screen cameras, overlay/GUI,
presentation and WaitForEndOfFrame. The immediate willCurrentFrameRender property
remains distinct when the interval changes during a frame. RT cameras retain the
existing per-camera path on every frame. Skipped frames use the existing
offscreen command path and Flush, without advancing the screen ring; frame mask
bookkeeping and raster defaults are still prepared. Awaitable is unchanged.

All 17 window frames match the Native screen-camera, GUI and EOF schedule, with
screen/EOF at w0, 2, 5, 9, 11, 13 and 15. RT callbacks occur every frame. The
interval, modulo and immediate-will fields match, including the transition
frames w0, w7 and w12.

The initial OnDemand batch matched 356 lines, with 326 missing and 326 extra.
The follow-up counter-phase correction, validated at `60a1cd68`, moves the
`Time.renderedFrameCount` increment to GUIClearEvents after EOF. All 313 window
reads now match the counter value, including the 262 camera/GUI/EOF reads that
were one high. It still advances once on rendered and skipped frames.

The strict comparison is now 594 matching / 88 missing / 88 extra lines. The
counter correction resolves 238 reference lines with zero lost matches. It
removes 262 previous window extras and preserves 24 newly unmatched actual
lines whose counter field changed. Those 24 lines retain the existing ORO-order
failure. The 64 non-window discrepancy pairs are byte-identical, including the
probe defects, precision ledger and significant Core API gaps.

Only the significant OnRenderObject handler-order gap remains in the window:
ScreenCam/RtCam/Cube here instead of Native's RtCam/ScreenCam/Cube, leaving 24
missing and 24 extra lines. The separate [oroorder witness](../oroorder/README.md)
now establishes stable removal and append on re-enable within the tested handler
class, independently of camera render/depth and instance-id order. Unbound's
List.Add/List.Remove dispatch already follows that contract. The differing
initial registration belongs to scene activation; file-order activation and
cross-type ordering remain unproved. This supplies no rule to sort handlers. Diagnostic
attribution can undo that swap to explain all 17 frames, but the comparator
does not transform anything. Strict failures, raw values and event order stay.

The retained-union audit binds the latest Start/texture capture to unchanged
executed probe and engine query source. Its 88 missing/extra pairs consist of:

| Cause | Pairs |
|---|---|
| Initial OnRenderObject order | 24 |
| Ledgered Pose rotation rounding | 21 |
| Batch activation callback order | 11 |
| Recorder numeric-token corruption | 23 |
| Scene-object instance-id sign | 2 |
| JSON numeric spelling | 2 |
| Large-float fixed-format text | 1 |
| Runtime exception text | 4 |

`Record` applied `Names` to the whole message. An ordinary `10` was
therefore rewritten as `#LazyData` when that asset's id was 10. The probe now writes the message
as it is; ids reach the text only through `Id`/`JsonIds`. The 23 affected
records contain 27 such substitutions in activation flag pairs and clone rotation
text; their underlying state/name/transform fields match Native. Earlier labels
calling them activation-state or clone-name defects were wrong. The recorder
repair has no new Native capture yet; the existing reference stays canonical until a strict pair
is compared with it. Its 617/65/65 result is an offline projection, not a new engine result or a
comparator normalization.

Both JSON payloads decode identically, including all three differing decimal
spellings' binary64 bits. Their two strict failures remain and are separately
ledgered as bounded nonblocking spelling differences. A general JSON algorithm
and exact-text/arbitrary-precision consumers are unmeasured. The sign correction
is held: positive scene-object IDs are observed, but a complete allocation and
sorted-query rule across classes/load modes is not established. There are no
Destroy rows in this union; that contract has its separate witness.

Audit evidence: `validation/runs/coreapis-retained-audit-20261104/` and Root
`validation/runs/coreapis-retained-root-review-20261104/root-validation.json`.

The matching suite is 1583/1594 with the identical 11 inherited failures; all
three counter-phase tests and the four OnDemand tests pass. The nine-module
API result, 0 errors / 2318 types, is reused through equality of all 65228
public/protected metadata lines and unchanged generated files and tool inputs.
Screencapture Linear and lifecycle remain byte-identical to the previous
runtime; messages retain their 316 passing invariants. Graphics was unchanged
in the earlier OnDemand batch and was not rerun for the counter correction.

Unity's exact counter node is not measured. Neither are custom loops without
GUIClearEvents, paused/stepped/Edit frames, other readers between render and
that node, FixedUpdate/startup reads or the absolute startup value.

Unmeasured cases remain explicit: skipped-frame visibility without an RT camera,
SRP/custom dispatch, motion history, UI panels, skipped screen readback and async
EOF. Interval-1 controls do not establish those paths. A skipped frame with a
configured scriptable pipeline logs one warning. Full receipts, raw unions and
the attribution remain under the validation cache at
`validation/runs/claude-fresh-20261101b/ondemand-validation/` and
`validation/runs/claude-fresh-20261101b/rfc-validation/`.

## Admission and bounded cleanup diagnostics

The Editor's values, logs, sources and inputs remain immutable after admission. The shared
runner can append hub-sweeps.jsonl after Editor exit. This registered diagnostic alone is
excluded from the immutable artifact set; its completed prefix is sealed and every complete
appended row must match the recorded cleanup schedule/schema. Prefix rewrite/removal, invalid
rows and any other changed artifact refuse the next repeat/publication. An in-progress final
write is not read as a capture value. Final receipts still retain/hash the entire journal after
all bounded owned sweepers finish. Repeated check verifies the original seal, never reseals it.

## Recorder preservation admitted on 2026-11-04

The whole-message `Names` scan is removed; numeric payloads stay literal and ids use the
existing explicit `Id`/`JsonIds` paths. Two fresh strict windowed GPU captures of this source
both equal the existing `bbc831a7` canonical,682 lines, byte-for-byte. The reference was not
rewritten. Root verified compile graphs, full IL/data identity and final cleanup seals.

This admits a measurement repair, not an engine API fix. The combined `a8d46d11` runtime
was measured at595/682 with the previous recorder. Its corrected-writer capture is pending;
618/64/64 is only the projection. Old raw captures and every remaining discrepancy are kept.
Evidence: `validation/runs/coreapis-recorder-root-admission-20261104/` and
`validation/runs/destroy-format-combined-root-admission-20261104/`.
