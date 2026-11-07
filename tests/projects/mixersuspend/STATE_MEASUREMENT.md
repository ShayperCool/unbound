# Suspension state controls

The `state.py` experiment records 27 independent mixers at 48000 and 44100 Hz,
stereo, 1024-frame DSP buffers. Each mixer has 512 complete blocks of device PCM,
listener samples and master sink samples. Every GetFloat observation is a separate
one-frame float stream, using the original comparator's float tolerance; metadata
and independent Native repeats compare exactly. No blocks are shifted or removed.

Echo, Lowpass, Highpass, SFX Reverb and Duck Volume each have an enabled-suspension,
disabled-suspension and fresh-history control. The short source ends after four
blocks. Suspension threshold is 0 dB, so the silent input permits suspension after
the one-second wait even when an effect has history. A new looping source wakes the
mixer at block 256. Echo/Reverb/filter wake input is silence. Duck wake input is
quiet constant stereo, making retained detector attenuation observable. Enabled,
disabled and fresh cases retain all silent blocks and retained sink buffers.

Snapshot cases explicitly select Base before the first audio update, except
`snap_selected`, which observes saved Editor selection. Instant, four-second and
ten-second transitions, Normal with timeScale 0/0.5/1, exposed overrides, ClearFloat
at wake, and live gain/snapshot/clear controls distinguish parameter ownership from
DSP suspension. The UnscaledTime case uses an instant transition with timeScale 0.
It measures applying a snapshot under pause, not a fixed realtime duration.

The earlier complete diagnostic experiment is preserved in
`Diagnostics/state-timeline-v1/`. Its timed UnscaledTime case progresses with wall
time while captureDeltaTime fixes the game clock. Independent Native runs disagree
on that case's entire timing-dependent output. It has no canonical reference and
is not a source of accepted samples for this experiment. Its complete captures,
comparisons and original private projects remain outside Git. The current experiment
adds active parameter controls and measures instant UnscaledTime separately; timed
UnscaledTime PCM remains unclaimed. No original suspension oracle or tolerance changes.

Record twice with fresh directories before publishing:

```sh
python3 tests/projects/mixersuspend/state.py native --release 6000.0.71f1 --rate 48000 --work "$work/a"
python3 tests/projects/mixersuspend/state.py native --release 6000.0.71f1 --rate 48000 --work "$work/b"
python3 tests/projects/mixersuspend/state.py publish --release 6000.0.71f1 --rate 48000 --candidate "$work/a/capture" --repeat "$work/b/capture" --report "$work/repeat.jsonl.gz"
```

Repeat at 44100 Hz. `state.py gate` uses only the committed authored project and
canonical references, without launching the Editor.
