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
}

/// Sent once per frame from `PostLateUpdate.UpdateAudio`.
#[repr(C)]
#[derive(Debug, Clone, Copy, Default)]
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
        assert_eq!(size_of::<AudioSourcePacket>(), 120);
        assert_eq!(size_of::<AudioSourceCommand>(), 32);
        assert_eq!(size_of::<AudioSourceState>(), 24);
        assert_eq!(size_of::<AudioFramePacket>(), 64);
        assert_eq!(size_of::<AudioStatus>(), 32);
    }
}
