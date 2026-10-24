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
