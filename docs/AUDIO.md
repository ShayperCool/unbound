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

## Wire (NativeApi 16)

`AudioSourcePacket` passes through `NativeApi.AudioUpdateSource`. Version 16 appends the filter
descriptors to it. No operation, table slot or signature changed. The managed side refuses any
other native version before it installs the table.

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

Unbound's choices (unmeasured):

- **Clamping.** AudioLowPassFilter.cutoffFrequency is documented to clamp to 10 to 22000 on set
  (https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AudioLowPassFilter-cutoffFrequency.html)
  and does. The other properties, and serialized values, return the value that was set; their
  pages do not say whether Unity clamps. The descriptor clamps to the
  documented ranges.
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
- These need a fresh, matching ABI-16 CLI and Core. Current API-15 binaries cannot run this
  source.
- A Native capture of the component chains is unfunded.
