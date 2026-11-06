# Audio: AudioSource filter components

This page covers the wire and lifecycle of the per-AudioSource filter components
`AudioLowPassFilter`, `AudioHighPassFilter` and `AudioEchoFilter`. The structures are in
`crates/unbound-runtime/src/audio/protocol.rs` and `managed/UnityEngine/Audio/AudioProtocol.cs`.
The managed components are in `managed/UnityEngine/Audio/AudioFilterComponents.cs`, and the
per-source chain is in `crates/unbound-runtime/src/audio/mixer.rs`.

Status: source ready, unvalidated. No Unity capture measures these components on an
AudioSource. The DSP reuses the AudioMixer Lowpass, Highpass and Echo kernels in `effects.rs`.
Those kernels were measured on mixer groups by `tests/projects/mixerfilters` and
`tests/projects/mixerecho` (Unity 6000.0.71f1, Linux, 48000 and 44100 Hz). Those captures do not
establish parity for component chains.

## Wire (NativeApi 18)

`AudioSourcePacket` passes through `NativeApi.AudioUpdateSource`. Version 16 appended the filter
descriptors to it; version 18 carries them together with render protocol 8 (`DrawIndices`,
opcode 28), which was version 17 alone. No audio operation, table slot or signature changed. The
managed side refuses any other native version before it installs the table.

| `AudioSourcePacket` field | Offset | Size |
| --- | --- | --- |
| (fields up to `spread_curve`, unchanged) | 0 | 120 |
| `filter_count: i32` | 120 | 4 |
| `_pad2: i32` | 124 | 4 |
| `filters: *const AudioFilterDescriptor` | 128 | 8 |
| total | | 136 (was 120) |

| `AudioFilterDescriptor` field | Offset |
| --- | --- |
| `id: u64`, the component's instance ID | 0 |
| `kind: i32` (1 LowPass, 2 HighPass, 3 Echo) | 8 |
| `stage: i32` (0 above the OnAudioFilterRead scripts, 1 below them) | 12 |
| `cutoff`, `resonance`, `delay`, `decay`, `dry_mix`, `wet_mix: f32` | 16 to 36 |
| total (alignment 8) | 40 |

### Validation

The mixer copies the descriptors before `audio_update_source` returns. The managed table is
pinned only for that call. It refuses all of these:

- a count outside 0 to 64 (a resource bound, not a Unity limit);
- a null or misaligned pointer when the count is not 0 (with count 0 the pointer is never read);
- an unknown kind or stage;
- a non-finite parameter;
- a duplicate identity;
- a component above the generating script of a clipless OnAudioFilterRead source;
- an Echo history beyond the 8 MiB backend bound.

A refusal is logged. The source keeps its previous chain and state, and the rest of the packet
still applies. `VirtualAudioBackend` makes the same checks for managed tests.

## Unity contract (6000.0)

Documented:

- **Order.** "The ordering of the components is important, since it reflects the order in which
  the effects will be applied to the source audio." An effect "is applied only when its component
  is enabled." Source: Manual, *Use audio filters*,
  https://docs.unity.com/en-us/engine/6000.0/manual/audio/reference/filters/class-audio-effect
- **Scripts.** An `OnAudioFilterRead` script "is inserted in the same order as the MonoBehaviour
  script is shown in the Inspector". A first-in-chain script on a clipless source plays as the
  source.
- **bypassEffects.** "Bypass effects (Applied from filter components or global listener filters)."
- **Defaults and ranges** (Scripting API and the installed 6000.0.71f1 doc XML):

  | Component | Parameter | Range | Default |
  | --- | --- | --- | --- |
  | LowPass | cutoff | 10 to 22000 Hz | 5000 |
  | LowPass | resonance Q | 1 to 10 | 1 |
  | HighPass | cutoff | 10 to 22000 Hz | 5000 |
  | HighPass | resonance Q | 1 to 10 | 1 |
  | Echo | delay | 10 to 5000 ms | 500 |
  | Echo | decay ratio | 0 to 1 | 0.5 |
  | Echo | dry mix | 0 to 1 | 1 |
  | Echo | wet mix | 0 to 1 | 1 |

  **Contradiction:** the Manual's LowPass page gives the cutoff range as 0.0 to 22000.0. The
  Scripting API, the XML and the HighPass page give 10.0 to 22000.0. The measured mixer kernel
  clamps at 10.
- The measured mixer Echo accepts 1 to 5000 ms. The component's documented range is 10 to 5000,
  and the descriptor uses that range.
- **Low-pass cutoff clamping.** AudioLowPassFilter.cutoffFrequency: "Values outside this range are
  clamped"
  (https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/audiolowpassfilter/cutofffrequency).
  The setter clamps to 10 to 22000.

Unbound's choices (unmeasured):

- **Clamping.** The other properties return the value that was set; their pages do not say
  whether Unity clamps. Serialized values, including the low-pass cutoff, are read as stored. The
  descriptor clamps to the documented ranges.
- **Signal point.** Components process the source's voices after volume, mute, panning and spatial
  gain. This is the same signal the existing OnAudioFilterRead path receives.
- **Chain processing.**
  - The chain runs only while a submitted voice is not paused, or while the script path runs.
  - Pause keeps state.
  - Virtual sources and mute feed silence through the chain.
  - Components below a clipless generator script also process its one-shots.
- **Lifetime.**
  - An unchanged identity keeps its histories. Changed parameters ramp, using the mixer kernel's
    measured slew.
  - A new identity starts empty: re-enabling, re-adding or replacing a component.
  - Stop resets the chain.
  - So does the end of a source's last voice, when it has no OnAudioFilterRead path. Tails are
    cut at that point.
  - `AudioSettings.Reset` (a new output rate) restarts every component empty at the new rate.
- **No group fade.** The mixer group's startup input fade is not applied to components.
- **Parameter timing.** Parameters reach the mixer with the source's next packet, at the latest
  the next audio update.
- **Serialized fields.** The serialized field names `m_CutoffFrequency`, `m_LowpassResonanceQ`,
  `lowpassLevelCustomCurve`, `m_HighpassResonanceQ`, `m_Delay`, `m_DecayRatio`, `m_DryMix` and
  `m_WetMix` are unverified hypotheses. Any other field is reported once per type, so a wrong
  name fails loudly.

## Fails loudly (not implemented)

- `AudioLowPassFilter.customCutoffCurve` throws `NotSupportedException`. A serialized custom
  curve logs an error, and the component runs at its fixed cutoff.
- Some chains cannot be placed: a component between two OnAudioFilterRead scripts, a component
  above a clipless generator script, or more than 64 components. Each logs an error once, and
  the source then sends no components.
- A component with a non-finite parameter logs an error and is skipped.
- Filter components on the hearing AudioListener log an error once per listener and are not
  applied.
- `AudioChorusFilter`, `AudioDistortionFilter`, `AudioReverbFilter` and `AudioReverbZone` remain
  generated stubs.

## Follow-up (unexecuted)

- Regenerate the stubs (`unbound gen-stubs`). The hand-written signatures should make the
  generator drop the three removed types.
- Bless `NativeApi.g.cs` (`UNBOUND_BLESS=1 cargo test -p unbound-runtime native_api`). Only the
  constant changes.
- Run the managed `AudioFilterComponentTests` and `NativeApiVersionTests`.
- Run the Rust `audio::mixer` and `audio::protocol` tests.
- These need a fresh, matching NativeApi-18 CLI and Core. Current API-15 binaries cannot run
  this source.
- A Native capture of the component chains is unfunded.

# AudioMixer: SFX Reverb

`crates/unbound-runtime/src/audio/reverb.rs` runs the SFX Reverb mixer effect; the managed mixer
(`managed/UnityEngine/Audio/AudioMixer.cs`) sends its parameters in the group configuration (JSON:
`sfx`, `mixLevel`, `mixAudible`; NativeApi unchanged). Validation: tests/projects/mixerreverb
(Unity 6000.0.71f1, Linux, 48000 and 44100 Hz).

## Unity contract (6000.0)

Documented, Manual *Audio SFX Reverb Effect*
(https://docs.unity.com/en-us/engine/6000.0/manual/audio/reference/effects/class-audio-reverb-effect):
the effect reverberates a group's output. Its parameters are Dry Level, Room, Room HF, Decay Time,
Decay HF Ratio, Reflections, Reflect Delay, Reverb, Reverb Delay, Diffusion, Density, HF Reference,
Room LF and LF Reference. Wet appears only when wet mixing is enabled ("Allow Wet Mixing"), in mB,
default 0. The Manual names no limitations.

Editor definitions of 6000.0.71f1 (`MixerEffectDefinitions`) differ from the Manual in two places.
They are used, and the reference measures them:

| Parameter | Range | Default |
| --- | --- | --- |
| Dry Level, Room, Room HF, Room LF | -10000 to 0 mB | 0, -10000, 0, **0** (Manual: Room LF -10000) |
| Decay Time | 0.1 to 20 s | 1 |
| Decay HF Ratio | 0.1 to 2 | 0.5 |
| Reflections | -10000 to 1000 mB | -10000 |
| Reflect Delay | **0 to 0.3 s** (Manual: -10000 to 2000) | 0.02 |
| Reverb | -10000 to 2000 mB | 0 |
| Reverb Delay | 0 to 0.1 s | 0.04 |
| Diffusion, Density | 0 to 100 % | 100 |
| HF Reference | 20 to 20000 Hz | 5000 |
| LF Reference | 20 to 1000 Hz | 250 |

## Measured model

The tables below are measured, not documented. They were identified from Native impulse responses,
and the engine reproduces every steady-state float of the reference within ATOL `2e-5`/RTOL `2e-4`.
A level `L` in mB is `10^(L/2000)`. Delays use float32 arithmetic, and the tap tables hold
milliseconds converted with `floor(ms * rate / 1000)`. The constants are in `reverb.rs`.

- **Input.** L+R (mono). Dry Level scales each original channel.
- **Room HF.** Two identical one-poles `y = (y - x) a + x`. Their combined gain at HF Reference is
  `G = level(Room HF)`, which gives `a = ((1 - Gc) - sqrt((1 - Gc)^2 - (1 - G)^2)) / (1 - G)` with
  `c = cos(2 pi HFRef / rate)`.
- **Room LF.** `G (s^2 + sqrt2 s + 1) / (G s^2 + sqrt(2G) s + 1)`, bilinear with the prewarp at LF
  Reference.
- **Early reflections.**
  - Four taps per channel at `max(floor(f32 ReflectDelay * rate) - 1, 0)` plus fixed offsets.
  - The taps pass two allpasses (5.885 and 7.676 ms, g 0.63).
  - Gain `level(Room + Reflections) / (2 sqrt2)`.
  - Reverb Delay, Density, Diffusion and Decay Time do not change them.
- **Late reverb.**
  - **Lines.** Eight lines `k` with nominal length `m_k = 61 ms * rate * f * 1.32^k`, where
    `f = 0.1 + 0.9 (Density/100)^3`. Each line is fed the input at its own tap. Line 0's tap is at
    `max(floor(f32 (ReflectDelay + ReverbDelay) * rate) - 1, 0)` plus its offset. The other lines
    take the separately rounded delays plus their offsets.
  - **Feedback.** Line `k` also receives `c1_k` times its successor's transformed output, delayed
    by `round(m_k) - 1`.
  - **Damping.** Each line has a one-pole with the DMO I3DL2 coefficients: `c1 = 10^(-3 m / (rate
    T))`, and `c2` from Decay HF Ratio at HF Reference. A ratio above 1 acts as 1, and `c2` is
    clamped to 0.99.
  - **Butterfly.** The transform has three stages of `[[cos, -sin], [sin, cos]]` with angle
    `pi/4 * Diffusion/100`.
  - **Output.** Transformed output `u` reaches channel `(u-1) mod 2` after the fan-out offset
    `(u-1) mod 8`.
  - **Gain.** `sqrt(mean_k(1 - c1_k^2) / 8) * level(Room + Reverb)`.
- **Wet mixing** (SFX Reverb only).
  - Output is `wet * effect + (1 - wet) * input`, where `wet` is the mixer's gain approximation
    (`AudioMixer.DspGain`) of the Wet level.
  - The slot output ramps in linearly over its first 64 frames.
  - At -70 dB and below, the effect output is dropped while `1 - wet` still applies. -68 dB keeps
    it; the boundary in between is unmeasured, and the engine uses -69 dB.
- **Group fade.** The group keeps its 1024-frame input fade even when the effect or the group is
  bypassed, as for Echo (`bypass_startup_effect`, `bypass_startup_group`).

## Limits

- Startup: Native spreads reverb arrivals of signals within the first DSP block across two frames,
  which is not modelled (a residual).
- Parameter changes take effect at once, keeping the histories. Smoothing while a tail runs and
  timed snapshot transitions are unmeasured.
- Measured only for stereo output at 48000 and 44100 Hz. HF Reference was measured at 1000, 4000
  and 5000 Hz. The Room HF formula clamps `cos` at 0.9999 like the DMO reference, so values below
  about 108 Hz at 48 kHz rely on an unmeasured guard.
- A Wet level above 0 dB (for example through an exposed parameter) fails loudly as unmeasured.
- Wet mixing on other effects, the other mixer effects, `AudioReverbFilter` and `AudioReverbZone`
  still fail loudly or remain stubs.
