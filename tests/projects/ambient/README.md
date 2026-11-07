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

The Q105 sun controls are opt-in and do not change the original capture or its flat reference.
Run the engine-only gate with an explicit release:

    UNBOUND=/path/to/unbound python3 tests/projects/ambient/run.py gate --release 6000.0.71f1 --label unique-label

`gate.json` declares this command using `$UNBOUND_PROBE_UNITY_VERSION`. The gate prints the
original comparator and raw image comparison, then verifies every record, field and coefficient,
including missing/extra values, with the original 0.0025 coefficient tolerance. Its JSON report
and both engine logs stay in the validation cache. Each label must be new. The declared gate also
runs five negative-path tests for failed Editor runs, incomplete captures and durable failure reports.

The six sun controls compare the default/tinted sky with None, High Quality, and High Quality
with zero sun size. They record both `ambientProbe` and an independent 64-texel camera projection
of the visible sky. Two independent GPU Editor captures on 6000.0.71f1 agreed byte-for-byte before
publishing `Reference/unity-6000.0.71f1/sun.jsonl`. Reproduce in fresh project copies:

    python3 tests/projects/ambient/run.py record --release 6000.0.71f1 --label unique-reference-label
    python3 tests/projects/ambient/run.py publish --release 6000.0.71f1 --label unique-reference-label

Publication refuses to replace a reference. The sampling window allows unchanged results and
checks sampled stability; it establishes no fixed update latency. Editor captures show identical
ambient SH across all three controls, while visible-sky projections differ. The old sun SH values
also exactly match the new ambient values. Before the fix Unbound projected the visible disk into
ambient SH. The fix omits its radiance only in the temporary built-in procedural environment
capture. The original material, selected shader variant, sun direction and ordinary sky draws
remain intact. Setting `_LightColor0` to zero for that draw follows the installed official shader:
it scales `sunColor`, while scattering uses `_WorldSpaceLightPos0`. This is our implementation,
not a claim about Unity's internal integration mechanism.

Unity 6000.0 contracts checked:

- [RenderSettings.sun](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderSettings-sun.html)
  selects the procedural light, falling back to the brightest directional light.
- [Procedural skybox](https://docs.unity3d.com/6000.0/Documentation/Manual/shader-skybox-procedural.html)
  documents None/Simple/High Quality controls and supports Built-in/URP, not HDRP. The measured
  integration here is Built-in only; no additional pipeline or Simple parity is claimed.
- [DynamicGI.UpdateEnvironment](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/DynamicGI.UpdateEnvironment.html)
  schedules a cubemap update. Async results may lag frames and excess requests may be dropped;
  without async readback, processing stalls the thread. There is no guaranteed frame delay.
- [ambientProbe](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderSettings-ambientProbe.html)
  supplies default environment lighting for objects without manually associated probes/lightmaps;
  changing it does not change baked or Enlighten lighting results. Exact coefficients and sun
  exclusion are observations from this release and fixture, not documented guarantees.
- [Light.color](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Light-color.html)
  is emitted color, with black equivalent to no illumination.
  [Color space](https://docs.unity3d.com/6000.0/Documentation/Manual/LinearRendering-LinearOrGammaWorkflow.html)
  distinguishes linear shader inputs from gamma-authored data. These controls preserve the
  fixture's linear space and explicit sun color/intensity, without changing general light conversion.
  [lightsUseLinearIntensity](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.GraphicsSettings-lightsUseLinearIntensity.html)
  specifies whether intensity multiplies linear or gamma color values. The fixture retains false.

Installed source inspected: official 6000.0.71f1 built-in archive,
`DefaultResourcesExtra/Skybox-Procedural.shader`, `vert`, `getMiePhase`, `calcSunAttenuation`, `frag`.
The archive content hash is `fe83b43c6b85818aa46dcb53b014ad15144202095df6283d85c490847f3ee90e`.
Source publication and its process-wide resolver are unchanged.
