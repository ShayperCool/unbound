# Reset callback execution context

This independent control extends the single-reset source/listener recording with
all callback phases, channel counts and an `isMainThread` boolean. The private
project adds observers and a save hook; the original fixture and canonical data
are unchanged. No audio sample or callback record is removed. Arrays are allocated
before capture; the observers do not write files during audio callbacks.

The Unity 6000.0
[OnAudioFilterRead documentation](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnAudioFilterRead.html)
states that the callback runs on an audio thread and restricts Unity API access.
The control makes this observable by comparing the current managed thread with
the main thread recorded in Awake. During the measured reset callbacks the source
and listener both receive stereo data off the main thread. The explicit Render
controls run their callbacks on the main thread in this Editor capture context;
that measured exception does not turn the documented audio-thread rule into a
universal main-thread guarantee.

Both complete Native repeats agree on all 18438 records, including PCM and the
thread/channel rows. Each contains three closing buffers, while the earlier
single-reset reference contains one. A later basic-control recording also contains
three. This variation is consistent with restoring a device's queued output; the
queue position and timing have not been certified here. The engine processes one
closing virtual-output buffer on a reusable audio worker and its thread invariant
is tested separately. Matching all real-device queued periods remains a follow-up.
No three-buffer timing reference is published or used to set a fixed callback count.
Both raw repeats and their whole exact comparison remain in the run cache.

```sh
python3 tests/projects/mixersuspend/Diagnostics/reset-threads-v1/run.py native --release 6000.0.71f1 --work "$work/native-a"
python3 tests/projects/mixersuspend/Diagnostics/reset-threads-v1/run.py native --release 6000.0.71f1 --work "$work/native-b"
python3 tests/projects/mixersuspend/compare.py --release 6000.0.71f1 --rate 48000 --exact --reference "$work/native-a/capture" --candidate "$work/native-b/capture" --report "$work/agreement.jsonl.gz"
python3 tests/projects/mixersuspend/Diagnostics/reset-threads-v1/run.py engine --release 6000.0.71f1 --project "$work/native-a/project" --runtime "$work/runtime" --work "$work/engine"
python3 tests/projects/mixersuspend/compare.py --release 6000.0.71f1 --rate 48000 --reference "$work/native-a/capture" --candidate "$work/engine/capture" --report "$work/full-difference.jsonl.gz"
```

`engine` checks the documented off-main-thread property without claiming timing
parity. The final comparator retains the full unresolved callback/PCM count
difference and returns failure. The certified delivery gate still compares its
original single-buffer reference strictly.
