# Mesh data that scripts can and cannot read

What scripts read from meshes in Play mode, readable and not: imported models with and without
Read/Write Enabled and a scripted mesh made no longer readable with `UploadMeshData(true)`.
Unity 6000.0's Mesh.isReadable
(https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh-isReadable.html): without
Read/Write the mesh's data is not addressable from the CPU; the import setting decides it.

`make_models.py` (Blender) writes `Layers.fbx`, a grid of 2x2 quads with eight UV maps (map i
holds the vertex's grid position scaled and offset by i) and byte vertex colors per face corner,
and `Plain.fbx`, the grid with one UV map and no colors. `LayersLocked.fbx` is a copy of
`Layers.fbx`. `ProbeRunner.Create` (batch mode, once; its metas are committed) imports `Layers`
with Read/Write Enabled and `LayersLocked` and `Plain` without.

`MeshReadableProbe` records for each mesh its readability, counts, bounds, vertex layout
(`GetVertexAttributes`, formats, dimensions, streams, offsets, strides) and sub-mesh, then every
read (arrays, `Get*` list readers of every UV channel, indices, `AcquireReadOnlyMeshData`) with
the count and first and last element it returns, what it throws and what it logs, and whether a
MeshCollider made of the mesh is hit by a ray. Then the engine's other uses of meshes that are not
readable: `CombineMeshes` from one and into one, `SkinnedMeshRenderer.BakeMesh` of one, setting
`vertices` on one and `CanvasRenderer.SetMesh` with one; then each write (every channel setter,
the index setters, the Recalculate methods, `Clear`) on a fresh copy of a scripted and an imported
mesh scripts cannot read, and, as the control, of the scripted mesh while it is readable, with the
copy's state after it (`write.*`). `MeshReadableEditMode` reads the imported mesh outside Play
mode, as the editor domain loads (`editMode.*`).

Measured in the Unity 6000.0.71f1 Editor (Linux, two identical captures):

- The importer keeps all eight UV maps (TexCoord0 to TexCoord7) and the colors as bytes
  (`Color: UNorm8 x4`, one stream of 108 bytes a vertex).
- A mesh that is not readable keeps its layout, counts, bounds and sub-meshes; every array getter
  and every `Get*` list reader of vertex data logs "Not allowed to access <what> on mesh
  '<name>' (isReadable is false; Read/Write must be enabled in import settings)" (`uv`, `uv2` ...
  `uv8`, `colors` for both color getters, `triangles/indices` for the index getters), also for a
  channel the mesh does not have, and returns nothing; `GetTriangles(List<int>, …)` and
  `GetIndices(List<int>, …)` return the indices without an error; `AcquireReadOnlyMeshData`
  throws InvalidOperationException; a MeshCollider of it collides. `UploadMeshData(true)` does the
  same to a scripted mesh.
- Every channel and index setter logs the read error of its channel (`SetUVs(4, …)`: `uv5`) and
  leaves the mesh as it was; the Recalculate methods log "Not allowed to call
  RecalculateNormals() on mesh '<name>'" (and the same for the others); `Clear` empties it,
  keeping its bounds and layout, as for a readable mesh.
- Outside Play mode the same reads and `CombineMeshes` work without an error (Mesh.isReadable:
  "Access is always allowed in the Unity Editor outside of the game and rendering loop").
- `CombineMeshes` leaves such a mesh out with the warning "Cannot combine mesh that does not allow
  access: <name>" and refuses to combine into one ("Cannot combine into mesh that does not allow
  access: <name>", an error); `BakeMesh` bakes it into a readable mesh; setting its `vertices`
  logs the read error; `CanvasRenderer.SetMesh` refuses it ("Mesh '<name>' is not readable and can
  not be used. The CanvasRenderer requires read access to the mesh.").

Unbound matches every value but two (`residuals.json`): `SetVertices` keeps a readable mesh's
bounds in the Editor, and RecalculateTangents of a mesh without normals and uvs adds a tangent
channel there. Unmeasured: Edit mode's [ExecuteAlways] updates (allowed); other model formats than FBX; colors that are not whole
bytes (rounded to the nearest byte); bone weights, bind poses and blend shapes of meshes that are
not readable, `SetVertexBufferData`/`SetIndexBufferData` and Optimize on them (Unbound does not
refuse those); serialized
mesh assets (Unbound refuses them alike).

    blender -b --factory-startup --python tests/projects/meshreadable/make_models.py   # once
    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -projectPath <copy> \
        -executeMethod ProbeRunner.Create          # once; copy the metas back
    python3 tests/projects/meshreadable/run.py record --label A    # and B; then publish A B
    python3 tests/projects/meshreadable/run.py gate
