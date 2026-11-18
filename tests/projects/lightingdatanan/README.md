# Lighting data with non-finite values probe

Built-in render pipeline, linear colour space. `Assets/Editor/LightingDataNanSetup.Create` made
`Assets/Scenes` and baked it in the Editor (CPU lightmapper, no filtering or compression), so the
bake in `Assets/Scenes/Probe/` (LightingData, a lightmap EXR, the reflection probe's and the sky's
reflection EXRs) is the Editor's own. Every static object contributes to global illumination: the
ground is lightmapped, two quads receive it from light probes, one of them with lightmap UVs that
are all zero. A baked reflection probe (a clear green, capturing nothing) holds one mirror sphere;
another mirror sphere outside it reflects the baked sky. The sun is a baked directional light.

`Assets/Scripts/LightingDataNanProbe.cs` records what the scene's lighting data gives the scene
(each renderer's lightmap index and scale-offset and its realtime ones, floats in round-trip text,
the lightmap count, the ambient probe, the reflection probe's baked cubemap, the default
reflection), then renders the main camera (`frame.png`, 160x90) and the sky towards the sun
(`sun.png`).

Documentation: [Renderer.lightmapIndex](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Renderer-lightmapIndex.html)
(6000.0: -1/0xFFFF no lightmap, the default; 0xFFFE "internally used" for objects that affect
lightmaps without a lightmap of their own). It does not say what a script reads for 0xFFFE, nor
what the scale-offset of such a renderer is: those below are observations of this capture.

## What the Editor does (6000.0.71f1, Linux, OpenGL Core, NVIDIA driver; two identical captures)

* The bake gives the quad whose lightmap UVs have no area a lightmap scale-offset of
  (Infinity, Infinity, NaN, NaN), and scripts read it so. The other probe-lit quad keeps a finite
  one. Both read `lightmapIndex` 65534 (0xFFFE); renderers outside the bake read -1.
* The baked reflection probe's `bakedTexture`/`texture` and `ReflectionProbe.defaultTexture` are
  the bake's cubemaps.
* The baked sun lights no object at run time, yet the procedural sky is drawn with it (its
  scattering and its disk).

Unbound: every value matches; `image_limits.json` records the image residuals (the edge pixels
of the default sky's simple sun disk; the disk was drawn as the high-quality one's wide glow until
built-in Materials took their stored keywords, `tests/projects/builtinresources` `state/*`). Before the fix Unbound dropped the whole lighting
data of such a bake (the reply wrote the non-finite floats as JSON null): no reflection probe
cubemaps, a black default reflection, no lightmap placement.

    python3 tests/projects/lightingdatanan/run.py record --label A   # Editor capture (GPU)
    python3 tests/projects/lightingdatanan/run.py publish A B        # two identical captures
    python3 tests/projects/lightingdatanan/run.py gate               # the land gate

On macOS arm64 the gate compares with `Reference/unity-6000.0.71f1/metal/` (tests/value_probe.py
`metal`), two identical captures of the macOS Editor in batch mode (Metal) on an Apple M2 Pro: its
values are the OpenGL Core ones and its images differ from them by the device's rounding (means
0.19 and 0.10, no pixel over 8). Unbound on Metal: 15/15 values; `frame.png` mean 0.09, five pixels
over 8 (up to 10) on the spheres' top silhouettes, `sun.png` mean 0.62 (limit 0.63), ten pixels over 8 (up to
37) at the sun disk's edge, as against the OpenGL Core image (`image_limits-metal.json`).
