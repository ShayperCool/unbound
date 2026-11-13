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

## Coverage and migration queue

The initial inventory has 1,032 candidates: 111 components, 31 ScriptableObjects,
362 classes and 528 structs. Another 409 types carry an explicit exclusion reason.
The original Editor reference has 11,421 rows, including five local recorder-control rows.
Its statuses are 9,464 values, 1,455 throwing getters, 62 non-readable members, 20 rejected factories, 11 context requirements
and 409 excluded types. The two final GPU repeats differ in 50 rows: clocks, creation dates, a random
seed, joint reaction/obsolete joint/bounds fields observed before simulation or
update, and the Terrain resource-retention flag before TerrainData is assigned. Those differences remain visible and queued.
Another 29 rows contain depth-limit markers and 78 contain cycle markers; their
nested coverage remains open.

Q411 adds 54 controls after the original capture: all thirteen removed getters on
GameObject and Component, on both empty objects and objects with a Rigidbody.
The controls record exception type and message; GetComponent distinguishes the two
contexts. Both independent GPU repeats and Unbound match all 54 controls. The original
11,421 reference rows remain byte-identical, giving 11,475 total rows.

The Core slice adds another 42 independent controls while preserving all prior rows,
for 11,517 rows at that stage. Physics2D adds 17 controls and Physics adds 31,
for 11,565 rows before Audio. Audio adds 25 controls, giving 11,590 current rows.
The comparison now has 10,585 exact rows, 1,005 differing reference rows, zero
missing rows and 30 extra implementation rows. Its 1,037 queue
entries retain the same 50 variable rows; two happen to equal the implementation
snapshot. Variable rows remain unresolved comparisons.

| Core cause | Resolved rows | Controls and rule |
| --- | ---: | --- |
| Root factory eligibility | 125 | Component, Behaviour, MonoBehaviour and Renderer reject direct creation. Valid derived components still attach to populated inactive objects. A script can satisfy its own base-type requirement; a missing abstract requirement rejects the script silently, while an existing concrete component satisfies it. |
| Constructor initialization | 13 | Annotation defaults, category ordering, SpaceAttribute collection application and the four Unity exception result codes. Explicit arguments, writable ordering, custom messages and inner exceptions remain independent. |
| Viewport context | 11 | The engine runner now uses the Editor's 640x480 viewport. Camera size/aspect/matrices and Canvas.pixelRect match; a target texture changes dimensions to 128x32 and removing it restores the viewport. |
| Camera aliases | 4 | near, far, fov and hdr read and write their modern property state in both directions. Their declarations come through the selected release's stub generator. |
| Refresh-rate arithmetic | 2 | Zero-denominator values, exact rational comparisons, integer rounding/overflow and 15-digit display formatting are recorded separately from finite normal rates. |
| Legacy UV getter | 1 | Mesh.uv1 remains null after writes and leaves uv2 unchanged. The generated member remains a compatibility stub. |

The Core slice removes 155 CoreModule rows and one Canvas row. CoreModule retains
116 rows. The three compatibility mechanisms for constructor initialization, factory
eligibility and refresh-rate arithmetic select exactly 6000.0.71f1. Other releases
retain their previous, unmeasured paths; their initialization and invalid-rate behavior
remain gaps. The generator format is now version 4 after adding the UV return rule.

Outstanding Core causes include physical-camera initialization; renderer/material and
light state; wrappers created without live operation, culling or mesh-data payloads;
invalid bare Object state; and imported or serialized defaults such as billboard
size. Runtime literals alone do not settle those contexts. No rendering effect,
serialized fallback or valid-handle contract is inferred from one creation snapshot.
Those exact rows remain queued for initialized-context and import controls.

Correct factory rejection removes 121 extra member rows. Two queued TextCore instance
IDs therefore have new exact implementation values because earlier allocations changed;
the reference values remain untouched and no variable patterns were added. Canvas
renderingDisplaySize still disagrees before layout, and its new 640x480 implementation
value remains queued. The remaining rejected-factory surface rows are still explicit.

The [6000.0 Component](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Component.html)
and [GameObject](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GameObject.html)
API pages describe supported component lookup through GetComponent. The exact removed
getter exceptions are observations of 6000.0.71f1 in this probe, not a documented
release-independent exception contract.

The Physics2D slice resolves 157 rows: 154 from the five rejected base factories,
two Collision2D constructor observations and the EdgeCollider2D composite capability.
Its 17 added controls retain all 11,517 earlier reference rows byte for byte. The
Physics2D package is now explicit so the required-collider and callback scripts can
reference the module. The 42 prior Core controls still match both GPU captures.

| Physics2D cause | Rule and controls |
| --- | --- |
| Factory eligibility | Collider2D, Joint2D, AnchoredJoint2D, Effector2D and PhysicsUpdateBehaviour2D reject direct creation. Box, distance-joint and point-effector controls remain valid. A missing abstract collider requirement rejects its script silently; an existing BoxCollider2D satisfies it. |
| Collision construction and legacy array | A manual Collision2D starts disabled with no contact source. Its first contacts read throws after caching an empty array; later reads reuse that array. Live enter callbacks remain enabled with valid contact data. Mutating the legacy array changes GetContact and both GetContacts results, while a real exit callback remains enabled with a reusable empty array. |
| Composite capability | Edge chains do not supply filled composite geometry, even when their endpoints form a closed loop. Merge assignments remain None for that edge; the neighboring BoxCollider2D accepts Merge. The same rule is used for runtime reads and copies. |

The [6000.0 contacts documentation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Collision2D-contacts.html)
warns that the property allocates and recommends GetContact/GetContacts. It does not
promise fresh arrays on repeated reads. The [enabled documentation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Collision2D-enabled.html)
describes response state and notes that callbacks can occur without response. The
manual-constructor, cache identity and exit observations above are release-specific
probe results, not broader callback guarantees.
[Composite operations](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Collider2D-compositeOperation.html)
apply to supported collider geometry and use the composite's material/trigger/effector
settings. The edge and box controls distinguish supported from unsupported sources.
The existing measured Physics2D compatibility mechanism selects these rules; other
releases retain their previous unmeasured contact and capability paths.

Physics2D keeps five rows: two polygon initialization values, the initial substep count,
and the two already-variable joint forces. Both scene values are valid/equal and report
one after a real Script simulation, ruling out invalid-scene and constant-counter
explanations for the earlier zero-versus-one discrepancy. The polygon radius and angular
arithmetic still need one reconstructed initializer plus import/runtime controls.
No cache writer, cache schema or serialized output changes in this slice; the runtime
reader applies the same capability rule to already parsed input.

The Physics slice resolves 89 rows (87 in PhysicsModule and two inherited collider
material rows in TerrainPhysicsModule/VehiclesModule) and preserves all 11,534 earlier reference rows.
Both independent GPU captures and Unbound match its 31 new controls.

| Physics cause | Resolved rows | Rule and controls |
| --- | ---: | --- |
| Base factories | 66 | Collider and Joint reject direct creation, including on a populated inactive object. BoxCollider and FixedJoint remain valid. A missing abstract Collider requirement blocks its script; an existing box satisfies it. |
| Materials | 8 | The parameterless PhysicsMaterial constructor uses DynamicMaterial; named constructors retain empty and explicit names. A collider without a material creates an unnamed reusable copy. A named shared material is copied with its physical values and an Instance suffix. A copy belongs to its collider: transferring it to a second collider creates a separate copy, and subsequent writes stay isolated. |
| Solver aliases | 2 | Both obsolete Rigidbody iteration aliases forward reads and writes to the corresponding current properties after their values change. Declarations follow the selected release's stub generator. |
| Removed settings | 2 | RaycastCommand.maxHits reads one and ignores writes, for both zero construction and a command with explicit query parameters. MeshCollider.smoothSphereCollisions reads true after false and true assignments. |
| Empty callback payloads | 5 | Object-dependent reads on a manually constructed Collision or ControllerColliderHit throw NullReferenceException. Real collision and controller-hit callbacks retain valid object/transform access; a static collider hit has no Rigidbody. |
| Joint drives | 6 | All six ConfigurableJoint drives normalize spring, damper and maximum force to the measured nonnegative finite range. Negative inputs become zero, positive infinity and float.MaxValue reach one float step below float.MaxValue, ordinary finite values survive, and NaN remains NaN. The standalone JointDrive value keeps its input. Raw-bit controls distinguish the upper bound from a formatting difference. |

The Unity 6000.0 [material](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Collider-material.html)
documentation describes copying a shared material for a collider, while
[sharedMaterial](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Collider-sharedMaterial.html)
writes affect every user of that material. The controls change friction before and
after copying, then transfer the copy between colliders to distinguish ownership.
[Solver iterations](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rigidbody-solverIterations.html)
are positive per-body settings; both alias controls use positive nondefault values.
The callback pages describe the hit objects, and [ControllerColliderHit.rigidbody](https://docs.unity3d.com/ja/6000.0/ScriptReference/ControllerColliderHit-rigidbody.html)
explicitly permits null for a static collider. The live controls exercise that case.
The empty-payload exceptions and exact [drive](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/JointDrive-maximumForce.html)
range are release observations, not documented universal initialization contracts.
Destroyed callback targets, articulation bodies and serialized drive edge cases
remain outside these controls.

Physics retains four GeometryHolder rows and its unchanged variable HingeJoint row. Its typed geometry representation remains
unimplemented: the descriptor must carry dimensions and shape state, so returning
only a matching type enum would conceal the missing payload. Correct factory and
material state do not resolve that mechanism. Material ownership, invalid callback
access, removed command settings and drive normalization use exact-release Compat
mechanisms. Other releases retain the previous unmeasured behavior. Runtime drive
reads share normalization with assignments; no importer/cache writer, schema or
serialized output changes. Stub table output changes bump the generator format to 5.

The Audio slice resolves 185 rows: 184 from AudioBehaviour and the six filter
factories, plus the removed AudioReverbZone roomRolloffFactor setting. Its 25 controls
preserve all 11,565 prior canonical rows byte for byte. Both independent NVIDIA GPU
Editor captures and the engine match the new controls, including all 144 prior
removed-getter, Core, Physics2D and Physics controls.

| Audio cause | Resolved rows | Rule and controls |
| --- | ---: | --- |
| Factory requirements | 184 | AudioBehaviour rejects direct creation on empty, source and listener hosts. Each of the six filters rejects an empty host and accepts either an existing AudioSource or AudioListener. A script requiring AudioBehaviour follows the same requirement rule. Controls use inactive hosts and retain their concrete providers. |
| Removed setting | 1 | roomRolloffFactor returns 10 and ignores assignments under Generic and Cave presets. Each of the two writes and two reads emits the release's exact warning. Its attribute declaration uses the selected release's constructor signature. |

Unity 6000.0 documents [filters](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioLowPassFilter.html)
in relation to sources and listeners, and [RequireComponent](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RequireComponent.html)
as a dependency constraint. Exact base-factory rejection, acceptable providers and
removed-setting warnings are observations of 6000.0.71f1. Existing concrete providers
change the factory result; the reverb writes change the requested value without
changing the returned value. The shared factory compatibility mechanism covers the
base type, and a removed-audio-settings mechanism selects the warning/value behavior.
Other releases retain their earlier unmeasured response. No audio processing algorithm,
importer, cache output or format changes in this slice.

Four WebCamTexture rows remain exact migration entries. Empty constructor arguments
do not settle device discovery, the no-device name, readability or backing graphics
resource state. The existing 16x16 placeholder lacks capture backing. Device/resource
lifecycle controls are needed; these NVIDIA-only observations must not become general
format or device-name constants. The optional UnityEditor.CoreModule work remains
queued: 94 importer rows, 97 window rows and 28 other editor-context rows. Remaining
ParticleSystem, UIElements, Animation and small-module rows are listed below.
All 50 variable policies and the constructor-store discrepancy remain unchanged.
The final counts include one lighting row resolved independently during integration.

| Release module | Queued rows |
| --- | ---: |
| UnityEngine.ParticleSystemModule | 347 |
| UnityEditor.CoreModule | 219 |
| UnityEngine.UIElementsModule | 159 |
| UnityEngine.CoreModule | 115 |
| UnityEngine.IMGUIModule | 29 |
| UnityEngine.GridModule | 26 |
| UnityEngine.AssetBundleModule | 24 |
| UnityEngine.AnimationModule | 20 |
| UnityEngine.TerrainModule | 13 |
| UnityEngine.TextCoreTextEngineModule | 10 |
| UnityEngine.TilemapModule | 10 |
| UnityEngine.VideoModule | 9 |
| UnityEngine.GameCenterModule | 7 |
| UnityEngine.SpriteShapeModule | 7 |
| UnityEngine.UnityWebRequestModule | 6 |
| UnityEngine.AIModule | 5 |
| UnityEngine.Physics2DModule | 5 |
| UnityEngine.PhysicsModule | 5 |
| UnityEngine.SpriteMaskModule | 5 |
| UnityEngine.AudioModule | 4 |
| UnityEngine.UIModule | 3 |
| UnityEngine.UnityWebRequestTextureModule | 3 |
| UnityEngine.TerrainPhysicsModule | 2 |
| UnityEngine.TextCoreFontEngineModule | 2 |
| UnityEngine.AndroidJNIModule | 1 |
| UnityEngine.VehiclesModule | 1 |

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
