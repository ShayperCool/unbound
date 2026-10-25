# Lighting probe

Small scenes lit by the built-in pipeline's forward lighting, rendered into PNGs: a ground plane,
two spheres and a cube with the Standard, Legacy Diffuse and VertexLit shaders under point, spot
and directional lights as pixel lights (ForwardAdd), vertex lights and SH lights, light render
modes and the quality level's pixel light count, cookies; and the light textures themselves
(`textures_*`: `Probe/AttenuationView` shows the attenuation texture and the spot cookie a
ForwardAdd pass samples, seen from above). `ambient.txt` notes the ambient probe before and after
the probe sets a flat ambient.

Record Unity's reference (the Editor needs a display):

    LIGHTING_PROBE_OUT=$PWD/tests/projects/lighting/Reference/images \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/lighting \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/lighting.log

Run Unbound (in a window: the images need a GPU) and compare:

    LIGHTING_PROBE_OUT=/tmp/lighting unbound run -p tests/projects/lighting --frames 10 --size 320x180 --no-watch
    python3 tests/projects/particles/compare_images.py tests/projects/lighting/Reference/images /tmp/lighting

What still differs: `five_points` (which of five lights becomes the vertex light of the large
ground plane), `textures_spot` (the default spot cookie is a fitted curve, within 2/255 on average).
Shadows are not drawn yet.
