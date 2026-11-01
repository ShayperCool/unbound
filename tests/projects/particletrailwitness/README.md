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

The first complete pair preserved the original image exactly on both runs and
matched the baked mesh bytes, but differed in 530 raw Vector4 storage slots in
`Trails.positions`. All other 132 records matched. Per the declared stop rule,
no canonical reference was published and no production change was made. The raw
captures and complete mismatch report remain in the validation cache handoff.


## Public rendering contract disposition

The full reflected storage repeat remains rejected. All seven Trails backing fields
are assembly-internal in the installed ParticleSystemModule metadata. A separate,
explicit contract was approved for the already captured public output. It includes
all 124 non-Trails records, with every raw mesh byte, typed attribute, index, particle,
camera, target and quality field. Both captures match exactly. The canonical file is
`Reference/unity-6000.0.71f1/public.jsonl`; `contract.json` binds its scope and inputs.
No second Native pair was run.

`public_contract.py` checks every count-described ring, including slot 4, and retains
all 530 raw Vector4 differences and the full rejected union outside Git. None lies
in any of the 93 count-described entries. All 95 baked center pairs are accounted
for by the three returned particle heads and their reversed rings, with only the
mathematical float rounding bound. The extra counted entry equals an existing birth
coordinate; its lifecycle meaning remains unknown. No active/dead assumption is used.
Backing-storage parity and the public GetTrails implementation remain unclaimed.

To audit an existing pair, supply its cache directory and the JSON output of the
offline `InspectLayout.cs` tool. This does not execute Native:

```sh
python3 tests/projects/particletrailwitness/public_contract.py --release 6000.0.71f1 \
  --pair "$PAIR" --layout "$LAYOUT" --out "$REPORT"
```

`engine.py --release 6000.0.71f1 --work "$WORK"` prepares the same authored trial
for public-only engine inspection. Run the engine with `PARTICLES_IMAGE_DIR` set
to `$WORK/capture`. `compare_public.py --release 6000.0.71f1 --actual
"$WORK/capture/witness.jsonl" --out "$REPORT"` retains every public field and all
raw byte/leaf differences. Numerical geometry keeps the streams comparator's
2e-4 absolute/relative tolerance. Raw buffers must agree with their complete typed
views. Tolerated coordinate bit differences are explicitly reported, never called
bit-exact parity. The original full-storage `publish` command still fails honestly.
