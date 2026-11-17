# Spatial six-input sources

Unity 6000.0.71f1 on Linux, with a private eight-channel PulseAudio device, 48000 and
44100 Hz and 1024-frame DSP periods, NVIDIA GPU Editor. The `six-spatial` profile records 43
controls in one session. Thirty-one play a non-streamed six-channel clip whose inputs each carry
one impulse of 0.25 at frame `4096 + 2048*input` (as in [SIX_LAYOUTS.md](SIX_LAYOUTS.md)), so
each Master and device frame shows one input's complete output row. Each of the six output
layouts (Mono, Stereo, Quad, Surround, 5.1, 7.1) plays five poses:

| Control | Spatial blend | panStereo | Spread | Position (listener at the origin, facing +z) |
| --- | --- | --- | --- | --- |
| `a100` | 1 | 0 | 0 | (1.97, 0, -0.347) |
| `a30_spread90` | 1 | 0 | 90 | (1, 0, 1.732) |
| `a300_half` | 0.5 | -0.5 | 60 | (-1.732, 0, 1) |
| `a160_spread200` | 1 | 0 | 200 | (0.684, 0, -1.879) |
| `centre_spread120` | 1 | 0 | 120 | at the listener |

Mono output adds a half-blend control at the listener (pan -0.5). Twelve further controls play the
probe's ordinary mono and stereo impulse clips: six at the listener in Mono output (a stereo
clip at full and half blend; a mono clip at full and half blend, with and without spread 90),
and six of a mono clip with spread 90 and pan -0.5 at (2, 0, 1), in 2D and at half blend, in
Mono, Stereo and 5.1 output.

| Basis | Contract or observation |
| --- | --- |
| Documented, Unity 6000.0 | [spatialBlend](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-spatialBlend.html) mixes 2D and 3D; [spread](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-spread.html) "sets the spread angle (in degrees) of a 3d stereo or multichannel sound in speaker space". The Manual's [Audio Source](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioSource.html) page describes spread for multichannel sounds. Neither specifies per-channel directions, levels at the listener's position or how a mono clip is affected. |
| Measured, Unity 6000.0.71f1, this profile | The rules below reproduce every recorded row within 2e-7. Independent captures agree byte for byte at both rates, and both rates give the same rows. |

## Rules

- **Mixing.** Each input's speaker levels are `(1 - blend) * 2D + blend * 3D`, then scaled by the
  blended distance attenuation `(1 - blend) + blend * rolloff`, as for mono and stereo clips.
  The 2D part is the balance and layout matrix of [SIX_LAYOUTS.md](SIX_LAYOUTS.md).
- **Directions.** Each input is panned on its own direction: the source's listener-space
  direction turned by a share of half the spread. Front left and right turn by +1/2 and -1/2
  (positive towards the listener's left), centre and LFE inputs by 0, rear left and right by
  +1 and -1. A stereo clip's inputs turn by +1 and -1.
- **Speakers.** Each direction is panned as a mono clip's would be: constant power in Stereo,
  between the enclosing speakers in wider layouts, and onto the single Mono speaker at unity.
  Every input, the LFE input included, also reaches the LFE output at unity in 3D. The LFE
  input is panned by direction like the others.
- **At the listener.** A source at the listener's position (no lateral or forward component)
  reaches each of a layout's `count` speakers at `sqrt(2) / count`, the Mono speaker included
  (`sqrt(2)`). Six-input and stereo clips take that level per input; a mono clip without spread
  takes the downmix of every speaker at `sqrt(2)`: `sqrt(7)` at full blend.
- **Mono clips with spread.** A mono clip with a nonzero spread is heard as two inputs, also in
  2D: its 2D gains are half of each balance gain on the front pair (none on LFE), averaged into
  the Mono speaker at 0.707 times each, and its 3D part averages the two spread directions.

Unmeasured parts of these rules: a source straight above or below the listener (no lateral or
forward component at a distance) takes the listener-position level, but every such control is
at the listener's position itself; the mono-clip spread rule is measured in Mono, Stereo and 5.1
output only and applied in Quad, Surround, 7.1 and Prologic as well. Spatial six-input voices in
Prologic output are not measured and fail to start with the existing diagnostic. Other input
widths and streamed six-channel clips follow [INPUT_CONTEXTS.md](INPUT_CONTEXTS.md); physical
drivers and other backends are not covered here.

The engine implements these rules on this release (`Compat` AudioOutputRules.multichannel_voices
for six inputs, the spatial profile's measured path for the mono-clip and listener-position
rules; other releases keep their earlier unmeasured gains). `context_run.py --profile
six-spatial` gates every control at both rates. Record two fresh projects per rate with
`native.py --release 6000.0.71f1 --rate RATE --set six-spatial --work FRESH_CACHE_DIRECTORY`,
then publish with `publish.py --six-spatial --release 6000.0.71f1 --rate RATE --capture
FIRST/capture --repeat SECOND/capture`. The canonical reference is under
`Reference/unity-6000.0.71f1/six-spatial`.
