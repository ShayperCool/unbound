# Animation remainder probe (Q629)

Unity 6000.0.71f1, recorded with the Linux GPU Editor (two byte-identical independent captures);
nothing here depends on the OS or graphics device. Reuses the project settings, scene, clips and
`ProbeTarget` script from `tests/projects/animatorapi`.

Sections (all run in Play mode):
* `blendtree.*`: `Motion.averageSpeed`, `averageAngularSpeed`, `apparentSpeed`, and `averageDuration`
  on `BlendTree` (empty, null child, empty clip, two clips with root translation/rotation, zero-speed
  non-empty clip, varied `ChildMotion.timeScale`, nested `BlendTree` flattening).
* `synclayer.*`: `Animation.SyncLayer` with two clips of different lengths, speeds, weights and
  initial times (`Time.captureDeltaTime = 0.125f`), with unsynced control, unsynced layer 1 control,
  disabled state control, and zero-weight control.
* `playtiming.*`: when a legacy `Animation` state started with `Play` first advances
  (`Time.captureDeltaTime = 0.03f`, `Time.fixedDeltaTime = 0.02f`, started in a frame whose time
  is 0.01 ahead of the fixed time): `Play` in `Update`, in `LateUpdate`, in a coroutine resumed
  after `Update`, in `FixedUpdate`, and for `AnimationUpdateMode.Fixed` in `Update` and in `FixedUpdate`. Four
  frames of state times next to the elapsed time and fixed time. A state's first step advances
  it by the clock time since `Play`: nothing in the step it began in, the whole step a frame
  later, 0.01 for the fixed-step state begun in `Update`, and 0.02 for the frame-stepped state
  begun in a `FixedUpdate` 0.02 before the frame's time.
* `propmod.extra.*`: `AnimationMode.AddPropertyModification` with a string property (recorded,
  and reverted to the modification's value) and on a prefab instance with and without an
  existing override, for both `keepPrefabOverride` values: the values are reverted and the
  instance's property modifications stay as they were before animation mode. Unbound reverts the
  values but records them as overrides; those two rows are listed in `residuals.json`.
* `culling.*`: setting obsolete `AnimationCullingType.BasedOnClipBounds` and `BasedOnUserBounds`
  versus `AlwaysAnimate` and `BasedOnRenderers` (getter readback and logs).
* `modebindings.*`: `AnimationUtility.GetCurveBindings` and internal `AnimationMode.GetCurveBindings`
  on authored `.anim` assets (`Mixed`, `Run`, `Pose`, `Empty`), runtime `SetEditorCurve` / `SetCurve`
  clips, edited copies of `Mixed`, and `null`.
* `propmod.*`: `AnimationUtility.PropertyModificationToEditorCurveBinding` (Transform, GameObject,
  MonoBehaviour float/int/bool/Vector3/enum/DiscreteEvaluation/Object/unknown, MeshRenderer, and
  null/unrelated edge cases) and `AnimationMode.AddPropertyModification` (outside mode,
  diverging modification value vs current value, duplicate registration order, bool/GameObject/
  Transform/objectReference revert, and null edge cases).
* `generic.*`: `AnimationUtility.EditorCurveBindingsToGenericBindings` and
  `UnityEngine.Animations.GenericBinding` properties across Transform, GameObject, MonoBehaviour
  (float/int/bool/discrete/enum/PPtr), MeshRenderer, Animator, and empty/null arrays.
* `playablecurve.*`: `AnimatorControllerPlayable.IsParameterControlledByCurve` before/during play
  and after replacing `Animator.runtimeAnimatorController`.

```
python3 tests/projects/animrest/run.py record --label native1
python3 tests/projects/animrest/run.py record --label native2
python3 tests/projects/animrest/run.py publish native1 native2
UNBOUND=<unbound> python3 tests/projects/animrest/run.py gate
```

Numeric values compare within `1e-4`; strings and booleans compare exactly.
