# Texture memory during reinitialization

Unity 6000.0.71f1 profiles Texture2D before reinitialization, after replacing
the CPU image, and after uploading it with Apply. The fixture covers same-area
and changed-area resizing, with and without an earlier upload, plus mipmaps,
block storage, a second resize, an explicit GraphicsFormat, and a final
Apply that discards CPU readability. Separate controls check that successful
compression completes the texture allocation and recompressing a block texture
leaves it unchanged. For the script-created, owned textures in the fixture,
unreadable input keeps its dimensions, format, mip count, readability and
reported allocation across all tested overloads and invalid-dimension attempts.
The graphics API identifies the measurements used for comparison.

[Unity 6.0 Texture2D.Reinitialize](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D.Reinitialize.html)
requires readable input and directs callers to Apply to upload the changed
pixels. The profiler's byte counts and their device differences are measured
by this probe for owned runtime textures. Imported and externally backed
textures retain the previous accounting. For the measured unreadable textures,
the explicit TextureFormat overload logs an error and returns false. The other
overloads throw UnityException without logging. The behavior and diagnostics of
resizing an external texture are unmeasured and retain their previous handling.
The LoadImage success control separately
records the existing readability difference when replacing an unreadable image.
[Unity 6.0 ImageConversion.LoadImage](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/ImageConversion.LoadImage.html)
documents that it replaces the image and uploads it automatically.
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
