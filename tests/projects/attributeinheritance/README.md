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
