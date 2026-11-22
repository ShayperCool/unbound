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
