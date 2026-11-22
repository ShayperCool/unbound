# Component storage in a script-created GameObject

Two independent Linux Unity 6000.0.71f1 Editor runs agree on all 27 values.
Two macOS Editor repeats report the same values.
An object starts with one component slot for its Transform. Adding components
doubles the capacity when it fills. Removing components leaves the allocation
in place. The profiler reports 240 bytes plus 24 bytes per slot.

Cloning an object reserves exactly its live component count. A clone with
three slots doubles to six when it gains a fourth component. The source
keeps its own allocation. The type-list constructor and RequireComponent
controls include the implicit Transform and any other components they add.
The dependency case reads the allocation from Awake. A second clone adds its
fourth component in Awake, and a ParticleSystem checks the implicit renderer's
slot. Publication uses Linux NVIDIA captures, with Metal runs available for
comparison.
Scope is script-created objects and their clones. Authored scene and prefab
lists and internal detached objects retain the prior accounting.

The Unity 6.0 [GameObject constructor](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GameObject-ctor.html)
and [Instantiate](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Object.Instantiate.html)
document which components become part of a new object. The storage growth
and reported byte counts are measured by this fixture.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/gameobjectmemory/run.py record --label first
python3 tests/projects/gameobjectmemory/run.py record --label second
python3 tests/projects/gameobjectmemory/run.py publish first second
python3 tests/projects/gameobjectmemory/run.py gate
```
