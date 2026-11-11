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
| Repair | defaults, for the models the unwrap has to repair (below) |
| RepairOff | generateSecondaryUV off, for Lines |
| Names | mesh optimization and generateSecondaryUV off, for `Sources/Names` |

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
exit status (the gate, `tools/agents/suite.py`: `compare.py ... --uv2 --expect Cube,Multi,Pieces,...` with
the models that match in every variant, the Repair models among them).

After changing a generator, regenerate (`blender -b --factory-startup --python
tests/projects/lightmapuvs/Tools/make_models.py -- tests/projects/lightmapuvs/Sources`, then
`python3 tests/projects/lightmapuvs/Tools/make_project.py`) and record the reference again.

What the reference shows (Unity 6000.0.71f1): a generated UV2 splits vertices along its seams
(Cylinder 66 → 74 vertices, Organic 555 → 1028), replaces an authored UV2 (TwoUV), and depends
on the hard angle and the distortion settings only where the mesh has charts to merge or split
(Organic, Torus). The margin method and its inputs move and scale every chart.

## Meshes the unwrap repairs

`Tools/make_repair_models.py` (Blender) writes `Sources/Repair`: Fin (an edge of three faces),
BowTie (two pyramids touching at one vertex), Doubled (a face listed twice), Flipped (two faces
back to back), Folded (a strip folded onto itself), Slivers (needle and collinear triangles, and
corners closer than the weld distance), SphereFin (a part of more than a hundred triangles with a
fin) and Lines (nothing but collinear triangles). The Repair variant also holds FlatQuad, FlatTri,
Pinch and WeldQuad of `Sources/Normals`.

What the reference shows (Unity 6000.0.71f1): all of these get a UV2 except Lines. A triangle
without area keeps its place in the mesh and its corners take the UV2 of a neighbouring triangle's
corners. For Lines the Editor logs that no triangles are left after the cleanup and imports the
mesh empty (no vertices, one empty sub-mesh), while RepairOff/Lines keeps its six vertices. That
is measured for this one mesh of one sub-mesh; a mesh of several sub-meshes or with blend shapes
or skinning, or with mesh optimization off, is not measured.

State in Unbound: the unwrap is reproduced, for connected parts of up to 100 triangles and for
larger ones (simplified first, segmented on the simplified copy and refined back), including
charts that touch or overlap themselves, degenerate triangles and non-manifold edges and
vertices. Cube, Multi, Pieces, Sphere, Ico, Barrel, Rock, Compound and Twins, plus Cylinder after
n-gon triangulation, Grid and TwoUV match in every variant (vertex count and UV2 per corner), and
so do the twelve Repair models. Organic matches in five of its six unwrap variants; in Hard30 four
single-triangle charts of equal size are packed in each other's places (968 triangles, 12 corners
differ by up to 0.034). Imported normals are normalized and attributes split with the measured
thresholds. The Torus (an OBJ file, its mesh named `default`) matches in every variant.

## Mesh and object names

`Sources/Names` holds hand-written OBJ files of one quad per part with `o` and `g` statements in
different arrangements, Order.obj with group names that sort differently by letter case, digits
and punctuation, and OrderFbx.fbx (`Tools/make_name_models.py`, Blender) with objects of the same
names. The Names variant records each model's renderers in hierarchy order: the object's path
and its mesh's name are part of the record's key.

What the reference shows (Unity 6000.0.71f1): an OBJ file's meshes follow its `g` statements
alone. `o` statements change nothing; faces before any group and under a `g` without a name are
the mesh `default`; a name of several words is joined by underscores; a group name that returns
later, also in another letter case, continues the same mesh (spelled as first seen); a group
without faces gives no mesh; a vertex no face names is in no mesh. Each mesh is a child object of
the root named like it, also when it is the only one. Siblings are ordered without regard to
letter case and with runs of digits as numbers (`9`, `10`, `_under`, `a01`, `A1`, `a1A`, `a1b`,
`a2`, `a10`, `Alpha`, `alpha`, `b-2`, `b.1`, `B_0`, `Beta`, `Zed`), the same for OBJ groups and FBX
nodes. Not measured: `sortHierarchyByName` off; names with characters outside ASCII (Unbound
folds only ASCII letter case when it joins groups); group names with tabs, repeated spaces or a
trailing comment (Unbound joins single spaces and keeps the rest of the line); and an OBJ face
that gives no triangle with mesh optimization off (Unbound keeps its points, as for FBX).

Unbound matches all 52 Names records in names, order, vertices and triangles; the calculated
normals of some of these slightly bent quads differ in the last float digit (at most 1e-7).

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

`compare_normals.py <reference> <candidate> --expect Authored,Bare,Bridge,BridgeObj,Cross,Cube,Cylinder,Edges,Faces,FlatPoly,FlatQuad,FlatTri,Grid,Groups,Ico,Kite,Multi,Organic,Pieces,Pinch,Polys,Prism,Seam,Slit,SlitGroups,Sphere,Star,StarObj,Tent,Torus,Triangle,TwoUV,WeldDiag,WeldQuad,WeldTri`
gates 420 cases. It writes every differing value, including ungated cases, to
`normal-comparison.json` beside the candidate. The original exact comparator retains tolerance 0.
The result is 274 exact cases and 146 calculated-normal cases with residuals of at most 1.8e-7 per
float32 component (topology, positions and UVs exact). The added non-planar polygon contributes nine
of these, at most 1.2e-7; the imperfect-content fixtures below, whose base quad is non-planar or
whose grid is waved, contribute 90, at most 1.5e-7. Residuals are marked separately from exact
matches. Multi's 12 material-slot ordering cases now match after the imported binding fix.

The SGI GLU sweep over mirrored float32 positions reproduces the convex, concave and non-planar
n-gon triangle sequences in the reference. Mesh building and UV unwrapping share these triangles.
FBX polygon masks keep their original bits. Edge smoothing becomes polygon masks by the
importer's walk over smooth edges (described in modelimport/README.md, Smoothing and take
ranges); Edges, Slit and Seam are exact in every variant. ASCII, legacy and per-corner smoothing
layers and junctions of sharp edges are measured by
`modelimport/smoothing.py`. Mesh optimization order and OBJ naming remain follow-ups. Imported material-slot order and
face bindings are also gated by `modelimport/material_slots.py`, including rendered slot overrides.

## Imperfect content

`Tools/make_imperfect_models.py` (Blender) writes ordinary but imperfect models into
`Sources/Normals`: bridged holes whose corners repeat a position (Bridge, BridgeObj), coincident
n-gon corners (Pinch), zero-area faces (FlatPoly, FlatTri, FlatQuad), faces folded by welding
(WeldTri, WeldQuad, WeldDiag), a self-intersecting pentagram (Cross), and sharp edges that enclose
no region (Slit as edge smoothing, SlitGroups as smoothing groups, Seam: a cylinder with sharp
caps and one sharp side edge). The Editor (6000.0.71f1, every `Norm*` variant) imports all of them:
- coincident corners of an n-gon merge into the first one; a zero-area n-gon gives no triangles;
- a triangle or quad first loses corners repeating the previous corner's welded point; three
  left make a triangle, four a quad; fewer, or a quad folded onto its diagonal, drop the face;
  zero-area faces with distinct points stay;
- a polygon whose edges cross is discarded with the rest of the mesh kept (Unbound warns);
- with mesh optimization off, control points no face uses keep a vertex with normal (1, 1, 1)
  and zero UVs (a separate capture with the default optimization showed them removed).
Unbound matches all of them (topology exact, normals within the recorded residuals).

The sheriff's `lightmapuvs` suite recipe checks both UV2 and this normal/topology matrix, without
starting the Editor. Its UV2 expected set includes Grid, TwoUV and Cylinder.
