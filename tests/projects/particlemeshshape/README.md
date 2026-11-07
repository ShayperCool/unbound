# particlemeshshape

Where the Shape module's Mesh, MeshRenderer and SkinnedMeshRenderer shapes emit in Unity
6000.0.71f1, and what a system built with `AddComponent<ParticleSystem>()` has before it is
activated. `Assets/Scripts/MeshShapeProbe.cs` builds every system in code (local space, constant
start values, start speed 1 so the velocity is the emission direction, seed 1234 unless stated)
and records, per particle, position, velocity, `rotation3D` and the seed's halves after
`Simulate` calls with fixed steps, and the start colours (bytes) in a `/color` record of their own.
Meshes: three triangles of different areas in two
submeshes with tilted normals, vertex colours and an unused vertex (`tris`); a 3x3 grid with a
zero-area triangle and a coincident vertex (`grid`); lines, points, an empty mesh, no mesh, and a
mesh made non-readable with `UploadMeshData(true)`.

Scenarios, each for Vertex, Edge and Triangle: `random/*` (a burst, a rate over steps, `Emit`, the
grid, another seed), `loop/*` and `pingpong/*` (speeds 1, 0.7 and 0, spread 0.25, the unused vertex,
Single Material), `material/*` (Single Material submesh 1, 0 and 5), `nocolors`, `normaloffset`,
`nonormals`, `align` (also a sphere and a cone as controls), `transform` (shape position, rotation,
scale), `modifiers` (random direction and position), meshes without triangles, a MeshRenderer moved,
turned and scaled away from the system in local and world space (two materials, also with Single
Material), a SkinnedMeshRenderer at rest, with a bone moved and turned, and with a blend shape at
half weight. `messages/*` records which warnings each scenario logged. `component/*` records the
renderer right after `AddComponent<ParticleSystem>()` on an active and an inactive object.

    python3 tests/projects/particlemeshshape/run.py native --label native1   # Editor capture
    python3 tests/projects/particlemeshshape/run.py publish native1 native2  # two identical captures
    python3 tests/projects/particlemeshshape/run.py check                    # Unbound run + compare (the gate)

The reference is `Reference/unity-6000.0.71f1.jsonl` (484 records; two independent GPU Editor
captures were byte-identical). The comparator is `particletrails/compare.py` at the particle
probes' bound, with the differences in `known.json`.

## Documented contract (Unity 6000.0)

| Source | What it says |
| --- | --- |
| [ParticleSystem.ShapeModule](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.ShapeModule.html) | `meshShapeType`, `meshSpawnMode`/`Spread`/`Speed`, `useMeshMaterialIndex`/`meshMaterialIndex`, `useMeshColors` ("Modulate the particle colors with the vertex colors, or the material color if no vertex colors exist"), `normalOffset` ("Move particles away from the surface of the source mesh"), `alignToDirection`, `mesh`, `meshRenderer`, `skinnedMeshRenderer`. No sampling distribution or random sequence. |
| [ParticleSystemMeshShapeType](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystemMeshShapeType.html) | Vertex: "Emit from the vertices of the mesh"; Edge: "from the edges"; Triangle: "from the surface". |
| [ParticleSystemShapeType](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystemShapeType.html) | Mesh, MeshRenderer, SkinnedMeshRenderer emit from a mesh, a MeshRenderer, a SkinnedMeshRenderer. |

Everything below is measured in this release, not documented.

## Measured behaviour

| Records | 6000.0.71f1 |
| --- | --- |
| `random/triangle/*` | One shape draw picks a triangle by area; two more (u, v), folded into the triangle when u + v > 1, weigh its first two corners, the third gets 1 - u - v. |
| `random/edge/*` | One draw picks a triangle by area; three times its place in that triangle's share picks the edge (a→b, b→c, c→a) and the point along it. |
| `random/vertex/*` | Two draws taken as whole words: a triangle uniformly by count (zero-area ones included), then one of its corners. |
| `loop/*`, `pingpong/*` | Edge moves the area draw with the spawn speed and snaps it to the spread. Vertex walks the vertex buffer (unused vertices too, also with Single Material) one vertex per particle, whatever the speed and spread; Ping-Pong turns back at each end and emits from it twice. Triangle stays random, without a warning. |
| `nonormals/*`, `normaloffset/*` | The direction is the interpolated normal, normalized; +Z without normals. The normal offset moves along the interpolated normal as it is, before the shape's transform. |
| `random/*`, `material/*`, `meshrenderer/*` | Vertex colours blend with weights in 1/255 steps; without them a renderer's particle takes the colour of the material of the submesh it came from (Single Material: that slot). Single Material past the last submesh uses the last one. |
| `align/*` | The 3D rotation gains (-asin(d.y), atan2(d.x, -d.z), 0): the pitch and yaw that turn -Z to the direction. The same on a sphere and a cone. |
| `meshrenderer/*`, `skinned/*` | The renderer's mesh (a skinned renderer's current pose) scaled by the renderer, in the frame of its position and rotation: local simulation space keeps that frame's coordinates, world space places the particle with the renderer. Areas weigh the unscaled mesh, a skinned renderer's in its rest pose. |
| `lines/*`, `points/*`, `empty/*`, `nomesh/*`, `messages/*` | A mesh without triangles logs "Mesh used in Particle System Shape Module is not valid, possibly due to missing read/write flag"; Edge and Triangle on a surface without area (also no mesh) log "Particle System '<name>' is trying to spawn on a mesh with zero surface area"; the particles start at the shape's origin facing +Z. |
| `unreadable/*` | A mesh made non-readable at run time still emits in the Editor, without a warning. |
| `component/*` | `AddComponent<ParticleSystem>()` adds the ParticleSystemRenderer at once, also on an inactive object: enabled, Billboard, one empty material slot, no shadows cast or received, four vertex streams. |

## Known differences (`known.json`)

- `colour-blend` (31 `/color` records): alpha, and where the weights come from a loop-mode edge
  position any channel, can differ by 1/255. Recorded as an insignificant residual; positions,
  directions and rotations stay in the strictly compared records.
- `lines-topology` (5): Unity reads a lines mesh's index buffer as triangles; Unbound warns the
  same and emits at the origin, as Unity does for points.
- `spread-boundary` (8): two rate particles born exactly on a spread step snap to the step before
  in Unbound.

Unmeasured: shape texture clipping and colour (Unbound reports the texture as not simulated), meshes
mixing triangle and other submeshes, imported non-readable meshes, which submesh a Vertex walk's
material comes from, other releases.
