# Wide output settings

The independent `wide` profile requests the full grid of 32000, 44100, 48000
and 96000 Hz; DSP buffers of 32, 64, 128, 256, 340, 480, 512, 1024, 2048,
4096 and 8192 frames; and Stereo, Quad, Surround, 5.1 and 7.1 output. Its 3080
controls are separate from the original `settings` reference.

| Basis | Contract or distinguishing control |
| --- | --- |
| Configuration | Unity 6000.0 [AudioSettings.Reset](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.Reset.html) may substitute a supported setting. Each control records the actual configuration and active callback width, rejecting substitutions. The sizes are those listed in the API example; this is a finite measured grid, not a claim about every positive integer. |
| Buffer granularity | [GetDSPBufferSize](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.GetDSPBufferSize.html) describes block processing. Live edits occur at three distinct render boundaries with audible input in each phase. Each control renders at least 32768 frames, including when the buffer does not divide that length. |
| Generator | [OnAudioFilterRead](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.OnAudioFilterRead.html) can supply a clipless source and receives interleaved channel data. The generator supplies distinct channel samples, including the last speaker in linked compression. Clip controls supply full-length noise at the actual output sample rate. |
| Effect coverage | Each configuration/layout runs dry output; live Distortion, Chorus, Flange, Normalize, Pitch Shifter and Reverb wet changes; unwrapped Reverb startup; live Send/Receive; Duck release; linked Compressor; source Chorus; listener Reverb; and a fully applied zone. Effect contracts and parameter controls also remain in [SETTINGS.md](SETTINGS.md) and [WET.md](WET.md). |
| Isolation | Each of the 220 configuration/layout cells uses its own process on both backends. Fourteen existing authored mixers are reused as aliases. `WideCases.tsv` is shared by the C# fixture and Python selectors; `wide_cases.py` regenerates it from the original fourteen setting controls. |

The private Editor project starts with both audio buffer settings at 8192 before
its first callback. Starting at 1024 and growing to 2048 previously produced
callback-buffer assertions in the wider layouts; starting at 2048 permitted
independent 96000 Hz / 2048-frame 5.0, 5.1 and 7.1 captures without assertions.
This is a capture setup observation, not a guarantee about runtime Reset recovery.
Authored project settings and the original references remain unchanged.

Record two independent projects with `native.py --release 6000.0.71f1 --rate
48000 --set wide --work FRESH_CACHE_DIRECTORY`. Verify every GPU renderer and
reject every Editor assertion. Compare with `compare.py --profile wide --exact`
before publishing with `publish.py --wide --repeat REPEAT`. Replay with
`context_run.py --release 6000.0.71f1 --profile wide`. The 48000 session header
is independent of each case's recorded output rate. Original float tolerances
and the one-step device PCM allowance remain unchanged; Master floats receive
no additional allowance. Mono and Prologic outside their existing profiles,
other rates and other buffer lengths remain outside this grid.
