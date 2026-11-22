# ASTC fallback allocation accounting

Unity 6000.0 documents that `Profiler.GetRuntimeMemorySizeLong` reports the native memory attributed to a Unity object. The texture format documentation describes platform-dependent texture storage. The byte attribution below is measured in the 6000.0.71f1 Linux Editor on NVIDIA OpenGL Core. [Profiler API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Profiling.Profiler.GetRuntimeMemorySizeLong.html), [Texture formats in memory](https://docs.unity3d.com/6000.0/Documentation/Manual/class-TextureImporterOverride.html)

Each profile runs in a new process. The first initialized ASTC fallback adds 3,200 bytes to the 2D texture or cubemap that uses it. Each additional footprint or HDR decoding profile adds 8 bytes to its first owner. Linear and sRGB textures of the same LDR footprint share the charge. Later textures use it without a new charge, including after the owner is destroyed. A clone leaves the charge on its source. Reinitialize preserves existing setup bytes. Its new storage starts CPU-only, and Apply uses the new format. An uninitialized constructor starts with CPU blocks and uses the normal allocation header. The initial setup occurs on its first Apply unless another texture needs the fallback first.

The initialized 2D array reserves the setup before its first Apply. Its reported memory includes its allocation header and texture storage. The Android 2D import control loads a readable ASTC image via Resources and records the charge on that texture. The HDR profile also measures the RGBAHalf GPU storage.

Unity checks sampling support for the explicit `GraphicsFormat` cube and array constructors. Following an unsupported request, the probe records the setup charge of the next eligible `TextureFormat` texture. The explicit constructors' return values and diagnostics are outside this comparison. [Unity 6000.0 constructor validation](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.0/Runtime/Export/Graphics/Texture.cs#L651-L666)

The OpenGL Core Editor refused a compressed Texture3D in the volume control. Unbound currently creates it, and the gate reports the status and extra size observations by name. Texture bundle loading, imported layered textures, HDR image import, image reload, native or external textures, other render devices and other release families are outside these captures.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/textureastcmemory/run.py record --profile Ordinary --label first
python3 tests/projects/textureastcmemory/run.py record --profile Ordinary --label second
python3 tests/projects/textureastcmemory/run.py publish --profile Ordinary first second
python3 tests/projects/textureastcmemory/run.py gate
```

Publishing checks two independent sealed captures per profile. Linux engine runs use disposable projects and compare every value exactly. The gate reports a skip on other platforms.
