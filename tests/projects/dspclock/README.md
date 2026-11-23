# AudioSettings.dspTime and the DSP clock

Unity 6000.0.71f1, recorded twice with the macOS Editor in batch mode (its CoreAudio output at
48000 Hz, 1024-sample buffers); the two captures are byte-identical. The gate compares every
missing, extra or unequal row exactly.

[AudioSettings.dspTime](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings-dspTime.html)
is "the current time of the audio system ... based on the actual number of samples processed by the
audio system". The probe reads it once per frame from `Update` for 150 frames, the 60th of which
sleeps 300 ms, and keeps a playable graph on its default clock (`DirectorUpdateMode.DSPClock`, as
tests/projects/playables `core.defaults` measures) whose root playable's time it reads in
`LateUpdate`. The rows are categories, because absolute times depend on the device and on when the
audio system started (the Editor's is at about 9.6 s when Play mode starts, counted since its audio
started):

- every read is a whole number of dsp buffers (`AudioSettings.GetDSPBufferSize` over
  `outputSampleRate`);
- the clock never goes backwards and, over the frames from the 10th on, moves as real time does;
- the frame after the 300 ms one reads a clock that has counted it, and the frame before it one
  that has not; the graph's time, read after `DirectorUpdate`, has counted it in that frame too;
- the graph's time is not a whole number of buffers and moves in every frame after the one that
  made it:
  between the audio clock's buffers it moves with the frames (an exploratory capture: by about
  each frame's real time).

A second phase starts three sources at frame 160 (a 10 s 440 Hz tone at 0.5 with an
`OnAudioFilterRead` script that counts its calls, `StallFilter`), at frame 199 plays a 0.5 s clip and
schedules a 10 s one 1 s ahead, and sleeps 3 s in frame 200's `Update`. Frame 201 reads (`stall.*`):
the clock counted the stall; the filter ran once per dsp buffer of it, with the tone (0.354 per
channel at centre pan) and buffers of the configured length; the tone moved by the stall; the short
clip ended; the scheduled source started and has played from its scheduled time. The Editor mixes
all of it on its audio thread during the stall. An exploratory capture also read the listener's
`GetOutputData` as all zeros in batch mode with the tone playing; it is not part of the probe.

Exploratory macOS Editor captures outside the reference also showed the clock moving within a frame
(a 30 ms busy wait in `Update` moved it by one or two buffers) and, with Disable Unity Audio set,
`dspTime` 0 in every frame, `outputSampleRate` and the buffer size 0 and the configuration's
speaker mode Raw. Neither is part of this probe: Unbound's clock without an output device moves
once per frame (at its start, by the frame's real time in whole buffers, so that runs at a fixed
frame rate stay deterministic), and a project with audio disabled runs it as any other
(docs/AUDIO.md). Not measured: the Linux Editor (its gate machine may have no output device, which
would leave `dspTime` at 0) and other releases.

```sh
export UNBOUND_PROBE_UNITY_VERSION=6000.0.71f1
python3 tests/projects/dspclock/run.py native --label reference-a
python3 tests/projects/dspclock/run.py native --label reference-b
python3 tests/projects/dspclock/run.py publish reference-a reference-b
python3 tests/projects/dspclock/run.py check
```

Captures, engine outputs and comparison reports stay in the run cache.
