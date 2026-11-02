# Particles probe

Particle systems built in code, each exercising one module with fixed values, stepped with
`ParticleSystem.Simulate` and recorded after every step: the system's time and state flags and
every particle (position, velocity, lifetime, size, color, rotation), as JSON lines. A second
phase plays systems over real frames at a fixed capture delta for what only the player loop does
(first-frame delta, collision and trigger messages, stop actions, the lights module), and an image
phase renders systems with known particles (render modes, orientation, texture sheets, sorting,
trails, missing materials, particle lights) into PNGs.

Record Unity's reference (`ProbeRunner.Create` made the empty scene; the Editor needs a display):

    PARTICLES_PROBE_OUT=$PWD/tests/projects/particles/Reference/unity-6000.0.71f1.jsonl \
    PARTICLES_IMAGE_DIR=$PWD/tests/projects/particles/Reference/images \
        ~/Unity/Hub/Editor/6000.0.71f1/Editor/Unity -batchmode -projectPath tests/projects/particles \
        -executeMethod ProbeRunner.Record -logFile ~/.cache/particles.log

Run Unbound (in a window: the images need a GPU) and compare:

    PARTICLES_PROBE_OUT=/tmp/particles.jsonl PARTICLES_IMAGE_DIR=/tmp/particles \
        unbound run -p tests/projects/particles --frames 60 --size 320x180 --no-watch
    python3 tests/projects/particles/compare.py tests/projects/particles/Reference/unity-6000.0.71f1.jsonl /tmp/particles.jsonl
    python3 tests/projects/particles/compare_images.py tests/projects/particles/Reference/images /tmp/particles

Values match within 1e-3 (colors within 1/255). Lifetimes and rates are chosen off the 0.025 s
step grid where they could otherwise land exactly on a step boundary, where which side of it
Unity's float arithmetic falls decides one particle.

What still differs, and why:

- `noise`: Unity's noise field is its own function; Unbound's moves particles differently.
- `external_forces_rotation`: force field rotation is not simulated (Unbound warns).
- `sub_emitters_child`, `sub_emitters_birth_child`: in `Simulate`, sub-emitter particles start a
  little older in Unity than in Unbound (by about a frame's delta), not yet understood.
- `simulate_long_call`, `set_particles`: a single long `Simulate` from a restart splits its time
  so that Unity's clock ends one float step lower, and the particle due exactly at the end is one
  step later.
- `collision_world`: a particle bouncing off a box collider lands about 0.005 apart.
- `custom_data`: one record's color stream is read at a different age.
- `lights` (image): the particle lights are created, but the built-in pipeline does not draw
  additional lights yet.

## Known numerical residuals, 2026-11-02

The measured trigger/rate/sub-emitter correction has no comparator-introduced failures
against either baseline. Counts, seed/order/state and IsAlive do not worsen in the
recorded histories; all 17 images are byte-identical. Of 9856 changed non-real-time
components, 7565 errors decrease and 2267 increase. Raw values and tolerances stay intact.

The 136 lifetime increases below are 1-162 float steps from the baseline. The maximum
additional error is 3.05e-7 seconds, total candidate error at most 1.058e-6. Their minimum
recorded lifetime is about 0.00928 seconds and none changes sign. Suspected cause is
newborn pre-age propagation through percent-age accumulation and float32 export.
They are nonblocking precision residuals in these measured histories, without a claim
about every possible future threshold. These are exact keys, not runtime exceptions:

| Scenario | Exact step: particle indices for `/particles/i/life` |
|---|---|
| `played_collision` | `frame 7: 1; frame 9: 2; frame 10: 1; frame 12: 2,3; frame 17: 4; frame 18: 2,3; frame 19: 4; frame 20: 2,3; frame 22: 6; frame 25: 4,6; frame 26: 3; frame 27: 3; frame 28: 3; frame 29: 3,4,5; frame 30: 3; frame 31: 4; frame 32: 3,9; frame 33: 4; frame 34: 3; frame 35: 3,4,9; frame 36: 3,9; frame 37: 3,4; frame 38: 4; frame 39: 4; frame 40: 4; frame 41: 4,12; frame 42: 4; frame 43: 4; frame 44: 4` |
| `played_trigger` | `frame 7: 1; frame 9: 2; frame 10: 1; frame 12: 2,3; frame 17: 4; frame 18: 2,3; frame 19: 4; frame 20: 2,3; frame 22: 6; frame 25: 4,6; frame 26: 3; frame 27: 3; frame 28: 3; frame 29: 3,4,5; frame 30: 3; frame 31: 4; frame 32: 3,9; frame 33: 4; frame 34: 3; frame 35: 3,4,9; frame 36: 3,9; frame 37: 3,4; frame 38: 4; frame 39: 4; frame 40: 4; frame 41: 4,12; frame 42: 4; frame 43: 4; frame 44: 4` |
| `prewarm` | `0: 4,5; 1: 0,4,5,9; 2: 0,4,9; 3: 0,2,4,9; 4: 0,2,4,9; 5: 0,2,4; 6: 0,1,2,4; 7: 0,2,4` |
| `prewarm_long_life` | `0: 2,16; 1: 2,16; 2: 2,16; 3: 15,22,24` |
| `prewarm_short_life` | `0: 4; 1: 4,6; 2: 4,7; 3: 4,6,7` |
| `start_curves` | `4: 3; 5: 3; 6: 3; 7: 3; 9: 3; 10: 0` |
| `sub_emitters` | `16: 1; 17: 1; 18: 1; 19: 1` |

Other small incremental error increases are at most 1e-5 in velocity, 2.62e-6 in
position, 3.6e-6 in rotation and 2.5e-7 in size. They are also nonblocking in these
controls. This disposition applies to the small increments only, not to the total
errors of already-failing sub-emitter positions or other functional gaps.

All exact keys, Native/baseline/candidate values and magnitudes remain in
`validation/runs/particle-causal-root-admission-20261102/precision-residuals.json`
under the global cache. The raw broad union still retains 305 failures and 3271
Native-differing image pixels. Emission retains 847 failures, including 366 lifetime
and 455 seed components; its model's predicted full bit equality did not materialize.
The separate lifetime probe's three child positions and three child ages remain
functional gaps. No Native witness or further precision-only research is requested.

## Death-event origin correction, 2026-11-02

The tested `2cc4eef0` origin correction changes only `sub_emitters_child` calls
15-19, particles 0/1, position z. Their error falls from about 0.04 to 0.01 and
remains above tolerance; all ten lives are unchanged. Every other broad module
and all 17 images are byte-identical, apart from the existing ignored wall-clock
fields. The broad comparator still retains 305 failures. First child age stays
functional and open. The earlier precision ledger and all its raw records remain
unchanged; no tolerance or ignore rule was added.
