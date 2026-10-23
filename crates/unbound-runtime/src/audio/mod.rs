//! Audio: the mixer the managed engine's AudioSources, AudioListener and AudioMixers drive, and
//! the output it renders to (see docs/AUDIO.md).
//!
//! The [`Mixer`] owns every voice; the managed side sends parameters and commands through the
//! native API and reads states back. Its dsp clock counts the frames rendered: with an output
//! device (`unbound run` in a window, see [`set_device_opener`]) the device's thread renders
//! continuously; without one (headless runs, no device, audio disabled) a virtual device renders
//! the frame's time in whole DSP buffers each frame, so playback, `time`, `isPlaying` and
//! scheduling behave the same, deterministically, without opening a device.
pub mod fft;
pub mod mixer;
pub mod protocol;
pub mod spatial;

use anyhow::Result;
pub use mixer::{Clip, Mixer, MixerConfig, SourceParams};
use protocol::{AudioFramePacket, AudioStatus, Callback, frame_flags};
use serde::{Deserialize, Serialize};
use std::{
    path::Path,
    sync::{
        Arc, Mutex, MutexGuard, RwLock,
        atomic::{AtomicBool, AtomicU32, AtomicU64, Ordering},
    },
};
use unbound_yaml::Value;

/// `ProjectSettings/AudioManager.asset`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ProjectAudioSettings {
    /// Global volume: `AudioListener.volume` starts at it.
    pub volume: f32,
    pub rolloff_scale: f32,
    pub doppler_factor: f32,
    /// `AudioSpeakerMode`.
    pub speaker_mode: i32,
    /// 0: the device's rate.
    pub sample_rate: u32,
    pub dsp_buffer_size: u32,
    pub virtual_voices: u32,
    pub real_voices: u32,
    pub disable_audio: bool,
    pub spatializer_plugin: String,
    pub ambisonic_decoder_plugin: String,
}

impl Default for ProjectAudioSettings {
    fn default() -> Self {
        Self {
            volume: 1.0,
            rolloff_scale: 1.0,
            doppler_factor: 1.0,
            speaker_mode: 2,
            sample_rate: 0,
            dsp_buffer_size: 1024,
            virtual_voices: 512,
            real_voices: 32,
            disable_audio: false,
            spatializer_plugin: String::new(),
            ambisonic_decoder_plugin: String::new(),
        }
    }
}

impl ProjectAudioSettings {
    pub fn read(project_root: &Path) -> Self {
        let path = project_root.join("ProjectSettings/AudioManager.asset");
        let body = std::fs::read_to_string(&path)
            .ok()
            .and_then(|text| unbound_yaml::parse_documents(&text).ok())
            .and_then(|docs| docs.into_iter().next())
            .map(|d| d.body);
        match body {
            Some(body) => Self::from_value(&body),
            None => Self::default(),
        }
    }

    pub fn from_value(v: &Value) -> Self {
        let d = Self::default();
        let f = |k: &str, default: f32| {
            v.get(k)
                .and_then(Value::as_f64)
                .map_or(default, |x| x as f32)
        };
        let i = |k: &str| v.get(k).and_then(Value::as_i64);
        let s = |k: &str| v.get(k).and_then(Value::as_str).unwrap_or("").to_string();
        // `m_RequestedDSPBufferSize` (2020+) is what the project asks for; 0 is the default.
        let buffer = i("m_RequestedDSPBufferSize")
            .or_else(|| i("m_DSPBufferSize"))
            .unwrap_or(0);
        Self {
            volume: f("m_Volume", d.volume),
            rolloff_scale: f("Rolloff Scale", d.rolloff_scale),
            doppler_factor: f("Doppler Factor", d.doppler_factor),
            speaker_mode: i("Default Speaker Mode").map_or(d.speaker_mode, |m| m as i32),
            sample_rate: i("m_SampleRate").unwrap_or(0).max(0) as u32,
            dsp_buffer_size: if buffer > 0 {
                buffer as u32
            } else {
                d.dsp_buffer_size
            },
            virtual_voices: i("m_VirtualVoiceCount").map_or(d.virtual_voices, |n| n.max(1) as u32),
            real_voices: i("m_RealVoiceCount").map_or(d.real_voices, |n| n.max(1) as u32),
            disable_audio: i("m_DisableAudio").unwrap_or(0) != 0,
            spatializer_plugin: s("m_SpatializerPlugin"),
            ambisonic_decoder_plugin: s("m_AmbisonicDecoderPlugin"),
        }
    }
}

/// Unity's `AudioConfiguration`.
#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct AudioConfiguration {
    pub speaker_mode: i32,
    pub dsp_buffer_size: u32,
    pub sample_rate: u32,
    pub num_real_voices: u32,
    pub num_virtual_voices: u32,
}

/// An open output device; dropping it closes it.
pub trait AudioDevice: Send {
    fn sample_rate(&self) -> u32;
    fn name(&self) -> String;
}

/// Renders interleaved frames for `channels` output channels; called on the device's thread.
pub type RenderFn = Box<dyn FnMut(&mut [f32], usize) + Send>;

/// Opens the platform's output device (`unbound-app` provides one).
pub trait DeviceOpener: Send + Sync {
    /// `sample_rate`: the rate to ask for (None: the device's own); `buffer_size`: frames per
    /// callback to ask for.
    fn open(
        &self,
        sample_rate: Option<u32>,
        buffer_size: u32,
        render: RenderFn,
    ) -> Result<Box<dyn AudioDevice>>;
}

static OPENER: RwLock<Option<Arc<dyn DeviceOpener>>> = RwLock::new(None);

/// Installs the output device opener; without one the virtual device is used. Call before the
/// game starts.
pub fn set_device_opener(opener: Arc<dyn DeviceOpener>) {
    *OPENER.write().unwrap() = Some(opener);
}

/// The dsp clock, readable without the mixer's lock (`AudioSettings.dspTime` is called from
/// `OnAudioFilterRead`, while the mixer is locked).
#[derive(Default)]
struct Clock {
    frames: AtomicU64,
    rate: AtomicU32,
}

impl Clock {
    fn store(&self, mixer: &Mixer) {
        self.rate.store(mixer.config.sample_rate, Ordering::Release);
        self.frames.store(mixer.frames(), Ordering::Release);
    }

    fn seconds(&self) -> f64 {
        let rate = self.rate.load(Ordering::Acquire).max(1);
        self.frames.load(Ordering::Acquire) as f64 / rate as f64
    }
}

/// The audio system of the running game.
pub struct Engine {
    mixer: Arc<Mutex<Mixer>>,
    clock: Arc<Clock>,
    device: Mutex<Option<Box<dyn AudioDevice>>>,
    /// Seconds the virtual device still owes.
    owed: Mutex<f64>,
    pub settings: ProjectAudioSettings,
    configuration: Mutex<AudioConfiguration>,
    warned: AtomicBool,
}

/// The virtual device's rate when the project leaves it to the device.
const VIRTUAL_SAMPLE_RATE: u32 = 48000;

impl Engine {
    /// Starts the audio system with the project's settings; `open_device`: try the installed
    /// output device (else the virtual one).
    pub fn new(settings: ProjectAudioSettings, open_device: bool) -> Self {
        let configuration = AudioConfiguration {
            speaker_mode: settings.speaker_mode,
            dsp_buffer_size: settings.dsp_buffer_size,
            sample_rate: settings.sample_rate,
            num_real_voices: settings.real_voices,
            num_virtual_voices: settings.virtual_voices,
        };
        let engine = Self {
            mixer: Arc::new(Mutex::new(Mixer::new(MixerConfig::default()))),
            clock: Arc::new(Clock::default()),
            device: Mutex::new(None),
            owed: Mutex::new(0.0),
            settings,
            configuration: Mutex::new(configuration),
            warned: AtomicBool::new(false),
        };
        engine.start(configuration, open_device);
        engine
    }

    pub fn mixer(&self) -> MutexGuard<'_, Mixer> {
        self.mixer.lock().unwrap_or_else(|e| e.into_inner())
    }

    /// Opens the device (or not) and configures the mixer for it.
    fn start(&self, requested: AudioConfiguration, open_device: bool) {
        let mut rate = if requested.sample_rate > 0 {
            requested.sample_rate
        } else {
            VIRTUAL_SAMPLE_RATE
        };
        let buffer = requested.dsp_buffer_size.max(64);
        let opener = OPENER.read().unwrap().clone();
        let mut device = None;
        if open_device && !self.settings.disable_audio {
            if let Some(opener) = opener {
                let mixer = self.mixer.clone();
                let clock = self.clock.clone();
                let render: RenderFn = Box::new(move |out, channels| {
                    let mut mixer = mixer.lock().unwrap_or_else(|e| e.into_inner());
                    mixer.render(out, channels);
                    clock.store(&mixer);
                });
                let wanted = (requested.sample_rate > 0).then_some(requested.sample_rate);
                match opener.open(wanted, buffer, render) {
                    Ok(opened) => {
                        rate = opened.sample_rate();
                        tracing::info!("audio output: {} at {rate} Hz", opened.name());
                        device = Some(opened);
                    }
                    Err(error) => {
                        if !self.warned.swap(true, Ordering::SeqCst) {
                            tracing::warn!(
                                "no audio output device ({error:#}); audio runs on a virtual clock"
                            );
                        }
                    }
                }
            }
        } else if self.settings.disable_audio && open_device {
            tracing::info!("audio is disabled in the project settings; it runs on a virtual clock");
        }
        {
            let mut mixer = self.mixer();
            mixer.reconfigure(MixerConfig {
                sample_rate: rate,
                buffer_size: buffer,
                speaker_mode: requested.speaker_mode,
                real_voices: requested.num_real_voices.max(1) as usize,
                virtual_voices: requested.num_virtual_voices.max(1) as usize,
                rolloff_scale: self.settings.rolloff_scale,
                doppler_factor: self.settings.doppler_factor,
            });
            mixer.discard_output = device.is_none();
            self.clock.store(&mixer);
        }
        *self.device.lock().unwrap() = device;
    }

    pub fn has_device(&self) -> bool {
        self.device.lock().unwrap().is_some()
    }

    /// `AudioSettings.GetConfiguration`: the configuration in use (the device's sample rate).
    pub fn configuration(&self) -> AudioConfiguration {
        let mut configuration = *self.configuration.lock().unwrap();
        let mixer = self.mixer();
        configuration.sample_rate = mixer.config.sample_rate;
        configuration.dsp_buffer_size = mixer.config.buffer_size;
        configuration
    }

    /// `AudioSettings.Reset`: restarts the output with another configuration; every voice stops.
    pub fn reset(&self, configuration: AudioConfiguration) -> bool {
        let had_device = self.device.lock().unwrap().take().is_some();
        *self.configuration.lock().unwrap() = configuration;
        let open = had_device || OPENER.read().unwrap().is_some();
        self.start(configuration, open);
        true
    }

    pub fn dsp_time(&self) -> f64 {
        self.clock.seconds()
    }

    pub fn set_callback(&self, callback: Option<Callback>) {
        self.mixer().set_callback(callback);
    }

    /// Once per frame: the listener, and the virtual device's time.
    pub fn frame(&self, packet: &AudioFramePacket) -> AudioStatus {
        let virtual_device = !self.has_device();
        let mut mixer = self.mixer();
        mixer.listener = spatial::Listener {
            position: packet.listener_position,
            forward: packet.listener_forward,
            up: packet.listener_up,
            velocity: packet.listener_velocity,
        };
        mixer.listener_volume = packet.listener_volume.max(0.0);
        mixer.has_listener = packet.flags & frame_flags::HAS_LISTENER != 0;
        mixer.listener_paused = packet.flags & frame_flags::PAUSED != 0;
        mixer.listener_filter = packet.flags & frame_flags::LISTENER_FILTER != 0;
        if virtual_device {
            let rate = mixer.config.sample_rate as f64;
            let buffer = mixer.config.buffer_size.max(1) as usize;
            let mut owed = self.owed.lock().unwrap();
            *owed += packet.delta_time.max(0.0);
            let buffers = (*owed * rate / buffer as f64).floor() as usize;
            if buffers > 0 {
                mixer.advance(buffers * buffer);
                *owed -= (buffers * buffer) as f64 / rate;
            }
        }
        self.clock.store(&mixer);
        AudioStatus {
            dsp_time: mixer.dsp_time(),
            sample_rate: mixer.config.sample_rate as i32,
            buffer_size: mixer.config.buffer_size as i32,
            speaker_mode: mixer.config.speaker_mode,
            device: (!virtual_device) as i32,
            real_voices: mixer.config.real_voices as i32,
            playing_sources: mixer.playing_sources() as i32,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use protocol::{AudioSourceCommand, command, voice_state};

    #[test]
    fn reads_the_audio_manager() {
        let text = "AudioManager:\n  m_Volume: 0.5\n  Rolloff Scale: 2\n  Doppler Factor: 0\n  Default Speaker Mode: 1\n  m_SampleRate: 44100\n  m_DSPBufferSize: 1024\n  m_VirtualVoiceCount: 64\n  m_RealVoiceCount: 8\n  m_DisableAudio: 1\n  m_RequestedDSPBufferSize: 256\n";
        let value = unbound_yaml::parse_value(text).unwrap();
        let s = ProjectAudioSettings::from_value(value.get("AudioManager").unwrap());
        assert_eq!(s.volume, 0.5);
        assert_eq!(s.rolloff_scale, 2.0);
        assert_eq!(s.doppler_factor, 0.0);
        assert_eq!(s.speaker_mode, 1);
        assert_eq!(s.sample_rate, 44100);
        assert_eq!(s.dsp_buffer_size, 256);
        assert_eq!((s.virtual_voices, s.real_voices), (64, 8));
        assert!(s.disable_audio);
    }

    #[test]
    fn the_virtual_device_advances_in_whole_buffers() {
        let engine = Engine::new(
            ProjectAudioSettings {
                sample_rate: 1000,
                dsp_buffer_size: 100,
                ..ProjectAudioSettings::default()
            },
            false,
        );
        let packet = |dt| AudioFramePacket {
            delta_time: dt,
            listener_volume: 1.0,
            flags: frame_flags::HAS_LISTENER,
            ..Default::default()
        };
        assert_eq!(engine.frame(&packet(0.15)).dsp_time, 0.1);
        assert_eq!(engine.frame(&packet(0.06)).dsp_time, 0.2);
        assert_eq!(engine.dsp_time(), 0.2);

        // A clip ends on the virtual clock.
        {
            let mut mixer = engine.mixer();
            mixer.add_clip(1, Clip::new(1, 1000, vec![0.0; 250]));
            mixer.update_source(
                9,
                SourceParams {
                    clip: 1,
                    ..SourceParams::default()
                },
            );
            mixer.command(
                9,
                &AudioSourceCommand {
                    kind: command::PLAY,
                    ..Default::default()
                },
            );
        }
        engine.frame(&packet(0.2));
        assert_eq!(engine.mixer().source_state(9).main, voice_state::PLAYING);
        engine.frame(&packet(0.1));
        assert_eq!(engine.mixer().source_state(9).main, voice_state::NONE);
    }

    #[test]
    fn reset_keeps_the_clock_and_stops_voices() {
        let engine = Engine::new(
            ProjectAudioSettings {
                sample_rate: 1000,
                dsp_buffer_size: 100,
                ..ProjectAudioSettings::default()
            },
            false,
        );
        engine.frame(&AudioFramePacket {
            delta_time: 1.0,
            ..Default::default()
        });
        let mut configuration = engine.configuration();
        assert_eq!(configuration.sample_rate, 1000);
        configuration.sample_rate = 2000;
        assert!(engine.reset(configuration));
        assert_eq!(engine.configuration().sample_rate, 2000);
        assert_eq!(engine.dsp_time(), 1.0);
    }
}
