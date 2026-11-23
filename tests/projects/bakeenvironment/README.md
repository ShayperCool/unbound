# Bake environment probe

Unity 6000.0.71f1, built-in render pipeline, linear colour space. What a bake in Edit mode
(`Lightmapping.Bake`) makes of the scene's environment: the ambient probe
(`RenderSettings.ambientProbe`) and the default reflection (`ReflectionProbe.defaultTexture`).
Unity documentation (6000.0):
[Lighting window](https://docs.unity3d.com/6000.0/Documentation/Manual/lighting-window.html)
(Environment Lighting: the ambient Source, Intensity Multiplier; Environment Reflections:
Source, Resolution, Compression, Intensity Multiplier),
[Lightmapping.Bake](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Lightmapping.Bake.html),
[RenderSettings.ambientProbe](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderSettings-ambientProbe.html),
[ReflectionProbe.defaultTexture](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ReflectionProbe-defaultTexture.html).
The documentation gives no rule for how the default reflection is drawn, stored or convolved:
what is compared here is the release's observed result.

## Content

Each scene is a plane under a directional light named as the scene's sun, with Lighting
Settings that bake neither baked nor realtime global illumination (the bake then makes the
environment alone), the default procedural skybox as the ambient source and the reflection's:

- `EnvSky`: ambient intensity 1, reflections of 128 texels.
- `EnvSkyBright`: ambient intensity 2.5, reflection intensity 0.5, reflections of 64 texels:
  the ambient probe scales with the intensity (2.5^2.2 times `EnvSky`'s) and the reflection
  does not (neither intensity is in it).
- `EnvSkySun`: the sun higher, turned, coloured and at intensity 2, reflections of 32.
- `EnvFlat`, `EnvTrilight`: an ambient colour and a gradient with the skybox still there: the
  ambient probe is the colour's or the gradient's, the reflection still the skybox's.
- `EnvNoSky`: no skybox material: no reflection is drawn and the stored probe is zero (the
  ambient probe then gives the ambient colour); the scene keeps the default reflection it had,
  here the built-in lighting data's `Default-Skybox-Cubemap`.
- `EnvSkyThenNone`: baked with the skybox, then without it (the recorder takes it away): the
  reflection the first bake made is gone (the Editor removes its file), and none is left: the
  black `UnityBlackCube`.
- `EnvBakedNoSky`: saved with the lighting data of an earlier bake with its skybox
  (`EnvBakedNoSky/LightingData.asset` and its `ReflectionProbe-0.exr`, made by `create`), then
  without the skybox: the bake removes that reflection's file too and leaves none (the black
  cube).
- `EnvNoBaking`: baked with SupportedRenderingFeatures saying the render pipeline bakes neither
  the ambient probe nor the default reflection (the recorder sets them for the bake): both are
  taken away (a zero probe, the black cube), not kept.

## Capture and comparison

`Assets/Editor/EnvironmentRecord.cs` opens each scene, bakes it and records (per scene
`record.txt`) the bake's answer, the ambient probe's 27 coefficients and those of the probe
the bake stored (read in the Custom ambient mode, which gives it as it is), the default
reflection's name, type, size, levels and format, and what the bake wrote; and (`cube.f32.z`,
zlib of 32-bit floats of RGBA) every texel of every level of the default reflection, level by
level and face by face (+x, -x, +y, -y, +z, -z). In the Editor the bake writes
`LightingData.asset` and, with a skybox, `ReflectionProbe-0.exr` (the six faces as a 6:1 strip of
half-float red, green and blue) beside the scene and imports it as a cube convolved for glossy
reflection and compressed (BC6H); its import settings are kept (`import.txt`, without the GUID,
new with every bake), and it is imported again readable and uncompressed to be read. A built-in
cube is not read.

    python3 tests/projects/bakeenvironment/environment_bake.py create      # the scenes (Editor)
    python3 tests/projects/bakeenvironment/environment_bake.py native --label L
    python3 tests/projects/bakeenvironment/environment_bake.py publish L1 L2
    python3 tests/projects/bakeenvironment/environment_bake.py check       # Unbound against the reference

Two captures make the reference; they are identical. `check` compares the bake's answer and the
default reflection's name, type, size and levels exactly; the ambient probe within 1e-3 of its
largest coefficient (the sky is drawn by the graphics device and projected in 32-bit floats:
the worst seen is 4.5e-4, alike in each sky's scene; a probe of zeros exactly), and the stored
probe likewise; each level of the default reflection within 2e-3 of the level's largest colour
value (the first level is the drawn sky as half floats, the worst seen one half-float step; the
others its convolution, the worst seen 1.46e-3; a black cube exactly). Listed,
not gated: what the Editor writes into the project (the files, the lighting data the scene then
names, the reflection's file, format and import settings), which Unbound does not write yet
(it keeps the bake's environment for the session, its reflection an uncompressed RGBAHalf cube).

Not measured, each said by Unbound's bake: a Custom ambient or reflection source (Unbound
keeps that part as the scene had it), gamma colour space projects and several open scenes
(Unbound makes the environment of the scene whose lighting settings are in effect alone, and
keeps the others'), a render pipeline asset (made as the built-in pipeline's). Without a
graphics device Unbound's bake makes no environment lighting (said). Not measured either: a
skybox other than the procedural one, Environment Reflections Compression other than Auto, the
bake on a scene's load (`Lightmapping.bakeOnSceneLoad`: still said and not made), a scene loaded
again after the bake (Unbound gives it the bake's environment, or the one it kept, as it gives
it the bake's realtime GI).
