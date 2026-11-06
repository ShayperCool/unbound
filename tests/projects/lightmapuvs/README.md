# Lightmap UV probe

What `ModelImporter.generateSecondaryUV` gives imported meshes: the UV2 (lightmap UVs) the
importer unwraps, with the vertex count and order it leaves. `Tools/make_models.py` (Blender)
writes the source models into `Sources/`: Cube (six quads), Cylinder (16 sides, smooth sides and
hard caps), Organic (Suzanne: more than a hundred triangles per connected part), Multi (three
materials), TwoUV (an authored second UV channel), Pieces (two disconnected boxes in one mesh) as
FBX, and Torus as OBJ; then parts of more than a hundred triangles as FBX: Sphere (UV sphere, 720
triangles, smooth), Ico (icosphere, 320, flat), Grid (a waved plane of 25×25 quads, flat), Barrel
(24 sides, 20 rings, fan caps, bulged), Rock (displaced icosphere, 5120 triangles, smooth), and a
large part after another part of one mesh: Compound (a box, then a UV sphere) and Twins (two UV
spheres). `Tools/make_project.py` copies each into `Assets/Resources/<variant>/`
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
exit status (the gate: `compare.py ... --uv2 --expect Cube,Multi,Pieces,Sphere,Ico,Barrel,Rock,Compound,Twins`).

After changing a generator, regenerate (`blender -b --factory-startup --python
tests/projects/lightmapuvs/Tools/make_models.py -- tests/projects/lightmapuvs/Sources`, then
`python3 tests/projects/lightmapuvs/Tools/make_project.py`) and record the reference again.

What the reference shows (Unity 6000.0.71f1): a generated UV2 splits vertices along its seams
(Cylinder 66 → 74 vertices, Organic 555 → 1028), replaces an authored UV2 (TwoUV), and depends
on the hard angle and the distortion settings only where the mesh has charts to merge or split
(Organic, Torus). The margin method and its inputs move and scale every chart.

State in Unbound: the unwrap is reproduced, for connected parts of up to 100 triangles and for
larger ones (simplified first, segmented on the simplified copy and refined back), including
charts that touch or overlap themselves. Cube, Multi, Pieces, Sphere, Ico, Barrel, Rock, Compound
and Twins match in every variant (vertex count and UV2 per corner). Grid's and TwoUV's UV2 match, their vertex
counts do not, also without generated UV2 (Unbound keeps the file's normals, which split vertices
the Editor welds). The Torus is unwrapped as the Editor does, but its mesh is named after the OBJ
object, not "default", so the comparison cannot pair it. Meshes Unbound does not reproduce yet
warn and keep the file's UV2: the Cylinder (its 16-gon caps are triangulated differently than by
the Editor's FBX reader) and Organic (non-manifold vertices after welding).

## Imported normals

The `Norm*` variants disable mesh optimization and generated UV2. They cover Import, Calculate,
None, smoothing angles 30/60/180, all five weighting modes, and each smoothing source. The
additional sources in `Sources/Normals` cover irregular faces, authored normals, FBX face/group/edge
smoothing, missing OBJ normals, convex and concave n-gons, and the triangle/quad numbering fixtures.
New records use the source asset and mesh index as their key, so independent OBJ files remain
separate even when Unity calls every mesh `default`. Existing unwrap records retain their keys.

Unity 6000.0's [Model tab](https://docs.unity3d.com/6000.0/Documentation/Manual/FBXImporter-Model.html)
documents the Import fallback when normals are absent, the five calculation modes, smoothing
sources, and None's empty normals. Its API describes
[normalCalculationMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ModelImporter-normalCalculationMode.html),
[normalSmoothingSource](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ModelImporter-normalSmoothingSource.html),
[normalSmoothingAngle](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ModelImporter-normalSmoothingAngle.html),
and [importNormals](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ModelImporter-importNormals.html).
The Manual describes Smoothness Source None as avoiding hard-edge splits; the captured meshes
instead have separate flat face normals. The reference records the measured result. Corner
numbering, normal arithmetic, split thresholds, and n-gon triangle order are measured details of
6000.0.71f1, not documented guarantees for other releases.
