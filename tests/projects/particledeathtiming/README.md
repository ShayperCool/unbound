# Particle death timing

A stationary naturally expiring parent separates Death-child initialization from
ordinary transform-child traversal. One Death burst emits a speed-1 particle;
continuous emission is disabled in every system. An ordinary child starts with
one speed-1 particle. The parent starts with lifetime .013 at z .46. Each
`Simulate(.025, true, false, false)` call records the parent and both children.
The fields follow the safe trigger-lifetime witness; renderer-dependent mesh
index reads are excluded because that getter crashed the earlier Native probe.

Unity 6000.0.71f1 removes the parent in the first call. Its Death child has
z .48, lifetime 1.993 and time .025, then z .505, lifetime 1.968 and time .05.
The ordinary child advances by .025 in each call. The Death captures use seed
692788716 and axis [0,1,0], which remain part of the full comparison.

The fixed-step timing variation changes `Time.fixedDeltaTime` from the project's
.02 to .01 while leaving `fixedTimeStep=false`. Every recorded particle and
system field stays identical. This does not explain the Death child's initial
.02 age and does not justify substituting the current fixed step or a constant
for that age. The control proves the difference from ordinary traversal.

One further paired discriminator changes only the external simulation delta to
.015. It records `Time.deltaTime` as .0199999921 throughout. The parent still
dies in call one, but the Death child now has zero initial age: z .46,
lifetime 2.013 and time .015. Call two gives z .475, lifetime 1.99799991 and
time .03. Its seed and axis match the other profiles. The ordinary child
advances .015 per call. This also excludes simply pre-aging by frame delta or
the entire external call. The event scheduling mechanism remains unresolved;
these measurements do not justify a fitted scheduling quantum.

`Reference/unity-6000.0.71f1/` is the single reference set for that release.
Its profiles are `default.jsonl`, `fixed-0.01.jsonl` and `delta-0.015.jsonl`. Each was checked
against an independent repeat before publication. Repeats, engine captures,
input hashes, commands, logs and full failure unions stay in the validation cache.
The original 27-record trigger-lifetime reference remains unchanged.

Run from a clean checkout, selecting the release explicitly:

```bash
RUN="$HOME/.cache/unbound/validation/runs/particledeathtiming-example"
python3 tests/projects/particledeathtiming/run.py prepare --unity-version 6000.0.71f1 --work "$RUN" --label native-a
python3 tests/projects/particledeathtiming/run.py prepare --unity-version 6000.0.71f1 --work "$RUN" --label native-b
python3 tests/projects/particledeathtiming/run.py native --unity-version 6000.0.71f1 --work "$RUN" --label native-a
python3 tests/projects/particledeathtiming/run.py native --unity-version 6000.0.71f1 --work "$RUN" --label native-b
python3 tests/projects/particledeathtiming/verify.py --unity-version 6000.0.71f1 --native-a "$RUN/native-a.jsonl" --native-b "$RUN/native-b.jsonl" --output "$RUN/publication.json"
```

For the variation, use new labels in `prepare`, then pass
`--fixed-delta-time .01` to both capture commands and `--profile fixed-0.01`
to the verifier. For the final discriminator, use new labels, pass
`--external-delta .015` to both capture commands and `--profile delta-0.015`
to the verifier. Preparation always copies authored inputs into the cache.
Each capture enters the installed heavy-job runner once.

For an engine capture, prepare a new label and use `run.py engine` with
`--cli /path/to/verified/unbound --runtime /path/to/verified/managed`.
Set `UNBOUND_CACHE` and `CARGO_TARGET_DIR` to private cache paths.
Compare it using `verify.py --actual /path/to/capture.jsonl`; optionally pass
`--baseline` for a complete resolved/retained/introduced union. The verifier
uses the original particle comparator tolerances and preserves missing and
extra particle objects, seeds, axes and all residual paths. Failed parity
returns a nonzero exit code.
