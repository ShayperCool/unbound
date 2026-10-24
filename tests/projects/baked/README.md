# Baked lighting probe

Built-in render pipeline, linear color space. A static ground, cube and column lit by a baked
directional light and a baked orange point light, a light probe grid (18 probes) and a dynamic
sphere the probes light. `Editor/BakedSetup.Create` made the scene and baked it with the CPU
lightmapper (no denoising, filtering or compression, non-directional), so the bake in
`Assets/Scenes/Baked/` (LightingData, one lightmap EXR, the skybox reflection EXR) is the
Editor's own.

`Scripts/BakedProbe` renders the main camera into a 320x180 render texture and writes it as a
PNG, with what the lighting data gives the scene next to it as JSON lines: the lightmaps (and
their texture format), each renderer's lightmap index and scale-offset, the probe interpolated
at the sphere, each light's baking output.

Record Unity's reference:

    BAKED_PROBE_OUT=$PWD/tests/projects/baked/Reference/unity-6000.0.71f1.png DISPLAY=:0 \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/baked \
        -executeMethod BakedRunner.Record -logFile ~/.cache/baked.log

Run Unbound and compare:

    BAKED_PROBE_OUT=/tmp/baked.png unbound run -p tests/projects/baked --no-watch --frames 20 --size 320x180
    python3 tests/projects/urp/compare.py tests/projects/baked/Reference/unity-6000.0.71f1.png /tmp/baked.png
    diff tests/projects/baked/Reference/unity-6000.0.71f1.jsonl /tmp/baked.jsonl

The JSON lines match the Editor's exactly; the image within a mean difference of 0.64 (worst
pixels: a one-pixel shift of the column's silhouette).
