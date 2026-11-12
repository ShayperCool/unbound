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
  settings rows: Unity packs a row as two bytes its shader reads back as high * 255 + low, so the
  copies from row 256 on sample the neighbouring row's settings); and the logs of the whole run.

Documented (Unity 6000.0 Scripting API: [VectorImage](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.VectorImage.html),
[Background](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.Background.html),
[Image](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.Image.html),
[MeshGenerationContext.DrawVectorImage](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/UIElements.MeshGenerationContext.DrawVectorImage.html);
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
