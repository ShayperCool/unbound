# Shadowmask mixing in the built-in render pipeline

Built-in render pipeline, linear color space, OpenGL Core. How baked shadow masks mix with
realtime shadows ([Lighting Mode: Shadowmask](https://docs.unity3d.com/6000.0/Documentation/Manual/LightMode-Mixed-Shadowmask.html),
[QualitySettings.shadowmaskMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/QualitySettings-shadowmaskMode.html),
[Light.lightShadowCasterMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Light-lightShadowCasterMode.html)).

`run.py setup` (`ShadowmaskSetup.Create` in the Editor) made `Assets/Scenes/Shadowmask.unity` and
baked it in the Shadowmask Lighting Mode with the CPU lightmapper (no denoising, filtering or
compression): a static ground, static blocks near the camera and beyond the shadow distance, a
mixed sun (shadow mask channel 0) and a mixed point light (channel 1) with hard shadows, light
probes, and dynamic spheres near and far.

`ShadowmaskProbe` renders the main camera into a 320x180 render texture per case (shadow
distance 12, one cascade, hard shadows) and writes PNGs, with each light's baking output and each
renderer's lightmap as keyed values: forward and deferred, each Shadowmask Mode (Shadowmask,
Distance Shadowmask) with the lights' shadow caster mode Default, NonLightmappedOnly and
Everything (`<path>_<mode>_<caster>.png`), and the Shadowmask mode with realtime shadows off
(`forward_shadowmask_noshadows.png`). Two dynamic spheres sit in the sun's shadow of a static
wall (near) and block (far), each with a light probe at its centre; their interpolated occlusion
is recorded (`occlusion/*`). Last, a small dynamic sphere made at runtime outside the probes'
hull but inside their bounds, beside the probes in the wall's shadow, is recorded close up in
forward and deferred (`<path>_outer.png`) with its interpolated occlusion and lighting
(`occlusion/Outer Sphere`, `sh/Outer Sphere/*`). The reference is two byte-identical Editor
captures.

```sh
python3 tests/projects/shadowmaskmix/run.py setup --label setup1   # only to remake and bake the scene
python3 tests/projects/shadowmaskmix/run.py record --label a       # and b
python3 tests/projects/shadowmaskmix/run.py publish a b
python3 tests/projects/shadowmaskmix/run.py gate
```
