# Mipmap streaming answers

Built-in render pipeline, Play mode, Unity 6000.0.71f1 Editor (Linux, OpenGL Core; two identical
batch captures). What [mipmap streaming](https://docs.unity3d.com/6000.0/Documentation/Manual/TextureStreaming.html)
answers for textures and in `QualitySettings`/`Texture`'s counters
([Texture2D.requestedMipmapLevel](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D-requestedMipmapLevel.html),
[QualitySettings.streamingMipmapsActive](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/QualitySettings-streamingMipmapsActive.html)).

`Assets/Resources/stream.png` (64x64, importer streamingMipmaps on, priority 2) and `plain.png`
(off), streaming `big.png` (1024x1024), `tiny.png` (4x4) and `nomip.png` (64x64, mipmaps off), and a
runtime `Texture2D(64, 64, RGBA32, mips)`; at `requested` also a copy of `stream` (Instantiate)
and a negative request on `tiny`; at the end a minimum level of 0, then 1, on `big` with and
without a request, then cleared, 3 and -1 (`minimum*`). `MipStreamingProbe` records each texture's
streaming members and the global settings and counters at five stages: as the scene loads
(`load`), after ten frames of a camera looking at a quad of each imported texture (`viewed`),
with `QualitySettings.streamingMipmapsActive` turned on (`active`), with `requestedMipmapLevel = 3`
on all three (`requested`), and after `ClearRequestedMipmapLevel`, `minimumMipmapLevel = 2` and
streaming turned off (`cleared`). Counters and memory totals depend on every texture the Editor
holds, so only whether they are 0 is recorded.

What it shows: a texture streams when its importer says so; loading and loaded levels are 0;
textures that do not stream, and a streaming one without mipmaps, keep -1 for requested, minimum,
calculated and desired levels and ignore a requested level; a streaming texture starts with
minimum level 8 whatever its size (3, 7 and 11 mips), takes a requested level clamped to 0 and to
`streamingMipmapsMaxLevelReduction` and its last mip (3 reads back 2, -4 reads back 0) and then
reports it as not loaded. While streaming is active, a texture the camera does not draw is
calculated at the maximum level reduction (2, and 5 at `reduction5`, also for the 4x4 texture);
the desired level is the requested one when set (whatever the minimum), else the calculated one
capped by the minimum (`minimum0`: 0); a set minimum is clamped like a request (3 reads back 2, -1
reads back 0) and a cleared one reads 8 again. A copy streams with
its source's priority and minimum 8, has no requested level and no calculated level, and reports
the source's level 2 as loaded. `residuals.json` lists what Unbound does not simulate: the
counters and memory totals, and the level of a drawn texture from its size on screen
(`reduction5/stream`, the Editor's 2). While the texture without mipmaps is loaded with streaming
active, the Editor's calculated levels of it and of `plain.png` swap between runs: it is recorded
before streaming is active and unloaded.

```sh
python3 tests/projects/mipstreaming/run.py record --label a   # and b
python3 tests/projects/mipstreaming/run.py publish a b
python3 tests/projects/mipstreaming/run.py gate
```
