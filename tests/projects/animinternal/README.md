# animinternal

What internal editor animation helpers packages call answer in Unity 6000.0.71f1 Play mode
(`Assets/Scripts/AnimInternalProbe.cs`, by reflection; two identical Linux batch Editor captures).

- `key.*`: `AnimationUtility.SetKeyLeftTangentMode`, `SetKeyRightTangentMode` and `SetKeyBroken`
  taking a `Keyframe` by reference (the VFX Graph editor calls them as it imports) on keys with no
  mode bits, every bit set, the broken bit, and tangents and weights: each sets only its bits of
  `tangentMode` (bit 0 broken, bits 1-4 left, 5-8 right); the broken bit, the tangents and the
  weights stay; a mode value is not masked (31 on the left gives 62).
- `record.*`: `AnimationMode.StartAnimationRecording`, `StopAnimationRecording` and
  `InAnimationRecording` (the Timeline editor): a flag, each call raising its event
  (`onAnimationRecordingStart`/`Stop`) again, independent of Animation mode.

`AnimationUtility.GetClosestEuler` is measured outside Git for a follow-up: it is not the
Transform's Euler rule (it does not normalize the rotation, and its gimbal-lock answers differ).

```sh
python3 tests/projects/animinternal/run.py record --label a
python3 tests/projects/animinternal/run.py record --label b
python3 tests/projects/animinternal/run.py publish a b
python3 tests/projects/animinternal/run.py gate
```
