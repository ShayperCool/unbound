# RaycastHit coordinates probe

Unity 6000.0.71f1, Linux, PhysX. The 3203 reference records cover rays, Collider.Raycast,
RaycastAll/NonAlloc, sphere/box casts, multiple submeshes with base vertices, imported and
runtime meshes, both UV channels and missing-channel diagnostics, renderer lightmap transforms,
kinematic body hierarchies, hit setters, live UV/mesh replacement, primitive and convex hits.
A shuffled 37-triangle mesh checks cooked face remapping; its UV queries still work after
UploadMeshData(true). All 3203 records match, including diagnostics.
The reference also records the NullReferenceException from a default hit's lightmapCoord.

A ray carries its barycentric data into the hit. Shape casts leave it at the first vertex.
Unity's barycentric setter stores the first two supplied components, then the getter reconstructs
the first weight. UV2 falls back to UV0 with a warning. Lightmap scale/offset applies even with
lightmapIndex = -1. Coordinates are looked up from the collider's current mesh.

```bash
RAYHITS_PROBE_OUT=/tmp/rayhits-editor Unity -batchmode -nographics \
  -projectPath "$PWD/tests/projects/rayhits" -executeMethod ProbeRunner.Record \
  -logFile /tmp/rayhits-editor.log
RAYHITS_PROBE_OUT=/tmp/rayhits-unbound unbound run \
  -p tests/projects/rayhits --headless 30000 --no-watch
python3 tests/projects/rayhits/compare.py tests/projects/rayhits/Reference /tmp/rayhits-unbound
```

ProbeRunner.Create recreates the empty scene and serialized mesh through the Editor.
