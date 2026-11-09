//! Audio: the mixer the managed engine's AudioSources, AudioListener and AudioMixers drive, and
//! the output it renders to (see docs/AUDIO.md).
//!
//! The [`Mixer`] owns every voice; the managed side sends parameters and commands through the
//! native API and reads states back. Its dsp clock counts the frames rendered: with an output
//! device (`unbound run` in a window, see [`set_device_opener`]) the device's thread renders
//! continuously; without one (headless runs, no device, audio disabled) a virtual device renders
//! the frame's time in whole DSP buffers each frame, so playback, `time`, `isPlaying` and
//! scheduling behave the same, deterministically, without opening a device.
pub mod capture;
pub mod chorus;
pub mod compressor;
pub mod duck;
pub mod effects;
pub mod fft;
pub mod flange;
pub mod mixer;
pub(crate) mod modulated_delay;
pub mod normalize;
pub mod pitch;
pub mod protocol;
pub(crate) mod ramp;
pub mod reverb;
pub mod spatial;
pub(crate) mod wavetable;

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
        let body = unbound_yaml::reads::read_documents(&path)
            .ok()
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

static NEXT_RENDER_TOKEN: AtomicU64 = AtomicU64::new(1);

/// A reusable execution thread for the captured output that closes during reset.
/// Buffers are allocated and retired by the caller; the render uses the mixer's scratch storage.
struct ResetWorker {
    token: u64,
    mixer: Arc<Mutex<Mixer>>,
    #[cfg(test)]
    allocations: Arc<AtomicU64>,
    sender: Option<std::sync::mpsc::SyncSender<Vec<f32>>>,
    finished: std::sync::mpsc::Receiver<Vec<f32>>,
    thread: Option<std::thread::JoinHandle<()>>,
}

impl ResetWorker {
    fn new(mixer: Arc<Mutex<Mixer>>, clock: Arc<Clock>) -> std::io::Result<Self> {
        let (sender, receiver) = std::sync::mpsc::sync_channel::<Vec<f32>>(0);
        let (finished, completion) = std::sync::mpsc::sync_channel(0);
        let token = NEXT_RENDER_TOKEN.fetch_add(1, Ordering::Relaxed);
        let worker_mixer = mixer.clone();
        #[cfg(test)]
        let allocations = Arc::new(AtomicU64::new(0));
        #[cfg(test)]
        let worker_allocations = allocations.clone();
        let thread = std::thread::Builder::new()
            .name("unbound-audio-reset".into())
            .spawn(move || {
                while let Ok(mut buffer) = receiver.recv() {
                    {
                        let mut mixer = worker_mixer.lock().unwrap_or_else(|e| e.into_inner());
                        mixer.capture_buffer_scope(token, true);
                        #[cfg(test)]
                        crate::audio::tests::allocations::start();
                        mixer.render_capture_block(&mut buffer);
                        #[cfg(test)]
                        worker_allocations.store(
                            crate::audio::tests::allocations::finish(),
                            Ordering::Relaxed,
                        );
                        mixer.capture_buffer_scope(token, false);
                        clock.store(&mixer);
                    }
                    if finished.send(buffer).is_err() {
                        break;
                    }
                }
            })?;
        Ok(Self {
            token,
            mixer,
            #[cfg(test)]
            allocations,
            sender: Some(sender),
            finished: completion,
            thread: Some(thread),
        })
    }

    fn render(&self, buffer: Vec<f32>) -> Result<()> {
        self.sender
            .as_ref()
            .unwrap()
            .send(buffer)
            .map_err(|_| anyhow::anyhow!("audio reset worker stopped before processing"))?;
        // Return ownership to the caller so the audio thread does not retire the buffer.
        let _buffer = self
            .finished
            .recv()
            .map_err(|_| anyhow::anyhow!("audio reset worker stopped during processing"))?;
        self.mixer
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .release_capture_buffers(self.token);
        Ok(())
    }
}

impl Drop for ResetWorker {
    fn drop(&mut self) {
        self.sender.take();
        if let Some(thread) = self.thread.take() {
            let _ = thread.join();
        }
        self.mixer
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .release_capture_buffers(self.token);
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
    capturing: Arc<AtomicBool>,
    capture_pending: Mutex<f64>,
    reset_worker: Mutex<Option<ResetWorker>>,
}

/// The virtual device's rate when the project leaves it to the device.
const VIRTUAL_SAMPLE_RATE: u32 = 48000;

impl Engine {
    /// Starts the audio system with the project's settings; `open_device`: try the installed
    /// output device (else the virtual one).
    pub fn new(settings: ProjectAudioSettings, open_device: bool) -> Self {
        let mut mixer = Mixer::new(MixerConfig::default());
        mixer.defer_voice_submission = true;
        let configuration = AudioConfiguration {
            speaker_mode: settings.speaker_mode,
            dsp_buffer_size: settings.dsp_buffer_size,
            sample_rate: settings.sample_rate,
            num_real_voices: settings.real_voices,
            num_virtual_voices: settings.virtual_voices,
        };
        let engine = Self {
            mixer: Arc::new(Mutex::new(mixer)),
            clock: Arc::new(Clock::default()),
            device: Mutex::new(None),
            owed: Mutex::new(0.0),
            settings,
            configuration: Mutex::new(configuration),
            warned: AtomicBool::new(false),
            capturing: Arc::new(AtomicBool::new(false)),
            capture_pending: Mutex::new(0.0),
            reset_worker: Mutex::new(None),
        };
        engine
            .start(configuration, open_device)
            .expect("initial audio configuration has no mixer effects");
        engine
    }

    pub fn mixer(&self) -> MutexGuard<'_, Mixer> {
        self.mixer.lock().unwrap_or_else(|e| e.into_inner())
    }

    fn mixer_config(&self, requested: AudioConfiguration, rate: u32) -> MixerConfig {
        MixerConfig {
            sample_rate: rate,
            buffer_size: requested.dsp_buffer_size.max(64),
            speaker_mode: requested.speaker_mode,
            real_voices: requested.num_real_voices.max(1) as usize,
            virtual_voices: requested.num_virtual_voices.max(1) as usize,
            rolloff_scale: self.settings.rolloff_scale,
            doppler_factor: self.settings.doppler_factor,
        }
    }

    /// Opens the device (or not) and configures the mixer for it.
    fn start(&self, requested: AudioConfiguration, open_device: bool) -> Result<()> {
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
                let capturing = self.capturing.clone();
                let render: RenderFn = Box::new(move |out, channels| {
                    let mut mixer = mixer.lock().unwrap_or_else(|e| e.into_inner());
                    if capturing.load(Ordering::Acquire) {
                        out.fill(0.0);
                        return;
                    }
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
            mixer.reconfigure(self.mixer_config(requested, rate))?;
            mixer.discard_output = device.is_none();
            self.clock.store(&mixer);
        }
        *self.device.lock().unwrap() = device;
        Ok(())
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
        self.reset_with_capture_tail(configuration, false)
    }

    pub fn reset_with_capture_tail(
        &self,
        configuration: AudioConfiguration,
        finish_capture_buffer: bool,
    ) -> bool {
        let rate = if configuration.sample_rate > 0 {
            configuration.sample_rate
        } else {
            VIRTUAL_SAMPLE_RATE
        };
        let config = self.mixer_config(configuration, rate);
        if let Err(error) = self.mixer().validate_configuration(&config) {
            tracing::warn!("audio reset rejected: {error:#}");
            return false;
        }
        let tail = if finish_capture_buffer && self.capturing.load(Ordering::Acquire) {
            let samples = self.mixer().config.buffer_size.max(1) as usize * 2;
            if samples > capture::MAX_SAMPLES {
                tracing::warn!(
                    "audio reset rejected: closing capture buffer exceeds the supported allocation limit"
                );
                return false;
            }
            let mut buffer = Vec::new();
            if buffer.try_reserve_exact(samples).is_err() {
                tracing::warn!("audio reset rejected: cannot allocate closing capture buffer");
                return false;
            }
            buffer.resize(samples, 0.0);
            Some(buffer)
        } else {
            None
        };
        if let Some(buffer) = tail {
            // Finish the old output on an audio thread before configuration notification.
            // Unity 6000.0.71f1, tests/projects/mixersuspend reset delivery controls.
            let mut worker = self.reset_worker.lock().unwrap();
            if worker.is_none() {
                match ResetWorker::new(self.mixer.clone(), self.clock.clone()) {
                    Ok(created) => *worker = Some(created),
                    Err(error) => {
                        tracing::warn!(
                            "audio reset rejected: cannot start closing output thread: {error}"
                        );
                        return false;
                    }
                }
            }
            if let Err(error) = self
                .mixer()
                .prepare_capture_block(worker.as_ref().unwrap().token)
            {
                tracing::warn!("audio reset rejected: cannot prepare closing output: {error:#}");
                return false;
            }
            if let Err(error) = worker.as_ref().unwrap().render(buffer) {
                tracing::warn!("audio reset failed: {error:#}");
                return false;
            }
        }
        let had_device = self.device.lock().unwrap().take().is_some();
        let open = had_device || OPENER.read().unwrap().is_some();
        if let Err(error) = self.start(configuration, open) {
            tracing::warn!("audio reset failed: {error:#}");
            return false;
        }
        *self.configuration.lock().unwrap() = configuration;
        self.capturing.store(false, Ordering::Release);
        true
    }

    pub fn dsp_time(&self) -> f64 {
        self.clock.seconds()
    }

    pub fn set_callback(&self, callback: Option<Callback>) {
        self.mixer().set_callback(callback);
    }

    pub fn capture_start(&self) -> bool {
        // Finish any device block, including its clock update, before handing the mixer to capture.
        let _mixer = self.mixer();
        let started = !self.capturing.swap(true, Ordering::AcqRel);
        if started {
            *self.capture_pending.lock().unwrap() = 0.0;
        }
        started
    }

    pub fn capture_stop(&self) -> bool {
        self.capturing.swap(false, Ordering::AcqRel)
    }

    pub fn capture_sample_count(&self, delta_time: f32) -> u32 {
        if !self.capturing.load(Ordering::Acquire) {
            return 0;
        }
        let mixer = self.mixer();
        let pending = *self.capture_pending.lock().unwrap();
        let block = mixer.config.buffer_size.max(1) as f64;
        (((pending + mixer.config.sample_rate as f64 * delta_time as f64).max(0.0) / block).floor()
            * block) as u32
    }

    pub fn capture_render(
        &self,
        length: usize,
        sinks: Vec<capture::SinkRequest>,
        delta_time: f32,
    ) -> Result<capture::Output> {
        if !self.capturing.load(Ordering::Acquire) {
            return Ok(capture::Output::default());
        }
        let mut mixer = self.mixer();
        anyhow::ensure!(
            mixer.config.speaker_mode == 2,
            "AudioRenderer currently supports stereo output"
        );
        let output = mixer.capture(length, sinks)?;
        let mut pending = self.capture_pending.lock().unwrap();
        *pending = (*pending + mixer.config.sample_rate as f64 * delta_time as f64).max(0.0)
            - (length / 2) as f64;
        self.clock.store(&mixer);
        Ok(output)
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
        // SAFETY: native_api passes a packet whose pointers are valid for this call.
        mixer.update_listener_filters(unsafe { mixer::decode_listener_filters(packet) });
        mixer.update_zone(&packet.zone);
        mixer.update_suspension();
        if virtual_device && !self.capturing.load(Ordering::Acquire) {
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

    pub(super) mod allocations {
        use std::{
            alloc::{GlobalAlloc, Layout, System},
            cell::Cell,
        };
        thread_local! {
            static COUNT: Cell<Option<u64>> = const { Cell::new(None) };
        }
        struct Allocator;
        #[global_allocator]
        static ALLOCATOR: Allocator = Allocator;
        unsafe impl GlobalAlloc for Allocator {
            unsafe fn alloc(&self, layout: Layout) -> *mut u8 {
                let _ = COUNT.try_with(|count| {
                    if let Some(n) = count.get() {
                        count.set(Some(n + 1));
                    }
                });
                unsafe { System.alloc(layout) }
            }
            unsafe fn dealloc(&self, ptr: *mut u8, layout: Layout) {
                unsafe { System.dealloc(ptr, layout) }
            }
            unsafe fn realloc(&self, ptr: *mut u8, layout: Layout, size: usize) -> *mut u8 {
                let _ = COUNT.try_with(|count| {
                    if let Some(n) = count.get() {
                        count.set(Some(n + 1));
                    }
                });
                unsafe { System.realloc(ptr, layout, size) }
            }
        }
        pub fn start() {
            COUNT.with(|count| count.set(Some(0)));
        }
        pub fn finish() -> u64 {
            COUNT.with(|count| count.replace(None).unwrap())
        }
    }

    #[test]
    fn first_and_repeated_reset_renders_allocate_nothing_with_routing_and_filters() {
        static FILTERS: AtomicU32 = AtomicU32::new(0);
        static GROUPS: AtomicU32 = AtomicU32::new(0);
        extern "C" fn callback(kind: i32, _: u64, _: *mut f32, _: i32, _: i32, _: i64) {
            if kind == protocol::callback::SOURCE_FILTER {
                FILTERS.fetch_add(1, Ordering::Relaxed);
            }
            if kind == protocol::callback::GROUP_OUTPUT {
                GROUPS.fetch_add(1, Ordering::Relaxed);
            }
        }
        let engine = Engine::new(ProjectAudioSettings::default(), false);
        for iteration in 0..3 {
            let before_filters = FILTERS.load(Ordering::Relaxed);
            let before_groups = GROUPS.load(Ordering::Relaxed);
            {
                let mut mixer = engine.mixer();
                mixer.set_callback(Some(callback));
                mixer.listener_filter = true;
                mixer.configure_group(serde_json::from_value(serde_json::json!({
                    "handle": 4, "mixer": 4, "parent": 0, "muted": false, "bypass": false, "observe": true, "effects": []
                })).unwrap()).unwrap();
                mixer.add_clip(1, Clip::new(1, 48000, vec![0.125; 48000]));
                mixer.update_source(
                    9,
                    SourceParams {
                        clip: 1,
                        group: 4,
                        flags: protocol::source_flags::FILTER | protocol::source_flags::LOOP,
                        ..Default::default()
                    },
                );
                mixer.command(
                    9,
                    &AudioSourceCommand {
                        kind: command::PLAY,
                        ..Default::default()
                    },
                );
                mixer.add_clip(
                    2,
                    Clip {
                        channels: 1,
                        rate: 48000,
                        frames: 5000,
                        stream: true,
                        ..Default::default()
                    },
                );
                mixer.update_source(
                    10,
                    SourceParams {
                        clip: 2,
                        group: 4,
                        flags: protocol::source_flags::LOOP,
                        ..Default::default()
                    },
                );
                mixer.command(
                    10,
                    &AudioSourceCommand {
                        kind: command::PLAY,
                        ..Default::default()
                    },
                );
                mixer.update_source(
                    11,
                    SourceParams {
                        group: 4,
                        flags: protocol::source_flags::FILTER,
                        ..Default::default()
                    },
                );
                mixer.command(
                    11,
                    &AudioSourceCommand {
                        kind: command::PLAY,
                        ..Default::default()
                    },
                );
                mixer.update_suspension();
                if iteration == 2 {
                    mixer.add_clip(3, Clip::new(1, 48000, vec![0.25]));
                    for _ in 0..4 {
                        mixer.command(
                            9,
                            &AudioSourceCommand {
                                kind: command::PLAY_ONE_SHOT,
                                clip: 3,
                                volume_scale: 1.0,
                                ..Default::default()
                            },
                        );
                    }
                    mixer.update_suspension();
                    mixer.render(&mut [0.0; 512], 2);
                    mixer.command(
                        9,
                        &AudioSourceCommand {
                            kind: command::PLAY_ONE_SHOT,
                            clip: 3,
                            volume_scale: 1.0,
                            ..Default::default()
                        },
                    );
                    mixer.update_suspension();
                }
            }
            assert!(engine.capture_start());
            assert!(engine.reset_with_capture_tail(engine.configuration(), true));
            assert!(FILTERS.load(Ordering::Relaxed) > before_filters);
            assert!(GROUPS.load(Ordering::Relaxed) > before_groups);
            let worker = engine.reset_worker.lock().unwrap();
            assert_eq!(
                worker.as_ref().unwrap().allocations.load(Ordering::Relaxed),
                0
            );
        }
    }

    #[test]
    fn capture_start_waits_for_the_in_flight_mixer_and_clock_update() {
        use std::{sync::mpsc, time::Duration};

        let engine = Arc::new(Engine::new(ProjectAudioSettings::default(), false));
        let mut mixer = engine.mixer();
        let (entered_tx, entered_rx) = mpsc::channel();
        let (started_tx, started_rx) = mpsc::channel();
        let capture = engine.clone();
        let thread = std::thread::spawn(move || {
            entered_tx.send(()).unwrap();
            let started = capture.capture_start();
            started_tx.send((started, capture.dsp_time())).unwrap();
        });
        entered_rx.recv_timeout(Duration::from_secs(5)).unwrap();
        let early = started_rx.recv_timeout(Duration::from_millis(100));
        let flag_during_device_block = engine.capturing.load(Ordering::Acquire);
        mixer.render(&mut vec![0.0; 2048], 2);
        engine.clock.store(&mixer);
        let completed_device_time = engine.dsp_time();
        drop(mixer);
        let started = match early {
            Ok(value) => value,
            Err(_) => started_rx.recv_timeout(Duration::from_secs(5)).unwrap(),
        };
        thread.join().unwrap();
        assert!(
            early.is_err(),
            "capture returned while the device still owned the mixer"
        );
        assert!(!flag_during_device_block);
        assert!(started.0);
        assert!(completed_device_time > 0.0);
        assert_eq!(started.1, completed_device_time);
    }

    #[test]
    fn offline_capture_preserves_native_partial_buffers_and_pending_sample_counts() {
        let reference = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../tests/projects/mixerfilters/Reference/unity-6000.0.71f1/renderer-api");
        let header: serde_json::Value =
            serde_json::from_slice(&std::fs::read(reference.join("capture.json")).unwrap())
                .unwrap();
        let engine = Engine::new(
            ProjectAudioSettings {
                sample_rate: 48000,
                ..Default::default()
            },
            false,
        );
        assert!(!engine.capture_stop());
        assert!(engine.capture_start());
        assert!(!engine.capture_start());
        // The measured property round trip truncates the requested duration at the existing
        // rational-time tick rate. Keep the exact float passed to AudioRenderer after that view.
        let ticks = ((1024.0f32 / 48000.0) as f64 * 141120000.0) as i64;
        let duration = (ticks as f64 / 141120000.0) as f32;
        let restored_ticks = (duration as f64 * 141120000.0) as i64;
        let duration = (restored_ticks as f64 / 141120000.0) as f32;
        assert_eq!(
            engine.capture_sample_count(duration),
            header["count.started"].as_u64().unwrap() as u32
        );
        for length in [0, 1, 2, 2046, 2047, 2048, 2049, 2050, 4096] {
            let mut actual = vec![123.0; length];
            let output = engine.capture_render(length, Vec::new(), duration).unwrap();
            assert!(output.ok);
            actual[..output.samples.len()].copy_from_slice(&output.samples);
            let expected = std::fs::read(reference.join(format!("render_{length}.f32"))).unwrap();
            let failures: Vec<_> = expected
                .chunks_exact(4)
                .zip(actual)
                .enumerate()
                .filter_map(|(i, (bytes, value))| {
                    let native = f32::from_le_bytes(bytes.try_into().unwrap());
                    (value != native).then_some((i, native, value))
                })
                .collect();
            assert!(
                failures.is_empty(),
                "length {length}, every failing (sample, Native, engine): {failures:?}"
            );
            assert_eq!(
                engine.capture_sample_count(duration),
                header[format!("count.{length}")].as_u64().unwrap() as u32
            );
        }
        assert!(engine.capture_stop());
        assert!(!engine.capture_stop());
    }

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
        assert!(engine.capture_start());
        configuration.sample_rate = 2000;
        assert!(engine.reset(configuration));
        assert!(!engine.capturing.load(Ordering::Acquire));
        assert_eq!(engine.configuration().sample_rate, 2000);
        assert_eq!(engine.dsp_time(), 1.0);
    }

    #[test]
    fn reset_finishes_the_captured_old_buffer_before_reconfiguring() {
        static CALLER: Mutex<Option<std::thread::ThreadId>> = Mutex::new(None);
        static SAME_THREAD: AtomicBool = AtomicBool::new(true);
        *CALLER.lock().unwrap() = Some(std::thread::current().id());
        static FRAMES: AtomicU32 = AtomicU32::new(0);
        static SAMPLE: AtomicU32 = AtomicU32::new(0);
        extern "C" fn callback(kind: i32, _: u64, data: *mut f32, frames: i32, _: i32, _: i64) {
            if kind == protocol::callback::LISTENER_FILTER {
                SAME_THREAD.store(
                    *CALLER.lock().unwrap() == Some(std::thread::current().id()),
                    Ordering::Relaxed,
                );
                FRAMES.fetch_add(frames as u32, Ordering::Relaxed);
                // SAFETY: the mixer lends the complete output buffer for this callback.
                SAMPLE.store(
                    unsafe { *data.add((frames as usize - 1) * 2) }.to_bits(),
                    Ordering::Relaxed,
                );
            }
        }
        let engine = Engine::new(
            ProjectAudioSettings {
                sample_rate: 1000,
                dsp_buffer_size: 100,
                ..Default::default()
            },
            false,
        );
        {
            let mut mixer = engine.mixer();
            mixer.listener_filter = true;
            mixer.set_callback(Some(callback));
            mixer.add_clip(1, Clip::new(2, 1000, vec![0.125; 2000]));
            mixer.update_source(
                9,
                SourceParams {
                    clip: 1,
                    ..Default::default()
                },
            );
            mixer.command(
                9,
                &AudioSourceCommand {
                    kind: command::PLAY,
                    ..Default::default()
                },
            );
            mixer.update_suspension();
        }
        FRAMES.store(0, Ordering::Relaxed);
        SAMPLE.store(0, Ordering::Relaxed);
        assert!(engine.capture_start());
        let configuration = AudioConfiguration {
            sample_rate: 2000,
            ..engine.configuration()
        };
        assert!(engine.reset_with_capture_tail(configuration, true));
        assert!(!SAME_THREAD.load(Ordering::Relaxed));
        assert_eq!(FRAMES.load(Ordering::Relaxed), 100);
        assert_eq!(f32::from_bits(SAMPLE.load(Ordering::Relaxed)), 0.125);
        assert_eq!(engine.dsp_time(), 0.1);
        assert_eq!(engine.mixer().source_state(9).main, voice_state::NONE);
        assert!(!engine.capturing.load(Ordering::Acquire));
        // An ordinary reset has no captured output to finish.
        assert!(engine.reset_with_capture_tail(configuration, true));
        assert_eq!(FRAMES.load(Ordering::Relaxed), 100);
        assert_eq!(engine.dsp_time(), 0.1);
    }

    #[test]
    fn reset_rejects_oversized_chorus_before_changing_the_configuration() {
        let engine = Engine::new(ProjectAudioSettings::default(), false);
        engine
            .mixer()
            .configure_group(
                serde_json::from_value(serde_json::json!({
                    "handle": 1, "parent": 0, "muted": false, "bypass": false,
                    "effects": [{ "id": "chorus", "kind": "Chorus", "chorus": {
                        "dry": 0.5, "wet1": 0.5, "wet2": 0.5, "wet3": 0.5,
                        "delay": 40.0, "rate": 0.8, "depth": 0.03 } }]
                }))
                .unwrap(),
            )
            .unwrap();
        let before = engine.configuration();
        assert!(engine.capture_start());
        assert!(!engine.reset(AudioConfiguration {
            sample_rate: i32::MAX as u32,
            ..before
        }));
        assert_eq!(engine.configuration(), before);
        assert!(engine.capturing.load(Ordering::Acquire));
    }

    #[test]
    fn rejected_reset_buffer_preserves_capture_and_does_not_process_the_old_output() {
        let engine = Engine::new(ProjectAudioSettings::default(), false);
        engine
            .mixer()
            .configure_group(
                serde_json::from_value(serde_json::json!({
                    "handle": 1, "parent": 0, "muted": false, "bypass": false,
                    "effects": [{ "id": "chorus", "kind": "Chorus", "chorus": {
                        "dry": 0.5, "wet1": 0.5, "wet2": 0.5, "wet3": 0.5,
                        "delay": 40.0, "rate": 0.8, "depth": 0.03 } }]
                }))
                .unwrap(),
            )
            .unwrap();
        let before = engine.configuration();
        assert!(engine.capture_start());
        assert!(!engine.reset_with_capture_tail(
            AudioConfiguration {
                dsp_buffer_size: u32::MAX,
                ..before
            },
            true
        ));
        assert_eq!(engine.configuration(), before);
        assert_eq!(engine.dsp_time(), 0.0);
        assert!(engine.capturing.load(Ordering::Acquire));
    }

    #[test]
    fn oversized_closing_buffer_fails_without_allocating_or_changing_capture() {
        let engine = Engine::new(
            ProjectAudioSettings {
                dsp_buffer_size: u32::MAX,
                ..Default::default()
            },
            false,
        );
        assert!(engine.capture_start());
        let before = engine.configuration();
        let valid = AudioConfiguration {
            dsp_buffer_size: 1024,
            ..before
        };
        assert!(!engine.reset_with_capture_tail(valid, true));
        assert_eq!(engine.configuration(), before);
        assert_eq!(engine.dsp_time(), 0.0);
        assert!(engine.capturing.load(Ordering::Acquire));
        assert!(engine.capture_stop());
        assert!(engine.reset_with_capture_tail(valid, true));
        assert_eq!(engine.configuration().dsp_buffer_size, 1024);
    }

    #[test]
    fn reset_rejects_oversized_echo_before_changing_the_configuration() {
        let engine = Engine::new(ProjectAudioSettings::default(), false);
        for delay in [500.0, 5000.0] {
            let spec = serde_json::from_value(serde_json::json!({
                "handle": 1, "parent": 0, "muted": false, "bypass": false,
                "effects": [{ "id": "echo", "kind": "Echo", "delay": delay }]
            }))
            .unwrap();
            engine.mixer().configure_group(spec).unwrap();
            let before = engine.configuration();
            if !engine.capturing.load(Ordering::Acquire) {
                assert!(engine.capture_start());
            }
            assert!(!engine.reset(AudioConfiguration {
                sample_rate: i32::MAX as u32,
                ..before
            }));
            assert_eq!(engine.configuration(), before);
            assert!(engine.capturing.load(Ordering::Acquire));
        }
        assert!(engine.reset(AudioConfiguration {
            sample_rate: 44100,
            ..engine.configuration()
        }));
        assert_eq!(engine.configuration().sample_rate, 44100);
    }
}
