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

Any State (`Any`: P, and an Any State transition to Q on a bool, 0.25 s, not to itself): the bool
set in the update a `Play` request enters P (`any.request`), without the request (`any.plain`) and
before the first update (`any.first`).

Humanoid cases (`human.*`, on HumanA, the humanoid probe's rig, which `run.py` copies to
`Assets/Rig/Resources`; records: the hips' local position, nine bones' local rotations and the
human pose a `HumanPoseHandler` reads back, body and every muscle by index), each with write
defaults on and off: clips HM (Spine Front-Back, Left Arm Down-Up, Left Forearm Stretch, Right
Upper Leg Front-Back) and HN (Spine Front-Back, Left Forearm Stretch, Head Nod Down-Up, Right Arm
Down-Up), no body or goal curves, HM alone (`alone`), HM to HN and back on a bool (`switch`, with
script writes to bones in `switch.*.poke`), a 1D tree of HM and HN (`tree`), HumanA's whole clip A
to HM and back (`froma`); A with HM on an override layer of weight 1 and 0.6 (`over`, `overhalf`),
through a humanoid mask with only the left arm (`mask`, `maskhalf`), on an additive layer of
weight 0.6 without and with that mask (`add`, `addmask`), and HN with HM on an override layer of
weight 0.5 (`overn`).

Legacy cases (a frame of 0.125 s each through `Time.captureDeltaTime`; actions in Update, records
in LateUpdate): Play of Q after P, CrossFade and CrossFade back, Blend at once and with a fade, a
lone state at half weight (also with script writes, and with a write before it first plays), two
states of 0.3, P on layer 1 over Q or R on layer 0 at several weights, P additive over Q, Stop,
fading P out without stopping it, the Once clip played and cross-faded in, the Animation component
disabled and the GameObject deactivated for two frames, the Bools clip, the Once clip played
backwards from its end (speed -1) alone and cross-faded in, and LE (A's x position, events at
0.3125, 0.4375, 0.5625 and 0.6875 s calling `PartialTarget.Hit`) with the events each frame
received in `.events` records: cross-faded out to Q over 0.25 s (its fade stops it), stopped by
`Stop`, and at weight 0; and LZ (events at 0 and 0.3125 s) started over P by `CrossFade` (0.25 s),
`Blend` (to 0.5 over 0.25 s), `Play`, `Play` with its weight set to 0, and `Play` at speed 0 for
three updates before speed 1 (`legacy.events.start.*`); a time set on LZ in the update Play starts it,
while it is held at speed 0, and while it plays (`legacy.events.start.timeset`,
`legacy.events.start.speed0.timeset`, `legacy.events.timeset`); Blend and CrossFade of P after Stop
(`legacy.stop.blend`, `legacy.stop.crossfade`) and Play of S during the fade out its CrossFade gave
it (`legacy.once.fade.play`); and Flags (LFlags), the Bools values on a SpriteRenderer's flips and
a CanvasGroup's bools and alpha, on a second rig (`flags.*` with an Animator, `legacy.flags.*`).

The records before the Any State, humanoid, reversed and event cases are two identical windowed
GPU Editor captures on Linux. Those cases were added from two identical batchmode captures of the
macOS Editor (`batch`, then `merge`, which adds only the records the reference lacks; the
captures' other records equal the reference's).

Assets are committed. To re-create them, record two independent windowed GPU Editor captures,
publish them when they agree, and run Unbound:

    python3 tests/projects/animpartial/run.py create  --release 6000.0.71f1
    python3 tests/projects/animpartial/run.py editor  --release 6000.0.71f1 --label <label>
    python3 tests/projects/animpartial/run.py publish --release 6000.0.71f1 --label <label>
    python3 tests/projects/animpartial/run.py batch   --release 6000.0.71f1 --label <label>  # any OS
    python3 tests/projects/animpartial/run.py merge   --release 6000.0.71f1 --label <label>
    UNBOUND=<unbound> python3 tests/projects/animpartial/run.py unbound --release 6000.0.71f1

The comparison (tests/projects/modelroot/compare.py) lists every missing, extra and differing
value; numbers match within 2e-4, a rotation also matches its negation.
