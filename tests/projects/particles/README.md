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
