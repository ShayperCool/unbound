# Lightmap UV probe

What `ModelImporter.generateSecondaryUV` gives imported meshes: the UV2 (lightmap UVs) the
importer unwraps, with the vertex count and order it leaves. `Tools/make_models.py` (Blender)
writes the source models into `Sources/`: Cube (six quads), Cylinder (16 sides, smooth sides and
hard caps), Organic (Suzanne: more than a hundred triangles per connected part), Multi (three
materials), TwoUV (an authored second UV channel), Pieces (two disconnected boxes in one mesh) as
FBX, and Torus as OBJ. `Tools/make_project.py` copies each into `Assets/Resources/<variant>/`
with the variant's unwrap settings:

| Variant | Settings |
|---|---|
| Default | hard angle 88, angle distortion 8, area distortion 15, margin Calculate (min lightmap resolution 40, min object scale 1) |
| Hard30 | hard angle 30 |
| Distort | angle distortion 2, area distortion 40 |
| Manual | margin method Manual, pack margin 10 |
| Calc | margin Calculate, min lightmap resolution 10, min object scale 0.5 |
| Off | generateSecondaryUV off (the file's own UV2) |
| Raw | mesh optimization off (triangles and vertices in import order), defaults otherwise |
| RawOff | mesh optimization and generateSecondaryUV off |

`LightmapUvProbe` records every mesh of every variant (vertex count, sub-mesh triangles,
vertices, normals, UV, UV2) as one JSON line per mesh and quits. The Editor records in Play mode
(`ProbeRunner.Record`, `-batchmode -nographics`).

    python3 tests/projects/lightmapuvs/run.py editor --release 6000.0.71f1 --label <L>    # two Editor runs
    python3 tests/projects/lightmapuvs/run.py publish --release 6000.0.71f1 --label <L>   # checks they agree
    UNBOUND_CLI=<unbound> python3 tests/projects/lightmapuvs/run.py unbound --release 6000.0.71f1 --label <L>
    python3 tests/projects/lightmapuvs/compare.py tests/projects/lightmapuvs/Reference/unity-6000.0.71f1.jsonl \
        ~/.cache/unbound/validation/runs/lightmapuvs/<L>/unbound/lightmapuvs.jsonl

The comparison is exact (float32 values at tolerance 0) and lists every mesh with each field that
is missing or differs. `--uv2` judges only the vertex count and the UV2 of every triangle corner
(found by the corners' positions and UVs, so independent of the vertex and triangle order the
Editor's mesh optimization gives); `--expect Cube,Multi,Pieces` lets only those meshes decide the
exit status (the gate: `compare.py ... --uv2 --expect Cube,Multi,Pieces`).

After changing a generator, regenerate (`blender -b --factory-startup --python
tests/projects/lightmapuvs/Tools/make_models.py -- tests/projects/lightmapuvs/Sources`, then
`python3 tests/projects/lightmapuvs/Tools/make_project.py`) and record the reference again.

What the reference shows (Unity 6000.0.71f1): a generated UV2 splits vertices along its seams
(Cylinder 66 → 74 vertices, Organic 555 → 1028), replaces an authored UV2 (TwoUV), and depends
on the hard angle and the distortion settings only where the mesh has charts to merge or split
(Organic, Torus). The margin method and its inputs move and scale every chart.

State in Unbound: the unwrap is reproduced for connected parts of up to 100 triangles. Cube, Multi
and Pieces match in every variant (vertex count and UV2 per corner); TwoUV's UV2 matches, its
vertex count does not (Unbound keeps the file's unnormalized normals, which split vertices the
Editor welds). The Torus is unwrapped as the Editor does, but its mesh is named after the OBJ
object, not "default", so the comparison cannot pair it. Meshes Unbound does not reproduce yet
warn and keep the file's UV2: the Cylinder (its 16-gon caps are triangulated differently than by
the Editor's FBX reader) and Organic (non-manifold vertices after welding, and parts of more than
100 triangles).
