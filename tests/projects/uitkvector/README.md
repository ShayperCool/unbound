# UI Toolkit vector images (SVG)

Measures what the Unity 6000.0.71f1 Editor makes of SVG files imported by the
`com.unity.vectorgraphics` 2.0.0-preview.25 package as UI Toolkit `VectorImage` assets (svgType 3)
and how UI Toolkit draws them, and compares the engine with it. The package's own scripted
importer runs on both sides.

`Assets/Resources/Vector/` holds five text-free SVG files: filled shapes with opacity and a
rotated group (`shapes`, 2:1), strokes with joins, caps, a curve and an ellipse (`strokes`),
linear, radial (with a focus), reflected and repeated gradients plus a gradient-stroked line
(`gradients`), a clip path over a gradient (`clip`, its meta setting `gradientResolution: 16`, a
control that importer settings other than the type are read), and a rounded frame for slicing
(`frame`). `VectorRunner.Record` re-imports them, recording the import's warnings, then enters
Play mode, where `VectorProbe` (from the first frame's `Start`) records:

- each asset's size, every vertex (position, tint, uv, setting index), the indices, the gradient
  settings and the atlas (size, format, filter, wrap and texels, read back through a render
  texture), and a `Painter2D.SaveToVectorImage` image as a control without import;
- `Image.vectorImage` clearing a texture, `Background.FromVectorImage`, `Resources.LoadAll` of an
  imported SVG;
- `GUIUtility.AlignRectToDevice` of a content rect inside a panel repaint at both scales;
- a runtime panel rendered at scale 1 and 1.25 (`vector-1.png`, `vector-1.25.png`): every image
  as a background with each `-unity-background-scale-mode`, background size, repeat (cut at the
  element's edges, centred, space and round) and position, tint, rounded corners and borders,
  opacity, a background colour, nine slices (even, uneven with a slice scale, on an element
  smaller than the slices, with scale-to-fit), a rotated and scaled element, a USS `url()`, an
  element with `overflow: hidden` whose vector background masks its child; `Image` elements with
  each scale mode, a uv part, a tint and padding; `MeshGenerationContext.DrawVectorImage` with a
  rotation and negative scales; 60 copies of `gradients` on one panel (more than 255 gradient
  settings rows: Unity packs a row as two bytes its shader reads back as high * 255 + low, that is
  row - (row >> 8), so the copies from row 256 on sample an earlier row's settings); and the logs
  of the whole run.

The appended controls load solid vector images through UXML inline `--unity-image` URLs
(absolute and relative) and `Button.iconImage`. A separate panel then overwrites the vector
image used by a clipping background with `Painter2D.SaveToVectorImage`. Its old background and
mask remain until `MarkDirtyRepaint`; changing overflow also refreshes the geometry. A hidden
parent still masks a visible child. Six control images record these transitions. Three further images below cover padding changes.
All previous 69 value rows and both previous panel images stay byte-identical.

Documented (Unity 6000.0 Scripting API: [VectorImage](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.VectorImage.html),
[Background](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.Background.html),
[Image](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.Image.html),
[MeshGenerationContext.DrawVectorImage](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.MeshGenerationContext.DrawVectorImage.html);
[VisualElement.MarkDirtyRepaint](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.VisualElement.MarkDirtyRepaint.html),
[Painter2D.SaveToVectorImage](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.Painter2D.SaveToVectorImage.html);
Manual: [Image](https://docs.unity3d.com/6000.0/Documentation/Manual/UIE-uxml-element-Image.html);
package manual: [Vector Graphics 2.0](https://docs.unity3d.com/Packages/com.unity.vectorgraphics@2.0/manual/index.html)):
the asset types and API. Observed in this capture only: how the images are placed and cut (the
rules in `managed/UnityEngine/UIElements/Rendering/UaPainter.Vector.cs`), that a background's
scale-and-crop stretches a vector image, that slices ignore the slice scale, that a vector
background is the stencil mask of an element that clips its children, that content rects are
aligned to the panel's pixel grid, and the importer's .meta field names (`m_SvgType` is
`svgType`).

```sh
python3 tests/projects/uitkvector/run.py record --label native-a
python3 tests/projects/uitkvector/run.py record --label native-b
python3 tests/projects/uitkvector/run.py publish native-a native-b
python3 tests/projects/uitkvector/run.py gate
```

Values compare within 2e-5: the package's tessellation is C# float arithmetic, which the Editor's
Mono and the engine's CoreCLR evaluate at different intermediate precision (vertex positions
differ by up to about 1e-5). The images fail beyond the residuals in `image_limits.json`. Dynamic atlas sampling removes
most of the earlier NaN-line and gradient-seam differences. Remaining vector-image mean errors
are 0.010405 and 0.016556 at scales 1 and 1.25, with worst channel differences 7 and 9, and zero
and two pixels above 8. These measurements are from NVIDIA only; the previous limits and numeric
tolerance are unchanged. The package is resolved through `Packages/packages-lock.json` (the Editor's).

The Image manual uses the `--unity-image` custom style property for image sources; a
`vector-image` UXML attribute is not a supported Image source. The SaveToVectorImage API
initializes an existing image, and MarkDirtyRepaint requests regeneration. The controls change
the image before requesting repaint, so the repaint changes the visible result. Mask retention
and refresh on overflow changes are observations in 6000.0.71f1, selected through Compat. Other
releases retain their previous per-paint mask generation, which remains unmeasured.

The new mask images retain small Painter2D fringe coverage residuals, with at most eight channel
steps on NVIDIA and no pixel above that. Their separate image limits bound the measured fringe
differences and reject the changed mask coverage seen before this fix. The original gradient image limits and numeric tolerance are unchanged.

Rounded ContentBox and PaddingBox controls keep a 64 by 64 outer rectangle while padding changes.
The ContentBox clip shrinks when its ancestor widens and percentage padding resolves from 10 to
14, then when padding is set to 20. The PaddingBox clip stays outside the padding. The reference
records both layout and content rectangles plus each image. Retained geometry checks the resolved
content rectangle, so changes from ancestor layout also refresh masks without explicit repaint.
See [OverflowClipBox](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.OverflowClipBox.html).

Atlas lifecycle controls use shared mutable textures with readability filtering disabled, separate
point and bilinear textures, clamp and repeat wrapping, growth, detached users and a replacement
texture. They record content before and after `SetTextureDirty`, removal of one and then both
users, reuse, reattachment, reset, a custom rejection filter and dirty notifications for textures
outside the atlas. Generation counters distinguish content updates from geometry regeneration.

The documented Unity 6000.0 contracts are
[SetTextureDirty](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.RuntimePanelUtils.SetTextureDirty.html),
which refreshes a contained texture after its content changes, and
[ResetDynamicAtlas](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.RuntimePanelUtils.ResetDynamicAtlas.html),
which releases textured geometry and rendering commands for regeneration. The
[dynamic atlas manual](https://docs.unity3d.com/6000.0/Documentation/Manual/UIE-control-textures-of-the-dynamic-atlas.html)
describes automatic texture ownership, filters, growth and fragmentation. The controls change
texture content before notifying the atlas, and remove most users before resetting, so these
operations change the result.

Observed in this release and project: pages keep their largest dimensions until reset. A shared
texture resized from 5 by 3 to 9 by 7 keeps its old allocation while another user remains. Resizing
alone does not regenerate the element. An explicit repaint regenerates that element without
refreshing the cached texture; a dirty notification copies the current source dimensions into
the existing page. These are measured observations, selected by Compat. The resize images and
separate generation counters preserve all four stages.

A raster Alpha8 font asset is drawn with all atlas admission filters disabled, then gains another
glyph without changing its 128 by 128 texture. Its render mode, glyph counts and both images are
recorded. Text retains its own font atlas. The small `AtlasControl.ttf` fixture is the licensed
`UnboundLayoutControl` subset also used by `fontfeatures`; see `ATLAS-FONT-LICENSE.txt` and
`tests/projects/fontfeatures/make_fonts.py` for its source and construction.

The added atlas images retain rectangle-edge coverage differences also present when sampling
textures directly: their separate limits record the measured mean and worst differences, all
with no pixel above eight channel steps. The comparator prints every raw metric. All limits established before Q505 remain
unchanged. The dirty-content and missing-reset failures are much larger than these fringes.
