# Blend shape probe

`BlendProbe` records blend shapes three ways:

- a scripted mesh (a 3x3 grid; shape `A` with one frame, shape `B` with frames at 25 and 100):
  the Mesh API's answers and what misuse throws or logs, then the vertices, normals and tangents
  a SkinnedMeshRenderer bakes at weights below, between, at and past the frames (negative too);
- the same mesh saved as an asset (`Resources/Shaped.asset`, Unity's serialized `m_Shapes`);
- a model, `Resources/Blob.fbx`, made in Blender by `Tools/make_blob.py`: a sphere with the shape
  keys `Puff` and `Squash` on two bones and an action animating them. The probe records the
  imported shapes (names, frames, the deltas of vertices, normals and tangents), bakes with
  shapes and a bent bone, samples the imported clip, and saves an image (`blob.png`).

Make the scene and the asset, and record Unity's reference (the Editor needs a display):

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -projectPath tests/projects/blendshapes \
        -executeMethod ProbeRunner.Create -logFile ~/.cache/blend-create.log
    BLEND_PROBE_OUT=$PWD/tests/projects/blendshapes/Reference \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/blendshapes \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/blend.log

Regenerate the model (Blender 4+ in the background):

    blender -b --factory-startup --python tests/projects/blendshapes/Tools/make_blob.py -- tests/projects/blendshapes/Assets/Resources/Blob.fbx

Run Unbound and compare:

    BLEND_PROBE_OUT=/tmp/blend unbound run -p tests/projects/blendshapes --frames 600 --size 320x180 --no-watch
    python3 tests/projects/blendshapes/compare.py tests/projects/blendshapes/Reference /tmp/blend
    python3 tests/projects/particles/compare_images.py tests/projects/blendshapes/Reference /tmp/blend

Everything matches. What the probe established:

- Weights: below the first frame, its deltas × weight / its weight (negative weights too);
  between frames, the two interpolated; past the last frame, the last frame's deltas × (weight −
  the frame before's) / (its weight − the frame before's). No clamping (`legacyClampBlendShapeWeights`
  off). Baked normals and tangents are not renormalized.
- Imported shapes' normals ("Calculate"): around each control point, the shaped polygons'
  normals weighted by their shaped area and by the corner's angle in the base mesh, normalized,
  minus the imported normal. Tangents: MikkTSpace on the shaped mesh, minus the base tangents.
- The renderer's weights start at the file's DeformPercent; clips animate `blendShape.<name>`.

Known differences: Unity logs an error and ignores a `vertices` assignment smaller than the
triangles reference; Unbound takes it. The clip's samples are within 0.06 of the Editor's (Unity
reduces the imported keys). The image is within 1.7/255 on average: the shading of the default
reflection differs a little; the shape is the same.
