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

    python3 tests/projects/indirectspec/preflight.py --rsp-dir <copy>/Library/Bee/artifacts/<h>.dag --out <new dir>
    python3 tests/projects/indirectspec/test_gate.py <new dir>                            # synthetic gate controls
    python3 tests/projects/indirectspec/run.py editor --release 6000.0.71f1 --label <L>   # 4 Editors, one at a time, gated
    python3 tests/projects/indirectspec/run.py publish --release 6000.0.71f1 --label <L>  # per profile, strict
    python3 tests/projects/indirectspec/analyze.py tests/projects/indirectspec/Reference/unity-6000.0.71f1/{linear,gamma}.jsonl

`run.py editor` admits one Editor at a time in the order linear/native1, linear/native2,
gamma/native1, gamma/native2. Each capture must pass `gate.py` before the next is admitted; the
first rejected capture ends the run and nothing is relaunched. `gate.py` requires exit 0 with no
forced stop of any kind, the NVIDIA renderer, a launch-bound input record of this checkout's
witness and the profile's color space, a log without C# or shader errors, and an output with
every predeclared key (derived from the witness's own `Slots` and `Settings`), no duplicate,
no diagnostic `* error` record, finite dump/Standard values, no placeholder or error-shader
dump, and exactly one completion marker, last. A profile is published only if both repeats
pass the gate and their outputs are byte-identical.
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

## Launch history

The first four launches (label `indirectspec`, predeclare commit eb25b98a) failed to compile in
all four Editors. The fixture's manifest enables only the imageconversion, imgui and
jsonserialize modules, so `Collider` (PhysicsModule) does not resolve. The witness no longer
references it. Nothing was published. An offline compile check must use the Editor's own
reference set for this project (`Library/Bee/artifacts/*/Assembly-CSharp.rsp` of a copy), not
every module in `Managed/UnityEngine`.

### Attempt 2 (label `indirectspec-2`)

The witness, shader, scenes, bake, metas, settings and `analyze.py` are those of eb25b98a, minus the
two `Collider` references (3173269f). Before any launch, `preflight.py` compiled the witness and
`ProbeRunner` with the Editor's own Roslyn and the `Assembly-CSharp(-Editor).rsp` that the failed
Editors wrote for this fixture (references, defines and options unchanged; the gamma copies' files
are byte-identical). It also reproduced the original CS1069 pair on the eb25b98a witness. The
four-at-once runner was replaced by the sequential gated one above. Budget: four launches
(linear x2, gamma x2), no relaunch.

If the Editor exposes no serialized `m_BakedAmbientProbeInLinear`, `analyze.py --l0` takes the
offline parse of the same bake (LightingData.asset sha256 `e5de0e37…`):
`0.18028377 0.22571412 0.30692285`. That value only binds the input. It neither confirms nor
refutes anything.

Result (4 launches, all gated, repeats byte-identical; linear `ea1a9761…`, gamma `05e8aeb7…`):
one predeclared rule holds for all 8 settings, `A=one R=x M=srgb`. That is
`unity_IndirectSpecColor.rgb = L0 * reflectionIntensity` in linear, and
`LinearToGammaSpace(L0 * reflectionIntensity)` per channel in gamma; ambientIntensity has no effect.
The largest errors are 3e-9 (linear) and 6e-8 (gamma). Every other candidate's worst setting misses by at least 0.061.
The immediate capture equals the settled one in every setting. Alpha is reflectionIntensity (recorded,
not part of the rule). The Editor exposes no serialized ambient probe, so L0 is the offline parse above.
The Standard sphere (tier High, BRDF1) is 0.02 x ISC in linear and 0.72 x 0.2209163 x ISC in gamma,
within 3.4e-9, and its bytes are the predicted encodings.

Not covered: a Custom default reflection, `DynamicGI.UpdateEnvironment`, Skybox/Flat ambient
modes, other skyboxes, authored (rather than script-set) intensities, and bakes other than this
one.
