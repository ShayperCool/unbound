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
