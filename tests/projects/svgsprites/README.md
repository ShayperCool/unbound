# SVG importer sprite, UI image and texture outputs

What the `com.unity.vectorgraphics` 2.0.0-preview.25 package's own SVG importer makes in the
Unity 6000.0.71f1 Editor for its other output types, and the engine compared with it. The package
code runs on both sides; nothing of it is ported.

`Assets/Resources/Svg/` holds six imports of the uitkvector fixtures: `shapes` (Vector Sprite),
`pivot` (the same file as a Vector Sprite with 50 pixels per unit and a bottom-left pivot),
`gradients` (Vector Sprite with gradients: an atlas texture and setting indices),
`frame` (UI SVGImage, aspect preserved), `strokes` (Textured Sprite, 64 pixels) and `clip`
(Texture2D, 48 pixels, point filtering, one sample, clamped); `Assets/Resources/Textures/` holds
two texture-imported sprites' textures (Single and Multiple mode, from spriteimport), whose
`SpriteEditorExtension.GetSpriteID` the capture records (the .meta's spriteIDs). The capture records each asset's
main object and objects, every sprite's rect, pivot, pixels per unit, bounds, vertices, UVs,
triangles, channels (`HasVertexAttribute`), colours and setting indices, every texture's size,
format, filtering, wrapping and texels (read through a render texture), the GameObject outputs'
components, sprites and materials, and a 240x96 render of copies of the three vector sprites'
GameObjects and the textured sprite, placed so that none overlaps another (the draw order of
overlapping sprites of one sorting order is unspecified).

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/svgsprites/run.py author --label author
python3 tests/projects/svgsprites/run.py native --label native-a
python3 tests/projects/svgsprites/run.py native --label native-b
python3 tests/projects/svgsprites/run.py publish native-a native-b
UNBOUND=/path/to/unbound python3 tests/projects/svgsprites/run.py check
```

The gate assembles the committed `Authored/` set and runs only the engine. Numbers compare within
1e-5 relative to max(1, |reference|); a NaN the Editor records matches only a NaN; image and texel
channels within 2/255. `residuals.json` lists the only pixels allowed to differ beyond that (four
pixels at the focus of a radial gradient, by at most 5/255); they stay printed in every report.

| Source | Contract or observation | Engine decision |
| --- | --- | --- |
| [Vector Graphics 2.0 manual](https://docs.unity3d.com/Packages/com.unity.vectorgraphics@2.0/manual/index.html) | Documented: the importer's output types (vector sprite, UI SVGImage, textured sprite, Texture2D). | Run the package's importer unchanged. |
| [6000.0 Sprite API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Sprite.html), [OverrideGeometry](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Sprite.OverrideGeometry.html) | Documented: vertices in pixels within the rect; vertex attributes through the data access API. | Keep colour and texture coordinate channels with the sprite and in its render mesh. |
| Native repeats, 6000.0.71f1 | Observed: the sprites the importer makes without a texture are drawn (vertex colours, `Unlit/Vector`), their UVs are zero; a sprite whose texture is missing is not drawn (tests/projects/spriteassets). The main object of each import is named after the file, although the importer names its GameObject `transform`. An imported sprite's ID is its .meta's spriteID. | Draw sprites made without a texture; name the main object after the file (Shaders keep their declared name); keep imported sprite IDs. Unbound's model, not recorded: an importer's GameObjects (made or instantiated) belong to the asset, in no scene and never awake (the render shows no second copy at the origin). |

Unbound's choices, unmeasured: a sprite without a texture in Sliced or Tiled mode is not drawn
(with a warning) and does not mask; OverrideGeometry drops the colour, normal, tangent, blend and
extra texture coordinate channels of the old vertices; a pixels per unit of 0 or less given to the
internal Create keeps 100.

Not covered: the physics shape the importer can generate (`generatePhysicsShape`, which traces a
rendered texture through `SpriteUtility.GenerateOutline`), the UI SVGImage drawn on a canvas, and
the 2D Sprite package's editor windows (Unbound has none; their registration hooks are kept).
