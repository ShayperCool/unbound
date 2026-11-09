# Reflection and light probe probe (built-in render pipeline)

Built-in render pipeline, linear color space, the Ultra quality level of builtinfx (realtime
reflection probes on). `Assets/Scripts/ReflectionProbesProbe.cs` runs in Play mode and writes
into `REFLECTION_PROBES_OUT`:

* **Imported glossy convolution** (`import_*.bin`, `import.*`): the mips of cubemaps imported with
  "Convolution Type: Specular" (6:1 strips, readable, uncompressed): float `Noise32` (six mips)
  and `Noise16` (five), byte `NoiseSrgb32` and `NoiseLinear32` (the same pixels, sRGB on and off).
* **Realtime convolution** (`realtime_sky32.bin`, `realtime.*`): a realtime probe (resolution 32,
  HDR) of a skybox showing the `Sky32` cubemap (float texels within 0..1), its cube's mips read
  back as half floats; `realtime_hdr_sky32.bin`: the same at exposure 4, values above 1;
  `realtime_texels_sky32.bin`: a run-time RGBAFloat cubemap with texels from 0.5 to 8.
* **Probe weights** (`weights.*`): `Renderer.GetClosestReflectionProbes` for cubes along two
  overlapping custom probes (A over x -2..2, B over 0..4): equal importance, B half the size, no
  blend distance, B more important, and a third probe C between them.
* **Blended frames** (`forward_*`, `deferred_*`): a glossy floor and spheres across A and B for
  the same cases plus box projection, in Forward and Deferred Shading.
* **Glossy spheres** (`glossy_realtime`, `glossy_imported`): metal spheres of smoothness 1, 0.9,
  0.75 and 0.5 in a realtime probe of coloured boxes and in a custom probe of the convolved
  `Environment64` cubemap.
* **Probe constants** (`sh.*`): unity_SHAr..unity_SHC and unity_LightmapST of a quad drawn with
  `Assets/Shaders/SHDump.shader`, not lightmapped and lightmapped (index 0 of a runtime
  lightmap), probes blended or off, forward and deferred, under a set ambient probe.

`ReflectionProbesRunner.Setup` wrote `Assets/Cubemaps` and the scene in the Editor.

Record and compare (`run.py` documents every mode):

    python3 tests/projects/reflectionprobes/run.py editor --release 6000.0.71f1 --label L
    python3 tests/projects/reflectionprobes/run.py publish --release 6000.0.71f1 --label L
    python3 tests/projects/reflectionprobes/run.py gate --release 6000.0.71f1

`compare.py` checks values (as builtinfx), cube mips (imported float within 0.5% of each mip's
largest value, bytes within 1, realtime half floats within 1%) and images (mean at most 2).

## What the Editor does (6000.0.71f1, Linux, OpenGL Core)

Documentation: [Reflection Probe](https://docs.unity3d.com/6000.0/Documentation/Manual/class-ReflectionProbe.html)
(importance, blend distance, box projection), [Cubemap import](https://docs.unity3d.com/6000.0/Documentation/Manual/class-Cubemap.html)
("Specular (Glossy Reflection)" convolution), [Deferred Shading](https://docs.unity3d.com/6000.0/Documentation/Manual/RenderTech-DeferredShading.html)
(per-pixel probe blending). None documents the filter, the probe order beyond importance or the
probe constants of lightmapped renderers: those below are observations of this capture.

* An imported cube's mip i of the first min(mip count, 7) is the GGX lobe of perceptual roughness
  i/(n-1) around each texel's direction (`crates/unbound-assets/src/texture/specular.rs` says how),
  further mips box-filtered. Byte faces are convolved in linear light whatever their sRGB setting
  and stored gamma-encoded; float faces stay linear.
* A realtime probe's mips are a Gaussian chain of Unity's Hidden/CubeBlur read back with
  Hidden/CubeCopy at the level of each mip's roughness (`ReflectionProbe.Convolution.cs`).
* Probes are ordered by importance, then by the share of the renderer's bounds they hold, then by
  the distance of their box's centre (tied probes in enabled order). Equal importance shares the
  weight by what each holds; a more important first probe weighs its share. Every touching probe is
  listed; past the first two with weight 0. Blend distance does not change the weights.
* A box-projected probe's per-object box (forward) grows to hold the renderer's bounds: the large
  floor of `forward_box` reflects as if the box were the floor's.
* A lightmapped renderer draws with the ambient probe's constants, whatever its probe usage: the
  built-in pipeline does not zero them as URP does. (A separate capture on tests/projects/baked's
  bake, with light probes, showed the same; it is not part of this reference.)
* Forward draws take probe changes (importance, size, box projection) from the next frame on;
  the probe waits a frame after each change. Unbound applies them at once (an open gap).
