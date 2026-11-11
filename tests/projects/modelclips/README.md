# Model clip settings probe

What the model importer makes of a generic rig's `clipAnimations` on Unity 6000.0.71f1, and how
the Animator plays the clips. `Rig.fbx` (Blender, `make_models.py`: 24 fps, one take "Scene" of
frames 0..48) has 30 clips in its `.meta`: ranges (also fractional and past the take's end), loop
time, loop pose, clip cycle offsets (with and without loop time and loop pose), mirror, wrap mode,
transform masks (from the model, from an AvatarMask asset, a stale copy of another mask, a list
with mask type None), additive reference frames (inside and outside the range, and disabled),
Animator parameter curves (float, int and bool parameters, a curve without a parameter, with loop
pose) and events (also with cycle offsets). `RigDefault`, `RigReduced` and `RigOptimal` are the same file with the default clip
settings and animation compression Off, KeyframeReduction and Optimal, and `RigStatic` without
animation import. Two Humanoid rigs carry one clip each under a reducing compression:
`HumanReduced` (Keyframe Reduction, Rotation and Scale Errors 5, Position Error 0.5) and
`HumanOptimal` (Optimal, Position Error 5); other Humanoid clip settings are not covered.

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
- Kept keys keep the time, value and tangents they have with compression Off; a curve whose
  keys all lie within the error of its first key becomes its two end keys with zero tangents.
- The key times are whole frames in single precision (frame times 1 / frame rate).
- The Editor's clips have no transform curves (mask type None); the probe records Animator
  curves only. Unbound keeps the transform curves of the two helper bones outside the human
  skeleton (docs/COMPATIBILITY_GAPS.md).
- A curve whose keys all stay below 1e-6 is recorded as `{"noise":1}`: such values are float
  noise of the muscle conversion around zero, above the reduction's own zero limit, and which
  of their keys stay follows their last bits (66 of the 130 curves of each model here).

Not in the reference (an exploratory capture of a richer rig, 130 curves over 31 frames, the
same in each of its 20 clips): the same rule gives the Editor's keys of all 130 curves at
Position Errors of 0.5 and 5 when it starts from the Editor's own every-frame curves (managed/UnityEngine.Tests
`HumanClipReductionTests` holds four of them); starting from Unbound's converted curves, four
muscle curves that stay below 6e-3 keep one key at a neighbouring frame at an error of 5.
Unmeasured for Humanoid clips: clips of fifty frames and more (the limit of a reduced segment
is the generic reduction's, tests/projects/modelaxis), other frame rates, a Position Error of
0 (Unbound then keeps every key) and a humanoid oversampling above one.

Unbound matches 3211/3211 records. The generic reducing compression modes are compared by their
bindings only here; their keyframe reduction is measured in tests/projects/modelaxis (`Reduced`
set).
