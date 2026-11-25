# animinternal

What internal editor animation helpers packages call answer in Unity 6000.0.71f1 Play mode
(`Assets/Scripts/AnimInternalProbe.cs`, by reflection).

- `key.*`: `AnimationUtility.SetKeyLeftTangentMode`, `SetKeyRightTangentMode` and `SetKeyBroken`
  taking a `Keyframe` by reference (the VFX Graph editor calls them as it imports) on keys with no
  mode bits, every bit set, the broken bit, and tangents and weights: each sets only its bits of
  `tangentMode` (bit 0 broken, bits 1-4 left, 5-8 right); the broken bit, the tangents and the
  weights stay; a mode value is not masked (31 on the left gives 62).
- `record.*`: `AnimationMode.StartAnimationRecording`, `StopAnimationRecording` and
  `InAnimationRecording` (the Timeline editor): a flag, each call raising its event
  (`onAnimationRecordingStart`/`Stop`) again, independent of Animation mode.
- `euler.*`: `AnimationUtility.GetClosestEuler` (the Timeline editor) for 36 rotations given as exact
  components (identity, Euler angles of edge and gimbal-lock cases, a non-unit rotation, pseudo-random
  ones, twelve whose angles lie near a thousandth's half, and middle angles of 89.8, 89.88 and 89.9
  degrees; `euler.q.*`), five hints and every rotation order; `euler.near.*`: hints 0.0004, 0.0008,
  0.0012 and 0.002 degrees from the rotation itself, in gimbal lock exactly at and 0.1 degree from it,
  and the same angles in another order. It is not the Transform's Euler rule: the rotation is not normalized, the
  angles are rounded to a thousandth of a degree before they are moved next to the hint, and a
  rotation within 0.001 degree of the hint's gives the hint itself (see
  `managed/UnityEngine/Editor/ClosestEulerAngles.cs`).

Recorded with the macOS Editor (two identical batch captures); its values equal two Linux batch
captures of the same rotations on every key both have. A zero rotation is left out: its angles are
undefined, the Linux Editor answers them as -2147483.75 degrees moved next to the hint (OrderXYZ, hint
0: `-83.75 0 -83.75`) and the macOS Editor as NaN. Unmeasured: an angle within the last bit of a
half-thousandth of a degree (about one call in a thousand on random rotations) may land on the
neighbouring thousandth.

```sh
python3 tests/projects/animinternal/run.py record --label a
python3 tests/projects/animinternal/run.py record --label b
python3 tests/projects/animinternal/run.py publish a b
python3 tests/projects/animinternal/run.py gate
```
