# Baked lighting probe

Built-in render pipeline, linear color space. A static ground, cube and column lit by a baked
directional light and a baked orange point light, a light probe grid (18 probes) and a dynamic
sphere the probes light; a baked reflection probe over the scene (it captures no object: none is
Reflection Probe Static, so its cubemap is its clear color) and a mirror-like sphere in it.
`Editor/BakedSetup.Create` made the scene and baked it with the CPU lightmapper (no denoising,
filtering or compression, non-directional); `BakedSetup.AddReflectionProbe` added the probe and the
sphere and baked again, so the bake in `Assets/Scenes/Baked/` (LightingData, one lightmap EXR, the
probe's and the skybox's reflection EXRs) is the Editor's own.

`Scripts/BakedProbe` renders the main camera into a 320x180 render texture (three frames in, once
the reflection probes are set up) and writes it as a PNG, with what the lighting data gives the
scene next to it as JSON lines: the lightmaps (and their texture format), each renderer's lightmap
index and scale-offset, the probe interpolated at the sphere, the reflection probe's baked cubemap
and the probes the mirror sphere uses, each light's baking output. Before the second render it
queues Graphics.DrawMesh spheres: one inside the light probes' hull (probe-lit), one just outside
it and one with light probes off (both lit by the ambient alone).

Record Unity's reference:

    BAKED_PROBE_OUT=$PWD/tests/projects/baked/Reference/unity-6000.0.71f1.png DISPLAY=:0 \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/baked \
        -executeMethod BakedRunner.Record -logFile ~/.cache/baked.log

Run Unbound and compare:

    BAKED_PROBE_OUT=/tmp/baked.png unbound run -p tests/projects/baked --no-watch --frames 20 --size 320x180
    python3 tests/projects/urp/compare.py tests/projects/baked/Reference/unity-6000.0.71f1.png /tmp/baked.png
    diff tests/projects/baked/Reference/unity-6000.0.71f1.jsonl /tmp/baked.jsonl

The JSON lines match the Editor's exactly; the image within a mean difference of 0.15 (worst
pixels: a one-pixel shift of the column's silhouette). The lighting data lists the probes'
cubemaps first and the skybox's last.
