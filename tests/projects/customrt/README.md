# Custom render textures

Unity 6000.0.71f1, built-in pipeline, Linear color space (`Assets/Scripts/CustomRtProbe.cs`).

Every section makes its custom render textures from script (or loads `.asset` ones), runs a few
frames at a fixed 0.05 s delta time and reads them back as RGBA floats. Four frame points record
the bottom-left pixel of each live texture, and of its second buffer when it is double buffered
(`t.<section>.<frame>.<point>`): `u` after the probe's Update, `b`/`a` right before and after
`PostLateUpdate.UpdateCustomRenderTextures` (systems the probe inserts around it), `e` at the
end of PostLateUpdate. `ev.<section>` lists the manager's events as `frame:point:event:name`,
`log.<section>` the log messages.

* **api**: constructor defaults (formats, depth 24, modes, colors, masks), update zones round
  trip, `GetAllCustomRenderTextures`.
* **init**: initialization by color (float and 8-bit targets), texture and color, texture
  alone, defaults, a material (texcoords and `_CustomRenderTextureInfo`), a two-pass material,
  Material source without a material, a material ignored by TextureAndColor, and an
  initialization followed by an update in the same frame.
* **modes**: every initialization/update mode pair as a counter adding 1 per update (Unity's
  `_SelfTexture2D`), with and without a material, `Update()` in the frame a texture is made, a
  single-buffered counter, a texture created with `Create()`.
* **calls**: `Update(count)`, `Initialize()`, both in one frame, `Update(0)`.
* **period**: `updatePeriod` 0, 0.04, 0.05, 0.12 and 1 s, then three frames at time scale 0.
* **zones**: normalized and pixel zones, rotation, per-zone passes (-1: `shaderPass`),
  wrapping, twenty zones (more than one batch), overlapping zones, shader variables.
* **double**: the two buffers after one and three updates, partial zones, `needSwap` zones,
  a single buffer with a self-reading material.
* **depend**: a texture whose material reads another texture made after it, and one made
  before it.
* **cube**: face masks, the direction and face variables, initialization materials with and
  without a face mask, the self cube.
* **volume**: a 3D texture's slices: texcoords, parameters, zones, the self volume.
* **volumeSelf** (after the other sections, added later): a double-buffered volume updated with
  the parameters pass, then with a material reading `_SelfTexture3D`; both buffers read.
* **changed** (last, added later): one property set on each loaded texture after an update
  (material, initialization material, color, texture, source and mode, update mode, face mask,
  shader pass, and the same material or color again), read before and in the next two frames;
  what an initialization material does to the initialization texture and color.
* **later** (last, added later): a double-buffered volume reading itself updated in two frames
  in a row and with `Update(2)`, both buffers read; textures updating on load and initialized
  by request whose material is changed after the shader pass was set to the info pass (the
  OnLoad update runs again, with that pass), or whose pass alone is set (nothing runs).
* **variables**: the custom render texture variables a blit sees before and after an update.
* **loading**: default textures made late in the run (untouched, created, a mode set, a
  material set): when they load and what the manager lists.
* **assets**: `.asset` textures from `Resources` (a realtime double-buffered counter and a
  pixel-space zoned texture with per-zone passes), then `Resources.UnloadAsset`.

## Recording

```
python3 tests/projects/customrt/run.py editor --release 6000.0.71f1 --label <fresh-label>
python3 tests/projects/customrt/run.py publish --release 6000.0.71f1 --label <fresh-label>
UNBOUND_CLI=/path/to/unbound python3 tests/projects/customrt/run.py unbound --release 6000.0.71f1 --label <fresh-label>
python3 tests/projects/customrt/compare.py tests/projects/customrt/Reference/unity-6000.0.71f1 \
    ~/.cache/unbound/validation/runs/customrt/<label>/unbound --report <cache>/report.json
```

`run.py create` remakes the scene, the two materials and the two `.asset` textures in a
batchmode Editor (`ProbeRunner.Create`); the committed ones come from it. Editors run windowed
on private Xvfb displays with the GPU through VirtualGL; `publish` requires two byte-identical
complete captures (verified NVIDIA renderer, exit status 0, `complete: true` last, exactly the
recorded images) and refuses to overwrite a reference. The comparator keeps the whole key union
and image manifest, with the rtdepth probe's float tolerance (absolute and relative 2e-4).

`Reference/unity-6000.0.71f1/` holds 352 values and 140 images from two byte-identical captures
(OpenGLCore 4.5, RTX 4060).

## What the Editor does (6000.0.71f1, Linux, OpenGL Core)

* A new custom render texture has depth 24 (D24S8), initialization mode OnDemand, update mode
  OnLoad, a white initialization color, face mask 0xFFFFFFFF; `(w, h)` is `R8G8B8A8_SRGB` in a
  Linear project. `SetUpdateZones(null)` throws ArgumentNullException.
* It is first seen in the next `UpdateCustomRenderTextures`: `textureLoaded` fires there, the
  texture is created, OnLoad (and Realtime) initialization and an OnLoad update run in that
  frame, Realtime updates from that frame on. `Update(n)` and `Initialize()` fire
  `updateTriggered`/`initializeTriggered` at the call and run at the next update point,
  initialization first. Destroying the texture or `Resources.UnloadAsset` fires `textureUnloaded`.
  `GetAllCustomRenderTextures` lists the loaded textures. Textures made in the first frame of
  Play mode (the api section) are not loaded by that frame's update point (no event, not
  created, not listed); textures made later load in their frame (the loading section).
* Initialization writes both buffers: with the material's first pass only (Material source),
  else the texture times the color converted to linear (sampled with the texture's own filter),
  else the color unconverted. Cubemaps initialize every face whatever the face mask.
* An update renders into the second buffer reading the first as `_SelfTexture2D` (`_SelfTextureCube`
  for cubemaps); the buffers swap before each update except the first after an initialization
  (or after loading), and before each zone with `needSwap`. A single buffer binds nothing (the
  gray default texture). Reading the texture therefore shows the previous update.
* Realtime textures update once their accumulated scaled delta time reaches `updatePeriod`
  (then it restarts from 0); the load frame's delta counts.
* Zones: y counts from the top in normalized and pixel space, rotation in degrees, a zone's
  `passIndex` replaces `shaderPass` (-1 keeps it), ids run over all zones (more than sixteen
  draw in several batches), later zones draw over earlier ones, wrapping zones repeat across
  the opposite edges. Without zones the whole texture draws with `shaderPass`.
* `_CustomRenderTextureInfo` is (width, height, depth, face or slice), the depth 1 for 2D
  textures and cubemaps; `CustomRenderTextureParameters` is (pixel space, the face for cubemaps
  or slice / depth for volumes, 3D, 0), also for initialization materials. None of these
  variables is visible to other draws after an update.
* Textures update after the textures their materials read, whichever was made first.
* 3D custom render textures work in the Editor (a depth buffer makes their creation fail with
  "3D textures with depth are not supported"): every slice is initialized and updated, with the
  slice in `_CustomRenderTextureInfo.w` and slice / depth as the third texcoord; a zone's z
  (scaled by the depth in normalized space) selects the slices it updates.
* A change of the material, the initialization material or color, the initialization or
  update mode or the face mask of a loaded texture queues it again as on its load: its OnLoad
  initialization and OnLoad update run at the next update point (`changed.*`; in `volumeSelf` the
  second material re-initializes the volume, so its update reads the initialization color). The
  same value again, the shader pass, the initialization texture and source do not. Setting an
  initialization material clears the initialization texture and makes the color white.
  The OnLoad update queued again runs with the shader pass the texture has then
  (`later.materialUpdate`; setting the pass alone runs nothing, `later.passOnly`).
* A double-buffered volume's update reads, through `_SelfTexture3D`, the buffer written last,
  also between the updates of one `Update(2)` (`later.volumeFrames`, `later.volumeTwice`).

## Unbound (2026-11-12)

446 of 448 values and 228 of 228 images match. The two values left: the api section's
textures, made in the first frame of Play mode, load in that frame in Unbound (`ev.api`,
`api.all.next`); the Editor never loads them (it lists none of them in the next frame and sends
them no events), while textures made in later frames load in their first frame in both.
Initializing a 3D custom render texture from a texture is not supported (an error, and the
initialization color instead); the probe does not record it in the Editor.
