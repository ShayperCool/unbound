# Other output rates and DSP buffers

The independent `settings` profile requests 32000 Hz / 256 frames, 48000 Hz /
512 frames and 96000 Hz / 2048 frames in Unity 6000.0.71f1 Linux. The first two pairs run in stereo, Quad, Surround, 5.1 and 7.1 output.
The 96000 Hz / 2048-frame pair covers stereo and Quad. Other combinations need
their own measurements.

| Basis | Contract or control |
| --- | --- |
| Documented configuration | [AudioSettings.Reset](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.Reset.html) can choose its nearest supported configuration and can stall while assets load. [AudioConfiguration](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioConfiguration.html) describes requested or current properties. Every case records actual rate, DSP buffer size, mode and listener callback width; verification rejects a substituted rate, buffer or narrower layout. |
| Documented buffer behavior | [GetDSPBufferSize](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.GetDSPBufferSize.html) describes processing in sample blocks and warns that larger blocks change command granularity. These controls create playback after the reset and four preflight buffers. They make no post-reset callback-recovery timing claim. |
| Distinguishing controls | Every configuration/layout has dry output, six mixer wet paths with live full/dry/half changes, unwrapped reverb startup, Send/Receive live levels, linked Duck and Compressor, source Chorus, listener Reverb and a fully applied zone. Distinct speaker inputs exercise the full layout. Clip-backed controls use the actual output rate to isolate DSP behavior from resampling. |
| Session boundaries | The session starts at 48000 Hz; each case requests its own recorded settings. The `rate-48000` reference path names that session header. The individual `.rate` and `.buffer` fields state the measured configuration of each control. Existing profiles and references remain separate. |

Each configuration and layout uses its own Editor process. Reusing an Editor
process while increasing callback buffer sizes produced assertions and a crash
in diagnostic captures. A repeat authors its inputs once, then `RecordPrepared`
opens that prepared scene for each remaining session. The engine uses the same
12 session boundaries. Diagnostic captures remain outside the canonical reference.

The 168 controls retain all captured samples. The comparison keeps ATOL 2e-5 /
RTOL 2e-4 and the existing one-step device PCM gate; Master floats receive no
extra allowance. Independent repeats must agree exactly before publication.

Record with `native.py --release 6000.0.71f1 --rate 48000 --set settings --work
FRESH_CACHE_DIRECTORY`. The private eight-channel PulseAudio sink is selected by
`PULSE_SERVER`, and each GPU Editor log must identify its renderer. Compare two
captures with `compare.py --profile settings --exact`, then publish one with
`publish.py --settings --repeat REPEAT`. Raw repeats and reports stay outside Git.
The engine-only replay is `context_run.py --release 6000.0.71f1 --profile settings`.

The reverb early taps start at 5 ms and grow by a 1.59 ratio. The two early
allpasses use 5.9 and 7.7 ms. Each nonzero single-precision seconds-to-samples
product is truncated and decremented once. The prior millisecond approximation
gives the same delays at 48000/44100 Hz but different delays at the added rates.
The existing transition compatibility flag selects these measured delays; other
releases retain the previous unmeasured approximation.

Q390 adds the [wide settings profile](WIDE.md), including 96000 Hz /
2048-frame Surround, 5.1 and 7.1. The private capture project starts with an
8192-frame callback capacity before its first callback. Earlier assertions and
the clip-backed width diagnostic remain recorded outside Git; they do not
supply reference PCM. The original 168-control reference stays unchanged.
The wider profile explicitly excludes two unrepeatable rate/buffer pairs and
does not certify post-reset recovery or arbitrary retained mixer histories.

Independent clean repeats agree exactly on 54,134,190 values. The engine passes
all values under the existing gate. The strict comparison retains 3,921
single-step device PCM differences; every Master float passes the original
float bounds. A previous repeat pair had one larger device-sample discrepancy
in the 32000 Hz / 256-frame 5.1 session. That entire session was recorded twice
again and matched exactly before publication; the original pair is retained as
a diagnostic. All 1,246 previously authored inputs remain byte-identical, and
336 inputs are added for this profile.
