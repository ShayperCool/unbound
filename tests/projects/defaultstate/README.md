# Default state of implemented API types

This probe records Unity 6000.0.71f1 Editor Play mode on NVIDIA GeForce RTX 4060,
then runs the same recorder in Unbound. It measures creation-time observations;
it does not establish release-independent or device-independent default contracts.

`unbound api-defaults -p tests/projects/defaultstate` inventories matching types in
the hand-written assembly, with the release's modules determining type category,
visibility, abstract/generic status and constructor availability. It reads metadata
and managed constructor IL without executing reference assemblies. Components use
`AddComponent` on a fresh GameObject, ScriptableObjects use `CreateInstance`, and
other public classes and structs use the public parameterless constructor or the
implicit zero-valued struct constructor. Abstract, static, open generic and
nonconstructible types remain explicit inventory rows. Nonpublic component and
ScriptableObject types are included when concrete.

Every public instance field and property is recorded, including inherited members.
Indexer overloads include their argument types in their keys and are reported as
requiring arguments. Private getters, exceptions and rejected factories have
explicit statuses. Each readable member gets a fresh instance, so stateful getters
cannot alter later creation-time observations. A local control increments state in one getter and
reads it in the next; its recorded values are 1 and 0 on both runtimes. Root objects
are destroyed or disposed after capture. Object identities and pointer addresses are represented
by type/name and null/non-null state, respectively. Delegates record method identities
and target state; reflected members record their declaring type, name and method
signature. Assembly/module values record names. Runtime paths, build identifiers
and metadata tokens are outside this value representation.

Nested structs use public fields when those fields cover all instance storage.
Other values include public readable properties too. A local inherited-state
control retains both a derived public field and a value backed by private base
storage; publication checks both. Recursion stops at depth
five and marks reference cycles explicitly. Such rows remain coverage gaps for
nested leaves; equality of a truncated row does not prove those leaves match.

## Context and documented factories

The release documentation describes [AddComponent](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GameObject.AddComponent.html)
and [CreateInstance](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ScriptableObject.CreateInstance.html)
as creation APIs. It does not promise every engine type accepts generic creation.
The reference therefore records a null return or an exception rather than treating
it as successful initialization.

Eleven property rows require a live context and are recorded as `requires-context`:

- `ModifiableContactPair.colliderInstanceID`, `otherColliderInstanceID` and
  `massProperties` require a [contact-modification callback payload](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ModifiableContactPair.html).
- `LocalKeyword.isDynamic`, `isOverridable` and `type` require a keyword initialized
  from a shader. Its [isValid](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.LocalKeyword-isValid.html)
  and name are still read for the default value.
- The three `LocalKeywordSpace` properties require a space obtained from a shader
  or compute shader.
- The two integrated subsystem descriptor property rows require a descriptor
  returned by `SubsystemManager`.

These are measured context limitations, not inferred default values. No engine
behavior changes are made by this probe.

## Run and publish

```bash
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
export UNBOUND_CLI=/absolute/path/to/the/slot/build/release/unbound
python3 tests/projects/defaultstate/run.py native --label native-a
python3 tests/projects/defaultstate/run.py native --label native-b
python3 tests/projects/defaultstate/run.py publish native-a native-b
python3 tests/projects/defaultstate/run.py check
python3 -m unittest discover -s tests/projects/defaultstate -p test_compare.py
```

Runs, both raw Native repeats, logs, inventory and the full comparison go under
`$XDG_CACHE_HOME/unbound/validation/runs/defaultstate`. The canonical release
reference contains one raw Editor capture and its type inventory. Publication
requires two independently successful GPU runs, identical inventories and row
keys, and identical values outside the explicitly queued variable leaves. It
refuses to overwrite an existing reference. New implemented types require a new
reviewed capture, while existing canonical rows must be preserved byte for byte
unless their measured context is explicitly changed and reviewed.

`debt.json` is an exact per-release migration queue with a reason for each row.
It retains both expected and current values. Variable creation values have anchored patterns limited to the observed leaves.
The Terrain resource-retention boolean has a separate reasoned context entry for
both observed values; the implementation value remains exact. The raw values
are never replaced or rounded, every difference is printed, and the complete
comparison includes all missing and extra keys. Unknown differences, changed
exception statuses, empty/duplicate reasons and stale entries fail the gate.
The variable entries are unresolved comparisons, not parity passes. They need
relational or initialized-context controls before a default contract can be claimed.

## Initial coverage and queue

The initial inventory has 1,032 candidates: 111 components, 31 ScriptableObjects,
362 classes and 528 structs. Another 409 types carry an explicit exclusion reason.
The Editor reference has 11,421 rows, including five local recorder-control rows.
Its statuses are 9,464 values, 1,455 throwing getters, 62 non-readable members, 20 rejected factories, 11 context requirements
and 409 excluded types. The two final GPU repeats differ in 50 rows: clocks, creation dates, a random
seed, joint reaction/obsolete joint/bounds fields observed before simulation or
update, and the Terrain resource-retention flag before TerrainData is assigned. Those differences remain visible and queued.
Another 29 rows contain depth-limit markers and 78 contain cycle markers; their
nested coverage remains open.

The first comparison has 9,143 exact rows, 2,278 differing reference rows,
zero missing rows and 541 extra implementation rows. The 2,821 queue entries
include 50 variable-reference rows, two of which happen to equal the engine
snapshot. All variable rows remain unresolved comparisons. Of the differing rows, 1,196 concern
removed Component/GameObject getters that throw in the release but return a
generated fallback value in Unbound; fix that shared mechanism before individual
component defaults. The module counts below are rows, not independent root causes.

| Release module | Queued rows |
| --- | ---: |
| UnityEngine.CoreModule | 570 |
| UnityEngine.Physics2DModule | 448 |
| UnityEngine.ParticleSystemModule | 386 |
| UnityEngine.PhysicsModule | 248 |
| UnityEngine.AudioModule | 228 |
| UnityEditor.CoreModule | 219 |
| UnityEngine.UIElementsModule | 172 |
| UnityEngine.AnimationModule | 124 |
| UnityEngine.TilemapModule | 49 |
| UnityEngine.AIModule | 44 |
| UnityEngine.UIModule | 43 |
| UnityEngine.GridModule | 39 |
| UnityEngine.IMGUIModule | 29 |
| UnityEngine.TerrainModule | 26 |
| UnityEngine.AssetBundleModule | 24 |
| UnityEngine.VideoModule | 22 |
| UnityEngine.SpriteShapeModule | 20 |
| UnityEngine.SpriteMaskModule | 18 |
| UnityEngine.TerrainPhysicsModule | 16 |
| UnityEngine.VehiclesModule | 15 |
| UnityEngine.ClothModule | 13 |
| UnityEngine.DirectorModule | 13 |
| UnityEngine.TextRenderingModule | 13 |
| UnityEngine.WindModule | 13 |
| UnityEngine.TextCoreTextEngineModule | 10 |
| UnityEngine.GameCenterModule | 7 |
| UnityEngine.UnityWebRequestModule | 6 |
| UnityEngine.UnityWebRequestTextureModule | 3 |
| UnityEngine.TextCoreFontEngineModule | 2 |
| UnityEngine.AndroidJNIModule | 1 |

The separate static scan compares direct literal field stores in straight-line
constructors, including field initializers and later overwrites. Matching includes
the constructor signature, declaring storage type, field name and field type,
including references to fields in generic types. Branches and exception
regions are undecided, as are computed stores. The scan does not infer final
state after arbitrary calls or native initialization. Its initial report contains
296 equal stores, one differing store, 924 unknown pairs, 1,865 unmatched fields,
1,341 unmatched constructors and 1,004 branching constructors across both sides.
The one queued difference is the parameterized `ShadowDrawingSettings` constructor's
split index, -1 in the release and 0 in Unbound. Its implicit struct default is a
separate case. Add a parameterized Editor control before changing it. Unmatched
storage and constructors do not establish differences and must not be counted
as equal.
