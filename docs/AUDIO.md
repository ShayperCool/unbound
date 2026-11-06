# Audio: AudioSource filter components

This page covers the wire and lifecycle of the per-AudioSource filter components
`AudioLowPassFilter`, `AudioHighPassFilter`, `AudioEchoFilter` and `AudioReverbFilter` (measured:
see "AudioReverbFilter" below). The structures are in
`crates/unbound-runtime/src/audio/protocol.rs` and `managed/UnityEngine/Audio/AudioProtocol.cs`.
The managed components are in `managed/UnityEngine/Audio/AudioFilterComponents.cs`, and the
per-source chain is in `crates/unbound-runtime/src/audio/mixer.rs`.

Status: source ready, unvalidated for LowPass, HighPass and Echo. No Unity capture measures
these three on an AudioSource. The DSP reuses the AudioMixer Lowpass, Highpass and Echo kernels in `effects.rs`.
Those kernels were measured on mixer groups by `tests/projects/mixerfilters` and
`tests/projects/mixerecho` (Unity 6000.0.71f1, Linux, 48000 and 44100 Hz). Those captures do not
establish parity for component chains.

## Wire (NativeApi 21)

`AudioSourcePacket` passes through `NativeApi.AudioUpdateSource`. Version 16 appended the filter
descriptors to it; version 18 carries them together with render protocol 8 (`DrawIndices`,
opcode 28), which was version 17 alone. No audio operation, table slot or signature changed. The
managed side refuses any other native version before it installs the table. Version 20 adds the
reverb kind (4) with its 14 parameters to the descriptor (96 bytes), and the listener's reverb
filters to `AudioFramePacket` (`filter_count` at 64, `filters` at 72). Version 21 appends the
reverb zone descriptor (`zone` at 80, 176 bytes) and sends the source's zone send gain in
`reverb_zone_mix`.

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
| `kind: i32` (1 LowPass, 2 HighPass, 3 Echo, 4 Reverb) | 8 |
| `stage: i32` (0 above the OnAudioFilterRead scripts, 1 below them) | 12 |
| `cutoff`, `resonance`, `delay`, `decay`, `dry_mix`, `wet_mix: f32` | 16 to 36 |
| `reverb: [f32; 14]`, the SFX Reverb parameters in `SfxReverbSpec` order | 40 |
| total (alignment 8) | 96 |

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
  - Measured with AudioReverbFilter (tests/projects/audioreverb): when a source without an
    OnAudioFilterRead path ends its last voice, the chain runs on to the end of the next DSP
    buffer, then the tail is cut and the chain reset (clip end at frame 12288: last sample 13311;
    at 12800: 14335). The engine applies this to every component kind; for LowPass, HighPass and
    Echo it is unmeasured. The end is taken at the 256-frame mix block in which the voice ends,
    so a device period that is not a multiple of 256 frames can lengthen the tail by up to one DSP
    buffer.
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
- On the hearing AudioListener only `AudioReverbFilter` runs. Other filter kinds there, and
  reverb filters next to listener OnAudioFilterRead scripts (an unmeasured order), log an error
  once per listener and are not applied.
- `AudioChorusFilter` and `AudioDistortionFilter` remain generated stubs.

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
    `f = 0.1 + 0.9 (Density/100)^3`. Each line is fed the input at its own tap, all in float32
    with `D = ReflectDelay + ReverbDelay`: line 0 at `max(floor(D * rate), 1)`, line `k > 0` at
    `floor((D + 18.7 ms * 1.29^(k-1)) * rate)` frames, each plus `frames(1.488 ms) - 1` (70 at 48
    kHz, 64 at 44.1 kHz). Adding the tap before truncating matters when `D * rate` is fractional:
    a reverb zone heard at weight 0.8 (691.2 frames at 48 kHz) puts lines 2 and 4 one frame later
    than truncating `D * rate` first (tests/projects/audioreverb). Every mixerreverb and reverb
    filter onset is unchanged.
  - **Feedback.** Line `k` also receives `c1_k` times its successor's transformed output, delayed
    by `round(m_k) - 1`.
  - **Damping.** Each line has a one-pole with the DMO I3DL2 coefficients: `c1 = 10^(-3 m / (rate
    T))`, and `c2` from Decay HF Ratio at HF Reference. A ratio above 1 acts as 1, and `c2` is
    clamped to 0.99, also when `c1^(2 - 2/ratio)` overflows float32 (short decays with a low
    ratio: the PaddedCell preset).
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
- Wet mixing on other effects and the other mixer effects still fail loudly or remain stubs.

# AudioReverbFilter and AudioReverbZone

`AudioReverbFilter` runs the AudioMixer SFX Reverb kernel (above) on an AudioSource's chain or on
the AudioListener's final mix, without wet mixing. `AudioReverbZone`
(`managed/UnityEngine/Audio/AudioReverbZone.cs`) blends the enabled zones at the listener into one
more instance of that kernel (`ZoneReverb` in `mixer.rs`), which every AudioSource feeds at its
`reverbZoneMix`. The probe is tests/projects/audioreverb (Unity 6000.0.71f1, Linux, 48000 and
44100 Hz, two Native repeats bit-identical). Its gate covers every case and property table.

## Unity contract (6000.0)

Documented in the Scripting API (installed 6000.0.71f1 doc XML, `UnityEngine.AudioModule.xml`) and
the Manual (*Audio Reverb Filter*, *Audio Reverb Zone*):

- The filter "takes an Audio Clip and distorts it to create a custom reverb effect". Defaults:
  Dry Level 0, Room 0, Room HF 0, Room LF 0, Decay Time 1.0, Decay HF Ratio 0.5, Reflections
  -10000, Reverb 0, Reverb Delay 0.04, HF Reference 5000, LF Reference 250, Diffusion 100,
  Density 100. Ranges: levels in mB as for the mixer effect, HF Reference 1000 to 20000 Hz.
  The `reflectionsDelay` page repeats the Reverb level text (a doc error); 0 is its default.
- `bypassListenerEffects`: "global effects on the AudioListener doesn't apply to the audio signal
  generated by the AudioSource. It also does'nt apply, if the AudioSource is playing into a mixer
  group."
- Zone: `minDistance` "will have full effect at" (default 10), `maxDistance` "will not have any
  effect" (default 15). `reverbZoneMix`: "the amount by which the signal from the AudioSource will
  be mixed into the global reverb associated with the Reverb Zones."

## Measured (filter)

- A new filter is `User` with the documented defaults (Reflections Delay 0).
- Selecting a preset writes Room, Room HF, Room LF (0), Decay Time, Decay HF Ratio, Reflections,
  Reverb, Reverb Delay, HF/LF Reference (5000/250), Diffusion and Density. It keeps Dry Level and
  Reflections Delay. The table is in `AudioFilterComponents.cs`. `Off` is Room -10000, Room HF
  -10000, so only the dry path sounds.
- Selecting `User` keeps the current values. While another preset is selected, the parameter
  setters do nothing. They store any value unclamped (1e9 reads back as 1e9).
- Serialized fields: `m_DryLevel`, `m_Room`, `m_RoomHF`, `m_DecayTime`, `m_DecayHFRatio`,
  `m_ReflectionsLevel`, `m_ReverbLevel`, `m_ReverbDelay`, `m_Diffusion`, `m_Density`,
  `m_HFReference`, `m_RoomLF`, `m_LFReference`, `m_ReflectionsDelay`, `m_ReverbPreset`. They are
  read as stored.
- Sound: the SFX Reverb with the same parameters, on the source's signal after volume and
  panning. A mono clip is processed as its stereo upmix. A disabled component and
  `bypassEffects` give the dry signal. The tail follows the chain lifetime above: it is cut one
  DSP buffer after the clip ends.
- On the AudioListener the filter processes the final mix and its tail runs on.
  `bypassListenerEffects` has no effect on a source routed into a mixer group, as documented.

## Measured (zone)

- A new zone is Generic (minDistance 10, maxDistance 15). Zone presets share the filter's levels
  and times but have their own Reflections Delay (for example Generic 0.007, Forest 0.162,
  Mountains 0.3); a preset also sets Room LF 0 and the references 5000/250 Hz and keeps the
  distances. User keeps the current values. Room, Room HF, Room LF, Reflections and Reverb are
  `int`.
- The setters apply under any preset (the preset stays) and clamp: `minDistance` at least 0,
  `maxDistance` at least `minDistance`, levels -10000 to 0 (Reflections to 1000, Reverb to 2000),
  Decay Time 0.1 to 20, Decay HF Ratio 0.1 to 2, Reflections Delay 0 to 0.3, Reverb Delay 0 to
  0.1, HF Reference 1000 to 20000, LF Reference 20 to 1000, Diffusion and Density 0 to 100.
  Serialized values are read as stored (`m_MinDistance` ... `m_RoomLF`).
- The blend at the listener (`AudioReverbZone.Blend`): a zone weighs 1 within minDistance, 0 from
  maxDistance on and 1 - (d - min)/(max - min) between; zones under 0.001 are left out. Each
  zone adds its parameters times its weight: the levels as e^(mB/500), HF/LF Reference as natural
  logarithms, everything else linearly. Below a total weight of 1 the remainder is an Off room
  (-10000 mB) and the sum is used undivided, so a partly heard zone also shortens its delays,
  decay and diffusion and lowers its references (5000 Hz at weight 0.5 becomes 70.7 Hz). Above 1
  the sum is divided by the total weight. The levels come back as whole mB, `(int)(500 ln x +
  0.5)`, so a zone heard at full weight sounds one mB higher than set (-1000 as -999). The result
  is clamped to the setter ranges; HF Reference stays at least 1000 Hz.
- Each AudioSource sends its output after its components at `reverbZoneMix` clamped to 0..1.1,
  converted to whole mB (2000 log10 up to 1, (mix - 1) * 10000 above, -10000 at 0); 0 with
  `bypassReverbZones`. The zone reverb (Dry Level -10000) is added to the final mix, before the
  listener's components. It is heard on the device output only (not in a mixer sink) and outlives
  the source.
- Each case of the probe starts with an empty zone reverb, also after a case whose tail still
  rang: the zone reverb starts empty when zones appear after none were enabled.
- The cases: the listener at 0, 11, 12.5, 14 and 20 from a Generic zone (10/15), `reverbZoneMix`
  0.5 and 0, User and Cave zones, two zones at the listener and a full zone with a half-heard
  one. All match within one device PCM step.

## Limits

- Parameter values outside the documented ranges reach the kernel clamped to the mixer ranges.
  Native's response to them is unmeasured, as is HF Reference below 1000 Hz.
- Unmeasured: an unrouted source with `bypassListenerEffects` or `bypassEffects` (it bypasses
  the listener components, as documented). Also unmeasured, and failing loudly on the listener: a
  listener reverb filter next to OnAudioFilterRead scripts, one on a GameObject that also has an
  AudioSource (it runs only in the source's chain), and other component kinds there. Parameter
  changes while a tail runs, and a mixer-routed source with `bypassEffects` (the engine still
  applies the listener components to it), are unmeasured too.
- Residuals: one-step device PCM flips only (filter and zone cases; the gate admits one step on
  device PCM, never on floats).
- Zones, unmeasured: a source routed into a mixer group (it sends like any other source),
  listener components processing the zone reverb, a zone disabled and enabled again within one
  frame (the reverb restarts), parameter changes while a tail runs, `reverbZoneMix` curves of
  more than one key (evaluated at the listener's distance over maxDistance), 3D sources, other
  sample rates and releases.

# AudioMixer: Send, Receive and Duck Volume

`crates/unbound-runtime/src/audio/duck.rs` runs the Duck Volume unit; `effects.rs`
(`Effect::process_routed`) runs Send and Receive and the sidechain buffers, and `mixer.rs`
(`processing_order`) orders the groups. The managed mixer sends `duck`, `sendTarget` and
`sendLevel` in the group configuration (JSON; NativeApi unchanged). Validation:
tests/projects/mixerduck (Unity 6000.0.71f1, Linux, 48000 and 44100 Hz).

## Unity contract (6000.0)

Documented, Manual *AudioGroup Inspector*
(https://docs.unity.com/en-us/engine/6000.0/manual/audio/mixer/inspectors) and *Introduction to
the Audio Mixer* (https://docs.unity.com/en-us/engine/6000.0/manual/audio/mixer/overview):

- A Send splits the signal and sends "a potentially attenuated copy of the signal to be used as a
  side-chain within another Effect Unit". It can go anywhere in the chain; a new Send has no
  destination and a Send Level of -80 dB. Placing it before or after the Attenuation unit is the
  pre/post-fader choice; "attenuation can be applied anywhere in the effect stack".
- A Receive mixes the signal of the Sends that target it into its group's signal; it has no
  parameters.
- Duck Volume "attenuates a signal based on audio being played somewhere else in the Audio
  Mixer"; it needs a Send to feed it. The 6000.0 Manual has no Duck Volume parameter table and no
  reference page.
- Effects apply in Inspector order. Snapshots capture Send levels.
- The Manual states nothing about group processing order for sends, latency, bypass of these
  units or their startup.

Editor definitions of 6000.0.71f1 (`MixerEffectDefinitions`): Threshold -80 to 0 dB (default -10),
Ratio 0.2 to 10 (displayed in %, default 2), Attack Time and Release Time 0 to 10 s (default 0.1),
Make-up Gain -80 to 40 dB (0), Knee 0 to 50 dB (10), Sidechain Mix 0 to 1 (1). A Send's level is
its mix level (`m_MixLevel`), and `m_SendTarget` names the Receive or Duck Volume unit.

## Measured model

Measured from the captures, not documented. The level detector and the gain curve are those of
the compressor in Unity's public NativeAudioPlugins (`Plugin_Multiband.cpp`, whose comment calls
it "duckEnvelope"); the time constants, channel link and interpolation were fitted. The engine
reproduces every float of the reference within ATOL `2e-5`/RTOL `2e-4`.

- **Detector.** Per frame `x = mix * sidechain + (1 - mix) * input` per channel. The level is
  `s = clamp(max(xL^2, xR^2), 1e-11, 100)`, so the louder channel drives both. The envelope is
  `env += (s - env) * k + 1e-16`, all in f32. `k` is the attack coefficient while `s > env` and
  the release coefficient otherwise: `k = 1 - 0.1^(1 / (T * rate))` (90 % of a step in `T`
  seconds), or 1 at `T = 0`.
- **Curve.** `t = 10 log10(env) - Threshold`, `c = 0.05 (1/Ratio - 1)`. For `|t| < Knee` the gain
  is `10^(c (t + Knee)^2 / (4 Knee))`; above that `10^(c t)`; otherwise 1. Make-up Gain scales it.
  Ratios below 1 expand.
- **Interpolation.** The gain is evaluated at the first frame of each 8-frame chunk (after that
  frame's envelope update) and approached linearly over the chunk; the unit starts from gain 0.
- **Defaults.** An unset Release Time acts as 0.3 s, not the Editor's 0.1 s (`release01` against
  `default`). The other unset parameters act as their Editor defaults.
- **Sends.** A Send adds `DspGain(level) * signal` at its chain position to its target, where
  `DspGain` is the mixer's gain approximation; -80 dB sends nothing. Several sends sum. A Receive
  adds the sum at its position.
- **Order.** Sends reach their target in the same block in every measured layout: siblings in
  both creation orders, a duck or receive in the parent. The engine processes children before
  parents and senders before the groups they send to. A send cycle (including into its own
  group) fails loudly as unmeasured.
- **Bypass.** Native ignores effect bypass and group bypass on all three units (the Editor
  disallows bypassing them).
- **Startup.** During the mixer's first DSP block nothing reaches a Receive or Duck Volume. The
  engine times this from the mixer's start, also for a sending group registered later (unmeasured). A
  group with Duck Volume has the 1024-frame group input fade (as Echo and SFX Reverb); groups with
  only Send or Receive do not. Signals there otherwise show only the voices' 64-frame start fade.

## Limits

- `ramp_ratio02`: an expanding ratio of 0.2 at up to 100x gain differs by up to 2.4e-5 relative
  (about 5e-6 of the log gain), inside the tolerance; probably Native's pow. Knee 50 and Ratio 10
  differ by under 1.4e-5.
- A Send level above 0 dB and Duck Volume values outside the Editor's ranges (through exposed
  parameters) fail loudly as unmeasured.
- Unmeasured: other releases, surround output, parameter changes and snapshot transitions while
  the detector runs, sending groups that start after their mixer, sends between mixers, a send into its own group,
  wet mixing on these units (fails loudly), and sample rates other than 48000 and 44100 Hz.
- Compressor, ParamEQ, the simple filters and Distortion are below; the other mixer effects
  still fail loudly.

# AudioMixer: Compressor, ParamEQ, Lowpass Simple and Highpass Simple

`crates/unbound-runtime/src/audio/compressor.rs` runs the Compressor; `effects.rs` runs ParamEQ
(`ParamEq`) and the two simple filters (`SimpleFilter`). The managed mixer sends `compressor` and
`eq` in the group configuration (JSON; NativeApi unchanged) and the simple filters' `cutoff`.
Validation: tests/projects/mixercompressor (Unity 6000.0.71f1, Linux, 48000 and 44100 Hz).

## Unity contract (6000.0)

Documented, Manual *Audio effects* (https://docs.unity.com/en-us/engine/6000.0/manual/audio/reference/effects):

- *Audio Compressor Effect* (`.../effects/class-audio-compressor`): "a limiter with a fixed ratio
  of ∞:1 ... the compressor clamps the signal output when it reaches the threshold". Threshold 0
  to -60 dB (default 0), Attack 10 to 200 ms (50), Release 20 to 1000 ms (50), Make up gain 0 to
  30 dB (0).
- *Audio Parametric Equalizer Effect* (`.../effects/class-audio-param-eqeffect`): Center freq 20
  to 22000 Hz (8000), Octave Range 0.2 to 5 (1), Frequency Gain 0.05 to 3 (1, no gain).
- *Audio Low Pass Simple Effect* and *Audio High Pass Simple Effect*
  (`.../effects/class-audio-low-pass-simple-effect`, `class-audio-high-pass-simple-effect`):
  Cutoff freq 10 to 22000 Hz, default 5000 for both, no resonance.
- The Manual states nothing about the detector, the filter shapes, bypass or startup.

Editor definitions of 6000.0.71f1 (`MixerEffectDefinitions`) match these ranges and defaults except
Highpass Simple's Cutoff freq default, 1000 Hz; Native also uses 1000 Hz for an unset cutoff
(`hps_default`). Unset Compressor and ParamEQ parameters act as their defaults (`comp_unset_times`,
`comp_step_thr20`, `eq_gain_only`).

## Measured model

Measured from the captures, not documented:

- **Compressor.** A linked peak limiter. Per frame the peak `p = max(p - 10/rate, max(|L|, |R|))`
  (it decays linearly by 10 per second) drives an envelope `env = env * c + p * (1 - c)` with
  `c = exp(-1000 / (T * rate))`, `T` the Attack while `p` is above the threshold and the Release
  otherwise. The gain is `min(1, threshold / env)` times Make up gain. The envelope starts at the
  threshold and the peak at zero. All of this runs in single precision, with `c` rounded from
  double precision: that rounding fixes the measured attack curve of 200 ms and the 2e-5 offset
  of the settled level.
- **ParamEQ.** A peaking biquad: `w0 = 2 pi center / rate` with the center limited to
  `rate / 2 - 100` Hz (22000 Hz becomes 21950 Hz at 44100 Hz), `alpha = sin(w0) * octaves / 2`
  and `A = gain` in the cookbook form, so the gain at the center is `gain^2` (9 at the maximum of
  3) and DC passes. The engine computes it in double precision.
- **Lowpass Simple / Highpass Simple.** One-pole stages `y += a (x - y)`: the low pass cascades two
  identical stages, the high pass is the input minus one stage. `a = w / (w + rate)` with
  `w = 2 pi cutoff` up to `cutoff = rate / pi` (where `a = 2/3`); above it `a` rises linearly to
  1 at 22000 Hz, so the low pass passes everything and the high pass is silent at the maximum.
- **Bypass.** Effect bypass and group bypass pass the signal unchanged for all four effects.
- **Startup.** A group with any of them has the 1024-frame group input fade, also when bypassed
  (`*_startup_bypass`), as Echo, SFX Reverb and Duck Volume.

## Limits

- ParamEQ: Native's single-precision arithmetic is not reproduced. A 100 Hz peak with gain 3
  changes the DC level by up to 3.4e-4 (`eq_startup`, about 2e-4 relative); see the residual
  ledger in docs/COMPATIBILITY_GAPS.md.
- Values outside the Editor's ranges (through exposed parameters) fail loudly as unmeasured, as do
  wet mixing on these effects.
- Unmeasured: other releases, surround output, parameter changes and snapshot transitions while
  the effects run, resuming after a bypass, and sample rates other than 48000 and 44100 Hz.

# Mixer and source Distortion

`effects.rs` runs Distortion on mixer groups and `AudioDistortionFilter` on AudioSources through
the existing component chain. NativeApi 23 adds kind 5 with its level in the descriptor's existing
`Cutoff` field, without changing the layout. Stubs are regenerated. Validation is
tests/projects/audiodistortion, Unity 6000.0.71f1 Linux Editor, 48000 and 44100 Hz. Two independent
Native captures per rate agree bit for bit across 5,767,292 values; one release reference is kept.

The [Distortion Effect Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioDistortionEffect.html),
[Distortion Filter Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioDistortionFilter.html)
and [distortionLevel API](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/audiodistortionfilter/distortionlevel)
document the range 0..1 and default 0.5. The Manual allows the component on a source or listener;
the listener case is unmeasured here and reports an error. The documented component order,
enabled state and OnAudioFilterRead placement apply, including the callback's audio-thread and
Web limitations. The full contract/cause table is in the probe's README.

Measured in this release/context, both forms follow the single-precision rational curve
`y = ((1+k)*x)/(1+k*abs(x))`, `k = 2*d/(1-d)`, with
`d = min(clamp(Level,0,1),0.9999)`. The Editor's parameter name is `Level`, the component's stored
field is `m_DistortionLevel`. Their setters preserve -1 and 2 but their processing clamps, and
level 1 uses the finite 0.9999 endpoint. Channels are independent and the captured response has
no history or rate dependence. These equations fit the captures; they are not an assertion about
Unity's internal implementation or other releases.

The 44 cases cover defaults/endpoints/intermediate levels, small/large/DC/impulse and bipolar
input, tone/noise, mono/stereo sources, serialized 0.37, exposed setters, component disable,
source bypassEffects/bypassListenerEffects, effect/group bypass, parent/prefader routing and a
halving callback on either side of the component. Startup uses the existing 64-frame source
input fade and 1024-frame mixer group input fade. `mix_clear` confirms the
[GetFloat contract](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/audio/audiomixer/getfloat)
that after ClearFloat the getter reports the snapshot value; the DSP still changes on its next
audio update.

Wet mixing is unmeasured and fails clearly when enabled. Listener Distortion, 3D sources,
surround output, live level/bypass transitions and other sample rates remain unmeasured.
Chorus, Flange, Normalize and Pitch Shifter remain separate work.
