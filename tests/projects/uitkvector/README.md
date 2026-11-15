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
differ by up to about 1e-5). The images fail beyond the residuals in `image_limits.json`: a
gradient-stroked line whose gradient coordinates the package computes as NaN (the GPU decides
what such a fragment samples; measured on the NVIDIA driver only), and single pixels at gradient
row ends and wrap/mirror seams, which the Editor samples from its dynamic-atlas copy of the
gradients. The package is resolved through `Packages/packages-lock.json` (the Editor's).

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
