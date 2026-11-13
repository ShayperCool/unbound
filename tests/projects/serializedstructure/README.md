# Serialized GameObject and Transform state

Two independent Unity 6000.0.71f1 Editor runs on the NVIDIA GPU agree on all 96
values in the reference. The probe records GameObject metadata, Transform scale
lock and Euler hints, component slots, child order and cloning.

The [6000.0 SerializedObject documentation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SerializedObject.html)
describes buffered edits and ApplyModifiedProperties. The component-reference and
child-order controls read live state before Apply, then observe the changed state
after Apply. The static-flag control changes the public property while retaining
an existing SerializedObject, then uses Update to refresh its stale view.
[StaticEditorFlags](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/StaticEditorFlags.html)
select editor precomputations; changing the flags at runtime does not rebuild those
systems. This change preserves their raw values; it does not run a new precompute.

The captures distinguish these rules:

- Icon, navigation layer and raw static flags retain serialized writes. Setting
  isStatic false clears the flags; true sets the positive 31 bits.
- The Euler hint is separate from the rotation. Writing the hint leaves the
  quaternion unchanged, and assigning localEulerAngles leaves the hint unchanged.
- Scale lock is stored without changing runtime localScale assignments. Runtime
  cloning resets scale lock and preserves the Euler hint.
- Component slots cannot be resized or reordered through SerializedProperty.
  Replacing a slot's reference is allowed and changes GetComponents. The removed
  component survives destruction of the GameObject whose list no longer names it.
- Reordering existing children changes GetChild order without reparenting or
  sending OnTransformChildrenChanged.

Native property definitions share the existing buffered serializer. Metadata is
read from authored scene/prefab data and retained in clones and JSON values.
Component lists are read from live slots, and child lists from the live hierarchy.
The focused managed tests include three children and authored nondefault metadata.

The measured structural write scope is explicit. Component replacements support
live components of the same GameObject, retaining the size and first Transform.
Child writes support permutations of the existing children. Null or foreign
component references, changing the first Transform, and child membership or
ownership changes through serialized arrays report an unsupported-operation
error. Their ownership, lifetime and invalid-data rules need separate controls.
Multi-target structural writes and JSON hierarchy-link writes remain unmeasured.
Other Unity releases have no reference for this schema here.

```sh
python3 tests/projects/serializedstructure/run.py editor --release 6000.0.71f1 --label fresh
python3 tests/projects/serializedstructure/run.py publish --release 6000.0.71f1 --label fresh
UNBOUND_CLI=/immutable/runtime/unbound python3 tests/projects/serializedstructure/run.py check --release 6000.0.71f1
```

The runner publishes only complete, equal GPU repeats. Existing editorcallbacks
references and comparator tolerances stay unchanged.
