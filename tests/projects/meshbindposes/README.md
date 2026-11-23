# Mesh bind poses

The Unity 6000.0 Scripting API assigns each bind pose to the corresponding index in the
renderer’s bone array. The probe compares distinct matrices through the native array, managed
array and list APIs. It also bakes a triangle using two moved bones so swapping the assigned
poses changes the vertex positions. [GetBindposes](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.GetBindposes.html) ·
[SetBindposes](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh.SetBindposes.html) ·
[Skinned Mesh Renderer](https://docs.unity3d.com/6000.0/Documentation/Manual/class-SkinnedMeshRenderer.html)

Unity 6000.0.71f1 returns a read-only borrowed view. A write that fits the existing storage is
visible to earlier views of that storage. Disposing a view leaves the mesh and other views
usable. The capture checks a fresh mesh, mesh cloning, invalid inputs, and the behavior of the
bind-pose APIs after clearing the geometry or making it unreadable. Unbound invalidates a
borrowed view when its backing storage is replaced or its mesh is destroyed or collected. When
the source is a slice of the mesh's own storage, Unity 6000.0.71f1 logs an assertion and reads the
slice after the log callback. The probe changes the slice's matrices during that callback.

```sh
python3 tests/projects/meshbindposes/run.py record --release 6000.0.71f1 --label repeat-a
python3 tests/projects/meshbindposes/run.py record --release 6000.0.71f1 --label repeat-b
python3 tests/projects/meshbindposes/run.py publish --release 6000.0.71f1 repeat-a repeat-b
python3 tests/projects/meshbindposes/run.py gate --release 6000.0.71f1
```
