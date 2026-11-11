# Instance ID signs of loaded and created objects

Unity 6000.0.71f1, windowed Linux GPU Editor, built-in pipeline. The two independent
captures agree byte for byte across 462 values. Each scene contains active and
inactive GameObjects with Transform, MonoBehaviour, Camera and BoxCollider components.

```bash
python3 tests/projects/instanceids/run.py editor --release 6000.0.71f1 --label identity
python3 tests/projects/instanceids/run.py publish --release 6000.0.71f1 --label identity
python3 tests/projects/instanceids/run.py unbound --release 6000.0.71f1 --label engine
```

Measured cases:

- Objects read from the initial, synchronous additive, asynchronous additive and
  synchronous single scene files have positive IDs, including inactive objects.
- A Light added to a loaded GameObject has a negative ID. New GameObjects,
  components and ScriptableObjects, and clones of scene objects, prefabs and
  materials, also have negative IDs.
- Prefab objects and components, ScriptableObject, TextAsset and Material assets
  loaded through Resources have positive IDs.
- DontDestroyOnLoad retains IDs of file-loaded and newly created objects through
  a single scene replacement. Moving an object to DontDestroyOnLoad changes neither its sign
  nor its ID. The old scene's destroyed object no longer resolves by ID.

The record tests ID lookup and validity, LazyLoadReference ID lookup, Object and
lazy-reference JSON round-trips, and IDs written into JSON for both signs. It
checks nonzero uniqueness and ascending FindObjectsByType InstanceID sorting,
including negative runtime objects before positive file objects. It checks that
assigning the positive-ID scene object as a LazyLoadReference asset is rejected:
a positive ID does not make an object a persistent asset.

The Unity 6000.0 [GetInstanceID API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Object.GetInstanceID.html)
documents nonzero uniqueness, positive IDs for file-loaded objects, negative IDs
for newly created objects, and that sign does not establish persistence. IDs are
session-local. The [FindObjectsByType API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Object.FindObjectsByType.html)
documents the selected sorting mode and exclusion of assets. The controls make
the signed boundary change the query result and test lookup and serialization
with both signs. They impose no order within either sign beyond numeric sorting.

Raw IDs are deliberately not canonical values. Relative allocation order of
file objects, saving newly created objects, scene-loaded prefab variants,
asset bundles, asynchronous single loads and other releases remain unmeasured
by this probe. Existing oroorder, coreapis, instantiateasync and assetclones
references and comparators remain unchanged.
