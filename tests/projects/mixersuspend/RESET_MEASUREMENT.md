# Controlled reset playback measurement

This definition is written before running or accepting the controlled reset profile.
The profile is selected with `--reset-playback`. It uses ResetPlaybackProbe and
ResetPlaybackRunner, separately from ResetProbe's reset/device-stop timeline.
It is a new experiment, never a filtered view of an earlier capture.

Every case starts offline capture, creates a stereo script-generated clip and plays
64 complete 1024-frame blocks. AudioRenderer remains capturing until AudioSettings.Reset
is called. The same valid configuration is requested, capture is restarted, and
1024 complete blocks are rendered after the reset. All cases capture the raw master
sink, returned device PCM and every listener callback throughout this sequence,
including callbacks inside Reset and every zero block. No sample is shifted or aligned.

Cases measure bypassed, disabled-suspension Echo and suspended Echo graphs; active
and ended voices; replay immediately or after one audio update; clip/group reassignment;
a new source using the old clip; data reload/rewrite; clip recreation; and a fresh
source with a recreated clip. The pending-sink case registers a live sentinel-filled
sink before resetting an active source and records the entire sink after Reset.

Acceptance metadata records every callback's phase, frame count and channel count,
source playing state and sample position at the specified public observation points,
clip load state and GetData success/value bits, and every Reset, Start, SetData and
LoadAudioData result. Each callback writes all floats to the listener stream.
All these playback, callback and metadata fields must agree exactly in two independent
Native runs before publication of a reference. Engine float tolerances remain ATOL
2e-5 and RTOL 2e-4; metadata is exact. An unsuccessful Native repeat gate leaves the
entire controlled profile unresolved. It must not be narrowed to an agreeing subset.

Absolute DSP timestamps are separately written for every callback and observation
point to `reset-playback-raw-clock.jsonl` in the run directory, outside `capture/`.
They are diagnostic observations and carry no timing-parity claim. They are never
used to shift audio or to remove a callback. The stopped-capture/device timing cases
belong only to the original unclaimed diagnostic experiment.

The original diagnostic sources are preserved verbatim in
`Diagnostics/reset-timeline-v1.cs.txt` and `Diagnostics/reset-timeline-v2.cs.txt`.
Their original run projects, timestamps, variable zero-block counts, all buffers and
full Native repeat/engine unions stay in cache. No non-agreeing diagnostic repeat is
published as a certified canonical reference. Stopped-capture/device timing and its
variable extra listener blocks remain unresolved.

For independent Native runs, choose fresh work directories:

```bash
python3 tests/projects/mixersuspend/native.py --reset-playback --release 6000.0.71f1 --rate 48000 --work "$work/native-a"
python3 tests/projects/mixersuspend/native.py --reset-playback --release 6000.0.71f1 --rate 48000 --work "$work/native-b"
python3 tests/projects/mixersuspend/compare.py --release 6000.0.71f1 --rate 48000 --exact \
  --reference "$work/native-a/capture" --candidate "$work/native-b/capture" --report "$work/repeat.jsonl.gz"
```

Repeat independently at 44100 Hz. A controlled reference may be placed only under
`Reference/unity-6000.0.71f1/reset-rate-<rate>/` after the full exact repeat gate passes.
