# bakemesh

`Physics.BakeMesh`, measured in the Unity 6000.0.71f1 Editor (`Assets/Scripts/BakeMeshProbe.cs`;
two byte-identical Linux batch Editor captures in `Reference/unity-6000.0.71f1/`). For each
case: what the call threw, what it logged, and whether a MeshCollider given the mesh afterwards
(convex as baked, the same cooking options) is hit by a ray; and `Texture2D.whiteTexture.name`.

* `plain`, `convex`, `options`, `nooptions`, `thread` (from a worker thread), `empty`,
  `emptyconvex`, `unreadable`, `unreadableconvex` (after `UploadMeshData(true)`): nothing thrown
  or logged; the colliders are hit but those of the empty meshes.
* `zero`, `notmesh` (the white texture), `notmeshnamed` (a named material), `destroyed`: a
  warning each, `Null mesh instance id is not allowed.`, `Resolved the instance id to a valid
  object, but that object is not a Mesh: ` (the object's name not appended),
  `Failed to resolve instance id: `.

Unbound cooks a mesh when a MeshCollider takes it, so BakeMesh checks the id and warns as the
Editor does. Values compare exactly.

    python3 tests/projects/bakemesh/run.py record --label L     # twice, then publish
    python3 tests/projects/bakemesh/run.py publish L1 L2
    UNBOUND=<binary> python3 tests/projects/bakemesh/run.py gate
