# Partial-curve playback probe

Clips that animate different parts of one generic rig, played by legacy Animation and by Animator
controllers, on Unity 6000.0.71f1. The rig (built by `PartialProbe`) has A (with child B), C (with
a MeshRenderer), D and a `PartialTarget` script (floats f and g, bool b, int i). The clips:

- P (1 s, loops): A's position and rotation, B's position, C's rotation, D inactive (m_IsActive 0), f;
- Q (1 s, loops): A's x position (no y, z curves) and scale, D's position, g;
- R (2 s, loops): A's position, f;
- S (0.5 s, played once): A's x position, g;
- Bools (1.375 s, once): values around the bool and int conversions, one per 0.125 s key, on D's
  activity, the script's and C's renderer's enabled, b and i.

Each exists as a generic clip and as a legacy clip (L prefix). Every record holds every part's
local position, rotation and scale, D's and the renderer's and the script's enabled state, f, g, b
and i; legacy records also hold each state's enabled flag, weight and time.

Animator cases (`Animator.Update(0)`, then ten of 0.125 s), each with write defaults on and off:
P to Q and back on a bool (0.25 s fixed transitions), with script writes during and after the
transition (`.poke`), the condition back true when the transition completes (`.bounce`) and the
condition true before the first update (`.first`); `Play` of Q and back without a transition
(`play.*`, Q's transition back to P is true at once); `CrossFade` over half of Q (`crossfade.*`); a
1D blend tree of P and Q at 0.3, 0.8 and 1 (`tree.*`, with script writes in `tree.*.poke`);
`Rebind` after script writes (`rebind.*`); and the Bools clip (`bools`).

Legacy cases (a frame of 0.125 s each through `Time.captureDeltaTime`; actions in Update, records
in LateUpdate): Play of Q after P, CrossFade and CrossFade back, Blend at once and with a fade, a
lone state at half weight (also with script writes, and with a write before it first plays), two
states of 0.3, P on layer 1 over Q or R on layer 0 at several weights, P additive over Q, Stop,
fading P out without stopping it, the Once clip played and cross-faded in, the Animation component
disabled and the GameObject deactivated for two frames, and the Bools clip.

Assets are committed. To re-create them, record two independent windowed GPU Editor captures,
publish them when they agree, and run Unbound:

    python3 tests/projects/animpartial/run.py create  --release 6000.0.71f1
    python3 tests/projects/animpartial/run.py editor  --release 6000.0.71f1 --label <label>
    python3 tests/projects/animpartial/run.py publish --release 6000.0.71f1 --label <label>
    UNBOUND=<unbound> python3 tests/projects/animpartial/run.py unbound --release 6000.0.71f1

The comparison (tests/projects/modelroot/compare.py) lists every missing, extra and differing
value; numbers match within 2e-4, a rotation also matches its negation.
