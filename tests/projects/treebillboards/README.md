# Tree billboards

Terrain trees whose prototype materials' shaders name a BillboardShader: a tree prefab drawn with
the Soft Occlusion shaders (one mesh, a trunk submesh with the bark material and a crown submesh
with the leaves material, whose texture has holes; no LOD group) on a flat 400 × 400 terrain lit by
one directional light. `TreeBillboardProbe` places the trees at run time in staggered rows from 30
to 380 units ahead of the cameras, of several width and height scales and colors, and records:

- `defaults`: the terrain's tree settings as a new terrain has them;
- `ground`, `pitch20`, `pitch55`: views from 7 units up and from above (13 and 55 degrees down)
  with the default settings (billboards from 50 units, a fade of 5);
- `atlas`: the render texture the billboards are cut from ("Tree Imposter Texture": size,
  format, sampler) as the ground view left it, and its first mip as `atlas.png`;
- `start20` (billboards from 20 units), `fade40` (from 25 units, a fade of 40) and `distance150`
  (tree distance 150): the ground view with other settings.

What it shows (Unity 6000.0.71f1, NVIDIA): such trees are drawn as their meshes up to the billboard
start plus the fade (distances across the ground), squashed towards the billboard across the fade,
and as camera-facing billboards past it, cut from an atlas cell into which the prototype is drawn
with its BillboardShader copies, seen orthographically from the camera's pitch and yaw. The mesh
trees take the forward vertex lights (`Lighting On` passes without a LightMode).

Make the assets once and record Unity's reference (two GPU captures, published when they agree):

    python3 tests/projects/treebillboards/record.py create --release 6000.0.71f1
    python3 tests/projects/treebillboards/record.py record --release 6000.0.71f1
    python3 tests/projects/treebillboards/record.py publish --release 6000.0.71f1

Run Unbound and compare (records exact; image means printed for the suite to track):

    UNBOUND=/path/to/unbound python3 tests/projects/treebillboards/run.py check
