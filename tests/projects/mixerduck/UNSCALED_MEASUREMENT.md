# Unscaled snapshot transitions

Unity 6000.0.71f1, Linux Editor, stereo AudioRenderer, 1024-frame buffers at
48000 and 44100 Hz. The `unscaled` profile extends DuckProbe. Independent complete
Editor repeats must agree bit-for-bit before `unscaled.py publish` accepts them.
Every device, master and listener sample and every per-block parameter bit value
is compared. The existing float tolerances and one device PCM step are unchanged.

| Cause | Contract or observation | Control |
|---|---|---|
| Clock selection | [AudioMixer.updateMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixer-updateMode.html) documents scaled game time for Normal and realtime independent of timeScale for UnscaledTime. | Send, Duck Threshold and fader transitions at timeScale 0, 0.5 and 2; Normal stays frozen while paused, then switching to UnscaledTime advances the already pending transition. |
| Transition interval | [AudioMixerSnapshot.TransitionTo](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixerSnapshot.TransitionTo.html) supplies a relative interpolation interval. | Each frame supplies eight times that frame's public unscaledDeltaTime as the interval. One audio update therefore advances exactly one eighth, independent of the variable wall frame duration. The return uses one times the clock. A duration-change control changes eight to sixteen midway. |
| Re-selecting a target | Measured: selecting the same snapshot retains its original start and accumulated normalized progress, while updating its duration. Weighted mixes, including a one-element array, start a new transition. | Repeated calls across blocks 8 through 19, a separate weighted control, and a Smoothstep target curve. |
| Exposed ownership | [SetFloat](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixer.SetFloat.html) takes control away from snapshots; [ClearFloat](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixer.ClearFloat.html) restores snapshot control. | A paused unscaled transition starts at the previously submitted, divergent override. It progresses underneath that override, then ClearFloat reveals its current interpolated value. |

The engine supplies an explicit public input clock through the existing reflection
helper. Native wall frame durations are variable; the power-of-two relative
interval makes the same normalized progress repeatable without shifting PCM,
replaying an oracle clock, omitting records, or substituting scaled time.
This certifies complete PCM for changing snapshot values and ownership. It does
not claim a fixed wall-time update sequence for an offline capture. The earlier
unclaimed burst experiments remain in `Diagnostics/` with their recorded failures.

```sh
python3 tests/projects/mixerduck/unscaled.py native --release 6000.0.71f1 --rate 48000 --work "$work/native-a"
python3 tests/projects/mixerduck/unscaled.py native --release 6000.0.71f1 --rate 48000 --work "$work/native-b"
python3 tests/projects/mixerduck/unscaled.py gate --release 6000.0.71f1 --work "$work/gate"
```

The gate assembles the committed `unscaled` authored set and runs both rates without
an Editor. Canonical captures are under `Reference/unity-6000.0.71f1/unscaled-rate-*`.
