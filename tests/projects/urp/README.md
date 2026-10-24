# URP probe

The Universal Render Pipeline 17 (the Editor's built-in package, unmodified) rendering a lit red cube,
an unlit green sphere and a lit grey ground under one directional light, read back from a 320x180
render texture by the same script in the Editor and in Unbound. Gamma color space, Forward renderer,
render graph (the native render pass compiler), no post-processing.

`Editor/ProbeSetup.Create` made the pipeline asset, renderer, materials and scene in the Editor.

Record Unity's reference:

    URP_PROBE_OUT=$PWD/tests/projects/urp/Reference/unity-6000.0.71f1.png DISPLAY=:0 \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/urp \
        -executeMethod ProbeRunner.Record -logFile /tmp/urp.log

Run Unbound:

    URP_PROBE_OUT=/tmp/urp_unbound.png unbound run -p tests/projects/urp --no-watch --frames 30 --size 320x180

## Scenarios

With `URP_PROBE_DIR` the probe records every scenario into that directory (an image and the
lighting constants URP set): `base`, `shadows`, `lights` (point and spot), `lights_shadows`,
`spot_shadows`, `point_shadows` (additional light shadows), `skybox`, `postfx` (bloom, ACES,
vignette, color adjustments) and `postfx_bloom`, `postfx_tonemap`, `postfx_vignette`,
`postfx_color` (one effect each), `transparent`, `fog`, `msaa`, `render_scale` (0.5),
`camera_stack` (an overlay camera), `state_block` (a renderer feature drawing with a
RenderStateBlock), `depth_opaque`, plus
`shadow-matrices.jsonl` (what CullingResults computes for directional shadows over lights, splits,
resolutions, distances and camera settings), `local-shadow-matrices.jsonl` (the same for point
light cube faces and spot lights) and `sky-mesh.jsonl` (the
sky mesh's vertices, read through a flat-shading probe shader). The Editor's are in
`Reference/scenarios/`:

    URP_PROBE_DIR=$PWD/tests/projects/urp/Reference/scenarios ... -executeMethod ProbeRunner.Record
    URP_PROBE_DIR=/tmp/urp unbound run -p tests/projects/urp --no-watch --frames 30 --size 320x180
    for f in base shadows lights skybox postfx; do python3 tests/projects/urp/compare.py tests/projects/urp/Reference/scenarios/$f.png /tmp/urp/$f.png; done
