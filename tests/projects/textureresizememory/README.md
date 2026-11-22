# Texture memory during reinitialization

Unity 6000.0.71f1 profiles Texture2D before reinitialization, after replacing
the CPU image, and after uploading it with Apply. The fixture covers same-area
and changed-area resizing, with and without an earlier upload, plus mipmaps,
block storage, a second resize, an explicit GraphicsFormat, and a final
Apply that discards CPU readability. It records the graphics API so each
capture is compared against that device's measurements.

[Unity 6.0 Texture2D.Reinitialize](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.Reinitialize.html)
requires readable input and directs callers to Apply to upload the changed
pixels. The profiler's byte counts and their device differences are measured
by this probe.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/textureresizememory/run.py record --device opengl --label first
python3 tests/projects/textureresizememory/run.py record --device opengl --label second
python3 tests/projects/textureresizememory/run.py publish first second --device opengl
python3 tests/projects/textureresizememory/run.py gate
```

Use Vulkan for the second Linux device and Metal on macOS. Publishing requires
two independent agreeing captures of the selected device and release.
