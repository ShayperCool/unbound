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
python3 tests/projects/coreapis/run.py editor --release 6000.0.71f1 --label <new-label>
python3 tests/projects/coreapis/run.py publish --release 6000.0.71f1 --label <new-label>
UNBOUND_CLI=/path/to/unbound python3 tests/projects/coreapis/run.py unbound --release 6000.0.71f1 --label <new-label>
python3 tests/projects/coreapis/compare.py tests/projects/coreapis/Reference/unity-6000.0.71f1.jsonl \
    ~/.cache/unbound/validation/runs/coreapis/<label>/unbound/values.jsonl --report <cache>/compare.json
```

The Editor captures run in windowed Editors (a private Xvfb, the GPU through VirtualGL): in batch
mode the Game view does not render, so the screen camera, OnGUI and WaitForEndOfFrame never run
and rendering is never skipped. Publishing requires two byte-identical captures on the NVIDIA
renderer that exited with 0. The comparator aligns the two event sequences and keeps every
missing and extra line, with no tolerance.

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
* **SetGameObjectsActive** (both overloads): `SetActive` on each id in order, each object's
  messages before the next one's (a reentrant change from OnDisable takes effect at once). Ids
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
  batch span = candidate batch NativeArray for the recorded cases. This attributes
  the limitation to the unchanged shared path; Native plain SetActive is unmeasured.
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
