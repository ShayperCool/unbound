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

`texture/*` sets a shape texture (start speed 0, so the recorded position is the start position):
a 256×256 texture whose texel (x, y) is (x, y, 128, x) names the texel each particle read, for every
shape, larger, partial-arc, thick and transformed shapes, mesh UV channels 0, 1 and a missing one;
clip channels and thresholds over a burst, a rate and `Emit` (also `Emit(10)` from the start and
after five live particles, clipped and not); the colour and alpha switches and a
start colour; point and bilinear sampling of a 4×4 texture in Clamp and Repeat; a texture made
non-readable; the script API's bounds. `topology/*` emits from meshes of six line indices, lines
whose indices would make triangles with area, a line strip, quads, and a triangle submesh beside
a lines submesh (also its lines submesh alone). `spread/*` snaps Loop and Burst Spread to arc,
radius and mesh spawn spreads at rates that hit steps exactly (20) and never (7). `obsolete/*`
sets the obsolete shell shape types from script.

    python3 tests/projects/particlemeshshape/run.py native --label native1   # Editor capture
    python3 tests/projects/particlemeshshape/run.py publish native1 native2  # two identical captures
    python3 tests/projects/particlemeshshape/run.py check                    # Unbound run + compare (the gate)

The reference is `Reference/unity-6000.0.71f1.jsonl` (781 records; two independent GPU Editor
captures were byte-identical; the 484 earlier records are unchanged). The comparator is `particletrails/compare.py` at the particle
probes' bound, with the differences in `known.json`.

## Documented contract (Unity 6000.0)

| Source | What it says |
| --- | --- |
| [ParticleSystem.ShapeModule](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.ShapeModule.html) | `meshShapeType`, `meshSpawnMode`/`Spread`/`Speed`, `useMeshMaterialIndex`/`meshMaterialIndex`, `useMeshColors` ("Modulate the particle colors with the vertex colors, or the material color if no vertex colors exist"), `normalOffset` ("Move particles away from the surface of the source mesh"), `alignToDirection`, `mesh`, `meshRenderer`, `skinnedMeshRenderer`. No sampling distribution or random sequence. |
| [ParticleSystemMeshShapeType](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystemMeshShapeType.html) | Vertex: "Emit from the vertices of the mesh"; Edge: "from the edges"; Triangle: "from the surface". |
| [ParticleSystemShapeType](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystemShapeType.html) | Mesh, MeshRenderer, SkinnedMeshRenderer emit from a mesh, a MeshRenderer, a SkinnedMeshRenderer. |
| [ParticleSystem.ShapeModule](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ParticleSystem.ShapeModule.html) texture properties | `texture` "tint[s] the particle's start colors"; `textureClipChannel`/`textureClipThreshold` discard particles "when they spawn on an area of the Texture with a value lower than this threshold"; `textureColorAffectsParticles`/`textureAlphaAffectsParticles` apply the RGB/alpha "when the particle spawns"; `textureBilinearFiltering` "takes four neighboring samples"; `textureUVChannel` picks a mesh's UV channel. No mapping onto the shapes is documented. |
| [Shape module manual](https://docs.unity3d.com/6000.0/Documentation/Manual/PartSysShapeModule.html) | Texture: "Selects a texture to use for tinting and discarding particles"; Spread: "0.1 means that particles can only spawn at 10% intervals around the shape". |

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
| `points/*`, `empty/*`, `nomesh/*`, `messages/*` | A mesh without triangles or segments logs "Mesh used in Particle System Shape Module is not valid, possibly due to missing read/write flag"; Edge and Triangle on a surface without area (also no mesh) log "Particle System '<name>' is trying to spawn on a mesh with zero surface area"; the particles start at the shape's origin facing +Z. |
| `unreadable/*` | A mesh made non-readable at run time still emits in the Editor, without a warning. |
| `component/*` | `AddComponent<ParticleSystem>()` adds the ParticleSystemRenderer at once, also on an inactive object: enabled, Billboard, one empty material slot, no shadows cast or received, four vertex streams. |
| `texture/uv/*`, `texture/size/*`, `texture/transform/*` | The texture coordinate belongs to the unit shape, before the shape's transform: Box, Box Shell, Box Edge and Rectangle (x + 0.5, y + 0.5); Circle 0.5 + 0.5·r·(cos, sin), Cone and Cone Volume 0.5 + 0.5·r²·(cos, sin), r the sampled radius over the radius; Sphere and Hemisphere (azimuth over a full turn, (z + 1)/2 of the direction); Donut 0.5 + 0.5·(the tube offset over the donut radius) in the ring's (radial, z) plane; Edge (the place along it, 0); meshes the interpolated UV channel times 0.9999, repeated. |
| `texture/filter/*`, `texture/uvchannel/*`, `texture/unreadable` | The coordinate is limited to 0.9999 and scaled by the size; point sampling takes the texel it falls in; bilinear starts half a texel lower (not below 0), blends with the right and upper texels clamped to the edge (the wrap mode does not matter) by 255ths of the fractions, truncated, a + (b − a)·w/256 per byte. A mesh without the channel discards every particle. A texture made non-readable still samples. |
| `texture/clip/*`, `texture/affects/*`, `texture/startcolor` | A channel byte below threshold·256 (truncated) clips: alpha 0. Otherwise colour and alpha left out read as 255. The texel multiplies the start colour bytes, rounded; with a threshold above 0 a particle whose alpha is then 0 is born and removed with the step's deaths. A step's births and an `Emit` call start as one batch of four-slot lanes after the next multiple of four, unused lanes included; the discarded lanes leave in ascending order, the last lane taking each place, and every one of them (unused lanes too) lowers the count started; the batch's last particles then fill the slots before its aligned start (`texture/clip/rate`: 0 and 1 of ten a step stay; `texture/clip/emit5then10`). Threshold within [0, 1], UV channel ≥ 0. |
| `texture/uv/boxshell`, `boxedge` | Seven shape draws a point: the unit point (three), an axis from the next whole draw word modulo 3, three more. Shell moves the point onto the face of that axis on its own side, Edge onto the edge along it. |
| `topology/*`, `lines/*` | Line and line-strip segments emit, weighed by length (triangles by area). Triangle takes a segment as (a, b, b); a mesh with line submeshes samples every entry as a segment in Edge (the area draw's place along a→b) and Vertex (a or b, a whole draw word modulo 2). Mixed primitive types assert ("can only spawn from mesh if all submeshes use the same primitive type") when the mesh is set and emit from all; quads and points emit at the origin as not valid. |
| `obsolete/*` | The obsolete shell types set from script emit at the origin facing +Z, untinted, and assert "Shape not implemented" once. |
| `random/*/color`, `material/*/color` | Vertex colours: each colour times its weight (255ths, truncated) rounded to a byte, the three added with saturation. |

## Known differences (`known.json`)

- `loop-edge-colour` (1): Edge Loop with Single Material submesh 1, one channel within 1/255 in
  some particles: the place along the edge falls differently by a float tie of the emission time.
- `spread-boundary` (21): particles whose place lands exactly on a spread step (rate births at
  0.25 s, 0.5 s, 1.25 s; the last of ten Burst Spread lanes) snap to the neighbouring step.
- `warning-timing` (6): Unity logs the invalid-mesh warning and the mixed-primitive assertion when
  the mesh is set, which the probe counts for the scenario before; Unbound logs them while emitting.

Unmeasured: the Sprite and SpriteRenderer shapes' texture mapping (reported as not simulated),
imported non-readable meshes, other releases.
