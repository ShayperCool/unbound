//! The structures the managed engine's audio code hands the mixer; see docs/AUDIO.md. The layouts
//! are shared with `managed/UnityEngine/Audio/AudioProtocol.cs`.
use crate::render_protocol::Vector3;

/// What an imported clip is (`AudioClip.samples`, `channels`, `frequency` and import settings).
#[repr(C)]
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct AudioClipInfo {
    pub frames: i32,
    pub channels: i32,
    pub frequency: i32,
    /// `AudioClipLoadType`.
    pub load_type: i32,
    pub preload: i32,
    pub load_in_background: i32,
    pub ambisonic: i32,
    /// `AudioCompressionFormat`.
    pub compression_format: i32,
}

/// `AudioSourcePacket.flags`.
pub mod source_flags {
    pub const LOOP: u32 = 1;
    pub const MUTE: u32 = 1 << 1;
    pub const IGNORE_LISTENER_VOLUME: u32 = 1 << 2;
    pub const IGNORE_LISTENER_PAUSE: u32 = 1 << 3;
    pub const BYPASS_EFFECTS: u32 = 1 << 4;
    pub const BYPASS_LISTENER_EFFECTS: u32 = 1 << 5;
    pub const BYPASS_REVERB_ZONES: u32 = 1 << 6;
    /// A script on the source's GameObject has `OnAudioFilterRead`.
    pub const FILTER: u32 = 1 << 7;
    pub const SPATIALIZE: u32 = 1 << 8;
    /// Keep the last output for `GetOutputData`/`GetSpectrumData`.
    pub const KEEP_OUTPUT: u32 = 1 << 9;
    /// Measured decoded resampling precision and non-looping endpoint handling.
    pub const DECODED_RESAMPLING: u32 = 1 << 10;
    /// Component tail cutoff follows the last voice's sample across device periods.
    pub const COMPONENT_FILTER_TAIL: u32 = 1 << 11;
    /// Measured mono/stereo spatial mapping, gain transitions and decoded playback.
    pub const SPATIAL_STEREO: u32 = 1 << 12;
    /// Per-voice zone sends precede components and omit the completion buffer.
    pub const ZONE_VOICE_SEND: u32 = 1 << 13;
    /// Listener poses and relative velocity cached at the source's selected sample phase.
    pub const LISTENER_SNAPSHOT: u32 = 1 << 14;
    /// A live bypass deactivated the filter components (with [`BYPASS_EFFECTS`]): they hold
    /// their state and silence every unit above them; scripts below them still run.
    pub const COMPONENTS_INACTIVE: u32 = 1 << 15;
    /// With [`COMPONENTS_INACTIVE`]: the last unit in Inspector order stays active after a
    /// chain reconnection and processes the silence above it.
    pub const LAST_COMPONENT_ACTIVE: u32 = 1 << 16;
    /// Component parameter changes use the measured effect transitions.
    pub const EFFECT_TRANSITIONS: u32 = 1 << 17;
}

/// An AudioSource's parameters, sent when they change and once per frame (its transform).
#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct AudioSourcePacket {
    /// The clip `Play` starts (0: none).
    pub clip: u64,
    /// The AudioMixerGroup it outputs to (0: straight to the listener).
    pub group: u64,
    pub position: Vector3,
    pub velocity: Vector3,
    pub volume: f32,
    pub pitch: f32,
    pub pan_stereo: f32,
    pub spatial_blend: f32,
    /// Degrees.
    pub spread: f32,
    pub doppler_level: f32,
    pub min_distance: f32,
    pub max_distance: f32,
    /// The linear gain of the source's send into the reverb zones (NativeApi 21): its
    /// `reverbZoneMix` as the level Unity applies, 0 with `bypassReverbZones`.
    pub reverb_zone_mix: f32,
    /// `AudioRolloffMode`: 0 logarithmic, 1 linear, 2 custom.
    pub rolloff_mode: i32,
    pub priority: i32,
    pub flags: u32,
    /// Samples of each curve below over [0, maxDistance]; 0 when none is set.
    pub curve_length: i32,
    pub _pad: i32,
    /// Custom rolloff (gain), used with `rolloff_mode` 2; nullable.
    pub rolloff_curve: *const f32,
    /// Spatial blend by distance; nullable (then `spatial_blend`).
    pub spatial_blend_curve: *const f32,
    /// Spread by distance, normalized (1 = 360 degrees); nullable (then `spread`).
    pub spread_curve: *const f32,
    /// Enabled filter components of the source's GameObject (NativeApi 16); 0 when none.
    pub filter_count: i32,
    pub _pad2: i32,
    /// `filter_count` [`AudioFilterDescriptor`]s in component order; null when the count is 0.
    /// Valid during the call only: the mixer copies them before it returns.
    pub filters: *const AudioFilterDescriptor,
    /// Source-loop listener samples (NativeApi 36); used with `LISTENER_SNAPSHOT`.
    pub listener_position: Vector3,
    pub listener_forward: Vector3,
    pub listener_up: Vector3,
    pub doppler_listener_position: Vector3,
}

/// `AudioFilterDescriptor.kind`: the filter components the mixer runs on a source.
pub mod filter_kind {
    /// `AudioLowPassFilter`.
    pub const LOWPASS: i32 = 1;
    /// `AudioHighPassFilter`.
    pub const HIGHPASS: i32 = 2;
    /// `AudioEchoFilter`.
    pub const ECHO: i32 = 3;
    /// `AudioReverbFilter` (NativeApi 20): [`AudioFilterDescriptor::reverb`].
    pub const REVERB: i32 = 4;
    /// `AudioDistortionFilter` (NativeApi 23): Level in `cutoff`; layout unchanged.
    pub const DISTORTION: i32 = 5;
    /// Chorus: first seven `reverb` entries are dry, wet1..3, delay, rate and depth (NativeApi 24).
    pub const CHORUS: i32 = 6;
    /// The highest kind: both backends accept exactly `LOWPASS..=LAST`. The managed
    /// `AudioFilterKind.Last` and `Supported` mirror this (`managed_filter_kinds_match`).
    pub const LAST: i32 = CHORUS;

    pub fn supported(kind: i32) -> bool {
        (LOWPASS..=LAST).contains(&kind)
    }
}

/// `AudioFilterDescriptor.stage`: where a component sits relative to the scripts with
/// `OnAudioFilterRead` on the same GameObject (the Inspector order is the processing order).
pub mod filter_stage {
    /// Above every such script, or on a source without one.
    pub const BEFORE_SCRIPTS: i32 = 0;
    /// Below every such script.
    pub const AFTER_SCRIPTS: i32 = 1;
}

/// The most filter components one source may send (a resource bound, not a Unity limit).
pub const MAX_SOURCE_FILTERS: i32 = 64;

/// One enabled filter component of an AudioSource's GameObject.
#[repr(C)]
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct AudioFilterDescriptor {
    /// The component's identity (its instance ID): state is kept while it stays the same.
    pub id: u64,
    /// [`filter_kind`].
    pub kind: i32,
    /// [`filter_stage`].
    pub stage: i32,
    /// Lowpass/Highpass: hertz.
    pub cutoff: f32,
    /// Lowpass/Highpass resonance Q.
    pub resonance: f32,
    /// Echo: milliseconds.
    pub delay: f32,
    /// Echo decay ratio.
    pub decay: f32,
    pub dry_mix: f32,
    pub wet_mix: f32,
    /// Reverb: the AudioMixer SFX Reverb parameters in the order of
    /// [`crate::audio::reverb::SfxReverbSpec`]'s fields (mB, seconds, percent, Hz).
    pub reverb: [f32; 14],
}

/// `AudioSourceCommand.kind`.
pub mod command {
    /// Start the main voice at dsp time `time` (seconds; 0 or past: now); resumes a paused one.
    pub const PLAY: i32 = 1;
    /// Stop the main voice and the one-shots.
    pub const STOP: i32 = 2;
    pub const PAUSE: i32 = 3;
    pub const UNPAUSE: i32 = 4;
    /// Play `clip` once on top, scaled by `volume_scale`.
    pub const PLAY_ONE_SHOT: i32 = 5;
    /// Seek the main voice (or set where the next `Play` starts) to `time` clip frames.
    pub const SEEK: i32 = 6;
    pub const SET_SCHEDULED_START: i32 = 7;
    pub const SET_SCHEDULED_END: i32 = 8;
    /// Stop the main voice only (the clip changed).
    pub const STOP_MAIN: i32 = 9;
    /// The source's filter chain reconnects: every component restarts empty.
    pub const RESET_COMPONENTS: i32 = 10;
}

#[repr(C)]
#[derive(Debug, Clone, Copy, Default)]
pub struct AudioSourceCommand {
    pub kind: i32,
    pub _pad: i32,
    pub time: f64,
    pub clip: u64,
    pub volume_scale: f32,
    pub _pad2: i32,
}

/// `AudioSourceState.main`.
pub mod voice_state {
    pub const NONE: i32 = 0;
    pub const SCHEDULED: i32 = 1;
    pub const PLAYING: i32 = 2;
    pub const PAUSED: i32 = 3;
}

/// Where a source's playback is.
#[repr(C)]
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct AudioSourceState {
    /// [`voice_state`] of the main voice.
    pub main: i32,
    /// One-shots still playing.
    pub one_shots: i32,
    /// Playing but not heard: beyond the real voice count.
    pub is_virtual: i32,
    pub _pad: i32,
    /// The main voice's position in clip frames (or where the next `Play` starts).
    pub position: f64,
}

/// `AudioFramePacket.flags`.
pub mod frame_flags {
    pub const HAS_LISTENER: i32 = 1;
    pub const PAUSED: i32 = 1 << 1;
    /// A script on the listener's GameObject has `OnAudioFilterRead`.
    pub const LISTENER_FILTER: i32 = 1 << 2;
    /// Compatibility-selected zone initialization and parameter transitions.
    pub const ZONE_STARTUP: i32 = 1 << 3;
    /// Listener component parameter changes use the measured effect transitions.
    pub const EFFECT_TRANSITIONS: i32 = 1 << 4;
}

/// Sent once per frame from `PostLateUpdate.UpdateAudio`.
#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct AudioFramePacket {
    /// The frame's unscaled time; the virtual output device advances by it.
    pub delta_time: f64,
    pub listener_position: Vector3,
    pub listener_forward: Vector3,
    pub listener_up: Vector3,
    pub listener_velocity: Vector3,
    /// `AudioListener.volume`.
    pub listener_volume: f32,
    pub flags: i32,
    /// The listener's filter components (NativeApi 20), in component order; null when none.
    pub filter_count: i32,
    pub _pad: i32,
    /// Valid during the call only: the mixer copies them before it returns.
    pub filters: *const AudioFilterDescriptor,
    /// The reverb zones' blended SFX Reverb at the listener (NativeApi 21): kind
    /// [`filter_kind::REVERB`] while initialized zone objects retain a shared return, 0 otherwise.
    /// Disabling every zone supplies inactive parameters while keeping its histories. A new `id`
    /// starts an empty return after the last initialized zone object was destroyed.
    pub zone: AudioFilterDescriptor,
}

impl Default for AudioFramePacket {
    fn default() -> Self {
        Self {
            delta_time: 0.0,
            listener_position: Vector3::default(),
            listener_forward: Vector3::default(),
            listener_up: Vector3::default(),
            listener_velocity: Vector3::default(),
            listener_volume: 0.0,
            flags: 0,
            filter_count: 0,
            _pad: 0,
            filters: std::ptr::null(),
            zone: AudioFilterDescriptor::default(),
        }
    }
}

#[repr(C)]
#[derive(Debug, Clone, Copy, Default)]
pub struct AudioStatus {
    pub dsp_time: f64,
    pub sample_rate: i32,
    pub buffer_size: i32,
    /// `AudioSpeakerMode`.
    pub speaker_mode: i32,
    /// 1: a real output device; 0: the virtual one.
    pub device: i32,
    pub real_voices: i32,
    pub playing_sources: i32,
}

/// Calls from the mixer into the managed engine: `(kind, handle, data, frames, channels, arg)`.
pub type Callback = extern "C" fn(i32, u64, *mut f32, i32, i32, i64);

/// `Callback` kinds.
pub mod callback {
    /// `OnAudioFilterRead` of the scripts of source `handle` on `data` (frames × channels).
    pub const SOURCE_FILTER: i32 = 0;
    /// `OnAudioFilterRead` of the scripts on the listener, on the final mix.
    pub const LISTENER_FILTER: i32 = 1;
    /// `PCMReaderCallback` of streamed clip `handle`: fill `data`; `arg` is the read position.
    pub const PCM_READ: i32 = 2;
    /// `PCMSetPositionCallback` of clip `handle` with position `arg`.
    pub const PCM_SET_POSITION: i32 = 3;
    /// Opt-in observation of a summed group after its effects and before its parent.
    pub const GROUP_OUTPUT: i32 = 4;
    /// Control-thread allocation of an exact-sized managed callback array for render token `handle`.
    /// `data` points to one status float: write 1 only on success (0 on entry).
    pub const PREPARE_RENDER_BUFFER: i32 = 5;
    /// Select (`arg` = 1) or leave (`arg` = 0) the prepared arrays on the rendering thread.
    pub const RENDER_BUFFER_SCOPE: i32 = 6;
    /// Retire a render token's managed arrays on the control thread.
    pub const RELEASE_RENDER_BUFFERS: i32 = 7;
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::mem::{offset_of, size_of};

    /// The managed side's layouts (checked by a matching test there).
    #[test]
    fn layouts() {
        assert_eq!(size_of::<AudioClipInfo>(), 32);
        assert_eq!(offset_of!(AudioSourcePacket, volume), 40);
        assert_eq!(offset_of!(AudioSourcePacket, flags), 84);
        assert_eq!(offset_of!(AudioSourcePacket, rolloff_curve), 96);
        assert_eq!(offset_of!(AudioSourcePacket, spread_curve), 112);
        assert_eq!(offset_of!(AudioSourcePacket, filter_count), 120);
        assert_eq!(offset_of!(AudioSourcePacket, filters), 128);
        assert_eq!(size_of::<AudioSourcePacket>(), 184);
        assert_eq!(offset_of!(AudioSourcePacket, listener_position), 136);
        assert_eq!(offset_of!(AudioSourcePacket, listener_forward), 148);
        assert_eq!(offset_of!(AudioSourcePacket, listener_up), 160);
        assert_eq!(
            offset_of!(AudioSourcePacket, doppler_listener_position),
            172
        );
        assert_eq!(size_of::<AudioFilterDescriptor>(), 96);
        assert_eq!(std::mem::align_of::<AudioFilterDescriptor>(), 8);
        assert_eq!(offset_of!(AudioFilterDescriptor, kind), 8);
        assert_eq!(offset_of!(AudioFilterDescriptor, stage), 12);
        assert_eq!(offset_of!(AudioFilterDescriptor, cutoff), 16);
        assert_eq!(offset_of!(AudioFilterDescriptor, resonance), 20);
        assert_eq!(offset_of!(AudioFilterDescriptor, delay), 24);
        assert_eq!(offset_of!(AudioFilterDescriptor, decay), 28);
        assert_eq!(offset_of!(AudioFilterDescriptor, dry_mix), 32);
        assert_eq!(offset_of!(AudioFilterDescriptor, wet_mix), 36);
        assert_eq!(offset_of!(AudioFilterDescriptor, reverb), 40);
        assert_eq!(size_of::<AudioSourceCommand>(), 32);
        assert_eq!(size_of::<AudioSourceState>(), 24);
        assert_eq!(size_of::<AudioFramePacket>(), 176);
        assert_eq!(offset_of!(AudioFramePacket, filter_count), 64);
        assert_eq!(offset_of!(AudioFramePacket, filters), 72);
        assert_eq!(offset_of!(AudioFramePacket, zone), 80);
        assert_eq!(size_of::<AudioStatus>(), 32);
    }

    /// The source packet's filter descriptors were added at NativeApi 16; 20 adds the reverb
    /// kind and parameters, 21 the frame packet's reverb zone, 23 Distortion kind 5, 24 Chorus kind 6. Versions 25, 26 and 27 add mixer Flange, Normalize and Pitch Shifter, 28 render protocol 12, 29 input frame versions, 30 a decoded-resampling flag and 31 the imported texture's reported format, without changing these layouts.
    /// Version 32 adds mesh colors, 33 component tail timing, 34 the instance multiplier, 35 deferred render-target mips, 36 the source-loop listener poses 37 the downloaded audio decoder 38 prepared audio callback buffers and 39 retained MSAA resolves.
    /// Version 40 adds parameterised mixer Pitch Shifter FFT size, 41 native rendering plugins and 42 legacy glyph bearings, without changing audio layouts; 43 adds the zone startup frame flag and 44 Pitch Shifter Max channels 45 precomputed realtime GI and 46 the driver capabilities reply and 47 the component activation flags and reset command 48 the realtime GI update threshold and 49 realtime GI light probes and 50 the screen's stand-in and 51 the realtime GI environment and 52 directional realtime lightmaps.
    /// The managed side pins the same version and refuses every other version.
    #[test]
    fn the_filter_descriptor_layout_is_native_api_52() {
        assert_eq!(crate::native_api::NATIVE_API_VERSION, 52);
        assert_eq!(
            [
                filter_kind::LOWPASS,
                filter_kind::HIGHPASS,
                filter_kind::ECHO,
                filter_kind::REVERB,
                filter_kind::DISTORTION,
                filter_kind::CHORUS
            ],
            [1, 2, 3, 4, 5, 6]
        );
        assert_eq!(
            [filter_stage::BEFORE_SCRIPTS, filter_stage::AFTER_SCRIPTS],
            [0, 1]
        );
        assert_eq!(MAX_SOURCE_FILTERS, 64);
    }

    /// The virtual backend (managed tests, no native mixer) must accept the same descriptor
    /// kinds as `decode_descriptors`: both use one range, `AudioFilterKind.Supported` there and
    /// [`filter_kind::supported`] here. Fails when a kind is added on one side only.
    #[test]
    fn managed_filter_kinds_match() {
        let protocol = include_str!("../../../../managed/UnityEngine/Audio/AudioProtocol.cs");
        let backend = include_str!("../../../../managed/UnityEngine/Audio/AudioBackend.cs");
        let class = &protocol[protocol.find("static class AudioFilterKind").unwrap()..];
        let class = &class[..class.find("\n    }").unwrap()];
        let constants: Vec<(&str, &str)> = class
            .lines()
            .filter_map(|line| line.trim().strip_prefix("internal const int "))
            .map(|rest| {
                let (name, value) = rest.trim_end_matches(';').split_once(" = ").unwrap();
                (name, value)
            })
            .collect();
        let numbers: Vec<(&str, i32)> = constants
            .iter()
            .filter_map(|(name, value)| value.parse().ok().map(|v| (*name, v)))
            .collect();
        assert_eq!(
            numbers,
            [
                ("LowPass", filter_kind::LOWPASS),
                ("HighPass", filter_kind::HIGHPASS),
                ("Echo", filter_kind::ECHO),
                ("Reverb", filter_kind::REVERB),
                ("Distortion", filter_kind::DISTORTION),
                ("Chorus", filter_kind::CHORUS),
            ]
        );
        let last = constants
            .iter()
            .find(|(name, _)| *name == "Last")
            .unwrap()
            .1;
        assert_eq!(
            numbers.iter().find(|(name, _)| *name == last).unwrap().1,
            filter_kind::LAST
        );
        assert!(class.contains("Supported(int kind) => kind >= LowPass && kind <= Last;"));
        let copy = &backend[backend.find("string CopyFilters(").unwrap()..];
        assert!(
            copy[..copy.find("\n        }").unwrap()]
                .contains("!AudioFilterKind.Supported(f.Kind)")
        );
        assert!(
            (0..=filter_kind::LAST + 1).all(|k| filter_kind::supported(k) == (1..=6).contains(&k))
        );
    }
}
