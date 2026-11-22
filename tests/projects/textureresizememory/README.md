# Texture memory during reinitialization

Unity 6000.0.71f1 profiles Texture2D before reinitialization, after replacing
the CPU image, and after uploading it with Apply. The fixture covers same-area
and changed-area resizing, with and without an earlier upload, plus mipmaps,
block storage, a second resize, an explicit GraphicsFormat, and a final
Apply that discards CPU readability. Separate controls check that successful
compression completes the texture allocation, recompressing a block texture
leaves it unchanged, and unreadable input retains the full reported allocation.
It records the graphics API so each
capture is compared against that device's measurements.

[Unity 6.0 Texture2D.Reinitialize](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.Reinitialize.html)
requires readable input and directs callers to Apply to upload the changed
pixels. The profiler's byte counts and their device differences are measured
by this probe for owned runtime textures. Imported and externally backed
textures retain the previous accounting. The unreadable controls cover memory
only. The return or exception from an unreadable Reinitialize remains open.
[Unity 6.0 Texture2D.Compress](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.Compress.html)
documents that an already compressed texture is left alone.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/textureresizememory/run.py record --device opengl --label first
python3 tests/projects/textureresizememory/run.py record --device opengl --label second
python3 tests/projects/textureresizememory/run.py publish first second --device opengl
python3 tests/projects/textureresizememory/run.py gate
```

Use Metal on macOS. Publishing requires two independent agreeing captures of
the selected device and release. The Metal overhead is 960 bytes in both
states. The OpenGL Core overhead is 880 bytes before uploading the new image
and 936 after. Raw Vulkan repeats differed during uploads in the first
resize sequence. Vulkan and other release families retain earlier accounting.
