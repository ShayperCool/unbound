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
for 11,565 rows before Audio. Audio adds 25 controls, giving 11,590 rows before ParticleSystem.
The ParticleSystem slice adds 362 controls. Declaration slices add five inventory rows.
The Editor slice adds 223 controls while preserving all 11,957 previous rows,
giving 12,180 rows before UIElements. UIElements adds 95 controls and six atlas-settings
rows, preserving all earlier raw rows, for 12,281 rows before the remaining Core work.
Declaration work adds one more factory row. Core adds 229 controls, giving 12,511 current rows. The comparison now has 12,165 exact
rows, 346 differing reference rows, zero missing rows and 25 extra implementation rows.
Its 373 queue entries retain the same 50 variable rows; two
happen to equal the implementation snapshot. Variable rows remain unresolved comparisons.

| Core cause | Resolved rows | Controls and rule |
| --- | ---: | --- |
| Root factory eligibility | 125 | Component, Behaviour, MonoBehaviour and Renderer reject direct creation. Valid derived components still attach to populated inactive objects. A script can satisfy its own base-type requirement; a missing abstract requirement rejects the script silently, while an existing concrete component satisfies it. |
| Constructor initialization | 13 | Annotation defaults, category ordering, SpaceAttribute collection application and the four Unity exception result codes. Explicit arguments, writable ordering, custom messages and inner exceptions remain independent. |
| Viewport context | 11 | The engine runner now uses the Editor's 640x480 viewport. Camera size/aspect/matrices and Canvas.pixelRect match; a target texture changes dimensions to 128x32 and removing it restores the viewport. |
| Camera aliases | 4 | near, far, fov and hdr read and write their modern property state in both directions. Their declarations come through the selected release's stub generator. |
| Refresh-rate arithmetic | 2 | Zero-denominator values, exact rational comparisons, integer rounding/overflow and 15-digit display formatting are recorded separately from finite normal rates. |
| Legacy UV getter | 1 | Mesh.uv1 remains null after writes and leaves uv2 unchanged. The generated member remains a compatibility stub. |

The first Core slice removes 155 CoreModule rows and one Canvas row. CoreModule retained
116 rows at that stage. The three compatibility mechanisms for constructor initialization, factory
eligibility and refresh-rate arithmetic select exactly 6000.0.71f1. Other releases
retain their previous, unmeasured paths; their initialization and invalid-rate behavior
remain gaps. The generator format is now version 4 after adding the UV return rule.

The remaining Core work resolves 127 more queue entries, including 76 CoreModule,
24 AssetBundle, five each ParticleSystem, SpriteMask and SpriteShape, seven Tilemap,
four web-request and one UIElements entry. Two independent NVIDIA Editor captures
and Unbound match all 229 added controls. All 12,281 previous reference lines are
preserved byte for byte. Generator format 10 suppresses the newly implemented
members and moves their required enum declarations into the implementation.

| Remaining Core mechanism | Controls and scope |
| --- | --- |
| Component factory backing | Nested construction of the same type cannot borrow the factory's backing. A constructor error is logged while the attached partial component survives and receives Awake/OnEnable. Uncreated wrappers reject their bound properties; attached constructor bindings reject that context. Attribute-driven edit mode and explicit instance writes share cached type metadata. |
| Operation backing and capabilities | Bare operations and derived bundle requests reject access. Completed resources and bundle requests store priority and ignore activation writes; async instantiation stores activation and ignores priority. A failed bundle can complete with progress zero. Late completion handlers run immediately on the same operation. A custom ResourceRequest result override remains callable. |
| Renderer configuration | Deprecated aliases read and write the modern state. Initial shadow/probe/motion/ray configuration is recorded for eight renderer families. Ray mode and build flags retain their measured low bits. LOD membership survives duplicate renderer entries, forced selection, inactive/disabled groups and removal. Billboard material slots retain their previous shape. |
| Curve ownership | Line and trail width curves have one weighted default key. Gets and sets copy the curve; later mutations do not change the renderer. Null assignments reject. |
| Mesh layout and view validity | Empty meshes have no vertex buffers, while an explicitly created empty position channel remains present. Clear retains or removes the layout as requested. Missing streams reject before changing data. Default and disposed MeshData views reject; valid read-only and writable views still work. |
| Camera and light configuration | Physical-camera fields clamp independently, preserve NaN where measured, and copy with the camera. Reversed curvature endpoints remain independent. Light units, reflector and lux-distance state remain writable; removed light settings ignore writes. These controls establish configuration storage, not unimplemented optical or lighting effects. |

The [AsyncOperation completion event](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AsyncOperation-completed.html)
documents immediate invocation for a completed operation; the new control subscribes
after actual completion. The [runInEditMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour-runInEditMode.html)
API is Editor-only. The [isLOD0](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Renderer-isLOD0.html)
controls put a renderer in the second level before changing group state. The
[MeshData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.MeshData.html)
controls use populated views before disposal and verify that read-only views reject writes.

CoreModule retains 36 rows. Scene handles and culling masks need scene/rendering
context; six culling-result getters and their two nested aggregates need actual
cull payloads. Bare Object identity, empty MeshFilter ownership, probe-volume defaults,
format selection, readback and acceleration-build state remain separate mechanisms.
Billboard dimensions and the removed projector alias have additional small-module
controls pending. Singular-matrix rotation and conservative empty trail bounds need
general numeric and lifetime rules. Three light-distance observations are retained
outside the canonical reference because storing distances alone would not implement
shadow-caster culling. No fallback textures, culling results or ray-tracing behavior
are inferred from configuration defaults.

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
format or device-name constants. The UnityEditor.CoreModule work remains
queued: 94 importer rows, 97 window rows and 28 other editor-context rows. Remaining
UIElements, Animation and small-module rows are listed below.
All 50 variable policies and the constructor-store discrepancy remain unchanged.
The final counts include one lighting row resolved independently during integration.

The ParticleSystem slice resolves 337 rows and retains all 11,590 earlier canonical
rows byte for byte. Both NVIDIA GPU Editor repeats and the engine match its 362 new
controls, including every property of a handle retained after its owner is destroyed.
The 169 earlier controls remain unchanged in both Editor repeats.

| ParticleSystem cause | Resolved rows | Rule and controls |
| --- | ---: | --- |
| Module handle reads | 322 | Missing owners return the measured property fallback. Curves and gradients are empty values; culling mode, texture clip channel, row mode and trail texture scale have explicit nonzero fallbacks. Managed owner-dependent counts and legacy flip reads still throw. An attached copied handle writes through to the same system, and destroying its owner changes the read result. |
| Removed accessors | 8 | The six legacy sub-emitter properties reject reads and writes, including on an attached system, while the current list remains empty. The removed collision-event accessor rejects both an empty payload and one with a real collider ID; colliderComponent still resolves that collider. |
| Constructor values and key metadata | 7 | Default module curve keys retain unweighted one-third handles. Emitter velocity uses Rigidbody, interior collisions start disabled, noise quality starts Medium, rows start Random, the sprite list starts with one empty slot and force-field length starts at one. Explicit quality/length writes and attached curve values remain writable. The first AddSprite replaces the single empty slot, another sprite appends, AddSprite rejects null and SetSprite accepts null. |

The [6000.0 module documentation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.MainModule.html)
describes module writes as changes to their owning system; the copied-handle control
changes the value before reading through both handles. The [sub-emitter API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.SubEmittersModule.html)
describes the current list operations, and [colliderComponent](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleCollisionEvent-colliderComponent.html)
identifies the struck collider. [AddSprite](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.TextureSheetAnimationModule.AddSprite.html)
adds a sprite to the animation. The initial empty-slot replacement, null handling,
missing-owner fallbacks and precise constructor values above are observations of
6000.0.71f1, rather than documented release-independent guarantees.

Missing-owner reads and removed particle accessors use separate exact-release Compat
mechanisms. Constructor values reuse the constructor-initialization mechanism. Other
releases retain their earlier unmeasured values and access paths. Simulation rules,
importer/cache writers and serialized cache payloads do not change. The existing
particlecurves, particlerender and particletrails suites retain their baseline results.

ParticleSystem keeps ten rows: four module aggregates with inactive gradient color
storage, the unchanged variable random seed and five inherited Renderer observations.
The canonical inactive color fields are not reproduced by later captures or by the
initialized managed values; active gradient keys/modes alone do not establish those
unused fields as deterministic defaults. Their exact raw rows stay queued. The emitter
velocity correction therefore does not remove the still-differing MainModule aggregate.
The inherited renderer rows remain part of the Core renderer follow-up. All 50 variable
policies and all earlier canonical rows remain unchanged.

The Editor slice resolves 132 rows: 91 importer-wrapper reads, three removed
texture settings, 32 window scalar/flag observations, three default collection counts
and three unset paths. Both independent NVIDIA captures and Unbound match all 223
new controls; all 531 earlier controls remain unchanged.

The Editor slice records [AssetImporter.GetAtPath](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetImporter.GetAtPath.html)
for MonoImporter, TextureImporter and a project ScriptedImporter, alongside manually
constructed wrappers. These are distinct contexts: manual wrappers have no object
identity and native-backed property reads throw NullReferenceException; the factory
returns a cached live importer. Property access from its constructor still follows
the constructor restriction. Same-type nested field initializers construct separate
unbacked wrappers. A constructor exception is logged once while the factory retains
the partially initialized live object. Common string settings normalize null to empty.
These exception, identity and initialization details are observations of 6000.0.71f1,
not release-independent guarantees. Controls modify settings and read the cached
importer again, distinguish nested instances, and capture constructor errors through
the managed log handler.

Three removed TextureImporter settings bypass backing checks: correctGamma and
generateMipsInLinearSpace return true, and spritePackingTag returns empty. Writes
of both boolean values, a nonempty tag and null leave these answers unchanged on
bare and factory-created importers. The generator preserves simple literal getters and empty setters through one
release-selected rule, including AudioImporter and VideoClipImporter legacy settings.
Controls cover all public bare-wrapper properties of both types, both writes to each
removed setting, and seven internal legacy audio information getters. The same rule
handles expression getters and block getters that return through a local variable.
AssetBundle-setting validation diagnostics and other importer methods are outside
these value controls.

Default TypeCache collection Count reads throw without storage, while empty query
results return zero and populated results return positive counts. The
[Count documentation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TypeCache.TypeCollection.Count.html)
describes collection size; it does not promise a valid result for a zero-constructed
collection. Other operations on zero-constructed collections remain unmeasured.
[AssetPostprocessor.assetPath](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetPostprocessor-assetPath.html)
and [ObjectIdentifier.filePath](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Build.Content.ObjectIdentifier-filePath.html)
retain null until assigned; empty, nonempty and null assignments remain distinct in
the controls. Their documentation describes paths in initialized import/build
contexts, rather than a universal non-null default. These raw constructor values
reuse the initialization compatibility mechanism.

EditorWindow scalar defaults are observed both in a derived constructor and in
Awake; DontSave flags are applied before user Awake. Explicit writes change size,
position, depth, title and save message, while the removed antiAlias setting keeps
one. The [minSize](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorWindow-minSize.html)
limit applies to floating windows, and setting
[position](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorWindow-position.html)
can undock a window. Docking and size constraint enforcement are outside these
scalar controls. [ScriptableObject.Awake](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ScriptableObject.Awake.html)
describes initialization; the precise flag phase here is a release observation.
Importer backing and removed window settings select measured Compat mechanisms;
window constructor values reuse the initialization mechanism. Other releases retain
the prior unmeasured wrapper, constructor and antiAlias paths. Importer property
stub output changes bump generator format to 8. Scripted import objects are retained
in memory; this slice changes no persistent importer cache format.

Editor keeps 87 rows: targetless inspectors and serialized state; window UI trees,
data-mode controllers and overlays; built-in window-specific titles, cameras and
event interests; view hierarchies; preview stages; and code-editor provider discovery.
Base constructor controls rule out borrowing one set of scalar defaults for those
contexts. The exact values and per-row follow-ups remain in the queue.

## UIElements constructor and data controls

The UIElements slice adds 95 independent controls and the six raw rows for
DynamicAtlasSettings. Both NVIDIA Editor repeats agree, and the engine matches all
101 rows. The slice resolves 59 UIElements entries and two record-string entries
in Event and jvalue, reducing its branch baseline from 561 to 500 queue entries.

| Cause | Controls and measured rule |
| --- | --- |
| Pointer conversion | Unknown and pen pointers derive altitude and azimuth from tilt. Touch pointers lazily derive tilt from angles. Setter/read order, pooled reset, public Touch/Pen factories, copied events, NaN and nine additional angle pairs exercise conversion caches and float argument rounding. |
| UXML descriptions | Attribute descriptions start Optional. Color descriptions start opaque black and use Color's RGBA string representation; writes preserve independent channels and usage values. |
| Text interfaces | TextElement, Label, Button and RepeatButton return themselves as experimental features. Rendered-text overrides affect the existing rendering value while logical text stays independent; a later text write resets the rendered value. |
| Containers | IMGUIContainer retains its callback without invoking it during construction, defaults to Editor context and accepts context/focus changes. RadioButtonGroup separates generated choices from manually added children. MinMaxSlider starts with Ignore picking. |
| Atlas settings | A plain DynamicAtlasSettings has zero storage; defaults creates a fresh 64/4096/64 configuration with all five measured filters. Setters retain raw integers, flags and delegates. Four private marked fields round-trip through JSON, while delegates stay excluded. Panels own distinct initial settings and preserve assigned references or null. |
| Hierarchy | Public Add/Insert track logical parents, while hierarchy.parent retains physical containment. Reparent, direct hierarchy insertion and removal change the relationships. ListView's foldout holds its scroll view logically and physically through its content container; the footer's logical parent is that container, both before and after panel attachment. |
| JSON strings | JsonUtility and EditorJsonUtility stop a string at its first embedded NUL on write and read. Overwrite starts from different text; literal backslash-zero and Unicode remain distinct. This also fixes the recorder's empty-character strings without changing character values or its formatter. |

The [pointer API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.PointerEventBase_1.html)
describes radians and pen/touch inputs. The cache invalidation and intermediate
rounding above are release observations, not an added normalization guarantee.
The [visual tree Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/UIE-VisualTree.html)
and [parent API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.VisualElement-parent.html)
describe parent-child relationships. Controls distinguish logical ownership from
physical placement, including after reparenting. Style matching, percentage lengths
and focus traversal continue through physical parents.
[JsonUtility](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/JsonUtility.html)
supports serializable classes and structs; these string controls use wrapped fields
rather than unsupported root strings. Embedded-NUL transfer is a measured detail.
No persistent cache calls this JSON path; Undo and CopySerialized keep their state
in memory. Generated declarations use format 9 after implementing the added atlas
class and text interfaces.

Pointer angle and JSON string transfer select measured Compat mechanisms for
6000.0.71f1. Other releases keep their previous independent pointer values and
embedded-NUL handling, which remain unmeasured. Constructor defaults reuse the
existing initialization mechanism.

UIElements retains 100 entries. Forty-two are timestamps and 41 are registration
identifiers. Relational controls establish stable identifiers within a type and
different identifiers across types; the API does not promise their absolute
numbers. The 50 existing variable policies remain unchanged. The remaining
entries cover font-dependent cursor/selection geometry, real Image and IMGUI draw
callbacks, UXML schema metadata and trait initialization, TextCore fallback assets,
and specialized text asset initialization. Constructor and write controls rule out
fixing these through one font-height constant, empty draw callbacks or sorting
all attribute lists. Every remaining raw value stays in the queue.

| Release module | Queued rows |
| --- | ---: |
| UnityEngine.CoreModule | 36 |
| UnityEngine.UIElementsModule | 99 |
| UnityEditor.CoreModule | 87 |
| UnityEngine.IMGUIModule | 28 |
| UnityEngine.GridModule | 26 |
| UnityEngine.AnimationModule | 20 |
| UnityEngine.TerrainModule | 13 |
| UnityEngine.ParticleSystemModule | 5 |
| UnityEngine.TilemapModule | 3 |
| UnityEngine.VideoModule | 9 |
| UnityEngine.TextCoreTextEngineModule | 8 |
| UnityEngine.GameCenterModule | 7 |
| UnityEngine.SpriteShapeModule | 2 |
| UnityEngine.UnityWebRequestModule | 2 |
| UnityEngine.AIModule | 5 |
| UnityEngine.Physics2DModule | 5 |
| UnityEngine.PhysicsModule | 5 |
| UnityEngine.AudioModule | 4 |
| UnityEngine.UIModule | 3 |
| UnityEngine.UnityWebRequestTextureModule | 3 |
| UnityEngine.TerrainPhysicsModule | 2 |
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

The Q410 UIElements declaration slice adds EventInterestAttribute and
UxmlObjectAttribute to the handwritten inventory. Two independent 6000.0.71f1 GPU
Editor captures confirm the three added rows. EventInterestAttribute has no public
parameterless constructor; UxmlObjectAttribute constructs successfully and exposes
its attribute type ID. All 11,565 prior raw reference rows remain byte-identical.

Q410 visibility alignment removes seven exact migration entries. Five rows were
implementation-only public members that are internal in the selected release;
two nested FaceInfo records now match after the same narrowing. Six remaining
raw observations retain updated exact implementation values and unchanged Native
expectations. No variable policy was widened. The strict comparison retains
561 queued rows and rejects every new or stale entry.

The Hash128 slice (Q433) adds HashUtilities and HashUnsafeUtilities to the handwritten
inventory as static rows. Two independent 6000.0.71f1 GPU Editor captures agree on both
`@factory` rows (excluded, static); they were inserted into the reference beside the rows
the captures place them after, and every prior row is byte-identical.
