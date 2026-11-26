# Mipmap streaming answers

Built-in render pipeline, Play mode, Unity 6000.0.71f1 Editor (Linux, OpenGL Core; two identical
batch captures). What [mipmap streaming](https://docs.unity3d.com/6000.0/Documentation/Manual/TextureStreaming.html)
answers for textures and in `QualitySettings`/`Texture`'s counters
([Texture2D.requestedMipmapLevel](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Texture2D-requestedMipmapLevel.html),
[QualitySettings.streamingMipmapsActive](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/QualitySettings-streamingMipmapsActive.html)).

`Assets/Resources/stream.png` (64x64, importer streamingMipmaps on, priority 2) and `plain.png`
(off), and a runtime `Texture2D(64, 64, RGBA32, mips)`. `MipStreamingProbe` records each texture's
streaming members and the global settings and counters at five stages: as the scene loads
(`load`), after ten frames of a camera looking at a quad of each imported texture (`viewed`),
with `QualitySettings.streamingMipmapsActive` turned on (`active`), with `requestedMipmapLevel = 3`
on all three (`requested`), and after `ClearRequestedMipmapLevel`, `minimumMipmapLevel = 2` and
streaming turned off (`cleared`). Counters and memory totals depend on every texture the Editor
holds, so only whether they are 0 is recorded.

What it shows: a texture streams when its importer says so; loading and loaded levels are 0;
textures that do not stream keep -1 for requested, minimum, calculated and desired levels and
ignore a requested level; a streaming texture starts with minimum level 8, takes a requested level
clamped to `streamingMipmapsMaxLevelReduction` (3 reads back 2) and then reports it as not loaded.
`residuals.json` lists what Unbound does not simulate: the counters and memory totals, and the
level the streaming system calculates while active (the Editor's 2 here).

```sh
python3 tests/projects/mipstreaming/run.py record --label a   # and b
python3 tests/projects/mipstreaming/run.py publish a b
python3 tests/projects/mipstreaming/run.py gate
```
