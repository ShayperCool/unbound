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
