# Runtime component allocation

Unity 6000.0's Profiler API describes the bytes as native memory attributed to the target object.
The Unity 6000.0.71f1 Editor records 288 bytes for a BoxCollider, 120 for a MeshFilter and 456
for a MonoBehaviour. The runs use attached runtime components. The original Linux appfacts
reference also gives those three initial sizes. [Profiler API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Profiling.Profiler.GetRuntimeMemorySizeLong.html)

The BoxCollider cost persists through resizing, physics material assignment, trigger state, an
attached rigidbody and disabling. The MeshFilter cost persists through a shared mesh reference,
a private mesh copy and clearing the reference. Two MonoBehaviour classes have the same cost
with different managed data and after deactivation. A clone retains these per-component costs.

Other component types, persistent prefab components, unattached wrappers and release families
other than 6000.0 are outside the measured scope.

```sh
python3 tests/projects/componentmemory/run.py record --release 6000.0.71f1 --label first
python3 tests/projects/componentmemory/run.py record --release 6000.0.71f1 --label second
python3 tests/projects/componentmemory/run.py publish --release 6000.0.71f1 first second
python3 tests/projects/componentmemory/run.py gate --release 6000.0.71f1
```

Publishing requires two independently sealed, identical Editor repeats. Gate runs use private
project copies and compare every recorded value exactly.

## Light command buffer allocation

The separate light profile records an attached Light using the Built-In Render Pipeline. Its base
cost is 1176 bytes. Each light event starts its command buffer list with one allocated slot and
doubles on growth. Each reserved slot adds 12 bytes. Editing or sharing a CommandBuffer leaves
other object costs separate. Removing a buffer removes all its occurrences at that event and
retains the allocation. Removing an entire event releases that event's storage immediately.
A cloned Light begins with empty command buffer lists. The exact allocation sizes come from
the captures. The Scripting API describes duplicate registrations and their removal. [AddCommandBuffer](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Light.AddCommandBuffer.html), [RemoveCommandBuffer](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Light.RemoveCommandBuffer.html)

Add `--profile light` to the commands above to record, publish or check this profile. Light memory
with a scriptable render pipeline and other release families remains unmeasured.

## AudioSource distance curve allocation

The audio profile records attached AudioSources with script-created and scene-authored distances.
Each measurement stores the key count followed by the reported bytes before and after retrieving
that curve. The profiler accounts 1128 bytes plus 32 per reserved key. A new component has
five default keys across its four curves, for 1288 bytes. Script writes retain each curve's
reservation when it shrinks or a scalar setter changes it to one key. A clone reserves its
current keys, and a loaded scene source begins with its authored keys. Empty scene curves use
the default keys. A clone or a loaded source with four single-key curves costs 1256 bytes.
AudioClip storage is separate. Persistent asset components, other release families and copying
built-in settings through EditorUtility.CopySerialized remain outside this profile.

Unity 6000.0 documents distance normalization and the single key returned after a corresponding
scalar setter. The 6000.0.71f1 measurements capture the empty-curve error and null-curve
exception with a populated curve before the call, the two log delivery paths, and validation
order for unknown curve types, destroyed sources and unattached wrappers. [SetCustomCurve](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.SetCustomCurve.html), [GetCustomCurve](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.GetCustomCurve.html)

The keyframe controls record positions, values, tangents, weights and wrap modes around each
rejected write. The Error stack setting includes a stack in ScriptOnly and omits it in None.
Full native stacks remain unmeasured. [Unity 6000.0 stack trace logging](https://docs.unity3d.com/6000.0/Documentation/Manual/stack-trace.html)

Add `--profile audio` to the record, publish and gate commands above.

## MeshRenderer material reservations

The renderer profile records live attached MeshRenderers in the Built-In Render Pipeline and
after a camera request renders through the active scriptable pipeline. A renderer accounts 656
bytes plus four bytes for each reserved material slot. Property block storage for individual slots starts on the first non-null indexed
write, including an empty block, and adds eight bytes per reserved slot. Both reservations retain
capacity when a material list shrinks. Indexed storage grows with the material list while
active. Clearing its last assigned block or setting zero materials ends that growth until the
next indexed assignment. Trimming removed indices keeps the storage active if slots remain.
A clone of a runtime renderer starts with its current materials and empty property blocks. The
controls include synchronous copies of a hidden detached object and asynchronous copies parented
under both a scene object and a prefab asset. Detached runtime renderers continue to account for
material assignments and discard trimmed overrides while their scene is invalid.
Renderer-wide blocks and the referenced mesh, materials and textures have separate costs.

Scene-authored empty and nonempty lists, list and array writes, rendering, clones, renderer-wide
and indexed overrides, and out-of-range diagnostic paths have separate controls. The Unity 6000.0
API declares valid material indices from zero through the current material count minus one and
allows null to clear an override. The indexed getter overwrites a prefilled block even when the
stored block is empty. [Renderer.SetPropertyBlock](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Renderer.SetPropertyBlock.html),
[Renderer.GetPropertyBlock](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Renderer.GetPropertyBlock.html)

Out-of-range writes report an Error through the application callback, honoring None and ScriptOnly
for stack presence. Clone behavior during importer callbacks and copies of asset components are
unmeasured. Those copies retain the earlier behavior. Persistent component accounting, other
release families, invalid indexed reads and empty-block rendering precedence are also outside
these controls.

Add `--profile renderer` to the record, publish and gate commands above.
