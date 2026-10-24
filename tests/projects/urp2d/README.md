# URP 2D probe

The Universal Render Pipeline 17 with its 2D renderer (the Editor's built-in package, unmodified):
an orthographic camera over a lit backdrop and ground, two boxes casting 2D shadows (ShadowCaster2D),
a lit and an unlit circle sprite, and 2D lights (global, point, spot, freeform, sprite), recorded
in scenarios that switch the lights. Gamma color space. Each scenario renders a frame after its
setup: Light2D builds its mesh and bounds in LateUpdate.

`Editor/ProbeSetup.Create` made the pipeline asset, the 2D renderer, the sprites, materials and
scene in the Editor:

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -projectPath tests/projects/urp2d \
        -executeMethod ProbeSetup.Create -logFile ~/.cache/urp2d-setup.log

Record Unity's reference (the Editor renders: `DISPLAY` and `XAUTHORITY` of the desktop):

    URP2D_PROBE_DIR=$PWD/tests/projects/urp2d/Reference/scenarios DISPLAY=:0 \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/urp2d \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/urp2d.log

Run Unbound (in a window: the images are read back from the GPU) and compare:

    URP2D_PROBE_DIR=/tmp/urp2d unbound run -p tests/projects/urp2d --no-watch --frames 40 --size 320x180
    for f in global ambient point spot freeform sprite_light shadows all; do
        python3 tests/projects/urp/compare.py tests/projects/urp2d/Reference/scenarios/$f.png /tmp/urp2d/$f.png; done

Every scenario matches the Editor within a mean difference of 0.01/255; `point`, `shadows` and `all`
keep a few single dark pixels (32 at most) inside the point light, where triangles of the light or
shadow geometry meet.
