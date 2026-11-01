# UI Toolkit pixel controls

Stages the existing UI Toolkit fixture in cache and runs its complete interaction sequence
unchanged. After all three original renders, it captures the scaled tree with transparent
text color, then sixty independent filled/bordered rectangles at scales 1 and 1.25. Rows
vary corner radius; columns vary border widths, including unequal adjacent sides and an
unbordered fill. Fractional positions exercise coverage at the pixel boundary.

The transparent-text diagnostic runs last because changing inherited colors can invalidate
Native text measurements. Inserting this diagnostic between original phases changes later
title/subtitle widths. Those intrusive diagnostic runs must not become references.

Use the installed shared job runner through this driver. Do not wrap the driver again.
Native uses the GPU through VirtualGL. Each run requires a fresh cache output directory.

```sh
pixel_work="${XDG_CACHE_HOME:-$HOME/.cache}/unbound/validation/runs/pixel-replay"
python3 tests/projects/uitkpixels/run.py native --release 6000.0.71f1 --output "$pixel_work/native-a"
python3 tests/projects/uitkpixels/run.py native --release 6000.0.71f1 --output "$pixel_work/native-b"
python3 tests/projects/uitkpixels/verify.py --release 6000.0.71f1 \
  --reference "$pixel_work/native-a/capture" --candidate "$pixel_work/native-b/capture" \
  --native-repeat --output "$pixel_work/agreement.json"
```

The verifier requires NumPy and Pillow. Original JSON and images always use the existing
release oracle. Publish only the new diagnostic and shape images from two agreeing Native
captures under `Reference/unity-<version>/`. Keep both raw repeats in cache.

For an engine capture, use `run.py engine` with `--cli` and `--managed` pointing to matching,
private runtime artifacts. Compare using `verify.py --release <version> --candidate <capture>
--before <baseline-capture> --output <cache-report.json>`. JSON uses the complete exact key
union. The accompanying compressed NumPy archive retains every raw RGBA channel and signed
delta, including alpha, with no excluded regions. The report counts every introduced,
retained, resolved, improved and worsened channel. A passing regression check permits only
unchanged or improved errors; it does not assert exact rendering parity.


`collapsed-borders.png` covers 20x20 rounded elements whose opposing border
widths leave zero or negative inner space, all four asymmetric corner orientations,
and a control with a positive inner area. The new image is published only after
two independent Editor recordings agree; all earlier UI and shape references
remain byte-identical.

`diagnose_border_fill.py` reads retained captures without running a renderer. It saves
complete RGBA arrays, signed deltas, failing channel indices, missing image entries,
JSON key unions, all 60 cell summaries at each scale, and corner coverage equations.
The equations are diagnostic hypotheses and do not change acceptance. Every hypothesis
disagreement remains in the report, including pixels that already passed.

```sh
python3 tests/projects/uitkpixels/diagnose_border_fill.py --release 6000.0.71f1 \
  --run "$HOME/.cache/unbound/validation/runs/uitk-pixels-20261031" \
  --output "$HOME/.cache/unbound/validation/runs/uitk-border-fill-offline/new-analysis"
```

The retained controls show eight bottom-left corner pixels consistent with independent
left and bottom coverage passes when both inner radii collapse. Applying that algebra
to all four eligible corner orientations fails seven other observations. This does not
establish a general production correction. The missing measurement is the generated
mesh and draw order for `shape-1-2`, including positions, indices, colors, circle
coordinates, arc flags, retained generation scale and resolved widths. Its four corners
distinguish side overlap from a single coverage product without another image matrix.
