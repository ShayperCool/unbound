# Canvas renderers left out for transparency

Which canvas renderers a Canvas draws when their alpha is zero or tiny, as the Unity 6000.0.71f1
Editor draws them. `CanvasRenderer.cullTransparentMesh` (Scripting API 6000.0): "Indicates whether
geometry emitted by this renderer can be ignored when the vertex color alpha is close to zero for
every vertex of the mesh."

Each cell is one Image drawn by `Probe/Solid`, which writes opaque red wherever the renderer is
drawn whatever its vertex colour, so a cell's centre pixel says whether the Canvas drew it:

- `renderer.<a>`: `CanvasRenderer.SetAlpha(a)` with the flag on, for alphas around zero, around
  half and one vertex-colour byte (0.5/255, 1/255) and ordinary values, and -0.1.
- `vertex.<b>`: the Image's colour alpha byte `b`, renderer alpha 1.
- `group.<a>`: a parent CanvasGroup at alpha `a`.
- `vertexRenderer.<b>.<a>`: both, with products whose vertex byte rounds to 1 or to 0.
- `noCull.*`, `noCullGroup.*`: the flag off, at renderer alpha 0, -0.1, 1e-3, a CanvasGroup at 0
  and 1e-3, and with transparent vertices.
- `reverseMask.<a>`: a Mask, a child that inverts the Mask's stencil where it draws (UI/Default
  with alpha clip, no colour, renderer alpha `a`) and a maskable blue Image after it: blue where
  the child left no hole, clear where it did. This is the reverse-mask technique UI packages use
  with a renderer alpha just above zero.
- `maskAlpha.<cull|noCull>.<a>`: a Mask whose own renderer alpha is `a`, with a red child.

The whole grid is drawn twice: through a Screen Space Camera canvas into a render texture, and
as a Screen Space Overlay canvas captured with `ScreenCapture.CaptureScreenshotAsTexture`
(`overlay.*` keys, on a 256x256 Game view). `alphaChanged.*` would list a renderer whose
`GetAlpha` differs from what was set (none do).

## Measured behaviour

A renderer whose alpha, its own times its CanvasGroups', is zero or less is never drawn, with or
without `cullTransparentMesh`. Any positive alpha (down to 1e-7) is drawn. The flag adds one
test: a mesh whose vertex alpha bytes are all zero is left out; without the flag it is drawn.
The renderer's alpha is not rounded to a byte for these decisions. The reverse mask cuts its
hole at alpha 0.002 and not at 0.00196: the vertex alpha byte rounds to 1 and 0, and the alpha
clip leaves no stencil write at 0. Camera and overlay canvases agree.

Unmeasured: pop instructions of a renderer left out at alpha zero (the engine still emits them;
a Mask's pop materials clip alpha, so they write nothing there), and releases other than
6000.0.71f1.

## Reproduce

```sh
python3 tests/projects/uitransparentcull/run.py record  --release 6000.0.71f1 --label rec
python3 tests/projects/uitransparentcull/run.py publish --release 6000.0.71f1 --label rec
UNBOUND=target/release/unbound python3 tests/projects/uitransparentcull/run.py gate --release 6000.0.71f1 --label gate-1
```

`record` runs two windowed Editors on the GPU (the overlay pass captures the Game view, which
`ProbeRunner.Record` sizes to 256x256); `publish` requires both captures to be identical. The
gate compares every value exactly and lists every missing, extra or differing key.
`ProbeRunner.Create` regenerates `Assets/Scenes/Probe.unity`.
