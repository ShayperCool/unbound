# Serialized collider bindings

Two independent Unity 6000.0.71f1 Editor captures on the existing NVIDIA display
agree on 155 values. The probe checks seven shared Collider fields and BoxCollider
size/center through SerializedObject, with sphere and capsule controls for the
inherited fields. It tests material assignment, layer masks, priority, trigger,
contact reporting and enabled state. Physics queries distinguish the original,
disabled, trigger and moved-geometry states.

The [6000.0 SerializedObject documentation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SerializedObject.html)
says edits are buffered until ApplyModifiedProperties, Update refreshes an existing
stream, and assignments affect all selected targets. Each guarantee has a divergent
control here. Before Apply, live values retain their old state; after a direct API
change the buffered view stays stale until Update. A box and sphere begin with
different trigger values, then receive the same serialized assignment. The
[SerializedProperty documentation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SerializedProperty.html)
describes the typed values and iteration API. The capture records field types and
also writes the m_Bits child of a LayerMask across both targets. A separate
ScriptableObject control assigns the first target's current scalar value to
divergent targets, edits vector and nested Bounds components while preserving
the other target's unedited components, and combines whole-value and component
writes. These use the same buffered write and apply rule as collider fields.

Bindings use the existing engine properties and their change notifications. The
same definitions are used by SerializedObject and the existing EditorJsonUtility
engine-object writer. No placeholder field values or parallel serializer are added.
The material reference type name follows Compat: Unity 6 documents
[PhysicsMaterial](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/PhysicsMaterial.html),
while 2022.3 documents
[PhysicMaterial](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/PhysicMaterial.html).
Only the 6000.0.71f1 serialized schema has an Editor capture here.

This closes the BoxCollider property-list omissions in editorcallbacks. GameObject
and Transform metadata and component/child arrays remain separate gaps. Authored
icon/navigation/static flags and scale/Euler hints need preserved state; live
component and child arrays need structural bindings. Other collider-specific
geometry, cross-object reference persistence and invalid-value validation remain
unmeasured. The API documentation warns that serialized writes bypass property
setter validation; the present controls use valid values.

```sh
python3 tests/projects/serializedbindings/run.py editor --release 6000.0.71f1 --label fresh
python3 tests/projects/serializedbindings/run.py publish --release 6000.0.71f1 --label fresh
UNBOUND_CLI=/immutable/runtime/unbound python3 tests/projects/serializedbindings/run.py check --release 6000.0.71f1
```

Publication requires two complete, identical GPU captures. The gate uses the
committed reference and retains every missing, extra or different value.
