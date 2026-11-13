# Attribute inheritance controls

The Unity 6000.0.71f1 GPU Editor reference has 215 records from two independent, byte-identical
captures on the NVIDIA driver. The controls distinguish CLR attribute inheritance from Editor
registration and from each registration's explicit child-target flag. These are measured answers
for this release and context; they do not establish a contract for other releases.

From the repository root, select the release explicitly:

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/attributeinheritance/run.py native --label native1
python3 tests/projects/attributeinheritance/run.py native --label native2
python3 tests/projects/attributeinheritance/run.py publish native1 native2
python3 tests/projects/attributeinheritance/run.py check
```

The gate uses only the committed reference. Every run copies the project, runs through the shared
job pool and keeps output under `$XDG_CACHE_HOME/unbound/validation`. Comparison is exact, lists
all missing, extra and different keys, and rejects duplicate keys or an incomplete capture.

## Controls and cause table

| Question | Documented contract or scope | Measured control |
| --- | --- | --- |
| CLR inheritance versus registration | AttributeUsage describes reflection inheritance; it does not select which declarations an Editor registry indexes. | Inheritable and noninheritable custom markers on a base and child; a subclassed attribute is recognized on its marked target, without promoting unmarked target subclasses. TypeCache type/method/field results are compared separately. |
| TypeCache | [GetTypesWithAttribute](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/TypeCache.GetTypesWithAttribute.html) includes derived attribute types. Its result is not specified as a sorted collection. | Queries use membership, never order. Base annotations are indexed; an unmarked target subclass and an overriding method are absent. |
| CustomEditor | [editorForChildClasses](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CustomEditor-ctor.html) controls whether the editor handles derived target classes. | A base target is handled, a child is omitted with false and handled with true. An unmarked editor subclass is not registered. |
| Multiple targets | [CanEditMultipleObjects](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CanEditMultipleObjects.html) marks an editor for multiple objects. | The attribute's CLR lookup inherits, but registration requires the marker on the chosen editor class. Two distinct targets make the result differ. |
| Drawers | [useForChildren](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CustomPropertyDrawer-ctor.html) controls derived target types. | Public GetPropertyHeight returns distinct custom heights for base/child targets. The unmarked drawer subclass has no inherited CustomPropertyDrawer annotation. |
| Initialization and menus | [InitializeOnLoad](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/InitializeOnLoadAttribute.html) runs marked static constructors; callback and menu registration is checked independently of general CLR reflection. | A marked base runs once, an unmarked derived constructor does not. Load, enter-play, reload, scene, runtime and menu controls distinguish the marked static method from an unmarked hiding method. |
| Fields | A reflected inherited field still identifies its original declaration; fields do not override annotations. | Thirteen field attribute kinds are read with both flags through an inherited field, then compared with an unmarked shadow field. TypeCache declaration identity and inherited ReflectedType membership remain separate records. |
| Required/unique components | [DisallowMultipleComponent](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/DisallowMultipleComponent.html) covers subclasses; the declaration itself has Inherited=false. | Explicit base traversal prevents a second component. Both the base's required BoxCollider and the child's required SphereCollider are present. |
| UXML | [UxmlAttribute](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.UxmlAttributeAttribute.html) exposes element members. | Inherited nonvirtual members retain text and a constrained BoxCollider type reference. The release's source generator rejects virtual UxmlAttribute properties with UITKSG008, so overrides are outside this supported context. |
| Image stage | [ImageEffectOpaque](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ImageEffectOpaque.html) moves an image effect before transparent rendering. | Camera.Render records image-before-post for the marked base and inherited method; an unmarked override records post-before-image. No pixel tolerance or unspecified collection order is involved. |
| Former field names | [FormerlySerializedAs](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Serialization.FormerlySerializedAsAttribute.html) preserves serialized field data after a rename. JSON behavior is measured separately. | A prefab's old field name restores 29 on a protected inherited field. Plain/inherited/nested JSON uses current names only. Overwrite controls begin at 31, test the old name, then prove the current name changes it to 47. |
| Editor JSON envelopes | [EditorJsonUtility](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorJsonUtility.html) handles engine objects as well as managed data. Envelope selection is a measured detail. | Engine objects use their matching class wrapper, including a root with extra keys. Bare/wrong wrappers leave fields alone but still call OnAfterDeserialize once. Plain managed objects use bare JSON. |

`managed/UnityEngine/ApiCheckAllowList.json` holds the exact lookup policies justified by these
controls. They permit direct declaration discovery where a blanket AttributeUsage comparison
would otherwise reject it. A new or changed lookup does not inherit an old exception.

## Conditional declarations

The 6000.0.71f1 assembly declarations include conditional profiler, collection-validation
and Editor-only calls. The probe records each affected method's conditional metadata.
A source file explicitly undefines `ENABLE_PROFILER` and `UNITY_EDITOR`: ten calls must
skip argument side effects, including calls through a null CommandBuffer receiver.
The CommandBuffer string overloads remain unconditional and evaluate two arguments.
A separate file defines `ENABLE_PROFILER` and verifies one argument evaluation.

Unity 6000.0 documents [frame metadata](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Profiling.Profiler.EmitFrameMetaData.html)
as profiler capture data and [CommandBuffer samples](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.BeginSample.html)
as profiling commands. Exact conditional names and overload selection come from the
6000.0.71f1 release assemblies; the paired captures check compilation and reflection.

The conditional expansion adds 23 rows from two identical GPU Editor captures,
bringing the canonical reference to 238 rows. All 215 earlier reference rows remain
byte-identical; the comparator still rejects every missing, extra or changed value.

## Obsolete enum declarations

The enum slice records 190 obsolete declarations from Unity 6000.0.71f1. Each row
contains the constructor arity and typed, length-prefixed arguments, including the
message and error flag. Enum and field names are stored as strings for reflection.
The stub generator's attribute renderer supplies the source annotations.

API tooling tests compile consumer code against a metadata copy of the engine.
`BuildTarget.MetroPlayer` and the `BlendWeights` type must produce errors,
`BuildTarget.BB10` must produce the release's warning and message, and
`RuntimePlatform.WindowsPlayer` must compile without diagnostics.

[ObsoleteAttribute](https://learn.microsoft.com/en-us/dotnet/api/system.obsoleteattribute?view=net-10.0)
distinguishes warnings from errors. Unity 6000.0's
[API updater](https://docs.unity3d.com/6000.0/Documentation/Manual/APIUpdater.html)
handles only diagnostics marked UnityUpgradable; the consumer controls check the
compiler diagnostics before any updater processing.

Two identical GPU Editor captures extend the canonical reference from 238 to 428
rows. Every earlier row remains byte-identical. The exact comparator covers all
190 added obsolete declarations as well as the earlier controls.

## UIElements declaration controls

The UIElements expansion adds 68 rows, bringing the canonical reference from 428 to
496. Two independent GPU Editor captures match, and all 428 earlier rows remain
byte-identical. Metadata controls cover field annotations, private setters, final
overrides, abstract methods, serializable flags and the optional panel dispatcher.
Event-interest constructor controls cover array identity, null, explicit flags and
AttributeUsage. A declared geometry event reaches an override attached to a panel.

Unity 6000.0's [SerializeField rules](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SerializeField.html)
require marked private fields to participate in serialization. Sort-column JSON
overwrite begins with a different name and changes the name, index and direction.
An angle's unmarked private fields remain excluded. A plain-data control changes
one field while retaining another omitted from the JSON, as documented by
[FromJsonOverwrite](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/JsonUtility.FromJsonOverwrite.html).
The [EventInterest contract](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.EventInterestAttribute.html)
guarantees listed events reach the method but permits unlisted events too; the
probe makes no assertion that an unlisted event is filtered.

## Member obsolete declarations

The member expansion records 299 exact type, property, method and accessor
annotations from Unity 6000.0.71f1 and 26 TextEditor state controls, bringing the
reference from 496 to 821 rows.
Two independent GPU Editor captures match. Every earlier raw row is unchanged.
Overloads are selected by their complete signatures, including generic and
reference parameter types. The shared formatter records constructor identity,
message and error flag.

Compiler controls use the engine assembly as metadata. They distinguish a removed
Cloth property from an error-free component overload, verify the exact deprecated
string-overload message, and check that the XR enabled setter warns while its
getter remains supported. Existing enum compilation controls share the same
metadata helper. No reference engine assembly is executed by these tooling tests.

The TextEditor controls put legacy fields and replacement properties into different
states. Legacy writes leave the new properties unchanged, and changing the new
properties leaves legacy storage unchanged. Focus controls start with a partial
selection in both text modes; the measured release selects all and reveals the
cursor. Moving the cursor alone does not reveal it, and losing focus does not
clear the reveal state. These focus answers are release measurements. Other
releases retain the previous unmeasured field linkage through the compatibility
profile, pending equivalent controls for those releases.

## Parameter declarations

The parameter expansion adds 73 metadata rows and seven invocation controls,
bringing the reference from 821 to 901 rows. Two GPU Editor captures match and
every earlier raw row remains unchanged. Each parameter records its direction
flags, optional/default presence and typed raw default value. Overload lookup uses
the shared signature helper.

Missing arguments invoke full mip-chain defaults for odd 2D and 3D dimensions;
an explicit partial mip count provides a control. A render-target constructor
with omitted arguments equals its explicit-default form. The four-argument mesh
color overload rejects a missing required flags argument, while the existing
three-argument overload still writes two distinct colors. Compiler controls cover
named render-target constructor arguments and the array clear overload with
omitted depth/stencil values. These are 6000.0.71f1 declaration and invocation
observations, without new rendering or mesh algorithm rules.

The Unity 6000.0 [mip-chain documentation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Experimental.Rendering.GraphicsFormatUtility.ComputeMipChainSize.html)
defines -1 as the full chain; the 3D partial-count control changes the result.
The [clear-target documentation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.ClearRenderTarget.html)
lists depth 1 and stencil 0, checked here as typed parameter defaults and by
compiler omission controls. No framebuffer clearing behavior changes in this slice.
[Mesh.SetColors](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.SetColors.html)
lists flags as required on the four-argument overload. Its existing three-argument
overload is tested separately. Direction flags are selected-release metadata
observations; these controls make no additional marshaling guarantee.

## Editor declaration shapes

The Editor expansion adds 33 metadata rows, three compilation-data setter
controls and twelve serialization controls, taking 901 rows to 949. Paired NVIDIA GPU Editor captures match and
all 901 prior raw rows remain unchanged. Type flags, getter readonly metadata
and private/absent setters come from the 6000.0.71f1 assemblies. A reflected
private setter changes an assembly name from before to after; outputPath remains
before.dll. These are declaration and reflection observations. They make no
new contract for compilation lifecycle, focus timing or wrapper memory layout.

Metadata-only compiler tests reject deriving from AssetDatabase and accept
deriving from PlayerSettings.Android. Engine tests retain PackageInfo factory
fallbacks, array identity, nulls and independent DependencyInfo copies.

The Unity 6000.0 [AssetDatabase declaration](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetDatabase.html)
is sealed; the derivation compiler control distinguishes this guarantee.
[Assembly.name](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Compilation.Assembly-name.html)
is publicly get-only. Its private setter is a selected-release reflection
observation, not a public mutation API. [PackageInfo](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PackageManager.PackageInfo.html)
describes package metadata; the engine factory controls preserve its existing
values without claiming new package-resolution behavior.

DependencyInfo serializes its selected private name/version fields. A boxed
overwrite changes the name, retains the omitted version and leaves the original
struct copy unchanged. Header controls place a nonserialized maximum at 9,
attempt to overwrite it with 17 and change a serialized column list at the same
time. The maximum stays 9 while the list changes. Field flags and the missing
SerializeField annotation are recorded separately. Nondefault GUID and
GlobalObjectId values retain their measured empty JSON objects.

Five larger Serializable flags remain queued with their payload migrations.
Their current package/request/cache properties do not represent the selected
serialized fields; those flags are kept strict in api-check.

The Unity 6000.0 [SerializeField contract](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SerializeField.html)
covers private-field serialization. [FromJsonOverwrite](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/JsonUtility.FromJsonOverwrite.html)
leaves omitted fields unchanged. Its struct limitation requires explicit boxing
and unboxing here; the separate original value checks that the overwrite changed
the boxed value. These controls use supported primitive fields. They do not
extend JSON support to other engine objects or unsupported field types.

## Runtime modifiers

The runtime expansion adds 47 metadata and eight value controls, taking 949 rows
to 1,004. The selected flags cover readonly getters, type serialization markers,
sealed classes and the virtual texture format getter. Existing property values
remain in the same storage order. Struct-copy controls change the original after
a copy; separate controls retain NaN and positive infinity. Serialization controls
cover existing public clip/texture fields and structs with no serialized fields.
The probe explicitly enables the built-in Physics 2D module for these controls.

[Texture.graphicsFormat](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture-graphicsFormat.html)
is virtual in the Unity 6000.0 API; the metadata-only compiler control overrides
it. [JointAngleLimits2D](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/JointAngleLimits2D.html)
is a value type with angular limits. Copy and nonfinite answers here are measured
value-storage observations, without a new physics-solver guarantee.

Three serialization-marker declarations remain queued as
Q410-Core-built-in-serialization. Their exploratory controls expose special
nested harmonic/layer-mask payloads. The strict reports preserve those differences
and the loss caused by removing markers without adapting the serializer. Existing
markers stay in place until the built-in JSON/YAML and cloning migration lands.
These cases do not claim serialization parity in the accepted modifier capture.

## Visibility

The visibility expansion adds 28 metadata rows, taking 1,004 rows to 1,032.
Eighteen type/member visibility fixes also correct ten getter/setter visibility
flags. These declarations come from the 6000.0.71f1 assemblies. The controls
include the internal FontEngine constructor and the protected GUI getter.

Compiler controls reject an internal SystemInfo helper, accept the public texture
limit and allow derived code to read the protected getter. Existing selected
module assembly friendships remain unchanged. No getter values, helper bodies,
physics behavior or font loading rules change in this slice.

The accessor controls add 27 selected setter declarations and seven runtime values,
bringing the reference to 1,066 rows. A reflected private setter changes one boxed
NavMesh filter while its original copy and agent ID remain unchanged. A resource
path setter preserves the supplied array identity and its independent field marker.
The selected release exposes the public getters described by
[Texture.updateCount](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture-updateCount.html)
and [Camera.current](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Camera-current.html).
This slice changes declaration metadata and preserves existing mutation timing.

The user annotation controls expand the reference from 1,066 to 1,193 rows.
They record 74 selected annotations, eight enum formatting controls at six values
each, and five component-requirement values. The Rigidbody dependency begins absent,
then exists after AddComponent; a second joint reuses it. The
[6000.0 RequireComponent contract](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RequireComponent.html)
checks missing dependencies when AddComponent runs, without retroactively repairing
existing components. These controls exercise that call with a missing dependency.

The serialization annotation controls bring the reference to 1,248 rows, preserving
all earlier 1,193 rows. They cover 37 declarations and 18 runtime values, including
negative random words, boxed overwrite, nested scene handles, converter key direction
and ScriptableObject clone state. The
[6000.0 SerializeField contract](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SerializeField.html)
includes eligible private fields; the
[FromJsonOverwrite contract](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/JsonUtility.FromJsonOverwrite.html)
preserves omitted fields. The controls change a private word and the holder control
while independent state remains unchanged.

ObjectIdentifier retains its flag exception. Its JSON payload is empty, but cloning
retains the target field initializer, 29, instead of the source value, 17. The clone
control guards against enabling the flag while the generic cloner still copies 17.
The full uncommitted trial captures remain outside Git; comparator rules and every
landed reference row remain unchanged.
