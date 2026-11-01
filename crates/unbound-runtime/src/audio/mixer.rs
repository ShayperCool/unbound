//! The mixer: clips, AudioSources with their voices (the main one `Play` starts and the
//! one-shots), mixer groups and the listener, rendered block by block into the output.
//!
//! Every position is sample accurate: a voice starts and ends at the dsp frame it is scheduled
//! for, and the dsp clock is the number of frames rendered. Voices resample their clip by linear
//! interpolation (FMOD's default resampler) at `pitch × group pitch × doppler × clip rate /
//! output rate`; a negative rate plays backwards. Gains ramp across a block, so parameter
//! changes do not click.
use super::{
    capture,
    effects::{GroupNode, GroupSpec},
    fft,
    protocol::{
        AudioSourceCommand, AudioSourcePacket, AudioSourceState, Callback, callback, command,
        source_flags, voice_state,
    },
    spatial::{self, Listener, SourceSpatial},
};
use crate::render_protocol::Vector3;
use std::collections::HashMap;

/// Frames rendered at a time (scheduling stays sample accurate inside a block).
pub const BLOCK: usize = 256;
/// Frames of output kept for `GetOutputData`/`GetSpectrumData` (16384 covers the largest
/// spectrum, 8192 bins).
const RING_FRAMES: usize = 16384;
/// Frames a streamed clip's PCM reader is asked for at a time.
const STREAM_CHUNK: usize = 4096;

/// Decoded (or user-made) audio data.
#[derive(Debug, Clone, Default)]
pub struct Clip {
    pub channels: usize,
    pub rate: u32,
    pub frames: usize,
    /// Interleaved; empty for a streamed clip, whose data comes from its PCM reader.
    pub samples: Vec<f32>,
    pub stream: bool,
}

impl Clip {
    pub fn new(channels: usize, rate: u32, samples: Vec<f32>) -> Self {
        let channels = channels.max(1);
        Self {
            frames: samples.len() / channels,
            channels,
            rate,
            samples,
            stream: false,
        }
    }

    /// The (left, right) of frame `i` (mono on both sides; beyond two channels the first two).
    fn frame(&self, i: usize) -> (f32, f32) {
        let base = i * self.channels;
        match self.samples.get(base..base + self.channels) {
            Some(s) if self.channels == 1 => (s[0], s[0]),
            Some(s) => (s[0], s[1]),
            None => (0.0, 0.0),
        }
    }
}

/// An AudioSource's parameters (a copy of its packet with the curves).
#[derive(Debug, Clone)]
pub struct SourceParams {
    pub clip: u64,
    pub group: u64,
    pub position: Vector3,
    pub velocity: Vector3,
    pub volume: f32,
    pub pitch: f32,
    pub pan_stereo: f32,
    pub spatial_blend: f32,
    pub spread: f32,
    pub doppler_level: f32,
    pub min_distance: f32,
    pub max_distance: f32,
    pub rolloff_mode: i32,
    pub priority: i32,
    pub flags: u32,
    pub rolloff_curve: Vec<f32>,
    pub spatial_blend_curve: Vec<f32>,
    pub spread_curve: Vec<f32>,
}

impl Default for SourceParams {
    fn default() -> Self {
        Self {
            clip: 0,
            group: 0,
            position: Vector3::default(),
            velocity: Vector3::default(),
            volume: 1.0,
            pitch: 1.0,
            pan_stereo: 0.0,
            spatial_blend: 0.0,
            spread: 0.0,
            doppler_level: 1.0,
            min_distance: 1.0,
            max_distance: 500.0,
            rolloff_mode: spatial::LOGARITHMIC,
            priority: 128,
            flags: 0,
            rolloff_curve: Vec::new(),
            spatial_blend_curve: Vec::new(),
            spread_curve: Vec::new(),
        }
    }
}

impl SourceParams {
    /// # Safety
    /// The packet's curve pointers must be null or point at `curve_length` floats.
    pub unsafe fn from_packet(p: &AudioSourcePacket) -> Self {
        let curve = |ptr: *const f32| -> Vec<f32> {
            if ptr.is_null() || p.curve_length <= 0 {
                Vec::new()
            } else {
                unsafe { std::slice::from_raw_parts(ptr, p.curve_length as usize) }.to_vec()
            }
        };
        Self {
            clip: p.clip,
            group: p.group,
            position: p.position,
            velocity: p.velocity,
            volume: p.volume,
            pitch: p.pitch,
            pan_stereo: p.pan_stereo,
            spatial_blend: p.spatial_blend,
            spread: p.spread,
            doppler_level: p.doppler_level,
            min_distance: p.min_distance,
            max_distance: p.max_distance,
            rolloff_mode: p.rolloff_mode,
            priority: p.priority,
            flags: p.flags,
            rolloff_curve: curve(p.rolloff_curve),
            spatial_blend_curve: curve(p.spatial_blend_curve),
            spread_curve: curve(p.spread_curve),
        }
    }

    fn has(&self, flag: u32) -> bool {
        self.flags & flag != 0
    }
}

/// A streamed clip's data read ahead from its PCM reader. The voice's position counts frames
/// played since `origin`, the clip frame it started (or was sought) at.
#[derive(Debug, Default)]
struct StreamState {
    /// Interleaved frames from played frame `base` on.
    buffer: Vec<f32>,
    base: u64,
    /// The clip frame the next read starts at.
    cursor: usize,
    origin: usize,
}

impl StreamState {
    fn at(origin: usize) -> Self {
        Self {
            cursor: origin,
            origin,
            ..Self::default()
        }
    }
}

#[derive(Debug)]
struct Voice {
    clip: u64,
    /// In clip frames; for a streamed clip, frames played since it started.
    position: f64,
    start_at: u64,
    end_at: u64,
    paused: bool,
    volume_scale: f32,
    /// The gain matrix of the last block (ramped from).
    matrix: [f32; 4],
    fresh: bool,
    /// Unity starts a voice with a 64-sample linear fade (measured by the dry step probe).
    fade_frames: u32,
    fade_initial: [f32; 4],
    fade_matrix: [f32; 4],
    stream: Option<StreamState>,
}

impl Voice {
    fn new(clip: u64, position: f64, start_at: u64, volume_scale: f32) -> Self {
        Self {
            clip,
            position,
            start_at,
            end_at: u64::MAX,
            paused: false,
            volume_scale,
            matrix: [0.0; 4],
            fresh: true,
            fade_frames: 0,
            fade_initial: [0.0; 4],
            fade_matrix: [0.0; 4],
            stream: None,
        }
    }
}

/// Recent stereo output.
#[derive(Debug)]
struct Ring {
    data: Vec<f32>,
    write: usize,
}

impl Ring {
    fn new() -> Self {
        Self {
            data: vec![0.0; RING_FRAMES * 2],
            write: 0,
        }
    }

    fn push(&mut self, stereo: &[f32]) {
        for frame in stereo.as_chunks::<2>().0 {
            self.data[self.write * 2] = frame[0];
            self.data[self.write * 2 + 1] = frame[1];
            self.write = (self.write + 1) % RING_FRAMES;
        }
    }

    /// The latest `out.len()` samples of `channel`, oldest first.
    fn latest(&self, channel: usize, out: &mut [f32]) {
        let n = out.len().min(RING_FRAMES);
        let skip = out.len() - n;
        out[..skip].fill(0.0);
        for (i, value) in out[skip..].iter_mut().enumerate() {
            let frame = (self.write + RING_FRAMES - n + i) % RING_FRAMES;
            *value = self.data[frame * 2 + channel.min(1)];
        }
    }
}

#[derive(Debug, Default)]
pub struct Source {
    pub params: SourceParams,
    main: Option<Voice>,
    one_shots: Vec<Voice>,
    /// Where the next `Play` starts (`time` set while stopped).
    pending_position: f64,
    is_virtual: bool,
    output: Option<Ring>,
}

impl Source {
    fn active(&self) -> bool {
        self.main.as_ref().is_some_and(|v| !v.paused) || self.one_shots.iter().any(|v| !v.paused)
    }
}

/// A mixer group's effective level (its volume chain down to the master, mute and solo).
#[derive(Debug, Clone, Copy)]
pub struct Group {
    pub gain: f32,
    pub pitch: f32,
}

/// The configuration the mixer runs with.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct MixerConfig {
    pub sample_rate: u32,
    pub buffer_size: u32,
    /// `AudioSpeakerMode`.
    pub speaker_mode: i32,
    pub real_voices: usize,
    pub virtual_voices: usize,
    pub rolloff_scale: f32,
    pub doppler_factor: f32,
}

impl Default for MixerConfig {
    fn default() -> Self {
        Self {
            sample_rate: 48000,
            buffer_size: 1024,
            speaker_mode: 2,
            real_voices: 32,
            virtual_voices: 512,
            rolloff_scale: 1.0,
            doppler_factor: 1.0,
        }
    }
}

pub struct Mixer {
    pub config: MixerConfig,
    frames: u64,
    clips: HashMap<u64, Clip>,
    sources: HashMap<u64, Source>,
    groups: HashMap<u64, Group>,
    group_nodes: HashMap<u64, GroupNode>,
    group_order: Vec<u64>,
    capture_sinks: HashMap<u64, capture::Sink>,
    pub listener: Listener,
    pub listener_volume: f32,
    pub listener_paused: bool,
    pub has_listener: bool,
    pub listener_filter: bool,
    listener_output: Ring,
    callback: Option<Callback>,
    /// Nothing listens (the virtual device): voices only advance, unless their samples are
    /// asked for (filters, output data).
    pub discard_output: bool,
    listener_output_wanted: bool,
    mix: Vec<f32>,
    source_buffer: Vec<f32>,
}

fn frame_of(seconds: f64, rate: u32) -> u64 {
    (seconds.max(0.0) * rate as f64).round() as u64
}

impl Mixer {
    pub fn new(config: MixerConfig) -> Self {
        Self {
            config,
            frames: 0,
            clips: HashMap::new(),
            sources: HashMap::new(),
            groups: HashMap::new(),
            group_nodes: HashMap::new(),
            group_order: Vec::new(),
            capture_sinks: HashMap::new(),
            listener: Listener::default(),
            listener_volume: 1.0,
            listener_paused: false,
            has_listener: true,
            listener_filter: false,
            listener_output: Ring::new(),
            callback: None,
            discard_output: false,
            listener_output_wanted: false,
            mix: vec![0.0; BLOCK * 2],
            source_buffer: vec![0.0; BLOCK * 2],
        }
    }

    /// Frames rendered so far: the dsp clock.
    pub fn frames(&self) -> u64 {
        self.frames
    }

    pub fn dsp_time(&self) -> f64 {
        self.frames as f64 / self.config.sample_rate.max(1) as f64
    }

    /// Switches to another configuration (`AudioSettings.Reset`): every voice stops, the dsp
    /// clock keeps its time in seconds.
    pub fn validate_configuration(&self, config: &MixerConfig) -> anyhow::Result<()> {
        for node in self.group_nodes.values() {
            for effect in &node.spec.effects {
                effect.validate(config.sample_rate)?;
            }
        }
        Ok(())
    }

    pub fn reconfigure(&mut self, config: MixerConfig) -> anyhow::Result<()> {
        self.validate_configuration(&config)?;
        let seconds = self.dsp_time();
        self.config = config;
        self.frames = frame_of(seconds, config.sample_rate);
        for node in self.group_nodes.values_mut() {
            node.rendered = 0;
            node.suspended = false;
            node.last_active = self.frames;
            node.energy = 0.0;
            node.effects.clear();
            node.update(node.spec.clone(), config.sample_rate)?;
        }
        for source in self.sources.values_mut() {
            source.main = None;
            source.one_shots.clear();
            source.pending_position = 0.0;
        }
        Ok(())
    }

    pub fn set_callback(&mut self, callback: Option<Callback>) {
        self.callback = callback;
    }

    // ------------------------------------------------------------------ clips

    pub fn add_clip(&mut self, handle: u64, clip: Clip) {
        self.clips.insert(handle, clip);
    }

    /// The clips held and the bytes of their samples (for the memory trace).
    pub fn clip_memory(&self) -> (usize, usize) {
        let bytes = self
            .clips
            .values()
            .map(|c| c.samples.len() * std::mem::size_of::<f32>())
            .sum();
        (self.clips.len(), bytes)
    }

    pub fn clip(&self, handle: u64) -> Option<&Clip> {
        self.clips.get(&handle)
    }

    pub fn release_clip(&mut self, handle: u64) {
        self.clips.remove(&handle);
    }

    /// `GetData`/`SetData`: `data.len()` samples from frame `offset` on, wrapping around the end
    /// of the clip. False for a missing or streamed clip.
    pub fn clip_data(&mut self, handle: u64, offset: usize, data: &mut [f32], write: bool) -> bool {
        let Some(clip) = self.clips.get_mut(&handle) else {
            return false;
        };
        let total = clip.samples.len();
        if clip.stream || total == 0 {
            return false;
        }
        let start = (offset % clip.frames.max(1)) * clip.channels;
        for (i, value) in data.iter_mut().enumerate() {
            let at = (start + i) % total;
            if write {
                clip.samples[at] = *value;
            } else {
                *value = clip.samples[at];
            }
        }
        true
    }

    // ------------------------------------------------------------------ sources

    pub fn update_source(&mut self, handle: u64, params: SourceParams) {
        let source = self.sources.entry(handle).or_default();
        if params.has(source_flags::KEEP_OUTPUT) && source.output.is_none() {
            source.output = Some(Ring::new());
        }
        source.params = params;
    }

    pub fn release_source(&mut self, handle: u64) {
        self.sources.remove(&handle);
    }

    pub fn source(&self, handle: u64) -> Option<&Source> {
        self.sources.get(&handle)
    }

    pub fn update_group(&mut self, handle: u64, gain: f32, pitch: f32) {
        self.groups.insert(handle, Group { gain, pitch });
    }

    /// Updates routing and parameters without discarding the filters' delay lines.
    pub fn configure_group(&mut self, spec: GroupSpec) -> anyhow::Result<()> {
        anyhow::ensure!(spec.suspend_threshold.is_finite() && spec.suspend_threshold >= 0.0,
            "invalid AudioMixer suspension threshold");
        for effect in &spec.effects {
            effect.validate(self.config.sample_rate)?;
            anyhow::ensure!(
                matches!(effect.kind.as_str(), "Attenuation" | "Lowpass" | "Highpass" | "Echo")
                    || effect.bypass
                    || spec.bypass,
                "unsupported AudioMixer effect '{}'",
                effect.kind
            );
        }
        let mut parent = spec.parent;
        for _ in 0..=self.group_nodes.len() {
            anyhow::ensure!(parent != spec.handle, "cyclic AudioMixer group routing");
            parent = self.group_nodes.get(&parent).map_or(0, |n| n.spec.parent);
            if parent == 0 {
                break;
            }
        }
        let handle = spec.handle;
        if self
            .group_nodes
            .get(&handle)
            .is_some_and(|node| node.spec == spec)
        {
            return Ok(());
        }
        let node = self.group_nodes.entry(handle).or_insert_with(|| GroupNode {
            spec: spec.clone(),
            rendered: 0,
            effects: Vec::new(),
            buffer: vec![0.0; BLOCK * 2],
            suspended: false,
            last_active: self.frames,
            energy: 0.0,
        });
        node.update(spec, self.config.sample_rate)?;
        // Children mix into parents. Stable order also makes independent sibling sums deterministic.
        let depth = |h: u64| {
            let mut d = 0;
            let mut p = h;
            while let Some(node) = self.group_nodes.get(&p) {
                d += 1;
                p = node.spec.parent;
                if p == 0 || d > self.group_nodes.len() {
                    break;
                }
            }
            d
        };
        self.group_order = self.group_nodes.keys().copied().collect();
        self.group_order
            .sort_by_key(|h| (std::cmp::Reverse(depth(*h)), *h));
        Ok(())
    }

    /// Unity 6000.0.71f1 observes suspension on audio updates, using the rendered DSP clock.
    /// Burst renders and no-render updates in tests/projects/mixersuspend constrain both clocks.
    pub fn update_suspension(&mut self) {
        let active = self.active_mixer_roots();
        for (handle, node) in &mut self.group_nodes {
            if node.spec.mixer != *handle { continue; }
            if active.contains(handle) || !node.spec.enable_suspend {
                node.suspended = false;
                node.last_active = self.frames;
            } else if self.frames.saturating_sub(node.last_active) > self.config.sample_rate as u64 {
                node.suspended = true;
            }
        }
    }

    fn active_mixer_roots(&self) -> std::collections::HashSet<u64> {
        let mut active = std::collections::HashSet::new();
        for source in self.sources.values().filter(|s| s.active()) {
            let mut handle = source.params.group;
            for _ in 0..self.group_nodes.len() {
                let Some(group) = self.group_nodes.get(&handle) else { break; };
                let root = group.spec.mixer;
                if root == 0 || !active.insert(root) { break; }
                let Some(master) = self.group_nodes.get(&root) else { break; };
                handle = master.spec.parent;
            }
        }
        active
    }

    pub fn command(&mut self, handle: u64, cmd: &AudioSourceCommand) {
        let now = self.frames;
        let rate = self.config.sample_rate;
        let callback = self.callback;
        let Self { sources, clips, .. } = self;
        let source = sources.entry(handle).or_default();
        let start_frame = |time: f64| -> u64 {
            if time < 0.0 {
                now + frame_of(-time, rate)
            } else {
                frame_of(time, rate).max(now)
            }
        };
        match cmd.kind {
            command::PLAY => {
                // Play on a paused source resumes it (as UnPause does).
                if let Some(main) = &mut source.main
                    && main.paused
                {
                    main.paused = false;
                    source.one_shots.iter_mut().for_each(|v| v.paused = false);
                    return;
                }
                let clip_handle = source.params.clip;
                let Some(clip) = clips.get(&clip_handle) else {
                    source.main = None;
                    return;
                };
                let mut position = source.pending_position;
                source.pending_position = 0.0;
                if position == 0.0 && source.params.pitch < 0.0 && !clip.stream {
                    position = clip.frames as f64;
                }
                let mut voice = Voice::new(clip_handle, position, start_frame(cmd.time), 1.0);
                if clip.stream {
                    voice.position = 0.0;
                    voice.stream = Some(StreamState::at((position as usize).min(clip.frames)));
                    if let Some(cb) = callback {
                        cb(
                            callback::PCM_SET_POSITION,
                            clip_handle,
                            std::ptr::null_mut(),
                            0,
                            0,
                            position as i64,
                        );
                    }
                }
                source.main = Some(voice);
            }
            command::STOP => {
                source.main = None;
                source.one_shots.clear();
                source.pending_position = 0.0;
            }
            command::STOP_MAIN => {
                source.main = None;
                source.pending_position = 0.0;
            }
            command::PAUSE => {
                if let Some(main) = &mut source.main {
                    main.paused = true;
                }
                source.one_shots.iter_mut().for_each(|v| v.paused = true);
            }
            command::UNPAUSE => {
                if let Some(main) = &mut source.main {
                    main.paused = false;
                }
                source.one_shots.iter_mut().for_each(|v| v.paused = false);
            }
            command::PLAY_ONE_SHOT => {
                let Some(clip) = clips.get(&cmd.clip) else {
                    return;
                };
                let position = if source.params.pitch < 0.0 && !clip.stream {
                    clip.frames as f64
                } else {
                    0.0
                };
                let mut voice = Voice::new(cmd.clip, position, now, cmd.volume_scale);
                if clip.stream {
                    voice.stream = Some(StreamState::default());
                }
                source.one_shots.push(voice);
            }
            command::SEEK => {
                let clip_handle = source.main.as_ref().map_or(source.params.clip, |v| v.clip);
                let frames = clips.get(&clip_handle).map_or(0, |c| c.frames) as f64;
                let position = cmd.time.clamp(0.0, frames);
                match &mut source.main {
                    Some(main) => {
                        if let Some(stream) = &mut main.stream {
                            *stream = StreamState::at(position as usize);
                            main.position = 0.0;
                            if let Some(cb) = callback {
                                cb(
                                    callback::PCM_SET_POSITION,
                                    clip_handle,
                                    std::ptr::null_mut(),
                                    0,
                                    0,
                                    position as i64,
                                );
                            }
                        } else {
                            main.position = position;
                        }
                    }
                    None => source.pending_position = position,
                }
            }
            command::SET_SCHEDULED_START => {
                if let Some(main) = &mut source.main
                    && now < main.start_at
                {
                    main.start_at = start_frame(cmd.time);
                }
            }
            command::SET_SCHEDULED_END => {
                if let Some(main) = &mut source.main {
                    main.end_at = frame_of(cmd.time, rate);
                }
            }
            _ => {}
        }
    }

    pub fn source_state(&self, handle: u64) -> AudioSourceState {
        let Some(source) = self.sources.get(&handle) else {
            return AudioSourceState::default();
        };
        let mut state = AudioSourceState {
            main: voice_state::NONE,
            one_shots: source.one_shots.len() as i32,
            is_virtual: source.is_virtual as i32,
            _pad: 0,
            position: source.pending_position,
        };
        if let Some(main) = &source.main {
            state.main = if main.paused {
                voice_state::PAUSED
            } else if self.frames < main.start_at {
                voice_state::SCHEDULED
            } else {
                voice_state::PLAYING
            };
            state.position = match (&main.stream, self.clips.get(&main.clip)) {
                (Some(stream), Some(clip)) if clip.frames > 0 => {
                    (stream.origin as f64 + main.position) % clip.frames as f64
                }
                _ => main.position,
            };
        }
        state
    }

    /// Sources with a voice that is not paused.
    pub fn playing_sources(&self) -> usize {
        self.sources.values().filter(|s| s.active()).count()
    }

    // ------------------------------------------------------------------ output data

    /// The latest output of a source (0: the listener's mix) for `channel`: samples, or with
    /// `window >= 0` (`FFTWindow`) the spectrum. False when the source is unknown.
    pub fn output_data(
        &mut self,
        handle: u64,
        channel: usize,
        out: &mut [f32],
        window: i32,
    ) -> bool {
        let ring = if handle == 0 {
            self.listener_output_wanted = true;
            &self.listener_output
        } else {
            let Some(source) = self.sources.get_mut(&handle) else {
                return false;
            };
            source.output.get_or_insert_with(Ring::new)
        };
        if window < 0 {
            ring.latest(channel, out);
        } else {
            let mut signal = vec![0.0; out.len() * 2];
            ring.latest(channel, &mut signal);
            fft::spectrum(&signal, window, out);
        }
        true
    }

    // ------------------------------------------------------------------ rendering

    pub fn capture(
        &mut self,
        length: usize,
        sinks: Vec<capture::SinkRequest>,
    ) -> anyhow::Result<capture::Output> {
        anyhow::ensure!(
            length <= 16_777_216,
            "invalid AudioRenderer stereo buffer length"
        );
        for request in &sinks {
            anyhow::ensure!(
                request.length == length,
                "AudioRenderer sink buffer length must match output"
            );
            anyhow::ensure!(
                self.group_nodes.contains_key(&request.handle),
                "unknown AudioRenderer mixer group"
            );
        }
        for request in sinks {
            self.capture_sinks.insert(
                request.handle,
                capture::Sink {
                    request,
                    samples: Vec::with_capacity(length),
                },
            );
        }
        let discarded = self.discard_output;
        self.discard_output = false;
        // Native AudioRenderer advances and writes whole DSP buffers; any partial suffix is left
        // untouched in the caller's NativeArray, including buffers shorter than one DSP block.
        let block_samples = self.config.buffer_size.max(1) as usize * 2;
        let mut samples = vec![0.0; length / block_samples * block_samples];
        self.render(&mut samples, 2);
        self.discard_output = discarded;
        for value in &mut samples {
            *value = capture::pcm16(*value);
        }
        let mut sinks: Vec<_> = self
            .capture_sinks
            .drain()
            .map(|(handle, sink)| capture::SinkOutput {
                handle,
                samples: sink.samples,
            })
            .collect();
        sinks.sort_by_key(|s| s.handle);
        Ok(capture::Output {
            ok: true,
            samples,
            sinks,
        })
    }

    /// Renders `out.len() / channels` frames of interleaved output.
    pub fn render(&mut self, out: &mut [f32], channels: usize) {
        let channels = channels.max(1);
        let frames = out.len() / channels;
        let mut done = 0;
        while done < frames {
            let n = (frames - done).min(BLOCK);
            self.render_block(n);
            let mono = self.config.speaker_mode == 1;
            for f in 0..n {
                let (l, r) = (self.mix[f * 2], self.mix[f * 2 + 1]);
                let frame = &mut out[(done + f) * channels..(done + f + 1) * channels];
                if channels == 1 || mono {
                    let m = (l + r) * 0.5;
                    frame.fill(0.0);
                    frame[0] = m;
                    if channels > 1 {
                        frame[1] = m;
                    }
                } else {
                    frame[0] = l;
                    frame[1] = r;
                    frame[2..].fill(0.0);
                }
            }
            done += n;
        }
    }

    /// Advances the clock by `frames` without an output device.
    pub fn advance(&mut self, frames: usize) {
        let mut done = 0;
        while done < frames {
            let n = (frames - done).min(BLOCK);
            self.render_block(n);
            done += n;
        }
    }

    fn render_block(&mut self, n: usize) {
        let t0 = self.frames;
        let out_rate = self.config.sample_rate.max(1);
        let callback = self.callback;
        let listener = self.listener;
        let listener_volume = self.listener_volume;
        let listener_paused = self.listener_paused;
        let has_listener = self.has_listener;
        let config = self.config;
        // New voices wake immediately, even when no main-thread update intervenes. Waking
        // preserves effect history and the input fade; parameter submissions alone do not wake.
        for root in self.active_mixer_roots() {
            if let Some(node) = self.group_nodes.get_mut(&root) && node.suspended {
                node.suspended = false;
                node.last_active = self.frames;
            }
        }
        let need_mix = !self.discard_output
            || self.listener_filter
            || self.listener_output_wanted
            || !self.group_nodes.is_empty();
        self.mix[..n * 2].fill(0.0);
        for node in self.group_nodes.values_mut() {
            node.buffer[..n * 2].fill(0.0);
        }

        // Spatialize, then decide which voices are real: the most important `real_voices`.
        let mut playing: Vec<(u64, i32, f32)> = Vec::new();
        let mut spatial: HashMap<u64, spatial::Spatialized> = HashMap::new();
        for (&handle, source) in &self.sources {
            let p = &source.params;
            let s = spatial::spatialize(
                &SourceSpatial {
                    position: p.position,
                    velocity: p.velocity,
                    pan_stereo: p.pan_stereo,
                    spatial_blend: p.spatial_blend,
                    spread: p.spread,
                    doppler_level: p.doppler_level,
                    min_distance: p.min_distance,
                    max_distance: p.max_distance,
                    rolloff_mode: p.rolloff_mode,
                    rolloff_curve: (!p.rolloff_curve.is_empty()).then_some(&p.rolloff_curve[..]),
                    spatial_blend_curve: (!p.spatial_blend_curve.is_empty())
                        .then_some(&p.spatial_blend_curve[..]),
                    spread_curve: (!p.spread_curve.is_empty()).then_some(&p.spread_curve[..]),
                },
                &listener,
                config.rolloff_scale,
                config.doppler_factor,
            );
            if source.active() {
                playing.push((handle, p.priority, s.audibility * p.volume));
            }
            spatial.insert(handle, s);
        }
        let mut real: std::collections::HashSet<u64> = playing.iter().map(|p| p.0).collect();
        if playing.len() > config.real_voices {
            playing.sort_by(|a, b| a.1.cmp(&b.1).then(b.2.total_cmp(&a.2)).then(a.0.cmp(&b.0)));
            real = playing
                .iter()
                .take(config.real_voices)
                .map(|p| p.0)
                .collect();
        }

        let Self {
            sources,
            clips,
            groups,
            group_nodes,
            mix,
            source_buffer,
            ..
        } = self;
        for (&handle, source) in sources.iter_mut() {
            source.is_virtual = source.active() && !real.contains(&handle);
            let flags = source.params.flags;
            let filter = flags & source_flags::FILTER != 0;
            let has_voices = source.main.is_some() || !source.one_shots.is_empty();
            // A filter-only source (no clip) runs its filter all the time.
            let filter_only = filter && source.params.clip == 0;
            if !has_voices && !filter_only {
                continue;
            }
            if listener_paused && flags & source_flags::IGNORE_LISTENER_PAUSE == 0 {
                continue;
            }
            let group = match source.params.group {
                0 => Group {
                    gain: 1.0,
                    pitch: 1.0,
                },
                g => groups.get(&g).copied().unwrap_or(Group {
                    gain: 1.0,
                    pitch: 1.0,
                }),
            };
            let s = spatial[&handle];
            let routed = group_nodes.contains_key(&source.params.group);
            let mut volume = source.params.volume.max(0.0) * if routed { 1.0 } else { group.gain };
            if flags & source_flags::MUTE != 0 || !has_listener {
                volume = 0.0;
            }
            if flags & source_flags::IGNORE_LISTENER_VOLUME == 0 {
                volume *= listener_volume;
            }
            let pitch = source.params.pitch as f64 * group.pitch as f64 * s.pitch as f64;
            let wants_samples = need_mix
                || filter
                || source.output.is_some()
                || flags & source_flags::KEEP_OUTPUT != 0;
            let audible = wants_samples && !source.is_virtual;
            let buffer = &mut source_buffer[..n * 2];
            buffer.fill(0.0);
            let target = s.matrix.map(|g| g * volume);
            let looping = flags & source_flags::LOOP != 0;
            if let Some(main) = &mut source.main {
                let finished = render_voice(
                    main,
                    clips.get(&main.clip),
                    n,
                    t0,
                    pitch,
                    out_rate,
                    target,
                    (source.params.spatial_blend <= 0.0
                        && source
                            .params
                            .spatial_blend_curve
                            .iter()
                            .all(|value| *value <= 0.0))
                    .then_some(source.params.pan_stereo),
                    looping,
                    audible.then_some(&mut *buffer),
                    callback,
                );
                if finished {
                    source.main = None;
                    source.pending_position = 0.0;
                }
            }
            source.one_shots.retain_mut(|voice| {
                !render_voice(
                    voice,
                    clips.get(&voice.clip),
                    n,
                    t0,
                    pitch,
                    out_rate,
                    target,
                    (source.params.spatial_blend <= 0.0
                        && source
                            .params
                            .spatial_blend_curve
                            .iter()
                            .all(|value| *value <= 0.0))
                    .then_some(source.params.pan_stereo),
                    false,
                    audible.then_some(&mut *buffer),
                    callback,
                )
            });
            if filter && let Some(cb) = callback {
                cb(
                    callback::SOURCE_FILTER,
                    handle,
                    buffer.as_mut_ptr(),
                    n as i32,
                    2,
                    0,
                );
            }
            if let Some(ring) = &mut source.output {
                ring.push(buffer);
            }
            if need_mix || filter {
                let target = group_nodes
                    .get_mut(&source.params.group)
                    .map_or(&mut mix[..n * 2], |node| &mut node.buffer[..n * 2]);
                for (m, b) in target.iter_mut().zip(buffer.iter()) {
                    *m += *b;
                }
            }
        }
        for &handle in &self.group_order {
            let root = self.group_nodes[&handle].spec.mixer;
            if self.group_nodes.get(&root).is_some_and(|node| node.suspended) {
                // A suspended graph does not write registered group sinks. The caller retains
                // its previous block, while the device stream and listener receive silence.
                continue;
            }
            let node = self.group_nodes.get_mut(&handle).unwrap();
            // Echo retains the measured group input fade even when bypassed.
            let active = node.effects.iter().any(|e| e.spec.kind == "Echo")
                || (!node.spec.bypass
                    && node.effects.iter().any(|e| {
                        !e.spec.bypass && matches!(e.spec.kind.as_str(), "Lowpass" | "Highpass")
                    }));
            if active {
                for (frame, stereo) in node.buffer[..n * 2].chunks_exact_mut(2).enumerate() {
                    let gain = ((node.rendered + frame) as f32 / config.buffer_size.max(1) as f32)
                        .min(1.0);
                    for sample in stereo {
                        *sample *= gain;
                    }
                }
            }
            node.rendered += n;
            for effect in &mut node.effects {
                effect.process(&mut node.buffer[..n * 2], node.spec.bypass);
            }
            if node.spec.muted {
                node.buffer[..n * 2].fill(0.0);
            }
            if root == handle {
                for &sample in &node.buffer[..n * 2] {
                    node.energy += sample as f64 * sample as f64;
                }
                let window = config.buffer_size.max(1) as u64;
                if (t0 + n as u64) % window == 0 {
                    if node.energy / (window * 2) as f64
                        >= (node.spec.suspend_threshold as f64).powi(2) {
                        node.last_active = t0 + n as u64 - window;
                    }
                    node.energy = 0.0;
                }
            }
            if node.spec.observe
                && let Some(cb) = callback
            {
                cb(
                    callback::GROUP_OUTPUT,
                    handle,
                    node.buffer.as_mut_ptr(),
                    n as i32,
                    2,
                    0,
                );
            }
            let parent = node.spec.parent;
            if let Some(sink) = self.capture_sinks.get_mut(&handle) {
                sink.samples.extend_from_slice(&node.buffer[..n * 2]);
                if sink.request.exclude_from_mix {
                    continue;
                }
            }
            // Remove the buffer temporarily to borrow the parent without copying or allocating.
            let buffer = std::mem::take(&mut node.buffer);
            let target = self
                .group_nodes
                .get_mut(&parent)
                .map_or(&mut self.mix[..n * 2], |node| &mut node.buffer[..n * 2]);
            for (m, b) in target.iter_mut().zip(buffer.iter()) {
                *m += *b;
            }
            self.group_nodes.get_mut(&handle).unwrap().buffer = buffer;
        }
        if self.listener_filter
            && has_listener
            && let Some(cb) = callback
        {
            cb(
                callback::LISTENER_FILTER,
                0,
                self.mix.as_mut_ptr(),
                n as i32,
                2,
                0,
            );
        }
        self.listener_output.push(&self.mix[..n * 2]);
        self.frames += n as u64;
    }
}

impl StreamState {
    /// Reads ahead until cumulative frame `upto` is buffered.
    fn ensure(
        &mut self,
        upto: u64,
        clip: &Clip,
        handle: u64,
        looping: bool,
        callback: Option<Callback>,
    ) {
        let channels = clip.channels.max(1);
        while self.base + (self.buffer.len() / channels) as u64 <= upto {
            if self.cursor >= clip.frames {
                if looping {
                    self.cursor = 0;
                    if let Some(cb) = callback {
                        cb(
                            callback::PCM_SET_POSITION,
                            handle,
                            std::ptr::null_mut(),
                            0,
                            0,
                            0,
                        );
                    }
                } else {
                    let len = self.buffer.len();
                    self.buffer.resize(len + STREAM_CHUNK * channels, 0.0);
                    continue;
                }
            }
            let chunk = STREAM_CHUNK.min(clip.frames - self.cursor).max(1);
            let len = self.buffer.len();
            self.buffer.resize(len + chunk * channels, 0.0);
            if let Some(cb) = callback {
                cb(
                    callback::PCM_READ,
                    handle,
                    self.buffer[len..].as_mut_ptr(),
                    chunk as i32,
                    channels as i32,
                    self.cursor as i64,
                );
            }
            self.cursor += chunk;
        }
    }

    fn frame(&self, i: u64, channels: usize) -> (f32, f32) {
        let Some(rel) = i.checked_sub(self.base) else {
            return (0.0, 0.0);
        };
        let at = rel as usize * channels;
        match self.buffer.get(at..at + channels) {
            Some(s) if channels == 1 => (s[0], s[0]),
            Some(s) => (s[0], s[1]),
            None => (0.0, 0.0),
        }
    }

    fn trim(&mut self, position: f64, channels: usize) {
        let keep_from = position.floor().max(0.0) as u64;
        if keep_from > self.base + STREAM_CHUNK as u64 * 2 {
            let drop = (keep_from - self.base) as usize;
            self.buffer
                .drain(..(drop * channels).min(self.buffer.len()));
            self.base += drop as u64;
        }
    }
}

/// Mixes `n` frames of a voice into `out` (stereo) when given, and advances it either way.
/// Returns true when the voice has finished.
#[allow(clippy::too_many_arguments)]
fn render_voice(
    voice: &mut Voice,
    clip: Option<&Clip>,
    n: usize,
    t0: u64,
    pitch: f64,
    out_rate: u32,
    mut target: [f32; 4],
    mono_pan: Option<f32>,
    looping: bool,
    mut out: Option<&mut [f32]>,
    callback: Option<Callback>,
) -> bool {
    let Some(clip) = clip else {
        return true;
    };
    if voice.paused {
        return false;
    }
    if clip.frames == 0 {
        return true;
    }
    if clip.channels == 1
        && let Some(pan) = mono_pan
    {
        let pan = pan.clamp(-1.0, 1.0);
        let linear = spatial::balance(pan);
        let angle = (pan + 1.0) * std::f32::consts::FRAC_PI_4;
        let equal_power = [angle.cos(), angle.sin()];
        for channel in 0..2 {
            let scale = if linear[channel] > 0.0 {
                equal_power[channel] / linear[channel]
            } else {
                0.0
            };
            target[channel * 2] *= scale;
            target[channel * 2 + 1] *= scale;
        }
    }
    if voice.fresh {
        voice.matrix = target;
        voice.fade_initial = target;
        voice.fresh = false;
    }
    let from = voice.matrix;
    voice.matrix = target;
    let frames = clip.frames as f64;
    let step = pitch * clip.rate as f64 / out_rate as f64;
    let inv_n = 1.0 / n as f32;
    for f in 0..n {
        let dsp = t0 + f as u64;
        if dsp < voice.start_at {
            continue;
        }
        if dsp >= voice.end_at {
            return true;
        }
        let (l, r) = if let Some(stream) = &mut voice.stream {
            let i = voice.position.floor();
            let frac = (voice.position - i) as f32;
            let i = i as u64;
            stream.ensure(i + 1, clip, voice.clip, looping, callback);
            let (l0, r0) = stream.frame(i, clip.channels);
            let (l1, r1) = stream.frame(i + 1, clip.channels);
            (l0 + (l1 - l0) * frac, r0 + (r1 - r0) * frac)
        } else {
            let i = voice.position.floor();
            let frac = (voice.position - i) as f32;
            let i0 = i as i64;
            let fetch = |i: i64| -> (f32, f32) {
                if i >= 0 && (i as usize) < clip.frames {
                    clip.frame(i as usize)
                } else if looping {
                    clip.frame(i.rem_euclid(clip.frames as i64) as usize)
                } else {
                    (0.0, 0.0)
                }
            };
            let (l0, r0) = fetch(i0);
            let (l1, r1) = fetch(i0 + 1);
            (l0 + (l1 - l0) * frac, r0 + (r1 - r0) * frac)
        };
        if let Some(out) = out.as_deref_mut() {
            let t = (f + 1) as f32 * inv_n;
            let mut m = [
                from[0] + (target[0] - from[0]) * t,
                from[1] + (target[1] - from[1]) * t,
                from[2] + (target[2] - from[2]) * t,
                from[3] + (target[3] - from[3]) * t,
            ];
            if voice.fade_frames < 64 {
                for (i, gain) in m.iter_mut().enumerate() {
                    *gain = if *gain == voice.fade_initial[i] {
                        voice.fade_matrix[i]
                    } else if voice.fade_initial[i] != 0.0 {
                        (*gain / voice.fade_initial[i]) * voice.fade_matrix[i]
                    } else {
                        *gain * voice.fade_frames as f32 / 64.0
                    };
                }
            }
            let v = voice.volume_scale;
            out[f * 2] += (m[0] * l + m[1] * r) * v;
            out[f * 2 + 1] += (m[2] * l + m[3] * r) * v;
        }
        if voice.fade_frames < 64 {
            // Native accumulates the gain-matrix step, rather than multiplying by age / 64.
            // Its float rounding affects the IIR state and the later integer capture boundary.
            for i in 0..4 {
                voice.fade_matrix[i] += voice.fade_initial[i] / 64.0;
            }
        }
        voice.fade_frames = (voice.fade_frames + 1).min(64);
        if let Some(stream) = &mut voice.stream {
            voice.position += step.abs();
            stream.trim(voice.position, clip.channels);
            // A non-looping stream ends at the end of the clip.
            if !looping && voice.position + stream.origin as f64 >= frames {
                return true;
            }
        } else {
            voice.position += step;
            if voice.position >= frames || voice.position < 0.0 {
                if !looping {
                    return true;
                }
                voice.position = voice.position.rem_euclid(frames);
            }
        }
    }
    false
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::audio::protocol::AudioSourceCommand;

    fn mono_gain(age: usize) -> f32 {
        if age >= 64 {
            return std::f32::consts::FRAC_1_SQRT_2;
        }
        (0..age).fold(0.0, |gain, _| gain + std::f32::consts::FRAC_1_SQRT_2 / 64.0)
    }

    fn mono_sample(value: f32, age: usize) -> f32 {
        value * mono_gain(age)
    }

    fn mixer() -> Mixer {
        Mixer::new(MixerConfig {
            sample_rate: 1000,
            ..MixerConfig::default()
        })
    }

    fn play(m: &mut Mixer, handle: u64, kind: i32, time: f64) {
        m.command(
            handle,
            &AudioSourceCommand {
                kind,
                time,
                ..Default::default()
            },
        );
    }

    fn source_with(m: &mut Mixer, handle: u64, clip: u64, f: impl FnOnce(&mut SourceParams)) {
        let mut params = SourceParams {
            clip,
            ..SourceParams::default()
        };
        f(&mut params);
        m.update_source(handle, params);
    }

    fn ramp_clip(frames: usize, rate: u32) -> Clip {
        Clip::new(
            1,
            rate,
            (0..frames).map(|i| i as f32 / frames as f32).collect(),
        )
    }

    #[test]
    fn a_voice_plays_its_clip_and_ends() {
        let mut m = mixer();
        m.add_clip(1, ramp_clip(100, 1000));
        source_with(&mut m, 7, 1, |_| {});
        play(&mut m, 7, command::PLAY, 0.0);
        assert_eq!(m.source_state(7).main, voice_state::PLAYING);
        let mut out = vec![0.0; 60 * 2];
        m.render(&mut out, 2);
        // Equal-power mono center and the independently measured startup fade.
        assert_eq!(out[10 * 2], mono_sample(0.1, 10));
        assert_eq!(out[10 * 2 + 1], mono_sample(0.1, 10));
        assert_eq!(m.source_state(7).position, 60.0);
        m.render(&mut out, 2);
        assert_eq!(m.source_state(7).main, voice_state::NONE);
        assert_eq!(m.source_state(7).position, 0.0);
        // The rest after the end is silent.
        assert_eq!(out[50 * 2], 0.0);
    }

    #[test]
    fn looping_wraps_and_pitch_scales_the_rate() {
        let mut m = mixer();
        m.add_clip(1, ramp_clip(100, 1000));
        source_with(&mut m, 7, 1, |p| {
            p.flags = source_flags::LOOP;
            p.pitch = 2.0;
        });
        play(&mut m, 7, command::PLAY, 0.0);
        m.advance(70);
        let state = m.source_state(7);
        assert_eq!(state.main, voice_state::PLAYING);
        assert_eq!(state.position, 40.0);
    }

    #[test]
    fn a_clip_at_another_rate_is_resampled() {
        let mut m = mixer();
        m.add_clip(1, ramp_clip(100, 500));
        source_with(&mut m, 7, 1, |_| {});
        play(&mut m, 7, command::PLAY, 0.0);
        let mut out = vec![0.0; 3 * 2];
        m.render(&mut out, 2);
        // Half a source frame per output frame, interpolated.
        assert!((out[2] - 0.005 * mono_gain(1)).abs() < 1e-6);
        assert_eq!(m.source_state(7).position, 1.5);
        // 100 frames at 500 Hz last 200 output frames.
        m.advance(196);
        assert_eq!(m.source_state(7).main, voice_state::PLAYING);
        m.advance(1);
        assert_eq!(m.source_state(7).main, voice_state::NONE);
    }

    #[test]
    fn negative_pitch_plays_backwards_from_the_end() {
        let mut m = mixer();
        m.add_clip(1, ramp_clip(100, 1000));
        source_with(&mut m, 7, 1, |p| p.pitch = -1.0);
        play(&mut m, 7, command::PLAY, 0.0);
        let mut out = vec![0.0; 10 * 2];
        m.render(&mut out, 2);
        assert_eq!(m.source_state(7).position, 90.0);
        assert!(
            (out[5 * 2] - 0.95 * mono_gain(5)).abs() < 1e-6,
            "{}",
            out[5 * 2]
        );
        m.advance(91);
        assert_eq!(m.source_state(7).main, voice_state::NONE);
    }

    #[test]
    fn scheduled_play_starts_on_its_frame() {
        let mut m = mixer();
        m.add_clip(1, Clip::new(1, 1000, vec![1.0; 50]));
        source_with(&mut m, 7, 1, |_| {});
        play(&mut m, 7, command::PLAY, 0.105);
        assert_eq!(m.source_state(7).main, voice_state::SCHEDULED);
        let mut out = vec![0.0; 110 * 2];
        m.render(&mut out, 2);
        assert_eq!(out[104 * 2], 0.0);
        assert_eq!(out[105 * 2], 0.0);
        assert_eq!(out[106 * 2], mono_gain(1));
        assert_eq!(m.source_state(7).main, voice_state::PLAYING);
        assert_eq!(m.source_state(7).position, 5.0);
        // A scheduled end stops it on its frame.
        play(&mut m, 7, command::SET_SCHEDULED_END, 0.120);
        m.render(&mut out[..40], 2);
        assert_eq!(out[9 * 2], mono_gain(14));
        assert_eq!(out[10 * 2], 0.0);
        assert_eq!(m.source_state(7).main, voice_state::NONE);
    }

    #[test]
    fn delayed_play_counts_from_now() {
        let mut m = mixer();
        m.add_clip(1, Clip::new(1, 1000, vec![1.0; 50]));
        source_with(&mut m, 7, 1, |_| {});
        m.advance(30);
        play(&mut m, 7, command::PLAY, -0.02);
        m.advance(19);
        assert_eq!(m.source_state(7).main, voice_state::SCHEDULED);
        m.advance(1);
        assert_eq!(m.source_state(7).main, voice_state::PLAYING);
    }

    #[test]
    fn pause_holds_the_position_and_play_resumes() {
        let mut m = mixer();
        m.add_clip(1, ramp_clip(100, 1000));
        source_with(&mut m, 7, 1, |_| {});
        play(&mut m, 7, command::PLAY, 0.0);
        m.advance(20);
        play(&mut m, 7, command::PAUSE, 0.0);
        m.advance(20);
        assert_eq!(m.source_state(7).main, voice_state::PAUSED);
        assert_eq!(m.source_state(7).position, 20.0);
        play(&mut m, 7, command::PLAY, 0.0);
        m.advance(10);
        assert_eq!(m.source_state(7).position, 30.0);
        // Play while playing restarts.
        play(&mut m, 7, command::PLAY, 0.0);
        assert_eq!(m.source_state(7).position, 0.0);
    }

    #[test]
    fn seeking_while_stopped_sets_where_play_starts() {
        let mut m = mixer();
        m.add_clip(1, ramp_clip(100, 1000));
        source_with(&mut m, 7, 1, |_| {});
        play(&mut m, 7, command::SEEK, 40.0);
        assert_eq!(m.source_state(7).position, 40.0);
        play(&mut m, 7, command::PLAY, 0.0);
        m.advance(10);
        assert_eq!(m.source_state(7).position, 50.0);
        play(&mut m, 7, command::STOP, 0.0);
        assert_eq!(m.source_state(7).position, 0.0);
    }

    #[test]
    fn one_shots_mix_on_top_and_end_by_themselves() {
        let mut m = mixer();
        m.add_clip(1, Clip::new(1, 1000, vec![0.25; 100]));
        m.add_clip(2, Clip::new(1, 1000, vec![0.5; 10]));
        source_with(&mut m, 7, 1, |_| {});
        play(&mut m, 7, command::PLAY, 0.0);
        m.command(
            7,
            &AudioSourceCommand {
                kind: command::PLAY_ONE_SHOT,
                clip: 2,
                volume_scale: 0.5,
                ..Default::default()
            },
        );
        let mut out = vec![0.0; 20 * 2];
        m.render(&mut out, 2);
        assert_eq!(out[5 * 2], 0.5 * mono_gain(5));
        assert_eq!(out[15 * 2], 0.25 * mono_gain(15));
        assert_eq!(m.source_state(7).one_shots, 0);
    }

    #[test]
    fn volume_mute_groups_and_listener_volume_scale_the_output() {
        let mut m = mixer();
        m.add_clip(1, Clip::new(1, 1000, vec![1.0; 100]));
        source_with(&mut m, 7, 1, |p| {
            p.volume = 0.5;
            p.group = 3;
        });
        m.update_group(3, 0.5, 1.0);
        m.listener_volume = 0.5;
        play(&mut m, 7, command::PLAY, 0.0);
        let mut out = vec![0.0; 4];
        m.render(&mut out, 2);
        assert_eq!(out[2], 0.125 * mono_gain(1));
        source_with(&mut m, 7, 1, |p| {
            p.volume = 0.5;
            p.group = 3;
            p.flags = source_flags::IGNORE_LISTENER_VOLUME;
        });
        m.render(&mut out, 2);
        assert_eq!(out[2], 0.25 * mono_gain(3));
        source_with(&mut m, 7, 1, |p| p.flags = source_flags::MUTE);
        m.render(&mut out, 2);
        assert_eq!(out[2], 0.0);
        // Muted sources keep playing.
        assert_eq!(m.source_state(7).position, 6.0);
    }

    #[test]
    fn listener_pause_holds_sources_unless_they_ignore_it() {
        let mut m = mixer();
        m.add_clip(1, ramp_clip(100, 1000));
        source_with(&mut m, 7, 1, |_| {});
        source_with(&mut m, 8, 1, |p| {
            p.flags = source_flags::IGNORE_LISTENER_PAUSE
        });
        play(&mut m, 7, command::PLAY, 0.0);
        play(&mut m, 8, command::PLAY, 0.0);
        m.listener_paused = true;
        m.advance(10);
        assert_eq!(m.source_state(7).position, 0.0);
        assert_eq!(m.source_state(8).position, 10.0);
    }

    #[test]
    fn sources_beyond_the_real_voice_count_go_virtual() {
        let mut m = Mixer::new(MixerConfig {
            sample_rate: 1000,
            real_voices: 1,
            ..MixerConfig::default()
        });
        m.add_clip(1, Clip::new(1, 1000, vec![1.0; 100]));
        source_with(&mut m, 7, 1, |p| p.priority = 200);
        source_with(&mut m, 8, 1, |p| p.priority = 0);
        play(&mut m, 7, command::PLAY, 0.0);
        play(&mut m, 8, command::PLAY, 0.0);
        let mut out = vec![0.0; 4];
        m.render(&mut out, 2);
        assert_eq!(out[2], mono_gain(1));
        assert_eq!(m.source_state(7).is_virtual, 1);
        assert_eq!(m.source_state(8).is_virtual, 0);
        // Virtual voices still advance.
        assert_eq!(m.source_state(7).position, 2.0);
    }

    #[test]
    fn clip_data_wraps_around() {
        let mut m = mixer();
        m.add_clip(1, Clip::new(2, 1000, vec![0.0, 1.0, 2.0, 3.0, 4.0, 5.0]));
        let mut data = [0.0; 4];
        assert!(m.clip_data(1, 2, &mut data, false));
        assert_eq!(data, [4.0, 5.0, 0.0, 1.0]);
        let mut data = [9.0, 9.0];
        assert!(m.clip_data(1, 1, &mut data, true));
        assert_eq!(m.clip(1).unwrap().samples, [0.0, 1.0, 9.0, 9.0, 4.0, 5.0]);
    }

    #[test]
    fn output_data_returns_the_latest_samples() {
        let mut m = mixer();
        m.add_clip(1, ramp_clip(100, 1000));
        source_with(&mut m, 7, 1, |p| p.flags = source_flags::KEEP_OUTPUT);
        play(&mut m, 7, command::PLAY, 0.0);
        m.advance(10);
        let mut out = [0.0; 4];
        assert!(m.output_data(7, 0, &mut out, -1));
        assert_eq!(
            out,
            [
                mono_sample(0.06, 6),
                mono_sample(0.07, 7),
                mono_sample(0.08, 8),
                mono_sample(0.09, 9)
            ]
        );
        assert!(m.output_data(0, 1, &mut out, -1));
        assert_eq!(out[3], mono_sample(0.09, 9));
    }

    #[test]
    fn gain_changes_ramp_over_a_block() {
        let mut m = mixer();
        m.add_clip(1, Clip::new(1, 1000, vec![1.0; 1000]));
        source_with(&mut m, 7, 1, |_| {});
        play(&mut m, 7, command::PLAY, 0.0);
        let mut out = vec![0.0; 4 * 2];
        m.render(&mut out, 2);
        source_with(&mut m, 7, 1, |p| p.volume = 0.0);
        m.render(&mut out, 2);
        assert_eq!(out[0], 0.75 * mono_gain(4));
        assert_eq!(out[3 * 2], 0.0);
    }
    fn filter_group(handle: u64, parent: u64, kind: &str) -> GroupSpec {
        serde_json::from_value(serde_json::json!({
            "handle": handle, "parent": parent, "muted": false, "bypass": false,
            "effects": [{ "id": format!("effect-{handle}"), "kind": kind,
                "cutoff": 1000.0, "resonance": 1.0 }]
        }))
        .unwrap()
    }

    #[test]
    fn suspension_uses_dsp_time_at_updates_and_preserves_delay_and_sink_storage() {
        let mut m = Mixer::new(MixerConfig::default());
        let mut group = filter_group(3, 4, "Echo");
        group.mixer = 4;
        group.effects[0].delay = 5000.0;
        m.configure_group(group).unwrap();
        m.configure_group(serde_json::from_value(serde_json::json!({
            "handle": 4, "parent": 0, "mixer": 4, "enableSuspend": true,
            "suspendThreshold": 0.0001, "muted": false, "bypass": false, "effects": []
        })).unwrap()).unwrap();
        let mut input = vec![0.0; 4096 * 2];
        input[2048 * 2] = 0.25;
        input[3072 * 2 + 1] = -0.125;
        m.add_clip(1, Clip::new(2, 48000, input));
        source_with(&mut m, 7, 1, |p| p.group = 3);
        play(&mut m, 7, command::PLAY, 0.0);
        let mut block = vec![0.0; 2048];
        for _ in 0..384 {
            m.update_suspension();
            m.render(&mut block, 2);
        }
        assert!(m.group_nodes[&4].suspended);
        // The complete Native wake_hold capture freezes after 50 DSP buffers at 48 kHz.
        assert_eq!(m.group_nodes[&3].rendered, 50 * 1024);
        let captured = m.capture(2048, vec![capture::SinkRequest {
            handle: 4, length: 2048, exclude_from_mix: false,
        }]).unwrap();
        assert!(captured.sinks[0].samples.is_empty());
        assert!(captured.samples.iter().all(|x| *x == 0.0));
        m.add_clip(2, Clip::new(2, 48000, vec![0.01; 1024 * 2]));
        source_with(&mut m, 8, 2, |p| { p.group = 4; p.flags = source_flags::LOOP; });
        play(&mut m, 8, command::PLAY, 0.0);
        m.render(&mut block, 2);
        assert!(!m.group_nodes[&4].suspended);
        assert_eq!(m.group_nodes[&3].rendered, 51 * 1024);
        // Neither time without rendering nor repeated graph submissions advance the history.
        for _ in 0..160 { m.update_suspension(); }
        assert_eq!(m.group_nodes[&3].rendered, 51 * 1024);
        let configuration = m.config;
        m.reconfigure(configuration).unwrap();
        assert!(!m.group_nodes[&4].suspended);
        assert_eq!(m.group_nodes[&3].rendered, 0);
    }

    #[test]
    fn group_filters_keep_tails_and_state_across_unchanged_parameter_updates() {
        fn run(update: bool) -> Vec<f32> {
            let mut m = Mixer::new(MixerConfig::default());
            m.configure_group(filter_group(3, 4, "Lowpass")).unwrap();
            m.configure_group(filter_group(4, 0, "Highpass")).unwrap();
            let mut input = vec![0.0; 65 * 2];
            input[64 * 2] = 0.25;
            m.add_clip(1, Clip::new(2, 48000, input));
            source_with(&mut m, 7, 1, |p| p.group = 3);
            play(&mut m, 7, command::PLAY, 0.0);
            let mut out = vec![0.0; 512 * 2];
            m.render(&mut out[..256], 2);
            assert_eq!(m.source_state(7).main, voice_state::NONE);
            if update {
                m.configure_group(filter_group(3, 4, "Lowpass")).unwrap();
            }
            m.render(&mut out[256..], 2);
            assert!(out[256..].iter().any(|x| x.abs() > 1e-6));
            assert!(out.chunks_exact(2).all(|f| f[1] == 0.0));
            out
        }
        assert_eq!(run(false), run(true));
    }

    #[test]
    fn mixer_group_cycles_and_unsupported_effects_fail_without_replacing_valid_state() {
        let mut m = Mixer::new(MixerConfig::default());
        m.configure_group(filter_group(3, 4, "Lowpass")).unwrap();
        assert!(m.configure_group(filter_group(4, 3, "Highpass")).is_err());
        assert!(m.configure_group(filter_group(3, 0, "Chorus")).is_err());
        assert_eq!(m.group_nodes[&3].spec.parent, 4);
        assert_eq!(m.group_nodes[&3].spec.effects[0].kind, "Lowpass");
    }
    #[test]
    fn malformed_echo_rates_fail_before_allocating_or_replacing_groups() {
        for delay in [500.0, 5000.0] {
            let mut mixer = Mixer::new(MixerConfig {
                sample_rate: i32::MAX as u32,
                ..Default::default()
            });
            let mut spec = filter_group(3, 0, "Echo");
            spec.effects[0].delay = delay;
            let error = mixer.configure_group(spec).unwrap_err();
            assert!(error.to_string().contains("8 MiB"));
            assert!(mixer.group_nodes.is_empty());

            let mut mixer = Mixer::new(MixerConfig::default());
            mixer.configure_group(filter_group(3, 0, "Echo")).unwrap();
            let before = mixer.config;
            let spec = mixer.group_nodes[&3].spec.clone();
            let error = mixer.reconfigure(MixerConfig {
                sample_rate: i32::MAX as u32,
                ..before
            }).unwrap_err();
            assert!(error.to_string().contains("8 MiB"));
            assert_eq!(mixer.config, before);
            assert_eq!(mixer.group_nodes[&3].spec, spec);
            assert_eq!(mixer.group_nodes[&3].effects.len(), 1);
        }
        // The resource limit still admits a five-second stereo delay at 384 kHz.
        let mut mixer = Mixer::new(MixerConfig { sample_rate: 384000, ..Default::default() });
        let mut spec = filter_group(3, 0, "Echo");
        spec.effects[0].delay = 5000.0;
        mixer.configure_group(spec).unwrap();
    }

    #[test]
    fn echo_matches_complete_native_stereo_impulse_and_tone_tails() {
        for rate in [48000, 44100] {
            for (key, delay, decay, bypass) in [
                ("default_stereo", 500.0, 0.0, false),
                ("delay1_01", 1.01, 0.5, false),
                ("delay10_125", 10.125, 0.5, false),
                ("delay25_3", 25.3, 0.5, false),
                ("delay5000", 5000.0, 0.0, false),
                ("tone_stereo", 25.0, 0.5, false),
                ("effect_bypass", 25.0, 0.5, true),
            ] {
                let mut m = Mixer::new(MixerConfig { sample_rate: rate, ..Default::default() });
                let mut group = filter_group(3, 0, "Echo");
                group.effects[0].delay = delay;
                group.effects[0].decay = decay;
                group.effects[0].bypass = bypass;
                m.configure_group(group.clone()).unwrap();
                let mut input = vec![0.0; 8192 * 2];
                for frame in 0..8192 {
                    for channel in 0..2 {
                        input[frame * 2 + channel] = if key == "tone_stereo" {
                            if frame < 4096 {
                                (0.125 * (2.0 * std::f64::consts::PI
                                    * if channel == 0 { 400.0 } else { 1700.0 }
                                    * frame as f64 / rate as f64).sin()) as f32
                            } else { 0.0 }
                        } else if channel == 0 && (frame == 0 || frame == 2048) {
                            0.25
                        } else if channel == 1 && (frame == 31 || frame == 3072) {
                            -0.125
                        } else { 0.0 };
                    }
                }
                m.add_clip(1, Clip::new(2, rate, input));
                source_with(&mut m, 7, 1, |p| p.group = 3);
                play(&mut m, 7, command::PLAY, 0.0);
                let file = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
                    .join(format!("../../tests/projects/mixerecho/Reference/unity-6000.0.71f1/rate-{rate}/{key}.master.f32"));
                let reference = std::fs::read(file).unwrap();
                let mut output = vec![0.0; reference.len() / 4];
                // Repeated graph submissions must preserve the delay after the source ends.
                for block in output.chunks_mut(2048) {
                    m.configure_group(group.clone()).unwrap();
                    m.render(block, 2);
                }
                for (i, (bytes, actual)) in reference.chunks_exact(4).zip(output).enumerate() {
                    let native = f32::from_le_bytes(bytes.try_into().unwrap());
                    assert!((native - actual).abs() <= 2e-5 + 2e-4 * native.abs(),
                        "{rate}/{key} sample {i}: native={native}, engine={actual}");
                }
            }
        }
    }

    #[test]
    fn dry_voice_matches_the_complete_native_step_capture() {
        let reference = include_bytes!(
            "../../../../tests/projects/mixerfilters/Reference/unity-6000.0.71f1/rate-48000/step_dry.master.f32"
        );
        for curve in [Vec::new(), vec![0.0; 65]] {
            let mut m = Mixer::new(MixerConfig::default());
            let input = (0..12288)
                .map(|i| if i < 2048 { 0.125 } else { 0.0 })
                .collect();
            m.add_clip(1, Clip::new(1, 48000, input));
            source_with(&mut m, 7, 1, |p| p.spatial_blend_curve = curve);
            play(&mut m, 7, command::PLAY, 0.0);
            let mut out = vec![0.0; reference.len() / 4];
            m.render(&mut out, 2);
            let failures: Vec<_> = reference
                .chunks_exact(4)
                .zip(&out)
                .enumerate()
                .filter_map(|(i, (bytes, actual))| {
                    let expected = f32::from_le_bytes(bytes.try_into().unwrap());
                    ((*actual - expected).abs() > 2e-5 + 2e-4 * expected.abs())
                        .then_some((i, expected, *actual))
                })
                .collect();
            assert!(
                failures.is_empty(),
                "every failing (sample, Native, engine): {failures:?}"
            );
        }
    }
    #[test]
    fn lowpass_startup_matches_the_complete_native_step_at_both_rates() {
        let captures: [(u32, &[u8]); 2] = [
            (
                48000,
                include_bytes!(
                    "../../../../tests/projects/mixerfilters/Reference/unity-6000.0.71f1/rate-48000/step_lp.master.f32"
                ),
            ),
            (
                44100,
                include_bytes!(
                    "../../../../tests/projects/mixerfilters/Reference/unity-6000.0.71f1/rate-44100/step_lp.master.f32"
                ),
            ),
        ];
        for (rate, reference) in captures {
            let mut m = Mixer::new(MixerConfig {
                sample_rate: rate,
                ..Default::default()
            });
            m.configure_group(filter_group(3, 0, "Lowpass")).unwrap();
            m.add_clip(
                1,
                Clip::new(
                    1,
                    rate,
                    (0..12288)
                        .map(|n| if n < 2048 { 0.125 } else { 0.0 })
                        .collect(),
                ),
            );
            source_with(&mut m, 7, 1, |p| p.group = 3);
            play(&mut m, 7, command::PLAY, 0.0);
            let mut out = vec![0.0; reference.len() / 4];
            m.render(&mut out, 2);
            let failures: Vec<_> = reference
                .chunks_exact(4)
                .zip(out)
                .enumerate()
                .filter_map(|(i, (bytes, value))| {
                    let expected = f32::from_le_bytes(bytes.try_into().unwrap());
                    ((value - expected).abs() > 2e-5 + 2e-4 * expected.abs())
                        .then_some((i, expected, value))
                })
                .collect();
            assert!(
                failures.is_empty(),
                "{rate}Hz every failing (sample, Native, engine): {failures:?}"
            );
        }
    }

    #[test]
    fn highpass_startup_matches_the_complete_native_tone_capture() {
        let reference = include_bytes!(
            "../../../../tests/projects/mixerfilters/Reference/unity-6000.0.71f1/rate-48000/tone_hp.master.f32"
        );
        let mut m = Mixer::new(MixerConfig::default());
        m.configure_group(filter_group(3, 0, "Highpass")).unwrap();
        let input = (0..12288)
            .map(|i| {
                let t = i as f64 / 48000.0;
                (0.125 * (std::f64::consts::TAU * 400.0 * t).sin()
                    + 0.0625 * (std::f64::consts::TAU * 6000.0 * t).sin()) as f32
            })
            .collect();
        m.add_clip(1, Clip::new(1, 48000, input));
        source_with(&mut m, 7, 1, |p| p.group = 3);
        play(&mut m, 7, command::PLAY, 0.0);
        let mut out = vec![0.0; reference.len() / 4];
        m.render(&mut out, 2);
        let failures: Vec<_> = reference
            .chunks_exact(4)
            .zip(&out)
            .enumerate()
            .filter_map(|(i, (bytes, actual))| {
                let expected = f32::from_le_bytes(bytes.try_into().unwrap());
                ((*actual - expected).abs() > 2e-5 + 2e-4 * expected.abs())
                    .then_some((i, expected, *actual))
            })
            .collect();
        assert!(
            failures.is_empty(),
            "every failing (sample, Native, engine): {failures:?}"
        );
    }
}
