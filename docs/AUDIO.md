# Audio: AudioSource filter components

This page covers the wire and lifecycle of the per-AudioSource filter components
`AudioLowPassFilter`, `AudioHighPassFilter`, `AudioEchoFilter`, `AudioReverbFilter`,
`AudioDistortionFilter` and `AudioChorusFilter`. Reverb, Distortion and Chorus have measured
sections below. The structures are in
`crates/unbound-runtime/src/audio/protocol.rs` and `managed/UnityEngine/Audio/AudioProtocol.cs`.
The managed components are in `managed/UnityEngine/Audio/AudioFilterComponents.cs`, and the
per-source chain is in `crates/unbound-runtime/src/audio/mixer.rs`.

Status: source ready, unvalidated for LowPass and HighPass on an AudioSource.
Echo source/listener controls below measure static delay conversion and the shared kernel;
other source Echo transitions remain unmeasured. The DSP reuses the AudioMixer Lowpass, Highpass and Echo kernels in `effects.rs`.
Those kernels were measured on mixer groups by `tests/projects/mixerfilters` and
`tests/projects/mixerecho` (Unity 6000.0.71f1, Linux, 48000 and 44100 Hz). Those captures do not
establish parity for component chains.

## Wire (NativeApi 36)

`AudioSourcePacket` passes through `NativeApi.AudioUpdateSource`. Version 16 appended the filter
descriptors to it; version 18 carries them together with render protocol 8 (`DrawIndices`,
opcode 28), which was version 17 alone. No audio operation, table slot or signature changed. The
managed side refuses any other native version before it installs the table. Version 20 adds the
reverb kind (4) with its 14 parameters to the descriptor (96 bytes), and the listener's reverb
filters to `AudioFramePacket` (`filter_count` at 64, `filters` at 72). Version 21 appends the
reverb zone descriptor (`zone` at 80, 176 bytes) and sends the source's zone send gain in
`reverb_zone_mix`. Version 23 adds Distortion kind 5 in the existing cutoff field; version 24
adds Chorus kind 6, using the first seven reverb entries for dry, wet1..3, delay, rate and depth.
These two additions keep the descriptor and packet layouts unchanged. Versions 25, 26 and 27 add
measured mixer Flange, Normalize and Pitch Shifter JSON parameters without changing these layouts.
NativeApi 33 adds the source flag for component tail timing across partial device periods,
without changing the packet layouts. NativeApi 36 appends per-source listener poses: distance/pan
uses the newer source-loop listener position; panning can retain an earlier pose on the fixed loop. Doppler uses the newer position and cached relative velocity.

| `AudioSourcePacket` field | Offset | Size |
| --- | --- | --- |
| (fields up to `spread_curve`, unchanged) | 0 | 120 |
| `filter_count: i32` | 120 | 4 |
| `_pad2: i32` | 124 | 4 |
| `filters: *const AudioFilterDescriptor` | 128 | 8 |
| `listener_position: Vector3` | 136 | 12 |
| `listener_forward: Vector3` | 148 | 12 |
| `listener_up: Vector3` | 160 | 12 |
| `doppler_listener_position: Vector3` | 172 | 12 |
| total | | 184 |

| `AudioFilterDescriptor` field | Offset |
| --- | --- |
| `id: u64`, the component's instance ID | 0 |
| `kind: i32` (1 LowPass, 2 HighPass, 3 Echo, 4 Reverb, 5 Distortion, 6 Chorus) | 8 |
| `stage: i32` (0 above the OnAudioFilterRead scripts, 1 below them) | 12 |
| `cutoff`, `resonance`, `delay`, `decay`, `dry_mix`, `wet_mix: f32` | 16 to 36 |
| `reverb: [f32; 14]`, the SFX Reverb parameters in `SfxReverbSpec` order, or Chorus parameters above | 40 |
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
  - On Unity 6000.0.71f1, disabling a source or listener component freezes its history;
    re-enabling keeps its identity. Explicit Stop also freezes source histories across idle
    buffers. `Compat.AudioComponentActivationMeasured` selects these rules and the source
    packet's history flag. Other releases retain their previous disabled-unit recreation and
    idle reset behavior until measured.
  - Adding/removing a source component reconnects the chain. Surviving units restart their
    histories, while LowPass keeps its resonant state and Chorus keeps its sweep span. A new
    component starts empty. The source/listener state-swap controls in
    [the live lifecycle profile](../tests/projects/audiodistortion/LIFECYCLE.md) cover independent
    units changing enabled state in the same update.
  - Measured with AudioReverbFilter (tests/projects/audioreverb): when a source without an
    OnAudioFilterRead path ends its last voice, the chain runs on to the end of the next DSP
    buffer, then the tail is cut and the chain reset (clip end at frame 12288: last sample 13311;
    at 12800: 14335). The engine applies this to every component kind; for LowPass, HighPass and
    Echo it is unmeasured. `Compat.AudioComponentTailMeasured` selects the exact last-voice
    ending sample before rounding to its containing DSP buffer, so partial device periods
    cannot lengthen the tail. Other releases retain the previous render-chunk frontier,
    unmeasured there. Eight added reverb clip lengths pin both sides of the buffer boundaries;
    Rust controls retain the complete waveform across 100/255/257/441/1000/1300-frame device
    periods, both rates, and a one-shot outliving the main voice.
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
- An active component with a non-finite parameter logs an error and is skipped. Edits made
  while a component is disabled are deferred until it is enabled.
- On the hearing AudioListener all six component kinds use the same ordered chain on the
  measured release. Components next to listener OnAudioFilterRead scripts, or on a listener
  sharing its GameObject with a source, log an error once and are not applied on the listener.
- Live mixer slot Wet changes on the five effects below are measured; unmeasured wet layouts/buffers fail clearly. Static Wet on
  Chorus and Distortion follows the shared rule described below.

## Follow-up

The compatibility stubs are regenerated. Broad source parameter sweeps for LowPass and HighPass
still need Native captures; their 1700 Hz, Q 1 live lifecycle controls are measured in
`audiodistortion`. Echo coverage is described below.
- Run the managed `AudioFilterComponentTests` and `NativeApiVersionTests`.
- Run the Rust `audio::mixer` and `audio::protocol` tests.
- These need a fresh, matching NativeApi-18 CLI and Core. Current API-15 binaries cannot run
  this source.
- LowPass and HighPass still need source-chain captures; the listener chains and static Echo controls below are measured.

# AudioListener component chains

The additional listener and Echo cases in tests/projects/audiodistortion measure Unity
6000.0.71f1 at 48000 and 44100 Hz, stereo output, 1024-frame DSP buffers. Two
independent Editor captures agree on every metadata value and sample at each rate.
The extension preserves every pre-existing reference value and tolerance.

| Basis | Contract, observation or limit |
| --- | --- |
| Documented, Unity 6000.0 | [Use audio filters](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioEffect.html) describes enabled listener and source filters in Inspector order. [Distortion](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioDistortionFilter.html) explicitly permits the listener; the [Chorus Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioChorusFilter.html) describes the three taps and controls. Neither specifies their placement relative to mixer output or other rendering stages. |
| Documented, Unity 6000.0 | [AudioSource.bypassListenerEffects](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-bypassListenerEffects.html) excludes listener effects from that source, with no effect when routed into a mixer group. [bypassEffects](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-bypassEffects.html) describes bypassing component and global listener effects. [AudioListener](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioListener.html) allows one listener in a scene. These pages do not define the per-sample arithmetic. |
| Measured in this release/context | The listener runs after the mixer output and after source summation. Distortion paired with LowPass, HighPass, Echo, Reverb and Chorus, in both orders, follows Inspector order. The Master float sink remains upstream of listener components. The listener chain shares the source/mixer kernels, with independent histories per component. Static disabled components are omitted. Live disable/re-enable retains the unit and its frozen history, measured by the appended audiodistortion lifecycle controls. |
| Measured in this release/context | An unrouted source with `bypassListenerEffects` joins after the listener chain; another source still passes through it. `bypassEffects` bypasses only the source's components, and does **not** bypass listener components, including on an unrouted source. Mixer-routed sources pass through listener components with either flag. The unrouted `bypassEffects` result narrows the wording of its API description. |
| Documented, Unity 6000.0 | [AudioEchoFilter.delay](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioEchoFilter-delay.html) and the [Echo Filter Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioEchoFilter.html) describe a float delay, 10..5000 ms, without specifying conversion. |
| Measured in this release/context | Echo component setters/getters store unsigned whole milliseconds: 17/17.1/17.5/17.9 all read 17, 18 reads 18, 10.125 reads 10, 1.25 reads 1, -1.25 reads 4294967296 as float, and 5000.75 reads 5000. The DSP minimum is 1 ms; the shared Echo core then applies its existing eight-frame conversion. `Compat.AudioComponentEchoDelayMeasured` selects this mapping for both source and listener, including the same source mono/noise/impulse controls. Other releases retain the prior float setter and 10 ms minimum until measured. |
| Compatibility | `Compat.AudioListenerChainMeasured` selects listener support and packet routing on 6000.0.71f1. Other releases retain their previous reverb-only listener support and unmeasured bypass routing. No DSP kernel or audio-thread allocation is added. |
| Still unmeasured | Listener components interleaved with OnAudioFilterRead scripts and listeners sharing their GameObject with an AudioSource fail clearly. Live Distortion/Chorus parameter changes are measured below; source bypass transitions are measured by the scripted spatial controls; listener bypass transitions are measured by Q327. Q337 measures source/listener Chorus and Reverb in quad/5.0/5.1/7.1 output; other wide listener chains remain unmeasured. |

# 3D component placement

The `spatial` profile in tests/projects/audiodistortion has 226 cases at each of
48000 and 44100 Hz, with 32768 stereo frames of device PCM and Master floats per
case. Two independent Unity 6000.0.71f1 Linux Editor captures agree exactly.
The additional `spatial-factor0` and `spatial-factor2` profiles repeat 18 moving
source/listener controls with the project's Doppler Factor set to 0 and 2.
Earlier oracle values and comparator tolerances are retained unchanged.

All 226 cases now pass at both rates, including the original 42-control matrix
and source Distortion/Chorus placement. The components are enabled for this
measured release. The gate uses the original Master float tolerances and the
existing one-step device PCM bound; strict reports retain every PCM flip.
Decoded interpolation weights the neighboring samples separately before adding
them. This operation order also prevents a small input difference from crossing
the Chorus history's quantization boundary.

Static spatial Distortion/Chorus with source OnAudioFilterRead is measured by [the scripted spatial controls](../tests/projects/audiodistortion/SCRIPT_MEASUREMENT.md). Live bypass changes reconnect the source chain as measured by the same profile: inactive components hold their state and silence the units above them, and the first change after a clip starts playing restarts the components with the last unit left active (`Compat.AudioComponentActivationMeasured`; see that page).

Zone sends receive each voice's spatial output before its source components.
A naturally ending voice omits its entire final DSP buffer from the zone send;
other overlapping voices keep sending. Explicit Stop removes subsequent sends
while the zone return continues its prior history. Device periods that split a
DSP buffer use the same completion decision, cached once per voice and buffer.
`Compat.AudioZoneVoiceSendMeasured` selects this behavior.

Cold/warm activation, source completion and activation during existing playback pass.
The [live zone controls](../tests/projects/audioreverb/LIVE_MEASUREMENT.md) retain every
sample of enable/disable, GameObject activation, source bypass and destroy/recreate
at both rates. Disabled zones keep their return histories while parameters crossfade
over a DSP buffer. Destroying the last initialized zone releases the return. A new
return fades its send from each already playing voice over 64 frames. The frame packet
selects this initialization and transition mechanism through `Compat.AudioZoneStartupMeasured`
(NativeApi 43); other profiles keep immediate parameter application. Mixer SFX Reverb
and `AudioReverbFilter` live parameter changes use the same one-buffer crossfade
(`Compat.MixerEffectTransitionsMeasured`, tests/projects/audiodistortion live
`sfxreverb_*_set` and `reverbfilter_*_source`). All 141 original cases, including the
five formerly rejected activation controls, are required.

## Measured spatial playback

These observations apply to Unity 6000.0.71f1, Linux stereo output and 1024-frame
DSP buffers. They are not promises for other releases or output layouts.

| Basis | Contract or observation |
| --- | --- |
| Documented, Unity 6000.0 | The [Audio Source component reference](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioSource.html) describes minimum distance, linear/custom maximum distance and logarithmic rolloff continuing beyond maximum distance. Spatial Blend controls how much the 3D settings apply. It does not specify gain arithmetic or DSP-buffer interpolation. |
| Documented, Unity 6000.0 | [spatialBlend](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-spatialBlend.html) combines 2D and 3D processing with stereo panning. [spread](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-spread.html) locates channels together at zero and opposite the source direction at 360 degrees. [SetCustomCurve](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.SetCustomCurve.html) scales distance curves over maxDistance and may normalize returned keys. [AnimationCurve.postWrapMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AnimationCurve-postWrapMode.html) defines behavior after the last key; the audio controls below distinguish default and Loop wrapping. |
| Documented, Unity 6000.0 | [velocityUpdateMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-velocityUpdateMode.html) chooses fixed updates for Auto with a Rigidbody and dynamic updates otherwise. [dopplerLevel](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-dopplerLevel.html) scales the effect. The [Audio Manager](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioManager.html) documents global Doppler Factor and logarithmic Rolloff Scale; it does not define the per-buffer calculation. |
| Documented, Unity 6000.0 | [AudioListener.velocityUpdateMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioListener-velocityUpdateMode.html) chooses the velocity update loop. [Time.fixedDeltaTime](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Time-fixedDeltaTime.html) is the interval of physics and other fixed updates, relative to game time and timeScale. These pages do not specify getter rounding, packet submission timing or velocity arithmetic. |
| Measured | For lateral unit direction `p`, stereo gains are `(sqrt(2-p*p)-p)/2` and `(sqrt(2-p*p)+p)/2`. Spread rotates two input directions by opposite half angles in listener space. Stereo retains both input channels; mono averages the two directions. Controls include vertical/coincident positions and listener rotation. |
| Measured | Channel mapping blends 2D panning with the 3D matrix; attenuation blends separately as `1-blend+blend*rolloff`. Stereo pan affects the 2D contribution. Mono 2D panning uses `sqrt((1-pan)/2)` and `sqrt((1+pan)/2)`. Logarithmic rolloff continues beyond maxDistance; linear rolloff stops there. Custom curves use their own wrap modes beyond their keys. |
| Measured | Source updates evaluate the actual curve keys at distance/maxDistance. SetCustomCurve sorts keys through AnimationCurve, then scales every key time and tangent when the last key time exceeds 1: time is multiplied by the reciprocal last time and tangents divided by that scale. Short and negative ranges stay unchanged; weights and the caller's curve are preserved. GetCustomCurve returns independent keys with default wrap modes, while internal evaluation retains the supplied wraps. Setting CustomRolloff preserves rolloffMode. Multi-key CustomRolloff, SpatialBlend and Spread controls cover knots and intervals. Loop wrapping continues beyond maxDistance for all four curve kinds, including CustomRolloff; at twice maxDistance the measured 0.8-to-0.2 curves return 0.8. Default wrapping holds the endpoint. The Manual describes the usual maximum-distance limit but does not specify this non-default wrap behavior. Gain changes accumulate over 64 output samples, starting with the previous gain and reaching the exact target at sample 64. Distance, blend, direction, spread, volume and moving-listener controls use the same ramp. |
| Measured | A source's own Rigidbody supplies linearVelocity; sources without one and listeners use transform delta times reciprocal clock duration. Auto selects the fixed loop with a Rigidbody on the object or a parent; Fixed always selects it, Dynamic selects the frame loop. Parent-body sources use their transform delta, not the parent's linearVelocity. The fixed node samples before script FixedUpdate and physics and submits its packets immediately. Starting playback resets the transform sample while retaining a source body's selected-loop velocity. The public fixed-step getter round-trips through the existing clock conversion (`Compat.FixedClockTicksMeasured`), which changes the 44.1 kHz body motion. The 40 audioreverb player-loop controls cover both endpoints, own/parent bodies, transform motion and opposing fixed/dynamic body writes and off-axis listener motion at both rates. Each source caches the listener pose and relative velocity at its selected loop. Fixed sources retain the earlier listener pose for panning; distance curves, attenuation and Doppler use the newer listener position. Frame sources use the latest sampled pose throughout. Doppler projects source-minus-listener velocity along their offset before normalizing. The factor is `max(1e-6, (340-scale*radialSpeed)/340)`, then blended with 1 by spatialBlend. The audiodistortion controls additionally cover source levels 0/1/2, project factors 0/1/2 and extreme velocities. |
| Measured | Decoded playback computes a single-precision frequency, capped at 1 MHz, before converting to a source-frame step and accumulating phase in double precision. Interpolation computes `first*(1-fraction)+second*fraction`. Non-looping interpolation reads the first frame for lookahead past the last frame, then stops. Non-integer pitch, differing clip/output rates, an unrouted control and extreme velocities distinguish these operations. |
| Implementation | `Compat.AudioSpatialStereoMeasured` selects these mechanisms. Each voice chooses its clip's mono/stereo matrix, including one-shots. Curves use cached packet storage. Spatial state and voice-priority storage are reused on the audio thread; priority selection partitions rather than sorting every source. Other releases retain the previous approximate spatial path until measured. |
| Documented, Unity 6000.0 | [Pause](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.Pause.html) pauses playback; [isPlaying](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-isPlaying.html) returns false while paused. [UnPause](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource.UnPause.html) resumes the retained playback, as does Play on a paused source. The added pause controls render four paused buffers and then resume a nonzero source with UnPause or Play, recording the state change and the resumed sample sequence after the cursor has advanced. Zone activation while such voices are retained is covered by the original pause/activate PCM control. |
| Measured startup | `Compat.AudioZoneStartupMeasured` selects the initial zone state. The first Generic zone uses processing delays of 0.02/0.04 seconds and reverb level 0 until the requested values change. Later activations use their requested values. A selected zone preset supplies the sound parameters; scalar setters retain values for User without changing the selected preset's sound. Reassigning the same preset preserves those stored scalar values. The additional first-zone controls cover Generic, Cave, User and individual overrides. Mid-playback activation now preserves histories and applies the measured parameter transitions. |

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
  `c = cos(2 pi HFRef / rate)`. The pole is limited to 0.99, measured at Room HF -10000 mB and
  HF Reference 1000 Hz (tests/projects/audioreverb live `filter_room_hf_floor` and
  `zone_room_hf_floor`), and on the mixer at HF Reference 50 Hz with Room HF -1000 mB
  (tests/projects/audiodistortion live `sfxreverb_hfref50`).
- **Room LF.** `G (s^2 + sqrt2 s + 1) / (G s^2 + sqrt(2G) s + 1)`, bilinear with the prewarp at LF
  Reference.
- **Early reflections.**
  - The input tap is `max(floor(f32 ReflectDelay * rate) - 1, 0)`.
  - Gain `level(Room + Reflections) / (2 sqrt2)` precedes two shared allpasses
    (5.885 and 7.676 ms, g 0.63), then four fixed output taps per channel.
  - Reverb Delay, Density, Diffusion and Decay Time do not change them.
- **Late reverb.**
  - **Lines.** Eight lines `k` with nominal length `m_k = 61 ms * rate * f * 1.32^k`, where
    `f = 0.1 + 0.9 (Density/100)^3`. Each line is fed the input at its own tap, all in float32
    with `D = ReflectDelay + ReverbDelay`: line 0 at `max(floor(D * rate), 1)`, line `k > 0` at
    `floor((D + 18.7 ms * 1.29^(k-1)) * rate)` frames, each minus one. The common
    output delay stays after the feedback network, so changing parameters does not move
    that output history into the input history. The combined static onset is unchanged.
  - **Feedback.** Line `k` also receives `c1_k` times its successor's transformed output, delayed
    by `round(m_k) - 1`.
  - **Damping.** Each line has a one-pole with the DMO I3DL2 coefficients: `c1 = 10^(-3 m / (rate
    T))`, and `c2` from Decay HF Ratio at HF Reference. A ratio above 1 acts as 1, and `c2` is
    clamped to 0.99, also when `c1^(2 - 2/ratio)` overflows float32 (short decays with a low
    ratio: the PaddedCell preset).
  - **Butterfly.** Three stages of `[[1, tan], [-tan, 1]]`, with angle
    `pi/4 * Diffusion/100`, follow a separate `cos(angle)^3` normalization gain.
  - **Output.** Transformed line `u` reaches channel `(u-1) mod 2` after output tap
    `(u-1) mod 8`. Tap delays start at 1.5 ms times output rate, multiply by 1.47
    in float32, then round to whole frames and subtract one. Only 48000 and 44100 Hz are
    measured; other rates follow the same formula unconfirmed.
  - **Gain.** `sqrt(mean_k(1 - c1_k^2) / 8) * level(Room + Reverb)`.
- **Wet mixing.** SFX Reverb follows the [shared static Wet rule](#static-effect-slot-wet-mixing)
  through 0 dB. The measured release uses the -70 dB processed-output boundary, passes exactly
  zero wet gain through without a slot fade, and applies the slot fade for nonzero wet gain.
  The shared notes retain the earlier approximation used on other releases.
- **Group fade.** Chains with SFX Reverb after Attenuation keep the 1024-frame group input fade
  even when the effect or the group is bypassed, as for Echo (`bypass_startup_effect`,
  `bypass_startup_group`). A wet slot before Attenuation follows the shared placement rule below.

## Limits

- Startup: Native spreads reverb arrivals of signals within the first DSP block across two frames,
  which is not modelled (a residual).
- Parameter changes take effect at once, keeping the histories. Smoothing while a tail runs and
  timed snapshot transitions are unmeasured.
- Measured only for stereo output at 48000 and 44100 Hz. HF Reference was measured at 20, 50,
  1000, 4000 and 5000 Hz. The Room HF formula uses `cos` as is; only its discriminant is limited
  at zero (`sfxreverb_hfref20`).
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
- The listener-chain controls above measure unrouted `bypassListenerEffects` and
  `bypassEffects`, and mixer-routed `bypassEffects`. Still unmeasured, and failing loudly: a
  listener reverb filter next to OnAudioFilterRead scripts, one on a GameObject that also has an
  AudioSource (it runs only in the source's chain), there. Parameter
  changes while a tail runs are unmeasured too.
- Residuals: one-step device PCM flips only (filter and zone cases; the gate admits one step on
  device PCM, never on floats).
- Added zone-send controls cover mono/stereo, 2D/3D, routed/unrouted sources, multi-key
  `reverbZoneMix` curves, distances at and between normalized keys, and clip/setter order.
  All 90 cases and their property tables pass at both rates (8847759 values per rate).
  The [Reverb Zone manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioReverbZone.html)
  describes the listener's zone influence; [reverbZoneMix](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSource-reverbZoneMix.html)
  describes the source send and permits 1..1.1 for a near-field boost. Neither specifies its
  whole-mB quantization or first-activation timing. Added lifecycle controls extend this
  to cold/warm activation, Stop, natural completion and overlapping voices. The additional
  live controls cover mid-playback activation, disabling and replacement; all original
  activation PCM is required by the gate.
- Q373 adds [live scripted and overlapping zones](../tests/projects/audiopitch/ZONES.md)
  in Stereo/Quad/5.0/5.1/7.1 at 48000/44100 Hz and 1024-frame buffers. The controls
  cover radii, zone/listener movement, source send level, User scalar edits, selected-preset
  retention and overlapping-zone edits. Zone returns join after listener components,
  confirmed with Chorus and dry/bypass companions. The low-frequency input shelf retains
  its transposed state through retuning and bypass. Reference requests persist across
  return lifetimes; unchanged requests leave a new return's default frequency in place.
  These rules reuse `Compat.AudioZoneStartupMeasured` and `Compat.MixerEffectTransitionsMeasured`.
  Other releases retain the previous low-frequency arithmetic and component placement.
- Still unmeasured: listener script processing of zone returns, disable/re-enable within
  one frame, parameter changes while only a tail runs, Mono and output settings outside
  the measured profiles.

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

The 6000.0 [SetFloat API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixer.SetFloat.html)
locks the exposed parameter against snapshots once set. Its limitations prohibit calls in Awake,
OnEnable and AfterSceneLoad; these captures change parameters after Start, between rendered blocks.
[TransitionTo](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixerSnapshot.TransitionTo.html)
reaches a snapshot over the requested interval;
[TransitionToSnapshots](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixer.TransitionToSnapshots.html)
reaches a weighted mixture. Neither documents DSP interpolation steps or detector-state handling.
The 6000.0 [updateMode API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixer-updateMode.html)
specifies scaled game time for Normal and realtime independent of timeScale for UnscaledTime.
The [mixer routing manual](https://docs.unity3d.com/6000.0/Documentation/Manual/AudioMixerSpecifics.html)
guarantees mixer-output routing into another mixer's group; it does not establish cross-mixer Send
semantics. The former AudioMixerEffects and AudioMixerSnapshots manual URLs return 404; effect descriptions
and transition overrides are in AudioMixerInspectors, linked above. That page specifies linear
transitions by default and per-parameter overrides selected by the target snapshot. It gives no
weighted-array override rule, curve arithmetic, buffer timing or clock-control guarantee for
offline AudioRenderer captures.

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
  adds the sum at its position. A live level change ramps linear gain across the next DSP block:
  `previous + (target - previous) * sample / blockSamples`, beginning at the previous gain. Samples are interleaved stereo, so the right channel advances
  half a frame ahead of the left during the ramp. Exposed
  SetFloat/ClearFloat and immediate snapshots use the same ramp; creating a late source does not
  restart it.
- **Order.** Sends reach their target in the same block in every measured layout: siblings in
  both creation orders, a duck or receive in the parent. The engine processes children before
  parents and senders before the groups they send to. A send cycle (including into its own
  group) fails loudly as unmeasured.
- **Bypass.** Native ignores effect bypass and group bypass on all three units (the Editor
  disallows bypassing them).
- **Startup.** During the mixer's first DSP block nothing reaches a Receive or Duck Volume. The
  engine times this from the mixer's start. Late source playback, source creation at blocks 3/8,
  and the resulting late group registration reach their target immediately, with no new sidechain
  drop. Post-fader late senders also retain the mixer's age for the input fade
  (`Compat.MixerGraphStartupMeasured`); other releases retain the unmeasured group-age path. A group with Duck Volume has the 1024-frame group input fade, as Echo and SFX Reverb do.
  Send or Receive after Attenuation also request that fade; before Attenuation they do not. The
  placement controls cover both units and chains containing both, with the dry input and send
  output recorded. Voices separately keep their 64-frame start fade.
- **Live detector parameters.** Threshold, Ratio, Attack Time, Release Time, Make-up Gain, Knee
  and Sidechain Mix changes retain the detector envelope and eight-frame interpolation history.
  Threshold, Ratio, Attack, Release and Make-up are covered through exposed SetFloat/ClearFloat
  and zero-time snapshots; Knee and Sidechain Mix are covered through zero-time snapshots.
  These are block-boundary changes. The additional timed cases and their limits are below.

## Limits

- `ramp_ratio02`: an expanding ratio of 0.2 at up to 100x gain differs by up to 2.4e-5 relative
  (about 5e-6 of the log gain), inside the tolerance; probably Native's pow. Knee 50 and Ratio 10
  differ by under 1.4e-5.
- A Send level above 0 dB and Duck Volume values outside the Editor's ranges (through exposed
  parameters) fail loudly as unmeasured.
- Send, Receive and Duck Volume require `Compat.MixerSidechainMeasured`, exactly 6000.0.71f1.
  Sends to another mixer fail explicitly, including when one mixer outputs into another mixer.
  Q141's separate cross-mixer diagnostic cases connect a Send across the mixer boundary in each
  direction. The 6000.0.71f1 Editor reports an invalid audio handle and delivers no sidechain;
  creating all four invalid graphs together can prevent entry into Play mode. The ordinary
  mixer-output control is captured twice at both rates and passes. Cross-mixer Send processing
  order and latency therefore remain unresolved; no rule is inferred from invalid graphs.
  Q167 adds a child mixer routed directly into the target's group, with a Send from the child
  targeting that group's Receive or Duck Volume. Both still produce invalid-handle errors in
  6000.0.71f1 and terminate before delivering usable buffers. The diagnostic cases are opt-in;
  their failures do not establish any processing-order rule.
- `Compat.MixerTimedSidechainMeasured` enables linear Normal-mode timed snapshots on exactly
  6000.0.71f1. The Q141 cases cover every Duck parameter, Send Level, weighted mixtures, half-speed
  scaled time and paused scaled time. Snapshot parameters advance once per game update in their
  stored units; the existing Send gain ramp and persistent detector then process each DSP block.
  Both independent Editor captures agree bit for bit at 48000 and 44100 Hz. Exposed overrides
  retain ownership, and ClearFloat can release one into a transition. The additional
  [UnscaledTime profile](../tests/projects/mixerduck/UNSCALED_MEASUREMENT.md) certifies complete
  PCM through paused, half-speed and double-speed game clocks using relative durations.
  It also covers mode switching, nonlinear curves, weighted selection, same-target duration
  changes and clearing an applied override during a transition. The older absolute wall-time
  diagnostic remains unchanged and unclaimed.
  The Ratio 10-to-0.2 boundary capture retains the rounded GetFloat result 0.199999809 and
  continues rendering. Snapshot-owned Duck parameters therefore admit rounding within one float
  step at the larger absolute Editor range bound; the DSP receives the stored value. Script
  overrides, including pending clears, retain strict range checks. The boundary's public float
  bits are part of the reference, with an immediate exposed-parameter control alongside it.
- `Compat.MixerSnapshotCurvesMeasured` extends Normal-mode single-snapshot transitions with
  Smoothstep, Squared, SquareRoot, BrickwallStart and BrickwallEnd on Send Level, every Duck
  parameter and ordinary volumes. One scalar evaluator serves all parameters. For normalized
  elapsed time `t`, these curves use `t*t*(3-2*t)`, `t*t`, `sqrt(t)`, `t>0 ? 1 : 0`, and
  `t>=1 ? 1 : 0`. BrickwallStart does not advance while scaled time is paused. The existing
  per-block Send ramp and Duck detector histories remain in use.
  The volume controls also distinguish attenuation placement: a trailing Attenuation applies
  the submitted gain directly. Before a following unit it starts from the prior actual gain,
  adds `(target-current)/DSP-buffer-frames` after each stereo frame, and retains that accumulated float32
  gain when the next buffer has a different target. An unchanged target is held exactly at the
  next buffer boundary. Both channels use the same gain. Duck, Receive, Send, Lowpass and Echo
  controls, with effect/group bypass and an unrelated Send in another group, distinguish chain
  position from effect kind or global sidechain presence. Attenuation after Duck stays direct.
  This refines `Compat.MixerParameterGainMeasured`. Slope and buffer position live on the existing
  effect instance; the audio thread allocates no storage for the ramp.
  The same captures show that `TransitionToSnapshots` ignores overrides and uses linear
  interpolation, including a one-element array, matching and differing overrides, and weighted
  blends in both directions. `AudioMixerSnapshot.TransitionTo` applies the target override.
  Both paths share one scheduler. Other releases retain the previous, unmeasured weighted-array
  override selection; timed sidechain changes on those releases still fail clearly. The
  Attenuation override and unknown override values remain unsupported on changing sidechains.
- The probe drives the engine with the public `Time.captureDeltaTime` getter, as the Editor
  does, and compares its bits exactly on every timed row. At 48000 Hz the requested `1024/rate`
  returns `0.0213333257`, not the original float expression. Using the unquantized expression
  shifts timed gains enough to cross an Echo history quantization boundary. The getter was
  already measured and implemented (`AudioCaptureTimeTests`); this corrects the probe input and
  leaves all prior Editor samples unchanged.
- Q167 repeats the opt-in unscaled Send and Threshold captures at both output rates. Realtime
  progression produces different parameters and master buffers under the same captureDeltaTime.
  These diagnostics are not oracles. A controllable unscaled reference clock remains a follow-up;
  an endpoint-only capture would not establish the running detector's timing.
- The 6000.0 Editor exposes no Allow Wet Mixing or Wet Level control on Send or Duck Volume.
  The published [6000.0 Inspector implementation](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.0/Editor/Mono/Audio/Mixer/GUI/AudioMixerEffectView.cs)
  excludes both units. Assets enabling wet mixing on either still fail explicitly.
- The Q141 PCM check verifies each device sample against saturating truncation of the master
  float multiplied by 32768, at both rates, for both the Editor and engine captures. Q167's eleven
  volume-placement controls also record the complete listener float stream: a prior mixer's
  Lowpass or Echo tail can still reach the device alongside the current mixer's master sink.
  Those rows check quantization from the complete pre-quantization mix. Both the original master
  and device streams remain bit for bit unchanged; listener floats use the existing float
  tolerances, without device PCM slack. There are
  no conversion differences. The original 891 one-step differences per rate originate in the
  mixer floats, with no evidence of dither or a different clamp order. The timed threshold cases
  add six such samples at 48000 Hz and five at 44100 Hz. Q167 totals 918 and 913 one-step device
  differences respectively across 146 cases; every master/listener float passes the unchanged tolerance.
- Q337 measures static surround Duck linking and detector timing in the audiopitch surround profile.
  Surround timed transitions, other sample rates/buffers and live effect insertion/retargeting remain unmeasured.
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
- Compressor DSP parameters clamp to the Inspector ranges on 6000.0.71f1 through
  `Compat.MixerCompressorParametersMeasured`; exposed values stay raw. Other units still reject
  out-of-range values as unmeasured. Wet mixing on these effects remains unsupported.
- Q337 measures Compressor linking in quad/5.0/5.1/7.1; ParamEQ and simple-filter surround output remain unmeasured.
- Unmeasured: other releases, parameter changes and snapshot transitions while
  the effects run, resuming after a bypass, and sample rates other than 48000 and 44100 Hz.

# Mixer and source Distortion

`effects.rs` runs Distortion on mixer groups and `AudioDistortionFilter` on AudioSources through
the existing component chain. NativeApi 23 adds kind 5 with its level in the descriptor's existing
`Cutoff` field, without changing the layout. Stubs are regenerated. Validation is
tests/projects/audiodistortion, Unity 6000.0.71f1 Linux Editor, 48000 and 44100 Hz. Two independent
Native captures per rate agree bit for bit across 5,767,292 values; one release reference is kept.
The engine passes all 5,767,292 values at each rate under the unchanged strict float/PCM
comparison, without invoking the one-step PCM allowance or adding residual bounds.

The [Distortion Effect Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioDistortionEffect.html),
[Distortion Filter Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioDistortionFilter.html)
and [distortionLevel API](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/audiodistortionfilter/distortionlevel)
document the range 0..1 and default 0.5. The Manual allows the component on a source or listener;
the listener case is measured by the controls above. The documented component order,
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

Static effect-slot Wet is covered below. 3D sources,
surround output, bypass transitions and other sample rates remain unmeasured. Live levels are covered below.
Flange is covered by `tests/projects/audioflange`; Normalize is described below.
Pitch Shifter is described below.


# Release gates of the measured effect kernels

Each measured mechanism has one compatibility-profile flag, true on exactly 6000.0.71f1. On
other releases an active effect or component that uses a DSP flag fails clearly (bypassed ones
load); the GetFloat flag never fails (see below the table):

| Flag | Mechanism | Users |
|---|---|---|
| `Compat.ModulatedDelayMeasured` | Q14 modulated delay line (`modulated_delay.rs`) | mixer Chorus and Flange, `AudioChorusFilter` |
| `Compat.DistortionMeasured` | waveshaper | mixer Distortion, `AudioDistortionFilter` |
| `Compat.MixerNormalizeMeasured` | linked peak envelope | mixer Normalize |
| `Compat.MixerPitchShifterMeasured` | phase vocoder | mixer Pitch Shifter |
| `Compat.MixerStaticWetMeasured` | static effect-slot mix and measured silence boundary | the five effects below and SFX Reverb boundary |
| `Compat.MixerSnapshotProgressMeasured` | normalized snapshot progress accumulation | the shared snapshot scheduler |
| `Compat.MixerEffectTransitionsMeasured` | live kernel-parameter submission | the five mixer effects below |
| `Compat.MixerWetTransitionsMeasured` | shared live wet/dry gain transitions | the five mixer effects below |
| `Compat.MixerClearFloatSubmissionMeasured` | GetFloat of an override cleared after its submission to audio | `AudioMixer` |

GetFloat reads the snapshot value after ClearFloat on every release, as documented. The last
flag adds the measured exception: an override already submitted to audio still reads until the
audio update. Other releases read the snapshot there too (unmeasured).

# Mixer and source Chorus

`chorus.rs` (on the delay line of `modulated_delay.rs`, which Flange shares with one tap for
both channels) runs the same measured kernel on AudioMixer groups and AudioSource
`AudioChorusFilter` components. NativeApi 24 adds kind 6 with seven parameters in the descriptor's
existing fixed array. The probe is tests/projects/audiochorus, Unity 6000.0.71f1 Linux Editor,
48000 and 44100 Hz, stereo output with 1024-frame DSP buffers. Two independent Native captures
per rate agree bit for bit across 49,022,046 values. One canonical reference per rate is committed,
with the Editor-authored inputs needed by the engine-only gate. The engine passes all
49,022,046 strict values at each rate with zero failures, no phase exclusions and no added
residual bounds; it does not need the separate one-step PCM allowance.

The [Chorus Effect Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioChorusEffect.html),
[Chorus Filter Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioChorusFilter.html)
and [AudioChorusFilter API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioChorusFilter.html)
describe dry and three wet volumes, delay, modulation rate and depth, with successive taps offset
by 90 degrees. The full contract table, member links and callback limitations are in the probe
README. Feedback is obsolete: the API getter warns and returns zero, and its setter only warns.
The obsolete mixer's Feedback control also changes none of the captured output. Slot wet mixing
is separate from Chorus's own dry/wet controls and follows the shared static rule below.

Measured in this release/context, setters retain their arguments, while the DSP clamps dry/wet
volumes and depth to 0..1 and rate to 0..20 Hz. Negative delay becomes zero; a delay above
100 ms is ignored by the DSP and leaves its previous valid delay, initially 40 ms. In stereo, wet1 processes left,
wet2 right, and wet3 is silent. Mono clips entering the stereo mix follow that same channel rule.
The wet path quantizes its history to Q14, wraps large inputs, and interpolates at modulated read
positions. A delay span of `max(4, delay_ms * depth * sample_rate / 1000 * depth)` slews from zero
by 1/64 sample per frame. Quarter-wave lookup and floating DSP-buffer coordinates reproduce the
captured phase, startup envelope and rare interpolation jumps near zero delay, including
the zero-extended history endpoint seen in the rate20 and delay100 captures. This algorithm
is an inference constrained by those samples, with arithmetic checked against the installed
Editor's DSP, and does not establish a contract for other releases.

The 115 cases retain all startup frames, isolated controls before combinations, long ramps,
noise and tones, impulses, small and large signed DC, defaults/endpoints/intermediates, exposed
setters and DSP clamps, serialized component fields, group/slot/source/component bypass, mono
and stereo clips, parent/prefader routing and source filters on both sides of OnAudioFilterRead.
Listener Chorus and its order with Distortion are measured by the listener controls above.
3D processing, other output layouts or buffer
sizes, bypass changes and rates beyond the two captured rates remain unmeasured. Live parameters are covered below.

# Mixer Normalize

`Normalize` is implemented for the captured Unity 6000.0.71f1 Linux profile in
`tests/projects/audionormalize`: stereo output, 1024-frame DSP buffers, 48000 or
44100 Hz, static mixer slots and parameters, mono or stereo clips. NativeApi 26
adds its three parameters to the existing mixer configuration JSON. No component
or additional control is invented.

The [6000.0 Normalize Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioNormalizeEffect.html)
describes gain towards a target amplitude and lists Fade in time, Lowest volume
and Maximum amp. It specifies no envelope, channel-link or initialization rule.
The [AudioGroup Inspector](https://docs.unity3d.com/6000.0/Documentation/Manual/AudioMixerInspectors.html)
defines ordered slots, complete bypass and the optional dry path around a wet-mixed
effect. [SetFloat](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixer.SetFloat.html)
takes exposed parameters away from snapshot control and warns against calling it
in Awake, OnEnable or AfterSceneLoad. This probe sets initial values in Start.

Measured defaults and ranges agree with that Manual: Fade in time 5000 ms,
0..20000; Lowest volume 0.1, 0..1; Maximum amp 20, 0..100000. Exposed values remain
verbatim; only DSP values clamp. The captured output supports a linked absolute
peak initialized to 1, decreasing by `1 / (fade_ms * sample_rate / 1000)` per frame
or 1 when fade is zero, bounded below by Lowest volume and both channel magnitudes.
Gain is `min(Maximum amp, 1 / peak)`. Single-precision arithmetic and its ordering
matter across the long decay. These captures support this
inference; it is not a documented guarantee for every Unity release or context.

Two independent Native projects agree bit for bit across 36,831,499 values per
rate before publishing one canonical `Reference/unity-6000.0.71f1` profile. The
engine passes all 36,831,499 strict values at each rate, with zero raw float/PCM
failures and no added residuals. Cases
include silence, impulses, small/large/bipolar DC, noise, tones, ramps and amplitude
steps in either channel, defaults and controls, serialized values, setter clamps,
mono clips, bypass and Master routing. Every startup frame remains compared.
The committed authored inputs assemble without the Editor; the declared gate runs
the engine and preserves strict float/PCM failures under the existing tolerances.

Q373 adds all-speaker Normalize in Quad/5.0/5.1/7.1 at these same rates and
buffer size. One envelope decays once per frame and takes the maximum over
every active speaker, including LFE. The unused storage slot of 5.0 is excluded.
See [the wet profiles](../tests/projects/audiopitch/WET.md) for front/rear/LFE
controls. Other rates, buffers and layouts fail explicitly. Static slot Wet follows the shared
rule below. Live Normalize parameters and timed snapshots are covered below; bypass and placement changes remain unmeasured and fail clearly. Existing audio
residuals are retained unchanged.

# Mixer Pitch Shifter

`Pitch Shifter` uses the Unity 6000.0.71f1 parameterised FFT profile in
`crates/unbound-runtime/src/audio/pitch.rs`, selected by `Compat.MixerPitchShifterMeasured`.
It shares the quarter-wave cosine table (`wavetable.rs`) with Chorus and Flange, and the measured
static-wet output layout check (`MEASURED_LAYOUT` in `effects.rs`, `RequireMeasuredOutput` on the
managed side) with Normalize. Without slot wet mixing, Pitch Shifter also accepts
256/512/2048-frame DSP buffers and mono, quad, 5.0, 5.1 and 7.1 output at 48000/44100 Hz.
NativeApi 40 adds FFT size to the existing group JSON and 44 the DSP value of Max channels. It
adds no source filter component.

The documented contract, Editor parameter queries, measured state and kernel inferences are
separated in [the probe cause table](../tests/projects/audiopitch/README.md). That table links
the official Unity 6000.0 Pitch Shifter, AudioMixer, AudioGroup Inspector and exposed-parameter
API pages. FFT size 1024, Overlap 4 and Max channels 0 are the actual Editor defaults.

Both independent Native repeats agree at 48000 and 44100 Hz. The engine passes 11,403,885 strict
values per rate with no float/PCM failures. The capture
includes constant downward/upward shifts, default/unity and bypass controls, stereo impulses,
noise, DC and tone bursts, all startup/tail samples, source endings and reuse after
AudioSettings.Reset. Pitch 1 is a delayed nonidentity effect. Bypass removes that processing.

The phase vocoder supports 256/512/1024/2048/4096 points and keeps separate channel input, output, overlap and phase histories. Exposed FFT sizes round upward and clamp to those endpoints. Effective overlap stays four; alternate overlap controls retain their raw values without changing PCM in this release. Changing FFT size clears channel histories and FIFO positions while replacing the analysis window and transform tables. The additional controls and actual output settings are recorded in the probe cause table.
`Effect::reset` clears them, and chunked rendering retains them across internal packets. Its
single-precision radix-4/2 transform follows Ooura fft4g's arithmetic order and uses the measured
quarter-wave lookup for twiddles and synthesis. This describes the measured implementation;
the Manual does not guarantee it for other releases.

Max channels is a channel limit: the clamped whole value (0..16), or the output layout's channel
count when it is 0. A group carrying more channels than the limit passes its input through
unchanged; a change of the processed count restarts the histories, an unchanged count keeps them.
In mono output the effect processes the single mono channel (see Output layouts below); in quad,
5.0, 5.1 and 7.1 it processes each active channel. Stereo 2D clips feed the front pair;
3D clips and scripts can feed other speakers. The Manual documents only the range and default of Max channels; the
rest is the measured release's behavior, recorded in the probe cause table.

Static slot Wet follows the shared rule below; live Pitch and wet changes are covered below. Active unmeasured releases,
Prologic outside its measured fallback, and output rates and buffers outside the measured set fail clearly. `unbound doctor`
lists these Pitch Shifter limits in the effect-class detail. No prior DSP kernel or ClearFloat
ownership behavior changes. The optional `audiopitch/regressions.py` runs the unchanged
Distortion, Chorus, Flange, Normalize and ClearFloat probes with one private runtime snapshot.

# Output layouts

`AudioSpeakerMode` names the channel count of each layout; the
[AudioSpeakerMode](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSpeakerMode.html)
page says the device may override a request ("Always check the actual mode at runtime") and
[AudioSettings.Reset](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioSettings.Reset.html)
that it uses "the closest match that it supports". The engine mixes every layout channel: the
front pair in the stereo graph and the other channels as extra stereo pairs in layout order
(`Lanes` in `mixer.rs`). Voices, source components, scripts, group effects, sends, receives, the
listener's components and its script all carry them. Channel-local histories stay in per-pair
instances; linked detectors and measured reverb networks process the complete layout together. Measured in Unity 6000.0.71f1 by
tests/projects/audiopitch layout and device controls:

| Basis | Behavior |
| --- | --- |
| Measured mono | Each voice enters the graph already mono, before source components and group effects. The 2D part mixes the panned speaker levels down: a stereo clip's channels at 0.707 (exactly) times their pan gains, a mono clip at the length of its two pan gains (pan-independent). The 3D part puts every direction on the one speaker at unity, so spread has no effect; spatial blend mixes the two parts' speaker levels and attenuation blends apart (`context_mono_3d_*`, including half blend with pan). Both lanes carry the mono signal, so channel-symmetric effects behave as Unity's one-channel graph. Engine: `MonoGains` in `spatial.rs`. |
| Measured scripts | `OnAudioFilterRead` on a clipless source and on the listener receives the layout's channel count: 1 in mono, 6 in 5.1. A listener script's writes reach every output channel in AudioRenderer captures (`context_*_listener`, `context_*_generator`). Engine: `call_in_layout`, generators in `GeneratorState`. |
| Measured quad/5.0/5.1/7.1 | Stereo 2D clips reach FL/FR exactly as in stereo; a mono 2D clip uses the stereo pan law on FL/FR and, in 5.1 and 7.1, feeds LFE at unity gain whatever the pan. 3D directions (listener-space lateral and forward components) pan between the two neighbouring speakers with power-normalized vector-base gains; speakers sit at (∓1, 1) and (∓1, −1), the centre at (0, 1), and in 7.1 at (∓0.5, 0.866), (0, 1), (∓0.707, −0.707) and (∓1, 0). Each 3D channel also feeds LFE at unity; a source at the listener reaches every speaker at √2/n. Spread rotates each clip channel's direction by half the spread either way, and a mono clip averages its two directions. Spatial blend mixes the 2D and 3D speaker levels; attenuation blends apart. Group effects on extra pairs, generators writing every channel and clip-source scripts writing beyond the front pair match. Engine: `spatial::surround`, `SpeakerLayout`. |
| Measured devices | `driverCapabilities` follows the device's channel count: 4 report Quad, 6 report 5.1, 8 report 7.1, and 1, 2, 3, 5 and 7 report Stereo. A request above the capabilities runs at them (5.0/5.1/7.1 on four channels run quad, 7.1 on six runs 5.1); Prologic always runs as two channels; `GetConfiguration` keeps the request (`device_1`..`device_8`). Engine: `driver_capabilities` and `active_speaker_mode` in `audio/mod.rs`. Without an output device the virtual device stands for a stereo device; `UNBOUND_AUDIO_DEVICE_CHANNELS` sets its channel count (the probe uses the capture device's). Normalize, static Wet and Pitch Shifter check the layout the mixer runs. |
| Measured surround effects | Q337, tests/projects/audiopitch `surround`: Compressor/Duck link every active speaker including LFE, with Duck timing scaled by channel count; SFX Reverb shares an eight-line network; Chorus cycles three wet taps across speakers; Flange shares its sweep with separate channel histories. Source/listener Chorus/Reverb and front/rear mono 3D zone sends/returns pass at 48000/44100 Hz, 1024-frame buffers, quad/5.0/5.1/7.1. `Compat.AudioSurroundEffectsMeasured` selects the rule. See [contracts and scope](../tests/projects/audiopitch/SURROUND.md). |
| Unmeasured | Other releases retain per-pair effects and front-pair zones, with a warning for SFX Reverb. Other rates/buffers outside SETTINGS.md, Prologic outside PROLOGIC.md, surround topology changes and zone contexts outside ZONES.md remain unmeasured, as do channel-asymmetric effects in mono. The separate transitions and wet profiles below cover the measured live and wet contexts. |

AudioRenderer buffers and group sinks hold the layout's channel count in every layout.

# Static effect-slot wet mixing

Unity 6000.0.71f1 is measured at 48000 and 44100 Hz with stereo, 1024-frame
DSP buffers in `audiodistortion`, `audiochorus`, `audioflange`, `audionormalize`
and `audiopitch`. Their `slot_wet_*` controls add 23 cases per effect and rate, plus a quiet startup
control for Flange.
Two independent Editor captures agree byte for byte. Existing reference samples,
metadata values and comparator tolerances are unchanged.

| Basis | Contract, observation or implementation |
|---|---|
| Documented, Unity 6000.0 | The [AudioGroup Inspector](https://docs.unity3d.com/6000.0/Documentation/Manual/AudioMixerInspectors.html) describes a dry path around the effect and a percentage split. Wet Level can be exposed to script. [SetFloat](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Audio.AudioMixer.SetFloat.html) takes ownership from snapshots; its limitations warn against calls in Awake, OnEnable or AfterSceneLoad. Editor measurements here call it from Start before playback. The engine capture bootstrap is only a capture adapter, not an assertion about Unity callback timing. |
| Documented effect controls | The [Distortion](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioDistortionEffect.html), [Chorus](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioChorusEffect.html), [Flange](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioFlangeEffect.html), [Normalize](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioNormalizeEffect.html) and [Pitch Shifter](https://docs.unity3d.com/6000.0/Documentation/Manual/class-AudioPitchShifterEffect.html) pages describe each kernel's controls. Chorus/Flange dry and wet taps are linear controls inside the effect; they are separate from the slot's Wet level. Those pages do not specify a slot equation, dB approximation, silence boundary or startup interpolation. Pitch Shifter documents variable FFT size, overlap and channel count; the measured FFT and overlap rules are described above; channel limits remain restricted to zero. |
| Measured steady state | For all five effects, `output = w * effect(input) + max(-1, 1-w) * input`. The effect receives the full input. Wet is in dB and uses the mixer's existing gain approximation, without Volume's +20 dB ceiling. Captured levels are -80, -70, -69.9, -69, -68, -12, -6, -2.5, 0, +6, +20 and +30 dB. For example, -6 gives `w=0.50105947`, +6 gives `w=1.9959166`, and +30 gives `w=31.674294`. The dry coefficient stops at -1. At or below -70 dB the processed contribution is dropped; the dry coefficient still uses the requested gain. -80 dB gives zero wet gain. |
| Measured startup and placement | For nonzero wet gain the slot multiplies the combined signal by `min(age/64, 1)` using frame age, identically on both channels. Exactly zero wet gain passes the dry input through without that slot fade. This also holds for SFX Reverb; the kernel history is retained. A wet slot after Attenuation retains the group's existing input fade. A wet slot before Attenuation does not request that additional group fade. The added SFX Reverb placement control establishes the same rule there. The `startup`, `before`, zero-wet controls at both placements and independent dry/processed DC controls retain every startup sample. Flange also keeps the slot fade at -70 dB, whose tiny nonzero gain still attenuates the dry path. This is a static startup observation, not a measured live parameter smoothing rule. |
| Measured parameter ownership | Script-set -6/+6 dB and snapshot-stored levels give the same audio. `mixerparams` adds 35 exact fields per rate for Wet on all five effects: +6 dB remains exposed across a snapshot target; clearing restores -12 dB after the update; the base snapshot restores -6 dB. These ownership controls have no playing source and establish no live DSP transition behavior. |
| Shared implementation | One wet capability/policy column accompanies each effect kind. Managed configuration validates both native and virtual submissions. `Compat.MixerStaticWetMeasured` selects this measured release. The shared wrapper retains dry input in storage allocated during configuration, runs the existing kernel and applies the same equation/fade. It adds no audio-thread allocation, lock or version parsing. Live parameter changes use the shared transition rules below. Placement/bypass changes are rejected before graph mutation. Send/Receive/Duck routing retains its separate measured Send Level behavior and wet mixing remains rejected on those units. |
| Limits | Static Wet is accepted through +30 dB on the measured stereo/rate/buffer profile. Higher values, other rates/buffers/layouts remain unmeasured and fail clearly. Alternate Pitch Shifter FFT sizes keep the same static-wet rule on that measured output profile. Non-wet support is unchanged. SFX Reverb retains its existing 0 dB upper bound; its extra -69/-69.9 dB and zero-wet startup controls establish the same processed-output boundary and identity case. Other releases retain the earlier unmeasured SFX Reverb boundary approximation and zero-wet/prefader startup fades. |

To reproduce the five extensions, run each probe's `native.py --release 6000.0.71f1
--rate RATE --cases LIST --work FRESH_CACHE_DIRECTORY` twice, with `plain` and all
`slot_wet_*` names in LIST. Its Editor runner authors all cases even when capture is
restricted. Publish with `python3 tests/publish_audio_extension.py PROBE --release
6000.0.71f1 --rate RATE --capture FIRST/capture --repeat SECOND/capture`. Publication
checks both complete captures of the extension byte for byte, preserves every
existing value/sample, and verifies the combined reference. Record authored inputs
with `tests/authored_project.py record`. The unchanged engine-only `run.py` gates
compare every old and new case at both rates.


# Live effect parameter and wet changes

The `audiodistortion` [live profile](../tests/projects/audiodistortion/LIVE.md) measures
Distortion, Chorus, Flange, Normalize and Pitch Shifter in Unity 6000.0.71f1 Linux:
48000/44100 Hz, stereo output, 1024-frame buffers, and the default Pitch FFT profile.
It covers exposed setters, immediate and timed snapshots, source/listener component
setters, mono input, wet zero/unity/boost/boundary changes, and parameter changes
inside a wet slot. Two independent Editor captures agree per case. The profile
keeps every startup sample and the existing float/PCM comparison bounds.

Submitted kernel parameters apply to the next rendered buffer without an extra
sample ramp. Q167's shared snapshot scheduler supplies each buffer's interpolated values.
`Compat.MixerSnapshotProgressMeasured` accumulates normalized frame durations;
the live public parameter/time traces distinguish this from dividing accumulated
elapsed time, which other releases retain as an unmeasured approximation. This
refinement changes 19 existing Q167 streams at 48000 Hz by float roundoff of at
most 2.98e-8 and ten one-step PCM samples; 44100 Hz remains byte-identical. Every
existing gate still passes. The [Deferred ledger](../tests/projects/mixerduck/README.md#deferred-snapshot-progress-roundoff)
lists each file and sample without changing an oracle or comparison bound.
Chorus and Flange reuse their delay/LFO cores and retain their histories. A Chorus
Delay change alone defers its span target; changing Depth recomputes that target
from the latest valid Delay. Component recreation starts fresh history. Normalize
retains its gain follower. A Pitch change clears analysis/synthesis phase tracking
while retaining the input/output FIFO, frame position and overlap audio. Ratio submissions
apply this phase restart even while a zero-wet slot pauses audio processing. The
existing exact-release kernel flags also select these state rules.

`Compat.MixerEffectTransitionsMeasured` admits the five mixer parameter updates;
`Compat.MixerWetTransitionsMeasured` selects one shared wet/dry transition mechanism.
The latter reuses `ParameterRamp` with Q167's attenuation path: non-unity targets
accumulate a 64-frame gain ramp, repeated submissions do not restart it, and the
endpoint snaps on the next boundary. A target of exactly unity applies immediately.
At a boundary a gain delta of at most 0.00032 retains the previous gain. Exactly
zero wet gain pauses the kernel history and mixes the raw input through the
remaining gains. Consequently a return to zero wet can briefly boost raw input.
These are observations of this capture context, not a backend synchronization
guarantee in the public Unity API. The linked probe contract table records the
6000.0 documentation and its timing limits.

Static references and tolerances stay unchanged. Live mixer-slot insertion, effect identity,
placement and bypass changes remain rejected; other wet output contexts, alternate
Pitch channel limits and other releases remain explicitly unmeasured
and are listed by `unbound doctor`. Live source/listener Distortion and Chorus
reuse the existing component kernels rather than separate transition code.

Source filter membership edits, live enabled state, listener bypass, explicit Stop/Play
histories and Reverb Dry Level changes are measured by the additional
[lifecycle controls](../tests/projects/audiodistortion/LIFECYCLE.md). These controls retain
all earlier sessions and use the existing float/PCM bounds at both rates.

The resonant Lowpass/Highpass kernels use the measured per-sample parameter steps on
`Compat.MixerEffectTransitionsMeasured`: cutoff moves toward its target by current cutoff /
100 + 10 Hz and resonance by one, clamping each step to the target. This covers live setters
and the first audible buffer of an initially disabled unit. Other releases retain the earlier
unmeasured endpoint-sum ramp. The shared cores serve mixer effects and components.

# Surround wet slots and silent groups

Q373 adds 238 wet controls and 18 placement controls in
[tests/projects/audiopitch/WET.md](../tests/projects/audiopitch/WET.md),
Unity 6000.0.71f1 at 48000/44100 Hz, 1024-frame buffers, Quad/5.0/5.1/7.1.
Distortion, Chorus, Flange, Normalize, Pitch Shifter and SFX Reverb cover
static -80 through +30 dB and live wet changes. Connection deadbands sum
absolute gain steps over active speakers; unity connections pass directly.
Normalize shares one envelope over active speakers. Send advances one gain
ramp across interleaved speakers, retains accumulated endpoints and advances
through the first muted period. `Compat.AudioSurroundEffectsMeasured` selects
these rules; other releases retain per-pair Send processing and refuse the
new wide wet/Normalize contexts.

`Compat.MixerGraphStartupMeasured` activates all groups together when a mixer
first gets a routed handle. Silent groups process with the graph, so their
gains retain actual parameter changes before a source is first created.
This is measured behavior, not an API promise about graph construction.

`Compat.MixerEffectTransitionsMeasured` also selects the SFX Reverb initial
coefficient transition. The empty network starts with 40/60 ms input taps,
input pole 0.02, zero early/late gains, line pole 0.6, feedback gain 0.32 and
unit rotation gains, then approaches its requested settings over one DSP
period. Zone initialization reuses the same coefficients. Other releases
retain their prior startup application and need measurement.

Every one of 94,505,124 values per rate passes the existing bounds. Strict
reports keep all one-step device differences, with no added allowance for
Master floats. Other wet-effect kinds, topology changes, Mono, Prologic outside its measured fallback and
other rates/buffers remain outside this measurement.

### Additional measured output settings

The audiopitch [settings profile](../tests/projects/audiopitch/SETTINGS.md) covers
32000 Hz / 256 frames and 48000 Hz / 512 frames in stereo, Quad, Surround, 5.1
and 7.1, plus 96000 Hz / 2048 frames in stereo and Quad, on Unity 6000.0.71f1.
Normalize, static/live wet slots and default Pitch Shifter admission use these
exact pairs through the existing surround compatibility selector. Other pairs
remain outside that extension. The reverb early taps and allpasses scale from
single-precision seconds; their sample offsets at 48000 and 44100 Hz are
unchanged. Other releases retain the previous unmeasured delay approximation.
The wider 96000 Hz / 2048-frame callback captures remain Q373-R2.

### Prologic fallback

The [paired Prologic profile](../tests/projects/audiopitch/PROLOGIC.md) records
Unity 6000.0.71f1 Linux falling back to Stereo while retaining the requested
Prologic getter. Each of 21 controls produces bit-identical PCM in separate
Stereo and Prologic sessions at 48000 and 44100 Hz with 1024-frame buffers.
Normalize, wet slots and default Pitch Shifter admission use that measured
fallback through the existing surround Compat selector. Matrix encoding and
other Prologic rate/buffer pairs remain unmeasured.
