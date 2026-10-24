# 2D probe

Grids, tilemaps (Tilemap Extras' rule and animated tiles too), their colliders and rendering, sprite
shapes (com.unity.2d.spriteshape's generated geometry, colliders and rendering) and a skinned sprite
(com.unity.2d.animation: the sprite's bones, bind poses, mesh and weights, a SpriteSkin deforming it
over frames, its rendering) run identically in the Unity Editor and in Unbound; each scenario writes its values to a JSON file and
`compare.py` lists the differences. Rendered images are 160x160 camera renders read back with
`ReadPixels` (16 pixels per unit, the sprites' own resolution).

The assets and the scene are made by `ProbeSetup.Create` and committed (re-run it only to change
them; it rewrites `Assets/Tiles`, `Assets/Shapes`, `Assets/Art` and the scene;
`ProbeSetup.AddShapesAndSkin` remakes only the sprite shapes and the skinned sprite):

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -projectPath tests/projects/twod \
        -executeMethod ProbeSetup.Create -logFile /tmp/setup.log

Record Unity's reference (the Editor renders, so no `-nographics`; it needs a display):

    TWOD_PROBE_OUT=$PWD/tests/projects/twod/Reference/unity-6000.0.71f1.json DISPLAY=:0 \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/twod \
        -executeMethod ProbeRunner.Record -logFile /tmp/twod.log

Run Unbound and compare (`--images DIR` writes the differing images as PNG):

    TWOD_PROBE_OUT=/tmp/unbound.json unbound run -p tests/projects/twod --headless 30
    python3 tests/projects/twod/compare.py tests/projects/twod/Reference/unity-6000.0.71f1.json /tmp/unbound.json

`TWOD_PROBE_ONLY=<prefix>` runs only the scenarios whose name starts with it.
