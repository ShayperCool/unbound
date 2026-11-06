# Animation culling probe

Animator.cullingMode (AlwaysAnimate, CullUpdateTransforms, CullCompletely) and
Animation.cullingType (AlwaysAnimate, BasedOnRenderers) against renderer visibility.
`Assets/Scripts/AnimCullingProbe.cs` makes one camera looking at the origin and, in frame 1's
Update, a holder per case with a rig under it: an Animator (controller `Culling`: state A, a 0.5 s
loop moving the empty child `Marker` along x, with an `OnClipEvent` event at 0.1 s, to state B by
exit time 1.2; or `RootMotion`: the root moving up 1 unit/s) or a legacy Animation (`LegacyLoop`),
plus the case's renderers. A schedule moves the holder between the origin and x = 1000 in Update:
A is on screen in frames 1-11 and 32-60, B from frame 20. Each LateUpdate (after the animation
update) records every renderer's `isVisible`, Marker's position, the root's position, the state and
its normalized time (or the legacy state's time), and the event, `OnAnimatorMove` and
`OnAnimatorIK` counts; the root-motion cases also record what `OnAnimatorMove` and the root showed
at the frame's Update (before that frame's animation, after the previous frame's rendering).
Frames are 0.02 s (`Time.captureDeltaTime`).

Cases: `a/` the state machine for each mode with schedules A and B; `rm/` builtin root motion;
`rmm/` root motion through `OnAnimatorMove` (A and B); `m/` two renderers, the second on screen
exactly while the holder is off and disabled from frame 20; `d/` and `i/` the only renderer disabled
or its GameObject inactive in frames 10-29; `n/` no renderer; `s/` a SkinnedMeshRenderer as the only
renderer, `updateWhenOffscreen` off and on; `h/` the humanoid model of `tests/projects/humanoid`
with an IK pass and a cube; `l*/` the legacy Animation counterparts; `late/` Animators and
Animations created in frame 5.

Measured in the Unity 6000.0.71f1 Editor (built-in pipeline, a windowed GPU Editor, its Scene views
closed so that the Game view's camera is the only one; a Scene view camera would count too):

* The culling uses `isVisible` at the animation update: what the previous frame's rendering left,
  after this frame's EarlyUpdate made renderers no camera drew invisible. A renderer disabled or
  deactivated earlier in the frame no longer counts. One visible renderer of several is enough; a
  hierarchy without renderers is always culled. A skinned renderer counts by its own visibility,
  whatever `updateWhenOffscreen`.
* CullUpdateTransforms: time, the state machine, transitions, events, root motion and
  `OnAnimatorMove` go on; the pose is not written (Marker keeps its last value, or its initial one)
  and `OnAnimatorIK` does not run.
* CullCompletely: nothing advances and nothing catches up afterwards. The Animator's first update
  still runs at zero time (`OnAnimatorMove` is called, no IK).
* When a renderer becomes visible (during the camera's culling), a culled Animator writes its pose
  and runs its IK pass once more; after a skipped update (CullCompletely, or that first update) its
  `OnAnimatorMove`/builtin root motion also runs again with the last evaluation's deltas, so the
  root moves by one extra delta.
* Legacy BasedOnRenderers: while culled the state's time, events and pose stay; the first update
  that runs again advances by all the culled updates' time and fires the skipped events. A state's
  first update (sampling its start) is spent even while culled.

Not part of the probe: Animators created in the first frame of Play mode are evaluated without
advancing in their first update (measured while designing the probe; it is outside culling, so the
rigs are built in frame 1).

Record (two independent windowed GPU Editor captures; publish only byte-identical ones):

    python3 tests/projects/animculling/run.py create --label create   # assets, scene (batchmode)
    python3 tests/projects/animculling/run.py editor --label native
    python3 tests/projects/animculling/run.py publish --label native

Check the engine (the landing gate, `gate.json`): `python3 tests/projects/animculling/run.py check`.
`compare.py` compares every record (numbers within 1e-4) and lists missing, extra and differing ones.
Run output stays under `$XDG_CACHE_HOME/unbound/validation/runs/animculling/`.

Unmeasured: other releases, particle/line/trail renderers, non-transform properties under
CullUpdateTransforms, the Physics update mode, playable outputs on a culled Animator, and the Scene
view camera.
