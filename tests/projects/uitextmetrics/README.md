# UI Toolkit text measurement

Measures the runtime theme font used by the existing UI Toolkit control fixture on Unity
6000.0.71f1. The original sequence runs first, with read-only observations after its three
renders. The label's layout, font face metrics and full glyph-pair feature table are recorded.
`MeasureTextSize` is deliberately deferred until the original sequence finishes because it
also updates the handle's stored measured sizes.

The focused controls cover font sizes and fractional boundaries, all four legacy font styles,
empty text, whitespace, newlines, rich text, constrained and intrinsic measurement, wrapping,
text changes, four panel scales and independent dynamic font assets. The driver stages the
existing `uitk` fixture and the additional script in cache. It never runs the Editor in tracked
sources. Native uses a genuine GPU through VirtualGL, with the shared heavy-job wrapper.

Run two independent Native captures before publishing the one release reference:

```sh
python3 tests/projects/uitextmetrics/run.py native --release 6000.0.71f1 --output "$XDG_CACHE_HOME/unbound/validation/text-native-a"
python3 tests/projects/uitextmetrics/run.py native --release 6000.0.71f1 --output "$XDG_CACHE_HOME/unbound/validation/text-native-b"
```

Use a fresh cache output for each engine capture, passing `--cli` and `--managed`. Verify with
`verify.py --release 6000.0.71f1 --candidate <capture>/metrics.jsonl --output <cache>/report.json`.
The verifier compares the full key union and preserves all missing, extra and failing values.
Its absolute numeric tolerance is 0.0001. `--before` retains all resolved, retained and introduced
discrepancies. The three original images and control observations are also emitted alongside
the focused capture and use the existing fixture's comparison rules.

The 93,300-row Native oracle has 608,384 scalar and structural observations. The unchanged
parent matches the font metrics and direct measurements. Twelve values fail: the inherited
label's final layout/content widths and the same widths at four interaction steps, plus warmed
widths after inherited color and disabled-state style changes. The final width first changes
at the foldout interaction.

Unity invalidates the text node's measurement when its stylesheet matching key changes. This
can happen even when an overriding declaration leaves the final text style equal. Inline color
writes, repaint and unmatched class changes preserve the cached measurement. The implementation
tracks matched rules and inherited style inputs for text nodes. Its layout invalidation lets
measurements use kerning loaded since the first layout. Other releases remain unmeasured.
