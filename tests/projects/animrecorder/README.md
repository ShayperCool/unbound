# animrecorder

What the Recorder editor's `UnityEditor.Animations.GameObjectRecorder` answers in Unity 6000.0.71f1
Play mode (`Assets/Scripts/AnimRecorderProbe.cs`), on a hierarchy of a root (a script with a float, an
int, a flag and an enum field, a Light, an Animator), a child with a SpriteRenderer and a grandchild.
Run it with `python3 tests/projects/animrecorder/run.py gate`; the macOS Editor records it in batch mode.

- `api.*`: the constructor, `root`, `isRecording`, `currentTime`, argument checks of `SaveToClip`
  (a frame rate of 0 or less, negative errors only with reduction on, nothing recorded), `Bind` of a
  type that is not a Unity object, a null type, an Animator's enabled state (never bound), a binding
  bound twice, `ResetRecording` (clears the bindings and the recording; `currentTime` stays until the
  next snapshot).
- `bind.*`, `animatable.*`: what `BindComponent`, `BindAll` and `BindComponentsOfType` bind and what
  `AnimationUtility.GetAnimatableBindings` offers: a Transform's rotation as its quaternion, then
  position and scale; a script's enabled state first, its enum as a discrete binding; nothing for an
  object outside the root; number bindings before object references in `GetBindings`.
- `same.*`: `Bind` beside `BindComponent` (see the residuals).
- `snap.*`, `motion.*`, `irregular.*`, `quat.*`, `values.*`, `euler.*`, `short.*`, `reuse.*`: the
  clips `SaveToClip` makes of Transform motion (regular and irregular snapshots), a rotation set as a
  quaternion, script, Light, active-state and sprite values, bound Euler angles and a recording
  shorter than a frame, at several frame rates and with the `CurveFilterOptions` on and off: every
  curve's binding and every key (time, value, tangents, tangent mode, weights).
- `read.*`: `AnimationUtility.GetEditorCurveValueType` and `GetFloatValue` of the bindings the
  recorder reads.
- `objectLength.*`: a clip's length with an object reference curve: its last key lasts a frame of
  the clip's frame rate.

Keys are written as JSON numbers and compare within 2e-4 (`run.py`): the recorded inputs and curve
evaluation differ from the Editor's in the last bits of single precision, which slopes over a short
frame magnify. Key counts, bindings, tangent modes and strings compare exactly. `residuals.json` lists
what stays different: material property bindings of a renderer's materials, which bindings `Bind`
adds a second time beside `BindComponent`'s, and the reduction with a float error of 0.
