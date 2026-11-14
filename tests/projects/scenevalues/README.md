# Scene value equality

Unity 6000.0.71f1, recorded twice on the NVIDIA GPU. The canonical reference contains 274 byte-identical rows. The gate compares every missing, extra or unequal row exactly.

Scene has no IEquatable interface in the selected declaration. Object equality, both operators, generic default equality and hashing use its handle. Seven seeded values include zero, duplicates, negative handles and both integer limits. The complete pair matrix tests equality in both directions; nullable boxing, null/wrong types, independent copies, hash-set duplication/removal and dictionary replacement/removal/lookup test the ordinary collection paths.

The [Scene equality operator](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SceneManagement.Scene-operator_eq.html) identifies references to the same scene in memory. The live control creates two distinct scenes, copies the first value, and looks it up again by name. Copied/fresh values retrieve the first dictionary entry; the second scene is unequal and retrieves its own entry. This distinguishes scene identity rather than accepting equal defaults.

After unloading the second scene, its retained value is invalid but still compares equal to its earlier copy, keeps its hash and retrieves its existing collection entry. Synthetic invalid handles and retained-value behavior are selected-release observations; the probe does not establish new loading or lifetime guarantees.

The engine's scene registries already use SceneHandle, integer keys or SceneData references. The source removes the extra public IEquatable declaration and duplicate explicit method, leaving generic lookups to the existing object equality/hash implementation. No new comparer or lifetime path is introduced. Other releases remain unmeasured.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/scenevalues/run.py native --label reference-a
python3 tests/projects/scenevalues/run.py native --label reference-b
python3 tests/projects/scenevalues/run.py publish reference-a reference-b
python3 tests/projects/scenevalues/run.py check
```

Captures, engine output and comparisons stay in the run cache. Existing references and tolerances remain unchanged.
