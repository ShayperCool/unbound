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
