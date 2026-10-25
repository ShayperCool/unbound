# Ambient probe

What the Editor derives from the skybox when a scene has no baked lighting data. The scene points
at the built-in LightingData (`unity_builtin_extra`, 20201), whose ambient probe and reflection
are the default sky's: that is the probe a scene starts with whatever its sky. After
`DynamicGI.UpdateEnvironment` the Editor projects the current skybox (with the scene's sun) onto
spherical harmonics; the default reflection stays.

`AmbientProbe` records the probe as loaded, then for each sky (the default procedural sky without
and with a sun light, a tinted one, a cubemap sky of six plain faces, the same with the ambient
intensity at 1.5) the probe after UpdateEnvironment and an image of a matte and a mirror sphere
lit by the environment alone.

What it established: Unity stores the probe as each band's projection times the cosine lobe's
factor (1, 2/3, 1/4) and the basis function's constant (a uniform sky of radiance L gives L in the
first coefficient; the six faces match to 3e-5); the ambient intensity scales it by
intensity^2.2; the built-in reflection's RGBM texels are sRGB colour data.

Make the assets and record Unity's reference (the Editor needs a display):

    ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -quit -projectPath tests/projects/ambient \
        -executeMethod ProbeRunner.Create -logFile ~/.cache/ambient-create.log
    AMBIENT_PROBE_OUT=$PWD/tests/projects/ambient/Reference \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/ambient \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/ambient.log

Run Unbound and compare:

    AMBIENT_PROBE_OUT=/tmp/ambient unbound run -p tests/projects/ambient --frames 600 --size 320x180 --no-watch
    python3 tests/projects/ambient/compare.py tests/projects/ambient/Reference /tmp/ambient
    python3 tests/projects/particles/compare_images.py tests/projects/ambient/Reference /tmp/ambient
