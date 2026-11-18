# Vertex colors

The colors renderers put in their vertices and per-draw constants, in the Gamma and the Linear
color space, through the built-in render pipeline and URP 17 (the Editor's built-in package,
unmodified): its Universal renderer with and without the SRP Batcher, and its 2D renderer.
`Assets/Scripts/VertexColorProbe.cs` builds every case at runtime: nine colors (channels on both
sides of the sRGB curve's knee, dark channels, channels whose linear byte depends on where the
color is rounded to 8 bits, channels outside 0..1, alphas below one and zero), each drawn as a
quad of half a unit on a grid of three by three in front of a camera of its own, into a float
target (`ARGBFloat`, linear), and records the pixel at each quad's center. The pipeline is set at
runtime (`GraphicsSettings.defaultRenderPipeline`, `UniversalRenderPipelineAsset.useSRPBatcher`);
the project starts with the built-in pipeline.

Cases (`<pipeline>.<case>.<quad>.<channel>`; pipelines `builtin`, `universal`, `2d`, `nobatcher`):

- `sprite.stock`, `sprite.material`, `sprite.raw`, `sprite.urpraw` (URP only): SpriteRenderer.color
  over a white texture, drawn by the pipeline's sprite shader (`Sprites/Default`; URP's
  `2D/Sprite-Unlit-Default`), by that shader with a material color, by `Probe/VertexColor` (a CG
  shader that writes the interpolated vertex color unchanged) and by `Probe/UrpVertexColor` (the
  same on URP's include).
- `tilemap.chunk`, `tilemap.individual`, `tilemap.raw`, `tilemap.tinted`,
  `tilemap.individualtinted`, `tilemap.urprawindividual` (URP only): Tilemap.SetColor per cell, drawn in Chunk and Individual mode, by
  the raw shader, and with a tilemap color.
- `spriteshape.stock`, `spriteshape.raw`: SpriteShapeRenderer.color of a filled square written
  through `GetSegments`/`GetChannels`/`Prepare` without a color channel, as the sprite shape
  package writes them (`spriteshape.urpraw`: by `Probe/UrpVertexColor`); `spriteshape.channels`: a white renderer whose vertices carry the color.
- `line.raw`, `trail.raw`: LineRenderer and TrailRenderer of a solid color gradient.
- `particles.start`, `particles.lifetime`: one still particle of a start color, or white with a
  constant color over its lifetime.
- `mesh.colors`, `mesh.colors32`: a mesh's own vertex colors (controls).
- `ui.image`: uGUI images on a world-space canvas with the default UI material (control).
- `<pipeline>.{sprite,tilemap,spriteshape}.defaultmaterials`: the materials of a renderer a
  script adds.

## Measured (Unity 6000.0.71f1, Linux, NVIDIA OpenGL; two identical captures per color space)

- A renderer's own color (SpriteRenderer.color, the product of a tilemap cell's and the
  tilemap's colors, SpriteShapeRenderer.color) is converted to linear in a linear project
  (`Color.linear`: the sRGB curve below 1, a power of 2.2 above it, the curve's linear part
  below 0; the alpha unchanged).
- Where it is a vertex color (the built-in pipeline's sprite shader, any shader that does not
  declare `unity_SpriteColor`, every tilemap chunk) it is stored as bytes: each channel clamped
  to 0..1, times 255, rounded half up. A shader that declares `unity_SpriteColor` takes the color
  there as it is, unclamped, and white vertices, whether it reads it or not: URP's shader library
  declares it for every program, so `Probe/UrpVertexColor` (URP's include, the vertex color
  written unchanged) draws white (`*.urpraw*`), with the SRP Batcher on or off.
- A sprite shape's color never reaches its vertices: a shader that declares neither constant
  draws it white. The built-in sprite shader takes it from `_RendererColor` as bytes. The vertex
  colors a script writes into its color channel are not converted.
- A mesh's own vertex colors are never converted. Lines, trails and particles convert their 8-bit
  colors (each by its own rounding: `tests/projects/lines`, `tests/projects/particlestreams`);
  uGUI converts as `tests/projects/uicolors` records.
- Sprite, tilemap and sprite shape renderers added by a script take the pipeline's default 2D
  material (URP: `Sprite-Unlit-Default`, whichever renderer the camera uses), else
  `Sprites-Default`; a sprite shape renderer has it twice (fill and edges). With fewer submeshes
  than materials the last submesh is drawn again with each further material, so the probe gives
  each sprite shape one material.

Not measured: a shader declaring `unity_SpriteColor` in the built-in pipeline, a URP shader reading
`_RendererColor` of a sprite shape, colors of a SpriteRenderer drawn through GPU instancing,
tilemaps in `TilemapRenderer.Mode.SRPBatch` (drawn as Chunk).

## Reproduce

    python3 tests/projects/vertexcolors/run.py record --space Gamma --label g1    # Editor capture (GPU)
    python3 tests/projects/vertexcolors/run.py publish g1 g2 --space Gamma         # two identical captures
    python3 tests/projects/vertexcolors/run.py gate                                # the engine, both spaces

Values compare within 1e-5 (the readback's six decimals); every byte step is 1/255.
