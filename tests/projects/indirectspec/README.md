# unity_IndirectSpecColor witness

Where the Editor's `unity_IndirectSpecColor` comes from in a 6000.0 scene. Since RenderSettings
serializedVersion 10, scenes no longer store `m_IndirectSpecularColor`. The global is what
`UnityGlobalIllumination.cginc` uses as the indirect specular light under
`_GLOSSYREFLECTIONS_OFF`.

## Fixture

The fixture is the light probe probe's baked `Main` scene (Trilight ambient, the default
procedural skybox, a mixed directional and a mixed point light, Shadowmask, the Editor's own
bake). It is copied byte for byte with its lighting data, lightmaps, skybox reflection, `Extra`
scene, `.meta` files, `Packages` and `ProjectSettings`: `tests/projects/lightprobes` tree
`a1d6184b` (Assets/Scenes) at branch `t3/light-probes` 5e849981, LightingData.asset sha256
`e5de0e37…`. No new bake. The lighting settings have `m_BakeOnSceneLoad: 0`. The light probe
probe's scripts are not copied. `Assets/Scripts/IndirectSpecWitness.cs`,
`Assets/Resources/IndirectSpecDump.shader` and `Assets/Editor/ProbeRunner.cs` are this probe's.

## What it records

`IndirectSpecWitness` writes JSON lines `{"k", "v"}`. The last line, and only it, is
`{"k":"complete","v":true}`. It sets `RenderSettings.ambientIntensity` {1, 0.5} x
`reflectionIntensity` {1, 0.25}, in the order (1, 1) (the scene's own), (1, 0.25), (0.5, 1),
(0.5, 0.25). Each setting is captured in the frame it is set (`immediate`) and again two frames
later (`settled`). Each capture has two consumers:

* `dump`: `Hidden/IndirectSpecDump` (ForwardBase pass, light probes off) writes
  `unity_IndirectSpecColor` into an RGBAFloat target, read back as raw floats. The same target
  holds `unity_SpecCube0_HDR`, the default reflection's last mip along the six axes (decoded and
  raw), `unity_SH*`, `unity_Ambient*`, `glstate_lightmodel_ambient` and
  `unity_ColorSpaceDielectricSpec`.
* `standard`: a Standard sphere drawn into an ARGB32 target (bytes) and an ARGBFloat target
  (floats). It has white albedo, metallic 0, smoothness 0, `_SPECULARHIGHLIGHTS_OFF` and
  `_GLOSSYREFLECTIONS_OFF`. Its light probe is a black `CustomProvided` SH, and every light is
  disabled. The centre pixel and the histogram of all pixels are recorded.

Each setting also records the RenderSettings (ambient mode/colors/intensity, reflection
intensity/mode/bounces/resolution, fog), `RenderSettings.ambientProbe`,
`ReflectionProbe.defaultTextureHDRDecodeValues` and `QualitySettings.activeColorSpace`. The
context records the lighting data's serialized ambient probes (Editor only, through
SerializedObject), the default reflection texture, the skybox, the build target and the
Standard shader tier. Before the sweep, the dump alone is captured once with the lights still
enabled.

## Profiles

`linear` is the project as committed (`m_ActiveColorSpace: 1`). `gamma` is the same copy with
`m_ActiveColorSpace: 0` and nothing else changed. Each profile is one canonical file:
`Reference/unity-<release>/linear.jsonl` and `gamma.jsonl`.

    python3 tests/projects/indirectspec/run.py editor --release 6000.0.71f1 --label <L>   # 4 Editors: 2 profiles x 2 repeats
    python3 tests/projects/indirectspec/run.py publish --release 6000.0.71f1 --label <L>  # per profile, strict
    python3 tests/projects/indirectspec/analyze.py tests/projects/indirectspec/Reference/unity-6000.0.71f1/{linear,gamma}.jsonl

A profile is published only if both repeats meet every condition: exit 0 with no forced stop,
the NVIDIA renderer, a launch-bound input record, the completion marker last, and
byte-identical output.
Run outputs stay in `$XDG_CACHE_HOME/unbound/validation/runs/indirectspec/<L>/`.

## Analysis (predeclared)

`analyze.py` tests a fixed set of parameter-free rules:
`ISC = M(L0 * A(ambientIntensity) * R(reflectionIntensity))`. L0 is the lighting data's
`m_BakedAmbientProbeInLinear` DC term, and A, R are each in {1, x, GammaToLinear(x)}. In linear
projects M is the identity. In gamma projects M is one of sRGB(product), sRGB(L0) * scale, or
the identity. A rule is proven only if it holds within 2e-5 for all 4 settings of both
profiles; anything else is negative evidence. Nothing is fitted. The Standard sphere is a
consistency check against the BRDF factor of the recorded tier (BRDF1: 0.02 linear,
0.72 * 0.2209 gamma).

Not covered: a Custom default reflection, `DynamicGI.UpdateEnvironment`, Skybox/Flat ambient
modes, other skyboxes, authored (rather than script-set) intensities, and bakes other than this
one.
