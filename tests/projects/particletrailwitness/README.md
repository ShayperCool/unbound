# Curved trail Native witness

This probe inspects only the existing `image_trails` trial from `particles`.
The runner copies its authored project into two private cache projects and extracts
the exact `Make`, material, texture, capture helpers and trail trial. The diagnostic
call runs immediately before the existing final `camera.Render`. No original
probe, material, simulation setting or reference is changed.

The predeclared witness uses Unity 6000.0.71f1, seed 12345, and the authored thirty
`Simulate(1f / 30f, true, i == 0, false)` calls. There are no parameter variants.
It records zero-initialized GetParticles results in returned order, the integer
GetTrails return and every backing field, default active trail streams, complete
baked mesh buffers and typed attributes, indices, camera matrices and target
settings. Every float includes round-trip text and its raw float32 bits.

Run commands in order, replacing `$WORK` with a fresh directory under
`$XDG_CACHE_HOME/unbound/validation/runs`:

```sh
python3 tests/projects/particletrailwitness/run.py prepare --release 6000.0.71f1 --work "$WORK"
python3 tests/projects/particletrailwitness/run.py native-a --release 6000.0.71f1 --work "$WORK"
python3 tests/projects/particletrailwitness/run.py native-b --release 6000.0.71f1 --work "$WORK"
python3 tests/projects/particletrailwitness/run.py compare --release 6000.0.71f1 --work "$WORK"
python3 tests/projects/particletrailwitness/run.py publish --release 6000.0.71f1 --work "$WORK"
```

Each Editor uses `tests/editor_runner.py` in graphical batch mode with one installed
launcher lease, private display, a 600 second hard deadline and a 240 second
probe-output deadline including startup. No interactive Hub retry is used.

Publishing requires two independent complete exact repeats, including raw padding,
and RGBA equality to the original `particles/Reference/images/trails.png`. It writes
one release directory under `Reference/`. Both complete captures stay outside Git.
Any nondeterministic raw field or changed canonical image stops publication. The
comparison retains the full missing/extra/value union and every changed image pixel.
The original image script's reporting threshold of 8 is not a pass/fail gate here.

The engine's current empty GetTrails stub is not Native evidence. This diagnostic
does not implement that API. Geometry analysis must compare the public Native data
against the independently validated source model and preserve any disagreement.
