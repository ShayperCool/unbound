# IMGUI borders and canvas alpha textures

Unity 6000.0.71f1, Linux GPU Editor (OpenGL Core), linear color space, Android as the active
build target (`ProbeRunner.Record` switches to it; the gate runs `--target Android`). Two
independent windowed captures were byte-identical. `UiAlphaBordersProbe` draws over the camera's
solid background (64, 76, 89) in a 320x180 game view and reads the screen at the end of the frame.

- `imgui.png`: [GUI.DrawTexture](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/GUI.DrawTexture.html)
  with a corner radius, a border, per-side widths (1, 2, 4, 8), per-corner radiuses (0, 4, 8, 16), a
  radius above half the rect (40 x 20, radius 30), a patterned border, ScaleToFit with a radius;
  alphaBlend false with a translucent tint, a texture with alpha steps, and with a radius;
  `GUI.Box` and `GUI.Button` of the default skin; a nine-sliced custom `GUIStyle`; translucent
  rounded and bordered draws over opaque bases; `GUI.DrawTextureWithTexCoords` with alphaBlend
  false and a translucent `GUI.color`; negative widths and radiuses.
- `canvas.png`: bare `CanvasRenderer`s with
  [SetAlphaTexture](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/CanvasRenderer.SetAlphaTexture.html)
  under `UI/DefaultETC1` and `UI/Default`, without one, over a material `_AlphaTex`, after
  `Clear`, set twice, on a `RawImage`, and pairs sharing one material; `Image`s and
  `SpriteRenderer`s of sprites with Split Alpha Channel (ETC_RGB4 on Android, the sRGB flag off,
  and the same with it on),
  of a sprite with a secondary texture named `_AlphaTex`, and of a plain transparent sprite.
- `batches.png`: renderers of one UI/DefaultETC1 material and texture with the stepped then the
  half alpha texture: beside a draw of another material that does not overlap them, around one
  that overlaps both, and both under one RectMask2D.
- `values.jsonl`: Canvas.GetETC1SupportedCanvasMaterial (name, shader, the same object, the
  default material or not); the sprites' texture formats, `associatedAlphaSplitTexture`, secondary textures
  and the materials the Image and SpriteRenderer draw with.

| Source (6000.0) | Documented | Measured here |
|---|---|---|
| GUI.DrawTexture | Border widths (left, top, right, bottom), corner radiuses (top-left, top-right, bottom-right, bottom-left); zero draws the plain texture; the texture patterns the border; alphaBlend: "whether to apply alpha blending". | Bordered or rounded draws are anti-aliased, the texture mapped over the whole rect, and alphaBlend has no effect on them; a radius above half the shorter side draws as half of it; negative widths and radiuses draw as zero ones. A plain draw with alphaBlend false (GUI.DrawTexture or DrawTextureWithTexCoords) shows the texture's colour without its alpha, blended by the tint's alpha (a white tint at 0.5 over the background is 160, 166, 172). |
| CanvasRenderer.SetAlphaTexture | "passed to the Shader under the _AlphaTex property". | It overrides the material's `_AlphaTex`; without one the material's own (else the shader's white default) is sampled; the last call wins; `Clear` drops it; a RawImage keeps it. Renderers with one material and texture take the first one's alpha texture, also with other draws between them, overlapping ones included, and under one RectMask2D. |
| Canvas.GetETC1SupportedCanvasMaterial | The material for ETC1 split alpha. | "ETC1 Supported UI Material" with UI/DefaultETC1, the same object each call, not the default UI material. |
| Sprite.associatedAlphaSplitTexture | The alpha texture made for ETC1 split alpha; null when none. | Null for every sprite: the Editor imports a Split Alpha Channel texture as ETC_RGB4 without its alpha and makes no alpha texture; with the sRGB flag on, the linear project's ETC2 promotion keeps the alpha as ETC2_RGBA8. A secondary texture named `_AlphaTex` is used by neither Image nor SpriteRenderer. |

Unbound matches every value. Images: imgui within 1/255, canvas within 3/255 and batches within 2/255 on the split-alpha sprites (`image_limits.json` lists the residuals and
their causes). Unmeasured: other graphics devices and drivers, a gamma project, `GUI.matrix`
rotation with radiuses, renderers under different clip rects, player builds' split alpha.

```sh
python3 tests/projects/uialphaborders/run.py record --label native-a
python3 tests/projects/uialphaborders/run.py record --label native-b
python3 tests/projects/uialphaborders/run.py publish native-a native-b
UNBOUND=/path/to/runtime/unbound python3 tests/projects/uialphaborders/run.py gate
```
