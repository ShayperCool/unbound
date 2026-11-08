# Reset delivery control

Unity 6000.0.71f1 Linux, stereo 48000 Hz, 1024-frame DSP buffers. This is an
independent single-reset experiment, alongside the unchanged original suspension
matrix and the earlier unclaimed reset experiments.

The [Reset documentation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.Reset.html)
guarantees a configuration-change notification with `deviceWasChanged=false` and
allows the closest supported configuration. It does not specify an exact count
of audio callbacks while a device closes. The observations below are measured
behavior of this release and offline capture context.

A looping mono clip supplies 0.125 input. A script on its source and a script on
the listener record every callback into preallocated arrays. After four warmup
renders, the control records one Render, Reset, capture restart and one Render
after an audio update. No callback writes files or builds strings. All callback
buffers and their phase/length records are retained. Source PCM is already stereo
when delivered to the source script.

Three independent Editor captures agree exactly on all 10245 records. The source
and listener both receive an additional complete 1024-frame stereo buffer during
Reset, before OnAudioConfigurationChanged. It contains the old graph's current
signal, 0.0883883461356163 per channel. The first Render after restart delivers
silence to the listener and no source callback. Phases in capture.json are 0 for
the control Render, 1 inside Reset before notification, 2 after notification,
3 during restart/wait, and 4 during the first restarted Render.

The canonical reference is `Reference/unity-6000.0.71f1/delivery-rate-48000`.
An authored project captured with the same complete result supplies the gate.
The implementation processes the closing offline buffer through the shared
capture path before clearing voices and generated clip data. It uses the old
configuration and preserves the buffer-sized listener delivery. The release
selection is `Compat.AudioResetCaptureTailMeasured`. A reset rejected by resource
validation processes no buffer and preserves the capture state.

```sh
python3 tests/projects/mixersuspend/delivery.py native --release 6000.0.71f1 --rate 48000 --work "$work/native-a"
python3 tests/projects/mixersuspend/delivery.py native --release 6000.0.71f1 --rate 48000 --work "$work/native-b"
python3 tests/projects/mixersuspend/delivery.py publish --release 6000.0.71f1 --candidate "$work/native-a/capture" --repeat "$work/native-b/capture" --report "$work/agreement.jsonl.gz"
python3 tests/projects/mixersuspend/delivery.py gate --release 6000.0.71f1
```

The gate compares every sample at the existing ATOL 2e-5 and RTOL 2e-4; all metadata
is exact. Native repeats compare float bits exactly. Publishing refuses a target
that already exists. It never changes the original reference or its tolerances.

## Unclaimed observations

Two 44100 Hz runs contain the same source/listener PCM but put the first restarted
listener callback in phase 3 and phase 4 respectively. No new 44100 Hz delivery
reference is published. The original 44100 Hz suspension oracle is still compared
whole. Earlier multiple-reset diagnostics also show additional old-device buffers
varying with scheduling. Those captures remain whole outside Git. Neither is used
to establish a universal callback count or a callback/Render ordering guarantee.
The asynchronous restart phase difference is deferred; extended realtime device
handoff remains outside this control's certification.
