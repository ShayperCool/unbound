# Other input widths and streamed six-channel clips

Unity 6000.0.71f1 on Linux, with a private eight-channel PulseAudio device, 48000 and
44100 Hz and 1024-frame DSP periods, NVIDIA GPU Editor. The `input-contexts` profile records
143 controls in one session:

- Non-streamed clips of 3, 4, 5, 7 and 8 channels, created with `AudioClip.Create` and read
  back with `GetData`, whose inputs each carry one impulse of 0.25 at frame
  `4096 + 2048*input`. Each width plays in Mono, Stereo, Quad, Surround, 5.1, 7.1 and Prologic
  output at `panStereo` 0, -0.65 and 0.65 in 2D, and in every layout but Prologic at spatial
  blend 0.5, pan -0.5 and spread 90 from (1, 0, 1.732).
- The six-channel impulse clip as a streamed clip with a PCM reader (`AudioClip.Create(...,
  stream: true, ...)`) in Stereo and 5.1 and spatially in 5.1, and as imported 48000 Hz assets
  with load type Streaming (5.1 and spatially in 5.1) and Compressed In Memory with PCM
  compression (Stereo, 5.1 and spatially in 5.1).

| Basis | Contract or observation |
| --- | --- |
| Documented, Unity 6000.0 | [AudioClip.channels](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioClip-channels.html), [AudioClip.Create](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioClip.Create.html) (streamed clips read through `PCMReaderCallback`), [AudioClipLoadType](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioClipLoadType.html) and [panStereo](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-panStereo.html). None of them specifies how channels beyond stereo reach the speakers. |
| Measured, Unity 6000.0.71f1, this profile | The rules below reproduce every recorded row within 1e-7. Independent captures agree byte for byte at both rates, and both rates give the same 2D gains. |

## Rules

- **2D gains per width and layout.** Each input reaches a fixed set of output channels with a
  fixed gain per width and layout (below). Each gain is scaled by one side of the pan balance:
  the left side `min(1, 1 - pan)`, the right side `min(1, 1 + pan)`, or none. The side belongs
  to the gain, not the input: a four-channel clip's fourth input reaches LFE unscaled but the
  right rear speaker with the right side's balance in 5.1. Prologic uses the Stereo gains.
- **Odd widths.** Clips of three, five and seven channels reach no speaker in 2D, except that
  the fourth input of a five- or seven-channel clip reaches LFE at unity in 5.1 and 7.1.
- **Spatial voices.** The rules of [SIX_SPATIAL.md](SIX_SPATIAL.md) apply to every width, with
  the 2D gains above. Inputs turn by these shares of half the spread: four inputs +1, -1, 0, 0;
  eight inputs +0.333, -0.333, 0, 0, +1, -1, +0.667, -0.667; other widths evenly from +1 for the
  first input to -1 for the last.
- **Streamed six-channel clips.** A PCM-reader clip, a Streaming asset and a Compressed In
  Memory PCM asset give the same rows, at the same frames, as the non-streamed clip.

2D gains at pan 0, listed as input → output channel: gain, then the balance side (L or R)
that scales it; gains without a side are unscaled. Outputs not listed receive nothing from
that input. Three-channel clips reach no output in any layout.

| Width | Layout | Input: output gain (side) |
| --- | --- | --- |
| 4 | Mono | in0 → 0: 1 L; in2 → 0: 0.5 L |
| 4 | Stereo, Prologic | in0 → 0: 1 L; in1 → 1: 1 R; in2 → 0: 0.5 L; in3 → 1: 0.5 R |
| 4 | Quad | in0 → 0: 1 L; in1 → 1: 1 R |
| 4 | Surround | in0 → 0: 1 L, 2: 0.5; in1 → 1: 1 R, 2: 0.5; in3 → 3: 1 R |
| 4 | 5.1 | in0 → 0: 1 L, 2: 0.5; in1 → 1: 1 R, 2: 0.5; in2 → 4: 1 L; in3 → 3: 1, 5: 1 R |
| 4 | 7.1 | in0 → 0: 1 L, 2: 0.5; in1 → 1: 1 R, 2: 0.5; in2 → 4: 0.5 L, 6: 0.5 L; in3 → 3: 1, 5: 0.5 R, 7: 0.5 R |
| 5 | Mono | none |
| 5 | Stereo, Prologic | none |
| 5 | Quad | none |
| 5 | Surround | none |
| 5 | 5.1 | in3 → 3: 1 |
| 5 | 7.1 | in3 → 3: 1 |
| 7 | Mono | none |
| 7 | Stereo, Prologic | none |
| 7 | Quad | none |
| 7 | Surround | none |
| 7 | 5.1 | in3 → 3: 1 |
| 7 | 7.1 | in3 → 3: 1 |
| 8 | Mono | in0 → 0: 1 L; in1 → 0: 1 R; in2 → 0: 1; in3 → 0: 1; in4 → 0: 1 L; in5 → 0: 1 R; in6 → 0: 1 L; in7 → 0: 1 R |
| 8 | Stereo, Prologic | in0 → 0: 1 L; in1 → 1: 1 R; in2 → 0: 0.5, 1: 0.5; in3 → 0: 0.5, 1: 0.5; in4 → 0: 0.5 L; in5 → 1: 0.5 R; in6 → 0: 0.75 L; in7 → 1: 0.75 R |
| 8 | Quad | in0 → 0: 1 L; in1 → 1: 1 R; in2 → 0: 0.5, 1: 0.5; in3 → 0: 0.25, 1: 0.25 |
| 8 | Surround | in0 → 0: 1 L; in1 → 1: 1 R; in2 → 2: 1; in5 → 3: 1 R; in7 → 3: 1 R |
| 8 | 5.1 | in0 → 0: 1 L; in1 → 1: 1 R; in2 → 2: 1; in3 → 3: 1; in4 → 4: 1 L; in5 → 5: 1 R; in6 → 4: 1 L; in7 → 5: 1 R |
| 8 | 7.1 | in0 → 0: 1 L; in1 → 1: 1 R; in2 → 2: 1; in3 → 3: 1; in4 → 4: 1 L; in5 → 5: 1 R; in6 → 6: 1 L; in7 → 7: 1 R |

Six-channel gains are those of [SIX_LAYOUTS.md](SIX_LAYOUTS.md) with sides by input (0 and 4
left, 1 and 5 right). The engine's tables (`input_layout` in
`crates/unbound-runtime/src/audio/mixer.rs`) hold every measured gain and side.

## Scope and unmeasured cases

A Streaming asset's first 2D Stereo playback in one of two independent captures repeated part
of its data about 0.4 s after its start; the other capture and every later streamed control
did not. The profile therefore records Streaming assets in 5.1 only. Clips wider than eight
channels, streamed downloads (`DownloadHandlerAudioClip` with `streamAudio`) and spatial voices
in Prologic output are unmeasured and fail to start with a clear diagnostic. Physical drivers,
other backends and releases are unmeasured.

The engine implements these rules on this release through the same per-voice matrix as
six-channel voices (`Compat` AudioOutputRules.multichannel_voices); other releases keep their
earlier first-two-input path, unmeasured. `context_run.py --profile input-contexts` gates every
control at both rates: all 64,128,704 values per rate pass, with no failure in the strict
comparison either. Before this change the engine played these widths through their first two
inputs and refused the streamed six-channel clips.

Record two fresh projects per rate with `native.py --release 6000.0.71f1 --rate RATE --set
input-contexts --work FRESH_CACHE_DIRECTORY`, then publish with `publish.py --input-contexts
--release 6000.0.71f1 --rate RATE --capture FIRST/capture --repeat SECOND/capture`. The
canonical reference is under `Reference/unity-6000.0.71f1/input-contexts`.
