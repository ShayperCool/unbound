# The Editor's animator controller API, AnimationUtility, AnimationMode and line bounds

Unity 6000.0.71f1, recorded with the macOS Editor in batch mode (two byte-identical captures,
585 values); nothing here depends on the OS or the graphics device. `AnimatorApiProbe.Setup`
authored the clips in `Assets/Clips` and `Assets/Controllers/Authored.controller` once (the
controller with the same builder the capture runs); `Spare.anim` is a copy of `Walk.anim` that
only the last section loads. `AnimatorApiProbe.Record` runs everything in Play mode:

* `model.*`: a controller built through `UnityEditor.Animations` at `Assets/Built.controller`
  (`CreateAnimatorControllerAtPath`): parameters (duplicates, objects, removal by index and by
  value, unique names), states and sub-machines (positions, unique names, adding one twice),
  every kind of transition (state, exit, to a machine, Any State, Entry, machine transitions,
  default exit times from the motion's duration), conditions, removals, display names, blend
  trees (automatic thresholds, nested, 2D, Direct, ranges), layers (unique names, a synced layer
  with effective motions and behaviours), state machine behaviours (contexts, `GetBehaviours`),
  removing a state, a machine and a layer; each step's answers and logs, then a dump of the whole
  model and the file the Editor writes, one row per document with file ids replaced by the
  object's place in the model. `memory.*` builds the same controller without an asset;
  `plain.*` and `withClip.*` the default blend parameter and `CreateAnimatorControllerAtPathWithClip`.
* `read.*`: the authored controller as `AssetDatabase` loads it, dumped the same way, and its copy
  `Unset.controller`, whose `.meta` names no main object (`mainObjectFileID: 0`).
* `clip.*`: Motion values, `GetAnimationClipStats`, curve and object curve bindings of each clip.
* `play.*`: an in-memory controller (A to B on a trigger) playing on an Animator, edited while it
  plays (a parameter, a state and transition, a state's speed); the authored controller on an
  Animator.
* `util.*`, `stream.*`: `onCurveWasModified` for each kind of curve edit, tangent modes,
  `GetAnimationClips`, `SetAnimationClips`, float, discrete and object values,
  `PropertyModificationToEditorCurveBinding`, generic bindings, `GetAnimationStreamBindings`.
* `mode.*`: `AnimationMode` with and without a driver: sampling, animated properties, recorded
  modifications, stopping.
* `bounds.*`: `bounds` and `localBounds` of line renderers (local and world space, loop, width
  curve, one and no points) and of a trail.
* `extra.*`: new transition and blend tree defaults, the messages a controller's compilation logs,
  name collisions, removal from a sub-machine, both destinations, ClampedAuto and Auto tangents of
  several shapes, the raw tangent mode bits, the clip callback of a load and of
  `AllocateAnimatorClip`, and whether blend trees with no, looping, non-looping and mixed children
  loop (as Motions and as the playing state reports it).

```
python3 tests/projects/animatorapi/run.py record --label <label>     # macOS: batch Editor
python3 tests/projects/animatorapi/run.py publish <label-a> <label-b>
UNBOUND=<unbound> python3 tests/projects/animatorapi/run.py gate
```

Values compare exactly. The Scripting API documents what each member does
([AnimatorController](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.AnimatorController.html),
[AnimatorStateMachine](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.AnimatorStateMachine.html),
[AnimatorState](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.AnimatorState.html),
[BlendTree](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.BlendTree.html),
[AnimationUtility](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimationUtility.html),
[AnimationMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimationMode.html));
the values below are observations of this release, not documented guarantees:

* A new `AnimatorStateTransition` has duration 0.1 and exit time 0.9 with an exit time and no
  fixed duration; `AddTransition` turns the exit time off and the fixed duration on, and with a
  default exit time sets duration 0.25 and exit time 1 - 0.25 / the motion's average duration
  (0.75 without a motion). Any State transitions get 0.25 and 0.75 without an exit time.
* Names made unique get " 0", " 1", ... (per state machine for states and machines, which do not
  collide with each other; per controller for parameters and layers). `AddParameter` of an object
  keeps its name; removing a parameter by value needs every field to match.
* A machine transition with neither a destination nor an exit is dropped from the machine's
  transitions (removing it still destroys it and answers false). Removing a state removes the
  transitions to it inside the machine it is removed from, and destroys them.
* Editing a controller (any of the edits above) while an Animator plays it makes the Animator start
  over at once: its parameters are back at their defaults, it has no current state until its next
  update, which evaluates the default state at time 0.
* Compiling a controller (its clips, `GetBehaviours`, an Animator taking it) logs an error for each
  parameter a blend tree uses that does not exist and a warning for each state or Any State
  transition left (not muted, solo where a state has solos) without an exit time or a condition.
* `GetBehaviours` lists, per layer, the state machines' behaviours and then the states'; a
  state machine behaviour's context is the machine and the state in it whose full path hash is
  smallest.
* A blend tree loops when every child loops (an empty one does), and so does its playing state. A
  controller whose `.meta` names no main object still has the controller as its main object.
* Automatic thresholds spread a tree's children evenly from its minimum to its maximum threshold;
  a blend tree's average duration is the mean of its clips' (nested trees flattened).
* ClampedAuto tangents are the central difference, zero at an extremum and at the ends, at most
  four times the smaller rise over the two intervals' span; Auto ones the mean of the two slopes
  (the one slope at an end); either mode on one side sets both tangents, after Linear and before
  Constant. Setting a mode marks the key broken.
* Line bounds are the points' box grown by the width multiplier (not its curve), in the line's
  space; a trail's are its points' box grown by its width.
