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

`ProbeRunner.Create` made the sprites in `Assets/Resources` (the Editor wrote them).

Record Unity's reference (the Editor needs a display; it renders on the GPU):

    RESOURCES_PROBE_OUT=$PWD/tests/projects/resources/Reference \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/resources \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/resources.log

Run Unbound and compare (`RESOURCES_PROBE_ONLY=<part>` runs the parts whose name starts with it):

    RESOURCES_PROBE_OUT=/tmp/resources unbound run -p tests/projects/resources --frames 2000 --size 640x480 --no-watch
    python3 tests/projects/resources/compare.py tests/projects/resources/Reference /tmp/resources

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

## What differs

* `mask_ranges_*`: a few edge pixels of the stretched tight circle mask (Unity generates the tight
  mesh's outline in native code; Unbound masks with the full rect and the alpha cutoff).
