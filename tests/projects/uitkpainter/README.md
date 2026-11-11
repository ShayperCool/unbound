# UI Toolkit Painter2D

Stages the `uitk` fixture in a fresh cache directory and replaces its interaction run with
`Assets/Scripts/PainterProbe.cs`: 67 Painter2D cases, one mechanism each (line widths, joins,
miter limit, caps, closed and open paths, arcs both ways, ArcTo, cubic and quadratic curves,
fills under both rules, holes, self-intersections, arcs and curves in fills) plus controls of
measured path rules (closing a quadratic path, Arc and ClosePath without MoveTo, round caps on a
whole circle, path reuse after Stroke, a single-piece arc fill, a stroke wider than its curve's
radius, a width equal to an arc's diameter, zero width, transparent and NaN input, collinear ArcTo,
ArcTo of radius 0, a tiny arc, a 0.1° arc with round caps, crossing sub-paths, ClosePath twice and after returning to the
start). One more panel cell draws allocated quads, painter strokes and a DrawMesh quad in one
callback (call order).

A second list holds the stroke gradient cases (`gradient-*`): two, three and eight keys on a line
(key rows, the eighth steps, Fixed and PerceptualBlend modes, linear and gamma colour space), every
join and cap, closed and separate sub-paths, zero-length and very short segments, arcs both ways,
a whole circle, ArcTo, curves (with keys inside their pieces, a straight curve, a stroke wider than
the curve's radius), a mixed closed path, and the rules around the property: null after a
gradient, strokeColor set after it, a transparent strokeColor or transparent keys, the caller's
gradient changed after it was set, Fill beside it, two strokes of one path, a gradient left set
for the next element. Their meshes follow every other line of `painter.jsonl`, and they are
rendered on the panel at scale 1 after the two images above (`painter-gradient.png`), so the
earlier lines and images do not depend on them. Key colours are whole 8-bit values whose
differences are multiples of 16, and alpha keys sit away from the middle of a colour span: no
colour a case reads lies halfway between two 8-bit values, where the last bit of the arithmetic
would decide the rounding.

Each case is drawn twice:

- by a detached painter, saved with `SaveToVectorImage`; `painter.jsonl` records every vertex
  (position, tint, uv, flags, arc circle), the indices and the size;
- by an element's `generateVisualContent` on the runtime panel, rendered at scale 1 and 1.25
  (`painter-1.png`, `painter-1.25.png`).

API lines record what the Editor's `ILogHandler` sees for: drawing before `BeginPath` (detached and
in a callback; the handler sees nothing, the Editor log has the messages), `Clear`,
two calls accumulating, `SaveToVectorImage(null)`, the default `strokeGradient`, a gradient
read back (after its source changed, after null, after `Clear`) and a stroke after `Clear`, the context
painter's state at the start of three consecutive elements, and the context painter used outside
its callback, after a detached painter was created, and for `Clear`/`SaveToVectorImage`.

The probe's own coordinates are exact (1/8 grid, no trigonometry): Unity's Mono evaluates float
expressions in double precision, CoreCLR in float, so computed inputs would differ.

```sh
cache="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/uitkpainter"
python3 tests/projects/uitkpainter/run.py native --release 6000.0.71f1 --output "$cache/native-a"
python3 tests/projects/uitkpainter/run.py native --release 6000.0.71f1 --output "$cache/native-b"
cmp "$cache/native-a/capture/painter.jsonl" "$cache/native-b/capture/painter.jsonl"
python3 tests/projects/uitkpainter/run.py engine --release 6000.0.71f1 --output "$cache/engine" \
  --cli /path/to/unbound --managed /path/to/managed
python3 tests/projects/uitkpainter/verify.py --release 6000.0.71f1 --candidate "$cache/engine/capture"
```

The gate (`gate.json`) runs `run.py gate`, the engine capture into a fresh cache run and
`verify.py`. The verifier compares meshes vertex by vertex (2e-4, relative above 1) except fills
bounded by arcs or curves, compared by their anti-aliased vertices and interior area (see its
docstring), every other line exactly, and prints image measurements. The published reference is one
of two byte-identical Editor recordings.
