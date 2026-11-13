# Model clip settings probe

What the model importer makes of a generic rig's `clipAnimations` on Unity 6000.0.71f1, and how
the Animator plays the clips. `Rig.fbx` (Blender, `make_models.py`: 24 fps, one take "Scene" of
frames 0..48) has 30 clips in its `.meta`: ranges (also fractional and past the take's end), loop
time, loop pose, clip cycle offsets (with and without loop time and loop pose), mirror, wrap mode,
transform masks (from the model, from an AvatarMask asset, a stale copy of another mask, a list
with mask type None), additive reference frames (inside and outside the range, and disabled),
Animator parameter curves (float, int and bool parameters, a curve without a parameter, with loop
pose) and events (also with cycle offsets). `RigDefault`, `RigReduced` and `RigOptimal` are the same file with the default clip
settings and animation compression Off, KeyframeReduction and Optimal, `RigLoose` the same under
KeyframeReduction with loose errors, and `RigStatic` without animation import. Two Humanoid rigs carry one clip each under a reducing compression:
`HumanReduced` (Keyframe Reduction, Rotation and Scale Errors 5, Position Error 0.5) and
`HumanOptimal` (Optimal, Position Error 5); `HumanLong` is the same rig with compression Off
and a clip of 31 frames (made by tests/projects/humanoid/make_models.py); `HumanTwistNone`,
`HumanTwistAll` and `HumanTwistPart` carry animated bones that are not human bones under three
clip masks; `HumanTwistGeneric` is the same file imported as Generic. `HumanSingle`,
`GenericSingle` and `LegacySingle` are `HumanLong` with one clip of no frames (first and last
frame 5) imported as Humanoid, Generic and Legacy; their records come after the others and were
recorded with the macOS Editor (two identical captures, whose other records match the Linux
reference); the `HumanMask*` models (see Body masks) come last. Other Humanoid clip
settings are not covered.

`ClipProbe` records each imported clip (length, frame rate, flags, events,
`AnimationUtility.GetAnimationClipSettings`, curve bindings, the curves' values at every half
frame, which clips `Resources.LoadAll` and `AssetDatabase` list) and plays every clip on a fresh
instance with `Animator.Update(0)` and then twenty steps of 0.125 s (three frames), in a base
layer, as an additive layer over the Full clip, as an override layer over it, and with a state
cycle offset; each step records the local transforms, the parameters, the states' time and the
events received.

The assets are committed. To make them again (after changing `make_models.py` or the clip list
in `ProbeRunner.Create`), regenerate the models and let a disposable Editor copy write the
`.meta`, masks and controllers back into `Assets/`:

```bash
blender -b --factory-startup --python tests/projects/modelclips/make_models.py
python3 tests/projects/modelclips/run.py create --release 6000.0.71f1 --label <label>
```

Record two independent Editor captures (GPU, private Xvfb), publish after they agree, run
Unbound, compare:

```bash
python3 tests/projects/modelclips/run.py editor --release 6000.0.71f1 --label <label>
python3 tests/projects/modelclips/run.py publish --release 6000.0.71f1 --label <label>
UNBOUND_CLI=<unbound> python3 tests/projects/modelclips/run.py unbound --release 6000.0.71f1 --label <run>
python3 tests/projects/modelclips/compare.py \
  tests/projects/modelclips/Reference/unity-6000.0.71f1.jsonl \
  ~/.cache/unbound/validation/runs/modelclips/<run>/unbound/values.jsonl
```

Captures go to `$XDG_CACHE_HOME/unbound/validation/runs/modelclips/<label>`; labels are
write-once. The comparison lists every missing, extra and differing value; numbers match within
2e-4 (absolute or relative), a rotation also matches its negation.

What the Editor does (measured):

- Clip IDs are the entries' `internalID`. A range past the take's end stops at the take's end; its
  length is computed in single precision. Every animated node gets position, rotation and scale
  curves, constant ones too, with every animation compression. Compression Off keeps keys at the
  range's start, on every take frame after it and at its end (a range from frame 4.5 has keys at
  4.5, 5, 6, ...); rotations and scales have smooth tangents (the slope between the neighbouring
  keys), positions are linear between keys.
- With animation import the model rests in the first take's pose at its start; without, in the
  file's static transforms (here frame 1's).
- A transform mask removes the curves of the transforms it leaves out; their children keep
  theirs. With `maskType` CopyFromOther the importer reads the `maskSource` asset, not the copy
  in `transformMask`; with mask type None the list is ignored.
- `additiveReferencePoseFrame` inside the range refers to the clip itself, at the time from its
  first frame; outside it, to the take's hidden `__preview__<take>` clip at the frame's time on the
  take. Without `hasAdditiveReferencePose` the frame's time on the take is kept.
- Parameter curves become float curves on the root's Animator, normalized time scaled to the
  clip's length (slopes divided by it). They drive float parameters only; int and bool parameters
  keep their values.
- Imported clips are NotEditable; the preview clip of every take is also HideInHierarchy. It is
  listed by `LoadAllAssetsAtPath`, not by `Resources.LoadAll` or `LoadAllAssetRepresentationsAtPath`.
- Loop pose and cycle offset act at play time, the curves stay as imported, and only when the
  clip loops. The clip's and the state's cycle offsets add up; a non-looping clip ignores both.
  Loop pose adds `(start − stop) × phase` to every float value and turns each rotation by
  `q · normalize(phase · d.xyz, d.w)` with `d = stop⁻¹ · start` (the sampled first and last
  frames), the phase being the sampled time over the length, cycle offset included.
- Events move with the cycle offsets; an `Animator.Update(0)` fires the events at the start time
  (once), and a state offset landing exactly on an event fires it.
- Mirror has no effect on generic clips; the setting is kept. Wrap mode is kept and does not
  change Animator playback.

## Humanoid clips under a reducing compression

`keys.<model>.<clip>` records the number of Animator curves of the clip, and
`keys.<model>.<clip>.|<curve>` every such curve key by key: times, values and both tangents
(compression Off keeps 21 keys a curve in this clip). The Manual's
[Animation tab](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AnimationClip.html)
says Keyframe Reduction applies to Legacy, Generic and Humanoid rigs and Optimal to Generic
and Humanoid ones, and gives the test of the Position and Scale Errors: a key goes while the
reduced value stays within the error's percentage of the original one. It does not say which
error a Humanoid clip's curves take.

- Every curve of the clip (the body's position and rotation components, the goals, the
  muscles) is reduced on its own as a float curve with the Position Error. Rotation and Scale
  Errors of 5 change nothing (`HumanReduced`), a Position Error of 5 drops more keys
  (`HumanOptimal`); Keyframe Reduction and Optimal reduce alike.
- Kept keys keep the time, value and tangents they have with compression Off; a curve that the
  constant at its first key's value holds (at every frame, key and halfway between keys)
  becomes two keys at its first and last times with that value and zero tangents.
- The key times are whole frames in single precision (frame times 1 / frame rate).
- A Humanoid clip ends at its last key: `HumanLong`'s 31 frames at 30 a second have a length
  of 1.03333342 (31 frames of 1/30 s in single precision), one step past the take's end. Its
  curves have a key per frame and one at the end, 32, and the last key's tangent is half the
  slope from the key before it: the length divided by the frame length rounds past 31, so
  the clip is sampled once more after its end, and that sample (the curve held at its end
  value) takes part in the last key's tangent without becoming a key. At 20 frames
  (`HumanReduced`'s clip with compression Off) the division is exact and the last tangent is
  that slope itself.
- These clips have no transform curves (mask type None); `keys.*` records Animator curves only.
- A curve whose keys all stay below 1e-6 is recorded as `{"noise":1}`: such values are float
  noise of the muscle conversion around zero, above the reduction's own zero limit, and which
  of their keys stay follows their last bits (66 of the 130 curves of each model here).

## Transform curves of Humanoid clips

`HumanTwistNone`, `HumanTwistAll` and `HumanTwistPart` are one file (made by
tests/projects/humanoid/make_models.py: the humanoid probe's small rig and clip, with its two
twist bones, which are not human bones, animated too: the left one turns, the right one moves)
under three clip masks: None; made from the model with every transform on; and the same with
the left twist bone off. `curves.<model>.<clip>` records the number of Animator curves, of
curves of other kinds and of paths with transform curves; `curves.<model>.<clip>.<path>` the
properties of such a path, the number of keys and the first and the middle key of each
(positions times 100, the inverse of the rig's root scale). The Manual's
[Avatar Mask](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AvatarMask.html) page
says a mask picks portions of the humanoid body or bones of the Transform hierarchy and can be
applied in the import settings of animation files; it does not say what a Humanoid clip keeps
without a mask.

- With mask type None the clip has its 130 Animator curves and no transform curve.
- With a mask made from the model the two twist bones keep their curves, all ten properties
  each (position, rotation and scale, 21 keys); the human bones, the `Spine` transform between
  two of them, the root above the hips and the model's root have none.
- A transform the mask turns off has no curves (`HumanTwistPart`: the right twist bone only).
- The Animator curves are 130 under each mask.

Body masks: `HumanMaskBody`, `HumanMaskAsset`, `HumanMaskStale`, `HumanMaskNone`,
`HumanMaskRest` and `HumanMaskRoot` are HumanTwistAll's file with body parts off in the clip's
`bodyMask` (`keys.*` and `curves.*` of each, recorded last with the macOS Editor, two identical
captures): the head, the left arm, the left fingers and the left hand's goal in a mask made
from the model (`HumanMaskBody`); a mask copied from `Assets/HumanMasks/BodyOff.mask`, which
turns off the root, the body, the right leg, the right fingers and the right foot's goal, while
the meta's copy has every part on (`HumanMaskAsset`) or the head off (`HumanMaskStale`); the
head off with mask type None (`HumanMaskNone`); every other part but the root
(`HumanMaskRest`); and the root alone (`HumanMaskRoot`).

- The clip has no Animator curves of the parts the meta's `bodyMask` turns off, whatever the
  mask type: 82 curves for `HumanMaskBody`, 118 for `HumanMaskStale` and `HumanMaskNone`, 55
  for `HumanMaskRest`, 123 for `HumanMaskRoot`. The asset's own body parts are not read: all
  130 for `HumanMaskAsset`.
- Root takes the root's motion curves (RootT, RootQ); Body the spine's, chest's and upper
  chest's muscles; Head the neck's, head's, eyes' and jaw's; a leg or an arm its muscles; the
  fingers a hand's finger muscles; an IK part its goal's seven curves. The other curves keep
  the values they have with every part on, and the twist bones keep their transform curves.

Not measured: an empty mask made from the model, a human bone turned off in the transform list,
an asset mask with a transform off, how a clip without some parts' curves plays, and a
transform with only some channels keyed or none (the exporter keys every channel of every bone).

Not in the reference (an exploratory capture of a richer rig, 130 curves over 31 frames, the
same in each of its 20 clips): the same rule gives the Editor's keys of all 130 curves at
Position Errors of 0.5 and 5 when it starts from the Editor's own every-frame curves (managed/UnityEngine.Tests
`HumanClipReductionTests` holds four of them); starting from Unbound's converted curves, four
muscle curves that stay below 6e-3 keep one key at a neighbouring frame at an error of 5.
Unmeasured for Humanoid clips: clips of fifty frames and more (the limit of a reduced segment
is the generic reduction's, tests/projects/modelaxis), other frame rates, a Position Error of
0 (Unbound then keeps every key) and a humanoid oversampling above one.

## Rotation signs

`nodes.HumanTwistGeneric` records the local rotation of every transform of `HumanTwistGeneric`
(the HumanTwist file imported as Generic) as imported, and its clip's rotation curves are
recorded like every clip's; the comparator matches the fields ending in `.q` and the curves'
values with their signs, not up to a negation (a quaternion's sign does not change the
rotation, but scripts read it).

- 20 node rotations, four of them with a negative w: each has the sign a rotation matrix gives
  its quaternion (w positive while |w| is above 1/2, else the component of the matrix's
  largest diagonal element positive).
- The 19 rotation curves start with the same sign at the take's start, four with a negative w,
  and every key lies in the hemisphere of the key before it.
- A clip from the middle of a take (`clip.Rig.Beyond`, from frame 40) starts with the sign the
  take's rotations have reached there: `Spin` has turned past half a turn and starts with a
  negative w, the opposite of its own matrix's sign.

Not measured: a tie with the x element and a trace of exactly zero (Legacy imports, converted
static models and a y-z tie: see the next section), a range that starts before its take, and a
turn of half a revolution or more between two frames.


## Signs of Legacy and axis-converted imports, a rotation within its error

Recorded last with the macOS Editor (two identical captures): `nodes.HumanTwistLegacy` and the
clip curves of `HumanTwistLegacy` (HumanTwistGeneric's file imported as Legacy), `nodes.AxisSigned/*`
(six static models of tests/projects/modelaxis' Signed set: Z-up and Y-up files with a reversed
front axis, with and without Bake Axis Conversion and Preserve Hierarchy), and `RigRotLoose`
(Rig under Keyframe Reduction with a rotation error of 45 degrees and position and scale errors
of 0.5%; Spine turns up to 40 degrees from its first key and ends 10 degrees from it).

- Legacy node rotations and curves take the Generic import's signs.
- A converted node takes the sign of its final rotation's matrix. On a tie between two diagonal
  elements the later axis decides: the root of `U2S1B0H0` and `U2S1B0H1` (y and z elements
  equal) has z positive and w negative.
- Spine's rotation becomes two keys with its first value, as positions and scales within their
  error of the first value do.

Not in the reference: Blob (tests/projects/blendshapes) under a Position Error of 100% keeps its
float curve's key at frame 29 (0.326) and ends at 1.00000012; Unbound drops that key and ends
at 1 (open).

## Whole-curve reduction

`RigLoose` is `Rig.fbx` under Keyframe Reduction with a Rotation Error of 15 degrees and Position
and Scale Errors of 40%. `curvekeys.RigLoose.Scene.<path>|<property>` records every key of each
curve of its clip (times, values, both tangents; curves that never leave float noise around zero
are recorded as noise). Several curves stay within the error of their first value but end
elsewhere: Head's z position (0.004, 0.006 at 1.5 s, 0.003 at the end), Hips' x scale (100 to
150) and its z scale.

- Each of them becomes two keys at the clip's first and last times, both with the first key's
  value and flat tangents (Head z: 0.004 and 0.004; Hips x scale: 100 and 100). A segment between
  the curve's own first and last values would end at the last value instead.
- The curves that leave the error of their first value are reduced from the start as in the
  `Reduced` set of tests/projects/modelaxis.

A rotation curve that stays within the Rotation Error of its first value and ends elsewhere
takes the rule too (`RigRotLoose`, see the section above). Not measured: a float curve (Animator
parameter or blend shape weight) that does so, and such Humanoid curves (the two-key curves of
`HumanReduced` have equal ends either way); the rule is applied to them as to the measured
curves.

Unbound matches 4534/4534 records. The generic reducing compression modes are compared by their
bindings only here; their keyframe reduction is measured in tests/projects/modelaxis (`Reduced`
set).
