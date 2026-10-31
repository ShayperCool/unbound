# Canvas rectangle clipping

Oracle: real Unity 6000.0.71f1 Linux GPU Editor, NVIDIA display :0. This fixture uses the
original built-in UI/Default, plus diagnostic shaders to expose incoming vertex coordinates,
world coordinates, uniforms and the clipping variant. Float RGBA readback uses a Linear
ARGBFloat render target. PNGs are visual companions; the comparator checks every float
channel at the existing absolute/relative 2e-4 tolerance, every observation key and the exact
image manifest. No image or key exclusions.

Initial Native runs `~/.cache/compat/rectclip-native1` and `rectclip-native2` agree on 976/976
observations and 186/186 full images, 12189696/12189696 channels. Compressed float buffers
in `Reference/Linear` preserve all images. Identical signed infinities in disabled rectangle
diagnostic output are compared as infinities, not treated as approximate finite values.

Reproduction uses `run.sh create`, `run.sh native native1`, `run.sh native native2`,
`run.sh build`, `run.sh engine after` and `python3 compare.py Reference/Linear
~/.cache/compat/rectclip-after`. Run these from this directory or the repository root with
the appropriate script path. `run.sh` acquires the global heavy-job lock itself and gives
each invocation a 600 second timeout, disables build servers, and uses private managed
output/intermediates. It never targets the real game. No api-check or stub generator is used.

The independent starting engine was built from 38e9279b in this worktree, with output copied
to `~/.cache/compat/rectclip-before-managed`. Its initial clipping run matches 27/186 images
and 9272624/12189696 channels. `rectclip-before-build.log`, `rectclip-before.log` and
`rectclip-before-compare.txt` retain evidence. `regress.sh before before-managed` captures
UI Toolkit against that independent build before implementation.

Measured: shader vertices are in root Canvas coordinates, including transformed renderers and
nested canvases with overrideSorting. Clipping overrides the material's rectangle, softness
and clip keyword. Disabling sets infinite clip bounds, zero softness and the disabled variant
even when the material explicitly enables it. A shader omitting the variant is not clipped.
Negative softness readback preserves negative values. Further measurements and validation
results will be recorded as implementation progresses.
