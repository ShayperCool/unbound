# Render resources probe

Render resources and low-level graphics as the Editor renders them (`Assets/Scripts/ResProbe*.cs`,
gamma color space, built-in pipeline, the Ultra quality level: MSAA 2; cameras render 160x160
textures with `Camera.Render`, 2D cameras at 32 pixels per unit, a few cases wait a frame):

* **Sprite masks** (`mask_*`, `mask.*`): each mask interaction, alpha cutoffs, the range a mask
  covers without and with a custom range (within a layer, across layers, an empty one), overlapping
  masks with different ranges, masks and sprites in sorting groups (nested, one mask per group, the
  order masks and sprites were made in), a disabled mask and one without a sprite, a mask whose
  source is the sprite renderer beside it, a target without a depth buffer, lines, trails,
  particles and tilemaps with a mask interaction; the range setters' rules.
* **Tiled and sliced sprites** (`tiled_*`, `sliced_*`, `size.*`): Continuous and Adaptive tiling
  with and without borders, adaptive thresholds over tiles of three sizes, sizes below the borders,
  flips, scales, tight sprites; what a renderer's size and its transform's scale do as sprites and
  draw modes change. The tile sprites' texels encode their column and row (red 16 + 18x, green
  16 + 20y, blue 64 on the border), so an image shows which texel lands where.
* **Secondary textures and vertex attributes** (`secondary.*`, `secondary_textures`,
  `sprite.attributes`, `defaults.texels`): imported and script-created sprites, empty and short
  destination arrays, null arguments, texture precedence over material property blocks, shader
  default texels, normals and tangents in Simple, Sliced and Tiled modes with all four flip states.
* **Command buffer renderers** (`command_early*`, `command_late*`): renderer and material-slot
  property blocks, a moving bone and blend shape, changes after recording the commands, at
  BeforeForwardAlpha and AfterEverything.
* **Texture mip chains** (`mips.*`): manual levels and GPU `tex2Dlod`, generation with power-of-two
  and NPOT sizes, byte and float formats, raw chains and `SetPixelData`, imported mipmaps and
  serialized `.asset` levels, constructor validation and access after discarding readability.
  `ProbeRunner.CreateMipAssets` created the two mip assets in the Editor.
* **RenderTexture mip chains** (`rtmips.*`): rendering into individual levels, explicit,
  automatic and command-buffer generation, ARGB32/Half/Float and RFloat precision, MSAA resolve,
  cubemap faces and texture-array slices. The layered sampling shader deliberately uses an HLSL
  entry point named `array`, which the WGSL compiler renames.
* **AssetBundle mip chains** (`bundlemips.*`): Editor-built DXT1 and DXT5 assets, four distinct
  mip colors, CPU/GPU reads, original texture/graphics formats and exact encoded raw data.
  `ProbeRunner.CreateMipBundle` creates the assets and `Reference/mip-bundles/mips`.
* **Low-level graphics** (`lowlevel.*`): scissor rectangles on draws, clears and blits, fractional,
  offscreen and empty rectangles, viewport intersection, different MRT clear colors, recorded
  color-array copying and retained MRT bindings, clear-color argument errors and Editor dynamic
  resolution factors.
* **Float component formats** (`floatformats.*`): R/RG/RGBA half and full float textures,
  Texture2DArray, Texture3D, Cubemap, CubemapArray and render targets. The 78 values compare exact
  float bits, including negative and HDR components, CPU quantization, missing channels and
  sampler changes after discarding readability. A numeric tolerance would hide float-to-half loss.

`ProbeRunner.Create` made the sprites in `Assets/Resources` (the Editor wrote them).

Record Unity's reference (the Editor needs a display; it renders on the GPU):

    RESOURCES_PROBE_OUT=$PWD/tests/projects/resources/Reference \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/resources \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/resources.log

Run Unbound and compare (`RESOURCES_PROBE_ONLY=<part>` runs the parts whose name starts with it):

    RESOURCES_PROBE_OUT=/tmp/resources unbound run -p tests/projects/resources --frames 2000 --size 640x480 --no-watch
    python3 tests/projects/resources/compare.py tests/projects/resources/Reference /tmp/resources

The comparison exits with failure for missing output, differing values or images whose mean
largest-channel difference exceeds 2/255. Its failure cases run with
`python3 tests/projects/resources/test_compare.py`.

## What the Editor does (6000.0.71f1, Linux, OpenGL Core)

* A renderer with a mask interaction draws inside (stencil 1 or more) or outside (0) the masks
  covering its place in the draw order. A mask covers the draws of its innermost sorting group
  (nested groups included; every draw when it is in none): all of them without a custom range,
  else those after its back and up to its front (a nested group counts at its own place). At the
  root a draw tying with the back or the front sorts before the mask's own draw; in a group the
  order the renderers were made in decides. Overlapping masks add up. The mask draws its sprite
  with its material, pixels under the alpha cutoff discarded; a SupportedRenderers mask takes the
  sprite renderer beside it (its sprite, not its color). Disabled masks and masks without a
  sprite mask nothing. A target without a depth buffer still masks (the camera draws into the
  quality level's MSAA buffer).
* The range setters keep the back from sorting after the front: the back's layer, then its order,
  are pulled down to the front's (so set the front first). A new mask: cutoff 0.2, source Sprite,
  the Sprites-Mask material.
* Sliced: borders keep their size unless the size is smaller than both together, then both shrink
  in proportion, squeezing their texels. Tiled: the same shrink cuts the borders instead (texels
  keep their size; the low border loses its inner end, the high one its outer end); the centre
  repeats from the low end, the last tile cut (Continuous), or stretches a whole number of tiles,
  at least one, adding one once the leftover fraction of a tile reaches the threshold times the
  tile's size in units (Adaptive). A tight sprite tiles as its full rect (with a warning).
  Bounds are the size around the pivot, 0.2 deep.
* A first sprite sets the renderer's size to its own (a later one leaves it); leaving Simple
  multiplies the transform's scale by the sprite's size over the renderer's, entering it by the
  inverse.
* Sorting groups made by a script sort from the next frame (renderers drawn at once sort by their
  own orders), so the group cases wait a frame.
* A sprite's secondary textures replace the material's and its property block's textures of the
  same name. `GetSecondaryTextures` copies as many entries as fit and returns that count; a null
  array throws `ArgumentNullException`.
* A declared `"gray"` texture default samples RGBA (127, 127, 127, 127), while an unassigned
  sampler with no ShaderLab property samples (128, 128, 128, 128). Keeping these distinct also
  preserves the built-in pipeline's camera-texture event probe.
* Sprite vertex normals point along -z. Tangents start at (1, 0, 0, -1); flipX negates their x,
  and exactly one flip negates their w. Flips affect vertex data, leaving the object's matrix
  alone. The three draw modes give the same attributes.
* `DrawRenderer` uses the current renderer geometry and property block each time the recorded
  command executes. A material-slot block replaces the renderer block. Bone motion and blend
  shapes are visible, including changes made after recording the commands.
* Runtime byte mip generation interpolates separately in x and y, truncating after each axis.
  CPU `GetPixelBilinear` addresses at u times width and v times height, unlike GPU sampling's
  texel-center convention. Texture color setters round byte half-values upward. A constructor
  mip count of zero requests the full chain; more levels than the dimensions allow fail.
* In the Editor, non-generic `GetRawTextureData()` still returns the data after
  `Apply(false, true)`. Pixel APIs respect `isReadable`: `GetPixels`, `GetPixels32` and
  `SetPixels32` throw `ArgumentException`; individual-pixel APIs, `SetPixels` and `Apply` throw
  `UnityException`.
* `Graphics.Blit` leaves its destination active. Subsequent clears affect that destination,
  while the previously active mip remains unchanged. Explicit MSAA resolve does not create an
  uncreated destination; it copies to a destination already created.

## What differs

* `mask_ranges_*`: a few edge pixels of the stretched tight circle mask (Unity generates the tight
  mesh's outline in native code; Unbound masks with the full rect and the alpha cutoff).
* `command_late*`: edge antialiasing differs. Without image effects Unbound keeps late camera
  command buffers in the scene's MSAA target so they retain scene depth, then resolves it; the
  Editor's late draws have sharper edges. Mean difference is at most 0.57/255 in these cases.
