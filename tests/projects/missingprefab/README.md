# References into instances of missing prefabs

Unity 6000.0.71f1, Linux GPU Editor (OpenGL Core). Our own content, authored once by
`Authoring/MissingPrefabAuthor.cs` (`run.py author`): a prefab `Inner` (a root with a `Marker`
script, a child `Wheel` with a BoxCollider, children `Wheel1`..`Wheel3` with BoxColliders), its
variant `InnerVariant`, a prefab `Outer` with a nested `Inner` instance `InnerA` (moved, its script
value and name overridden, a SphereCollider added to its root, a CapsuleCollider to `Wheel2`, a
child added to its root and one to `Wheel`) and a `Holder` script whose fields reference InnerA's
objects (root, children's GameObjects, Transforms, colliders, the script, the added objects), and
a scene with an `Outer` instance, two `Inner` instances (one referenced by a scene `Holder`, with
a child added under `Wheel`), an `InnerVariant` instance and the scene `Holder`.

Two variants of the project, each captured twice (byte-identical):

- `present`: as authored; the control.
- `missing`: `Inner.prefab` and its `.meta` deleted before the Editor opens the project.

`MissingPrefabRunner` records, in Edit mode, the opened scene's hierarchy (names, activity, local
positions, component types, script values), the scene Holder's references, PrefabUtility's
instance status of every object, the `Outer` asset and a PrefabUtility.InstantiatePrefab instance
of it with their Holder's references, and the variant asset; in Play mode (entered with domain
reload disabled), the references every Holder's Awake reads, the scene, an Object.Instantiate copy
of `Outer` and of the variant; and every log message mentioning prefabs, by phase (the start-up
import messages as a sorted set: the Editor imports in an order of its own).

| Source (6000.0) | Documented | Measured here |
|---|---|---|
| [PrefabUtility.IsPrefabAssetMissing](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PrefabUtility.IsPrefabAssetMissing.html) | "Returns true if the given object is part of a Prefab instance but the source asset is missing." | True for the objects of a scene's instance of a missing prefab (status MissingAsset); an instance nested in a prefab that is present answers Connected. |
| Missing prefab instances | Not documented beyond the above. | The instance loads as a root named "<name> (Missing Prefab with guid: <guid>)" with the root Transform's modifications and the added components; each other stripped object becomes a child "Placeholder for referenced <class> in Prefab instance (<n>)" (class id then file id order, n after the added children of the root), so serialized references read objects, in Edit mode, Play mode and copies. A variant whose parent is missing does not load; its instances are missing prefabs. Each scene load logs one error per missing prefab in GUID order. |

Unbound: present 255/255; missing 277/280 with 3 recorded residuals (`residuals.json`): the
errors of the scene reload on leaving Play mode (Unbound's Edit mode starts from an empty scene
then) and the import errors of a fresh Library (Unbound imports no prefab at start-up).
Unmeasured: placeholders of other classes than GameObject, Transform, BoxCollider and
MonoBehaviour (their order by class id is inferred), an instance without root modifications.

```sh
python3 tests/projects/missingprefab/run.py editor --release 6000.0.71f1 --label L
python3 tests/projects/missingprefab/run.py publish --release 6000.0.71f1 --label L
UNBOUND_CLI=/path/to/runtime/unbound python3 tests/projects/missingprefab/run.py check --release 6000.0.71f1
```
