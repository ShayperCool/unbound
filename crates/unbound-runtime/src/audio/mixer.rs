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
    effects::{Effect, EffectKind, EffectSpec, GroupNode, GroupSpec},
    fft,
    protocol::{
        AudioFilterDescriptor, AudioFramePacket, AudioSourceCommand, AudioSourcePacket,
        AudioSourceState, Callback, MAX_SOURCE_FILTERS, callback, command, filter_kind,
        filter_stage, source_flags, voice_state,
    },
    ramp::ParameterRamp,
    reverb::{SfxReverb, SfxReverbSpec},
    spatial::{self, Listener, MATRIX_ENTRIES, SourceSpatial, SpeakerLayout},
};
use crate::render_protocol::Vector3;
use std::collections::{HashMap, HashSet};

/// Frames rendered at a time (scheduling stays sample accurate inside a block).
pub const BLOCK: usize = 256;
/// Frames of output kept for `GetOutputData`/`GetSpectrumData` (16384 covers the largest
/// spectrum, 8192 bins).
const RING_FRAMES: usize = 16384;
/// Frames a streamed clip's PCM reader is asked for at a time.
const STREAM_CHUNK: usize = 4096;
/// Source gain changes accumulate for 64 samples, then reach the exact target.
/// Measured by tests/projects/audiodistortion at both output rates.
const VOICE_GAIN_RAMP_FRAMES: u32 = 64;

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
    pub listener_snapshot: Option<(Listener, Vector3)>,
    pub volume: f32,
    pub pitch: f32,
    pub pan_stereo: f32,
    pub spatial_blend: f32,
    pub spread: f32,
    pub doppler_level: f32,
    pub min_distance: f32,
    pub max_distance: f32,
    /// The linear send gain into the reverb zones.
    pub reverb_zone_mix: f32,
    pub rolloff_mode: i32,
    pub priority: i32,
    pub flags: u32,
    pub rolloff_curve: Vec<f32>,
    pub spatial_blend_curve: Vec<f32>,
    pub spread_curve: Vec<f32>,
    /// The enabled filter components of the source's GameObject, in component order.
    pub filters: Vec<AudioFilterDescriptor>,
    /// Why the packet's filter descriptors were refused; the source then keeps its chain.
    pub filters_rejected: Option<String>,
}

impl Default for SourceParams {
    fn default() -> Self {
        Self {
            clip: 0,
            group: 0,
            position: Vector3::default(),
            velocity: Vector3::default(),
            listener_snapshot: None,
            volume: 1.0,
            pitch: 1.0,
            pan_stereo: 0.0,
            spatial_blend: 0.0,
            spread: 0.0,
            doppler_level: 1.0,
            min_distance: 1.0,
            max_distance: 500.0,
            reverb_zone_mix: 1.0,
            rolloff_mode: spatial::LOGARITHMIC,
            priority: 128,
            flags: 0,
            rolloff_curve: Vec::new(),
            spatial_blend_curve: Vec::new(),
            spread_curve: Vec::new(),
            filters: Vec::new(),
            filters_rejected: None,
        }
    }
}

/// Copies a packet's filter descriptors into owned memory and checks them.
///
/// # Safety
/// With a count in range, `filters` must be null or point at `filter_count` descriptors.
unsafe fn decode_filters(p: &AudioSourcePacket) -> anyhow::Result<Vec<AudioFilterDescriptor>> {
    // A clipless OnAudioFilterRead source plays its first script as the clip; what a component
    // above that script receives is unmeasured.
    let generator = p.clip == 0 && p.flags & source_flags::FILTER != 0;
    unsafe { decode_descriptors(p.filter_count, p.filters, generator, false) }
}

/// The listener's component descriptors; placement/release validation is on the managed side.
///
/// # Safety
/// As [`decode_filters`], for the frame packet's `filters`.
pub unsafe fn decode_listener_filters(
    p: &AudioFramePacket,
) -> anyhow::Result<Vec<AudioFilterDescriptor>> {
    unsafe { decode_descriptors(p.filter_count, p.filters, false, true) }
}

unsafe fn decode_descriptors(
    count: i32,
    pointer: *const AudioFilterDescriptor,
    generator: bool,
    listener: bool,
) -> anyhow::Result<Vec<AudioFilterDescriptor>> {
    anyhow::ensure!(
        (0..=MAX_SOURCE_FILTERS).contains(&count),
        "AudioSource filter count {count} is outside 0..={MAX_SOURCE_FILTERS}"
    );
    if count == 0 {
        return Ok(Vec::new());
    }
    anyhow::ensure!(
        !pointer.is_null(),
        "AudioSource filter descriptors are null"
    );
    anyhow::ensure!(
        pointer.is_aligned(),
        "AudioSource filter descriptors are misaligned"
    );
    // Copied before returning: the managed array is pinned for this call only.
    let filters = unsafe { std::slice::from_raw_parts(pointer, count as usize) }.to_vec();
    for (i, f) in filters.iter().enumerate() {
        if listener && i > 0 {
            anyhow::ensure!(
                filter_stage::position(filters[i - 1].stage) <= filter_stage::position(f.stage),
                "listener filter script positions are out of order"
            );
        }
        anyhow::ensure!(
            filter_kind::supported(f.kind),
            "unsupported AudioSource filter kind {}",
            f.kind
        );
        anyhow::ensure!(
            if listener {
                filter_stage::valid_listener(f.stage)
            } else {
                f.stage & !(filter_stage::AFTER_SCRIPTS | filter_stage::BYPASSED) == 0
            },
            "invalid audio filter stage {}",
            f.stage
        );
        anyhow::ensure!(
            !(generator && f.stage == filter_stage::BEFORE_SCRIPTS),
            "an AudioSource filter component above a clipless OnAudioFilterRead generator is unsupported"
        );
        anyhow::ensure!(
            [
                f.cutoff,
                f.resonance,
                f.delay,
                f.decay,
                f.dry_mix,
                f.wet_mix
            ]
            .iter()
            .chain(&f.reverb)
            .all(|v| v.is_finite()),
            "AudioSource filter parameters must be finite"
        );
        anyhow::ensure!(
            filters[..i].iter().all(|other| other.id != f.id),
            "duplicate AudioSource filter component {}",
            f.id
        );
    }
    Ok(filters)
}

/// The mixer kernel a filter component reuses: the AudioMixer effect of the same kind. The
/// reverb filter is the SFX Reverb without wet mixing (tests/projects/audioreverb).
fn effect_spec(d: &AudioFilterDescriptor) -> EffectSpec {
    let r = d.reverb;
    EffectSpec {
        id: d.id.to_string(),
        kind: match d.kind {
            filter_kind::LOWPASS => "Lowpass",
            filter_kind::HIGHPASS => "Highpass",
            filter_kind::REVERB => "SFX Reverb",
            filter_kind::DISTORTION => "Distortion",
            filter_kind::CHORUS => "Chorus",
            _ => "Echo",
        }
        .into(),
        bypass: d.stage & filter_stage::BYPASSED != 0,
        gain: 0.0,
        gain_immediate: false,
        gain_ramp_measured: false,
        cutoff: d.cutoff,
        resonance: d.resonance,
        delay: d.delay,
        decay: d.decay,
        max_channels: 0.0,
        dry_mix: d.dry_mix,
        wet_mix: d.wet_mix,
        sfx: (d.kind == filter_kind::REVERB).then(|| sfx_spec(&r)),
        mix_level: None,
        slot_wet_measured: false,
        effect_transitions_measured: false,
        surround_effects_measured: false,
        slot_wet_transitions_measured: false,
        mix_audible: true,
        duck: None,
        send_target: None,
        send_level: 0.0,
        compressor: None,
        eq: None,
        distortion: (d.kind == filter_kind::DISTORTION).then(|| d.cutoff.clamp(0.0, 1.0)),
        flange: None,
        normalize: None,
        pitch_shifter: None,
        chorus: (d.kind == filter_kind::CHORUS).then(|| super::chorus::ChorusSpec {
            dry: r[0].clamp(0.0, 1.0),
            wet1: r[1].clamp(0.0, 1.0),
            wet2: r[2].clamp(0.0, 1.0),
            wet3: r[3].clamp(0.0, 1.0),
            delay: (r[4] <= 100.0).then_some(r[4].max(0.0)),
            rate: r[5].clamp(0.0, 20.0),
            depth: r[6].clamp(0.0, 1.0),
        }),
    }
}

/// A reverb descriptor's 14 parameters in [`SfxReverbSpec`]'s field order.
fn sfx_spec(r: &[f32; 14]) -> SfxReverbSpec {
    SfxReverbSpec {
        dry_level: r[0],
        room: r[1],
        room_hf: r[2],
        decay_time: r[3],
        decay_hf_ratio: r[4],
        reflections: r[5],
        reflect_delay: r[6],
        reverb: r[7],
        reverb_delay: r[8],
        diffusion: r[9],
        density: r[10],
        hf_reference: r[11],
        room_lf: r[12],
        lf_reference: r[13],
    }
}

/// The reverb zones' SFX Reverb (`AudioReverbZone`; tests/projects/audioreverb). Every source
/// sends its output at its `reverbZoneMix` level; the reverb's output joins the final mix and
/// outlives the sources. The frame's identity retains its histories until the managed zone
/// lifetime ends, as measured by the lifecycle controls.
struct ZoneReverb {
    id: u64,
    measured_startup: bool,
    spec: SfxReverbSpec,
    reverb: SfxReverb,
}

struct SourceFilter {
    id: u64,
    kind: i32,
    stage: i32,
    effect: Effect,
}

enum ListenerStep {
    Scripts(std::ops::Range<usize>),
    Components(std::ops::Range<usize>),
}

/// A source's filter components, each with its own channel histories: no state is shared with
/// another source (or another mixer source of the same GameObject).
#[derive(Default)]
struct FilterChain {
    filters: Vec<SourceFilter>,
    /// Samples went through since the last reset.
    primed: bool,
}

impl std::fmt::Debug for FilterChain {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("FilterChain")
            .field("filters", &self.filters.len())
            .field("primed", &self.primed)
            .finish()
    }
}

impl FilterChain {
    /// Checks every component at `rate` (the Echo history bound) without changing anything.
    fn validate(filters: &[AudioFilterDescriptor], rate: u32, buffer: u32) -> anyhow::Result<()> {
        filters
            .iter()
            .try_for_each(|d| effect_spec(d).validate(rate, buffer))
    }

    /// Matches the components by identity and kind: an unchanged one keeps its histories, a
    /// changed one keeps them and ramps to its new parameters, a new one starts empty. Call
    /// after [`FilterChain::validate`]; a failed allocation then leaves the chain empty.
    fn apply(
        &mut self,
        filters: &[AudioFilterDescriptor],
        rate: u32,
        buffer: u32,
        transitions: bool,
    ) -> anyhow::Result<()> {
        let mut old = std::mem::take(&mut self.filters);
        let mut chain = Vec::with_capacity(filters.len());
        for d in filters {
            let spec = EffectSpec {
                effect_transitions_measured: transitions,
                ..effect_spec(d)
            };
            let effect = match old.iter().position(|f| f.id == d.id && f.kind == d.kind) {
                Some(index) => {
                    let mut effect = old.remove(index).effect;
                    effect.update(spec, rate, buffer)?;
                    if d.stage & filter_stage::RECREATED != 0 {
                        effect.reset();
                    } else if d.stage & filter_stage::RECONNECTED != 0 {
                        effect.restart();
                    }
                    effect
                }
                None => Effect::new(spec, rate, buffer)?,
            };
            chain.push(SourceFilter {
                id: d.id,
                kind: d.kind,
                stage: filter_stage::position(d.stage),
                effect,
            });
        }
        self.filters = chain;
        Ok(())
    }

    fn set_layout(&mut self, config: &MixerConfig, measured: bool, offset: usize) {
        for filter in &mut self.filters {
            filter.effect.set_layout(config.output_channels());
            filter.effect.set_channel_offset(offset);
            filter.effect.spec.surround_effects_measured = measured;
        }
    }

    /// Runs each component across the complete layout before advancing to the next component.
    /// A shared reverb network sees every speaker; other effects retain per-pair histories.
    fn process_layout(
        &mut self,
        lanes: &mut [FilterChain],
        samples: &mut [f32],
        extra: Option<&mut Lanes<'_>>,
        stage: Option<i32>,
        bypass: bool,
        last: bool,
    ) {
        let range = if last {
            self.filters
                .iter()
                .rposition(|f| stage.is_none_or(|s| f.stage == s))
                .map_or(0..0, |index| index..index + 1)
        } else {
            0..self.filters.len()
        };
        self.process_range_layout(lanes, samples, extra, range, stage, bypass);
    }

    fn process_range_layout(
        &mut self,
        lanes: &mut [FilterChain],
        samples: &mut [f32],
        extra: Option<&mut Lanes<'_>>,
        range: std::ops::Range<usize>,
        stage: Option<i32>,
        bypass: bool,
    ) {
        let frames = samples.len() / 2;
        let mut blocks = extra.map(|e| e.pairs(frames));
        let first = range.start;
        for (offset, filter) in self.filters[range].iter_mut().enumerate() {
            let index = first + offset;
            if stage.is_some_and(|s| filter.stage != s) {
                continue;
            }
            if let Some(blocks) = blocks
                .as_mut()
                .filter(|_| !lanes.is_empty() && filter.effect.links_channels())
            {
                filter
                    .effect
                    .process_shared_reverb(samples, &mut blocks[..lanes.len()], bypass);
            } else {
                filter.effect.process(samples, bypass);
                if let Some(blocks) = blocks.as_mut() {
                    for (pair, chain) in lanes.iter_mut().enumerate() {
                        chain.filters[index].effect.process(blocks[pair], bypass);
                    }
                }
            }
            self.primed |= !bypass;
            for lane in lanes.iter_mut() {
                lane.primed |= !bypass;
            }
        }
    }

    /// Runs the components of `stage` on the front pair alone.
    #[cfg(test)]
    fn process(&mut self, samples: &mut [f32], stage: Option<i32>, bypass: bool) {
        self.process_layout(&mut [], samples, None, stage, bypass, false);
    }

    /// A chain of the same components with the same parameters, each starting empty: the
    /// chain of another channel pair.
    fn mirrored(&self, rate: u32, buffer: u32) -> anyhow::Result<Self> {
        let filters = self
            .filters
            .iter()
            .map(|f| {
                Ok(SourceFilter {
                    id: f.id,
                    kind: f.kind,
                    stage: f.stage,
                    effect: Effect::new(f.effect.spec.clone(), rate, buffer)?,
                })
            })
            .collect::<anyhow::Result<_>>()?;
        Ok(Self {
            filters,
            primed: false,
        })
    }

    /// The chain reconnected: each component restarts empty (see [`Effect::restart`]).
    fn restart(&mut self) {
        self.filters.iter_mut().for_each(|f| f.effect.restart());
    }

    /// Clears every history (the source stopped or went idle); the components stay.
    fn reset(&mut self) {
        if self.primed {
            self.filters.iter_mut().for_each(|f| f.effect.reset());
            self.primed = false;
        }
    }

    /// A new output rate (`AudioSettings.Reset`): the components restart empty at it.
    fn reconfigure(&mut self, rate: u32, buffer: u32) -> anyhow::Result<()> {
        for filter in &mut self.filters {
            let spec = filter.effect.spec.clone();
            filter.effect.update(spec, rate, buffer)?;
            filter.effect.reset();
        }
        self.primed = false;
        Ok(())
    }
}

/// One chain per extra channel pair, mirroring `main`: kept when the count is unchanged.
fn sync_lanes(
    lanes: &mut Vec<FilterChain>,
    main: &FilterChain,
    pairs: usize,
    config: &MixerConfig,
) -> anyhow::Result<()> {
    if lanes.len() != pairs {
        lanes.clear();
        for _ in 0..pairs {
            lanes.push(main.mirrored(config.sample_rate, config.buffer_size)?);
        }
    }
    Ok(())
}

/// Assigns the actual layout and global speaker offsets to a component chain and its lanes.
fn configure_chains(
    chain: &mut FilterChain,
    lanes: &mut [FilterChain],
    config: &MixerConfig,
    measured: bool,
) {
    chain.set_layout(config, measured, 0);
    for (pair, lane) in lanes.iter_mut().enumerate() {
        lane.set_layout(config, measured, (pair + 1) * 2);
    }
}

impl SourceParams {
    /// # Safety
    /// The packet's curve pointers must be null or point at `curve_length` floats; its filter
    /// pointer must be null or point at `filter_count` descriptors.
    pub unsafe fn from_packet(p: &AudioSourcePacket) -> Self {
        let curve = |ptr: *const f32| -> Vec<f32> {
            if ptr.is_null() || p.curve_length <= 0 {
                Vec::new()
            } else {
                unsafe { std::slice::from_raw_parts(ptr, p.curve_length as usize) }.to_vec()
            }
        };
        let (filters, filters_rejected) = match unsafe { decode_filters(p) } {
            Ok(filters) => (filters, None),
            Err(error) => (Vec::new(), Some(format!("{error:#}"))),
        };
        Self {
            clip: p.clip,
            group: p.group,
            position: p.position,
            velocity: p.velocity,
            listener_snapshot: (p.flags & source_flags::LISTENER_SNAPSHOT != 0).then_some((
                Listener {
                    position: p.listener_position,
                    forward: p.listener_forward,
                    up: p.listener_up,
                    velocity: Vector3::default(),
                },
                p.doppler_listener_position,
            )),
            volume: p.volume,
            pitch: p.pitch,
            pan_stereo: p.pan_stereo,
            spatial_blend: p.spatial_blend,
            spread: p.spread,
            doppler_level: p.doppler_level,
            min_distance: p.min_distance,
            max_distance: p.max_distance,
            reverb_zone_mix: p.reverb_zone_mix,
            rolloff_mode: p.rolloff_mode,
            priority: p.priority,
            flags: p.flags,
            rolloff_curve: curve(p.rolloff_curve),
            spatial_blend_curve: curve(p.spatial_blend_curve),
            spread_curve: curve(p.spread_curve),
            filters,
            filters_rejected,
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
    submitted: bool,
    /// The audio update has not yet published the initial scheduled start.
    start_pending: bool,
    volume_scale: f32,
    /// The gain matrix of the last block (ramped from), `[output * 2 + input]`.
    matrix: Matrix,
    fresh: bool,
    /// Startup and measured parameter changes use the same 64-sample gain ramp.
    fade_frames: u32,
    /// Target minus the gain at the start of the current ramp.
    fade_delta: Matrix,
    /// Accumulated gain for the next sample.
    fade_matrix: Matrix,
    stream: Option<StreamState>,
    zone_send: ZoneSendState,
    /// Set after the first rendered frame; gain changes do not make a voice new again.
    rendered: bool,
}

impl Voice {
    fn new(clip: u64, position: f64, start_at: u64, volume_scale: f32) -> Self {
        Self {
            clip,
            position,
            start_at,
            end_at: u64::MAX,
            paused: false,
            submitted: true,
            start_pending: false,
            volume_scale,
            matrix: [0.0; MATRIX_ENTRIES],
            fresh: true,
            fade_frames: 0,
            fade_delta: [0.0; MATRIX_ENTRIES],
            fade_matrix: [0.0; MATRIX_ENTRIES],
            stream: None,
            zone_send: ZoneSendState::default(),
            rendered: false,
        }
    }

    /// Advances the gain ramp of the first `entries` matrix entries.
    fn advance_fade(&mut self, measured: bool, entries: usize) {
        if self.fade_frames < VOICE_GAIN_RAMP_FRAMES {
            for i in 0..entries {
                self.fade_matrix[i] += self.fade_delta[i] / VOICE_GAIN_RAMP_FRAMES as f32;
            }
            self.fade_frames += 1;
            if measured && self.fade_frames == VOICE_GAIN_RAMP_FRAMES {
                self.fade_matrix = self.matrix;
            }
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
    /// Computed once per render chunk and reused while mixing this source's voices.
    spatial: spatial::Spatialized,
    main: Option<Voice>,
    one_shots: Vec<Voice>,
    /// Where the next `Play` starts (`time` set while stopped).
    pending_position: f64,
    is_virtual: bool,
    output: Option<Ring>,
    generator: GeneratorState,
    generator_gain: Option<ParameterRamp>,
    played: bool,
    // Processing survives natural voice completion; explicit Stop tears it down.
    filter_started: bool,
    filter_paused: bool,
    filtered: Vec<f32>,
    /// The scripts' buffer in the output layout's channel count (see [`call_in_layout`]).
    layout_scratch: Vec<f32>,
    bypass_dry: Vec<f32>,
    filtered_offset: usize,
    component_playback: bool,
    completed_shots: Vec<u64>,
    completed_main: Option<u64>,
    completion_frontier: u64,
    /// Its filter components (`params.filters`), with this source's own state.
    chain: FilterChain,
    /// The same components on each extra channel pair of the output layout.
    lanes: Vec<FilterChain>,
    /// The extra channel pairs of `filtered` and `bypass_dry`, one buffer length per pair.
    filtered_extra: Vec<f32>,
    bypass_extra: Vec<f32>,
    /// The mixer frame up to which the components still run after the last voice ended.
    chain_tail: Option<u64>,
    /// Explicit Stop freezes component histories until playback resumes.
    components_stopped: bool,
}

impl Source {
    /// Sizes the scripts' and components' buffers for `config` on the control thread, so the
    /// first render after Play, Stop or a configuration change does not allocate: the scripts'
    /// layout buffer and queue, the generator's two buffers and the extra pairs' buffers.
    fn reserve_buffers(&mut self, config: &MixerConfig) {
        let channels = config.output_channels();
        let reserve = |buffer: &mut Vec<f32>, samples: usize| {
            buffer.reserve(samples.saturating_sub(buffer.len()));
        };
        let frames = config.buffer_size.max(1) as usize;
        reserve(
            &mut self.layout_scratch,
            config.buffer_size.max(BLOCK as u32) as usize * channels,
        );
        reserve(&mut self.filtered, frames * 2);
        reserve(&mut self.generator.current, frames * channels);
        reserve(&mut self.generator.next, frames * channels);
        self.reserve_lanes(config);
    }

    /// Sizes the extra pairs' script and bypass buffers for `config` on the control thread.
    fn reserve_lanes(&mut self, config: &MixerConfig) {
        let pairs = config.extra_pairs();
        let length = config.buffer_size.max(1) as usize * 2;
        self.filtered_extra.resize(pairs * length, 0.0);
        self.bypass_extra.resize(
            pairs * config.buffer_size.max(BLOCK as u32) as usize * 2,
            0.0,
        );
    }

    fn process_components(
        &mut self,
        samples: &mut [f32],
        extra: Option<&mut Lanes<'_>>,
        stage: Option<i32>,
        bypass: bool,
        last: bool,
    ) {
        self.chain
            .process_layout(&mut self.lanes, samples, extra, stage, bypass, last);
    }

    /// `begin`: the extra pairs' dry signal moves aside and the pairs carry silence, as
    /// [`Source::begin_bypass`] does for the front pair; otherwise it joins them again.
    fn bypass_lanes(&mut self, extra: Option<&mut Lanes<'_>>, frames: usize, begin: bool) {
        let Some(extra) = extra else {
            return;
        };
        let length = self.bypass_extra.len() / self.lanes.len().max(1);
        for pair in 0..self.lanes.len() {
            let Some(dry) = self
                .bypass_extra
                .get_mut(pair * length..pair * length + frames * 2)
            else {
                return;
            };
            let samples = extra.pair(pair, frames);
            if begin {
                dry.copy_from_slice(samples);
                samples.fill(0.0);
            } else {
                for (sample, dry) in samples.iter_mut().zip(dry.iter()) {
                    *sample += *dry;
                }
            }
        }
    }

    fn reset_chains(&mut self) {
        self.chain.reset();
        self.lanes.iter_mut().for_each(FilterChain::reset);
    }

    fn filter_only(&self) -> bool {
        self.params.clip == 0 && self.params.has(source_flags::FILTER) && self.main.is_some()
    }

    fn begin_bypass(&mut self, samples: &mut [f32]) {
        self.bypass_dry[..samples.len()].copy_from_slice(samples);
        samples.fill(0.0);
    }

    fn scale_generator(
        &mut self,
        samples: &mut [f32],
        mut extra: Option<&mut Lanes<'_>>,
        target: f32,
    ) {
        let gain = self
            .generator_gain
            .get_or_insert_with(|| ParameterRamp::new(0.0));
        for (frame, stereo) in samples.chunks_exact_mut(2).enumerate() {
            let value = gain.gain(target, VOICE_GAIN_RAMP_FRAMES);
            if value != 1.0 {
                stereo[0] *= value;
                stereo[1] *= value;
                if let Some(lanes) = extra.as_deref_mut() {
                    for channel in 0..lanes.channels {
                        let i = lanes.index(channel, frame);
                        lanes.samples[i] *= value;
                    }
                }
            }
        }
    }

    /// Active components of a bypassed source process silence; their output (tails of earlier
    /// input) joins the dry route.
    fn run_bypassed_components(
        &mut self,
        samples: &mut [f32],
        mut extra: Option<&mut Lanes<'_>>,
        stage: Option<i32>,
        last: bool,
    ) {
        if self.chain.filters.is_empty() {
            return;
        }
        let Some(silence) = self.bypass_dry.get_mut(..samples.len()) else {
            return;
        };
        silence.fill(0.0);
        self.bypass_extra.fill(0.0);
        let stride = self.bypass_extra.len() / self.lanes.len().max(1);
        let mut lanes = Lanes {
            samples: &mut self.bypass_extra,
            stride,
            offset: 0,
            channels: extra.as_ref().map_or(0, |e| e.channels),
        };
        self.chain.process_layout(
            &mut self.lanes,
            silence,
            extra.as_ref().map(|_| &mut lanes),
            stage,
            false,
            last,
        );
        for (sample, wet) in samples.iter_mut().zip(silence.iter()) {
            *sample += *wet;
        }
        if let Some(extra) = extra.as_mut() {
            for pair in 0..self.lanes.len() {
                for (sample, wet) in extra
                    .pair(pair, samples.len() / 2)
                    .iter_mut()
                    .zip(&self.bypass_extra[pair * stride..pair * stride + samples.len()])
                {
                    *sample += *wet;
                }
            }
        }
    }

    fn end_bypass(&self, samples: &mut [f32]) {
        for (sample, dry) in samples.iter_mut().zip(&self.bypass_dry) {
            *sample += *dry;
        }
    }

    fn active(&self) -> bool {
        self.main.as_ref().is_some_and(|v| !v.paused) || self.one_shots.iter().any(|v| !v.paused)
    }
}

/// Clipless filters supply PCM ahead of playback, in configured DSP buffers. The
/// first pull primes the current and next buffer; later pulls consume the prior
/// callback's samples while requesting the next one. Pausing preserves that queue.
#[derive(Debug, Default)]
struct GeneratorState {
    current: Vec<f32>,
    next: Vec<f32>,
    offset: usize,
}

impl GeneratorState {
    /// An empty queue that keeps `previous`'s buffers, so the next fill reuses them.
    fn restarted(mut previous: Self) -> Self {
        previous.current.clear();
        previous.next.clear();
        previous.offset = 0;
        previous
    }

    /// Fills stereo `out` from DSP buffers of `frames` frames generated in the output layout's
    /// `channels` (Unity 6000.0.71f1 passes the layout's channel count to a clipless source's
    /// `OnAudioFilterRead`, tests/projects/audiopitch `context_*_generator`).
    fn render(
        &mut self,
        handle: u64,
        out: &mut [f32],
        mut extra: Option<&mut Lanes<'_>>,
        frames: usize,
        channels: usize,
        callback: Option<Callback>,
    ) {
        let length = frames * channels;
        let fill = |buffer: &mut Vec<f32>| {
            buffer.resize(length, 0.0);
            buffer.fill(0.0);
            if let Some(cb) = callback {
                cb(
                    callback::SOURCE_FILTER,
                    handle,
                    buffer.as_mut_ptr(),
                    frames as i32,
                    channels as i32,
                    0,
                );
            }
        };
        if self.current.len() != length {
            self.current.clear();
            self.next.clear();
            self.offset = 0;
            fill(&mut self.current);
        }
        let mut written = 0;
        while written < out.len() {
            if self.offset == 0 {
                fill(&mut self.next);
            }
            let count = ((out.len() - written) / 2).min((length - self.offset) / channels);
            fold(
                &self.current[self.offset..self.offset + count * channels],
                &mut out[written..written + count * 2],
                extra
                    .as_deref_mut()
                    .map(|lanes| lanes.advanced(written / 2))
                    .as_mut(),
            );
            written += count * 2;
            self.offset += count * channels;
            if self.offset == length {
                std::mem::swap(&mut self.current, &mut self.next);
                self.offset = 0;
            }
        }
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

/// `AudioSpeakerMode` values.
pub mod speaker_mode {
    pub const MONO: i32 = 1;
    pub const STEREO: i32 = 2;
    pub const QUAD: i32 = 3;
    pub const SURROUND: i32 = 4;
    pub const MODE_5POINT1: i32 = 5;
    pub const MODE_7POINT1: i32 = 6;
    pub const PROLOGIC: i32 = 7;
}

impl MixerConfig {
    /// Channel pairs beyond the front pair: 1 in quad, 2 in 5.0 and 5.1, 3 in 7.1.
    pub fn extra_pairs(&self) -> usize {
        self.output_channels().saturating_sub(1) / 2
    }
    /// Output channels of `speaker_mode` (`AudioSpeakerMode`), in AudioRenderer and
    /// `OnAudioFilterRead` order: Mono 1, Quad 4, Surround 5, Mode5point1 6, Mode7point1 8;
    /// Stereo and Prologic (an encoded stereo pair) 2.
    pub fn output_channels(&self) -> usize {
        match self.speaker_mode {
            speaker_mode::MONO => 1,
            speaker_mode::QUAD => 4,
            speaker_mode::SURROUND => 5,
            speaker_mode::MODE_5POINT1 => 6,
            speaker_mode::MODE_7POINT1 => 8,
            _ => 2,
        }
    }
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

#[derive(Default)]
struct DeferredListenerReturn {
    bypass: bool,
    zone: bool,
}

fn sum_listener_return(
    mix: &mut [f32],
    extra: &mut [f32],
    front: &[f32],
    lanes: &[f32],
    n: usize,
    pairs: usize,
) {
    for (sample, value) in mix[..n * 2].iter_mut().zip(&front[..n * 2]) {
        *sample += *value;
    }
    for pair in 0..pairs {
        let range = pair * BLOCK * 2..pair * BLOCK * 2 + n * 2;
        for (sample, value) in extra[range.clone()].iter_mut().zip(&lanes[range]) {
            *sample += *value;
        }
    }
}

pub struct Mixer {
    /// Accumulated startup gains, shared by all mixers using this DSP period.
    input_fade_curve: Vec<f32>,
    pub config: MixerConfig,
    pub output_rules: unbound_compat::AudioOutputRules,
    /// The engine submits component voices at its audio update; direct DSP users may render
    /// immediately. Full AudioMixer graphs also require update-based submission.
    pub defer_voice_submission: bool,
    frames: u64,
    clips: HashMap<u64, Clip>,
    sources: HashMap<u64, Source>,
    /// Capacity grows only when a source is registered on the control thread.
    voice_priority: Vec<(u64, i32, f32)>,
    groups: HashMap<u64, Group>,
    group_nodes: HashMap<u64, GroupNode>,
    group_order: Vec<u64>,
    /// Signal sent to each Receive or Duck Volume unit (by unit id) in the current block.
    sidechains: HashMap<String, Vec<f32>>,
    activated_mixers: HashSet<u64>,
    active_roots: HashSet<u64>,
    capture_sinks: HashMap<u64, capture::Sink>,
    pub listener: Listener,
    pub listener_volume: f32,
    pub listener_paused: bool,
    pub has_listener: bool,
    pub listener_filter: bool,
    /// The listener's filter components as last sent, and their state; they run on the final
    /// mix before the listener's OnAudioFilterRead scripts.
    listener_filters: Vec<AudioFilterDescriptor>,
    listener_chain: FilterChain,
    listener_rejected: bool,
    zone: Option<ZoneReverb>,
    /// The zone id whose non-finite parameters were reported (once).
    zone_rejected: Option<u64>,
    zone_input: Vec<f32>,
    zone_extra: Vec<f32>,
    pub(crate) surround_effects: bool,
    pub(crate) listener_chain_measured: bool,
    pub(crate) listener_plan: u64,
    /// Unrouted sources with `bypassListenerEffects`, joined after the measured listener filters.
    listener_bypass: Vec<f32>,
    /// The listener scripts' block in the output layout's channel count.
    listener_layout: Vec<f32>,
    /// The speakers of a layout wider than stereo (see [`spatial::surround`]).
    speakers: Option<SpeakerLayout>,
    /// Extra channel pairs of the final mix, the listener-bypass route and the source being
    /// mixed: `MAX_EXTRA_PAIRS` blocks each (see [`Lanes`]).
    mix_extra: Vec<f32>,
    listener_bypass_extra: Vec<f32>,
    source_extra: Vec<f32>,
    /// Per extra pair, the sidechain blocks that sends write and receives and ducks read.
    sidechain_lanes: Vec<HashMap<String, Vec<f32>>>,
    /// The listener's components on each extra pair.
    listener_lanes: Vec<FilterChain>,
    /// SFX Reverb on a surround layout was reported once.
    warned_surround_reverb: bool,
    listener_output: Ring,
    callback: Option<Callback>,
    capture_listener_batch: bool,
    /// Post-script returns for one complete offline DSP period, reserved on the control thread.
    capture_listener_return: Vec<f32>,
    capture_listener_return_used: bool,
    /// Nothing listens (the virtual device): voices only advance, unless their samples are
    /// asked for (filters, output data).
    pub discard_output: bool,
    listener_output_wanted: bool,
    mix: Vec<f32>,
    source_buffer: Vec<f32>,
}

/// Children before their parents, and a group with a Send before the group of its target unit;
/// otherwise deeper groups first, then by handle, so independent sums stay deterministic. A
/// cycle through sends fails: the Editor refuses one and Native's order for it is unmeasured.
fn processing_order(specs: &HashMap<u64, &GroupSpec>) -> anyhow::Result<Vec<u64>> {
    let depth = |h: u64| {
        let mut d = 0;
        let mut p = h;
        while let Some(spec) = specs.get(&p) {
            d += 1;
            p = spec.parent;
            if p == 0 || d > specs.len() {
                break;
            }
        }
        d
    };
    let mut order: Vec<u64> = specs.keys().copied().collect();
    order.sort_by_key(|h| (std::cmp::Reverse(depth(*h)), *h));
    let owner: HashMap<&str, u64> = specs
        .iter()
        .flat_map(|(h, spec)| spec.effects.iter().map(move |e| (e.id.as_str(), *h)))
        .collect();
    let mut before: HashMap<u64, Vec<u64>> = HashMap::new();
    for (&h, spec) in specs {
        if specs.contains_key(&spec.parent) {
            before.entry(spec.parent).or_default().push(h);
        }
        for target in spec.effects.iter().filter_map(|e| e.send_target.as_deref()) {
            if let Some(&group) = owner.get(target) {
                before.entry(group).or_default().push(h);
            }
        }
    }
    // The depth order already satisfies the parent edges, so this keeps it unless a send
    // requires a later group first.
    let mut done = HashSet::new();
    let mut result = Vec::with_capacity(order.len());
    while result.len() < order.len() {
        let next = order.iter().copied().find(|h| {
            !done.contains(h)
                && before
                    .get(h)
                    .is_none_or(|b| b.iter().all(|d| done.contains(d)))
        });
        let Some(next) = next else {
            anyhow::bail!("cyclic AudioMixer send routing");
        };
        done.insert(next);
        result.push(next);
    }
    Ok(result)
}

fn frame_of(seconds: f64, rate: u32) -> u64 {
    (seconds.max(0.0) * rate as f64).round() as u64
}

fn source_spatial(
    p: &SourceParams,
    listener: &Listener,
    config: MixerConfig,
    speakers: Option<&SpeakerLayout>,
) -> spatial::Spatialized {
    let (sampled_listener, doppler_listener_position) = p
        .listener_snapshot
        .map_or((*listener, None), |(pose, doppler)| (pose, Some(doppler)));
    spatial::spatialize_in(
        &SourceSpatial {
            position: p.position,
            velocity: p.velocity,
            doppler_listener_position,
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
        &sampled_listener,
        config.rolloff_scale,
        config.doppler_factor,
        p.has(source_flags::SPATIAL_STEREO),
        speakers,
    )
}

impl Mixer {
    fn listener_return_capacity(config: &MixerConfig) -> usize {
        // Offline output cannot render a DSP period larger than the capture resource limit.
        // Invalid periods must not reserve storage before the control path checks their size.
        (config.buffer_size.max(1) as usize)
            .checked_mul(config.output_channels())
            .filter(|&samples| samples <= capture::MAX_SAMPLES)
            .unwrap_or(0)
    }

    fn input_fade_curve(buffer: u32) -> Vec<f32> {
        let frames = buffer.max(1);
        let mut gain = ParameterRamp::new(0.0);
        (0..frames.min(unbound_compat::AudioOutputRules::MAXIMUM_BUFFER))
            .map(|_| gain.next(1.0, frames))
            .collect()
    }

    pub fn new(config: MixerConfig) -> Self {
        Self {
            input_fade_curve: Self::input_fade_curve(config.buffer_size),
            config,
            output_rules: Default::default(),
            defer_voice_submission: false,
            frames: 0,
            clips: HashMap::new(),
            sources: HashMap::new(),
            voice_priority: Vec::new(),
            groups: HashMap::new(),
            group_nodes: HashMap::new(),
            group_order: Vec::new(),
            sidechains: HashMap::new(),
            activated_mixers: HashSet::new(),
            active_roots: HashSet::new(),
            capture_sinks: HashMap::new(),
            listener: Listener::default(),
            listener_volume: 1.0,
            listener_paused: false,
            has_listener: true,
            listener_filter: false,
            listener_filters: Vec::new(),
            listener_chain: FilterChain::default(),
            listener_rejected: false,
            zone: None,
            zone_rejected: None,
            zone_input: vec![0.0; BLOCK * 2],
            zone_extra: vec![0.0; MAX_EXTRA_PAIRS * BLOCK * 2],
            surround_effects: false,
            listener_chain_measured: false,
            listener_plan: 0,
            listener_bypass: vec![0.0; BLOCK * 2],
            listener_layout: Vec::with_capacity(BLOCK * MAX_OUTPUT_CHANNELS),
            speakers: SpeakerLayout::of(config.output_channels()),
            mix_extra: vec![0.0; MAX_EXTRA_PAIRS * BLOCK * 2],
            listener_bypass_extra: vec![0.0; MAX_EXTRA_PAIRS * BLOCK * 2],
            source_extra: vec![0.0; MAX_EXTRA_PAIRS * BLOCK * 2],
            sidechain_lanes: (0..MAX_EXTRA_PAIRS).map(|_| HashMap::new()).collect(),
            listener_lanes: Vec::new(),
            warned_surround_reverb: false,
            listener_output: Ring::new(),
            callback: None,
            capture_listener_batch: false,
            capture_listener_return: vec![0.0; Self::listener_return_capacity(&config)],
            capture_listener_return_used: false,
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
                effect.validate_in_group(config, node.spec.bypass)?;
            }
        }
        for source in self.sources.values() {
            FilterChain::validate(
                &source.params.filters,
                config.sample_rate,
                config.buffer_size,
            )?;
        }
        Ok(())
    }

    pub fn reconfigure(&mut self, config: MixerConfig) -> anyhow::Result<()> {
        self.validate_configuration(&config)?;
        let seconds = self.dsp_time();
        if self.config.buffer_size != config.buffer_size {
            self.input_fade_curve = Self::input_fade_curve(config.buffer_size);
        }
        self.capture_listener_return
            .resize(Self::listener_return_capacity(&config), 0.0);
        self.config = config;
        self.speakers = SpeakerLayout::of(config.output_channels());
        // Channels of the previous layout must not reach the new one.
        for buffer in [
            &mut self.mix_extra,
            &mut self.listener_bypass_extra,
            &mut self.source_extra,
        ] {
            buffer.fill(0.0);
        }
        self.frames = frame_of(seconds, config.sample_rate);
        for node in self.group_nodes.values_mut() {
            node.rendered = 0;
            node.suspended = false;
            node.last_active = self.frames;
            node.energy = 0.0;
            node.effects.clear();
            node.update(node.spec.clone(), &config)?;
        }
        self.listener_chain
            .reconfigure(config.sample_rate, config.buffer_size)?;
        self.listener_lanes.clear();
        sync_lanes(
            &mut self.listener_lanes,
            &self.listener_chain,
            config.extra_pairs(),
            &config,
        )?;
        configure_chains(
            &mut self.listener_chain,
            &mut self.listener_lanes,
            &config,
            self.surround_effects,
        );
        self.warn_surround_reverb();
        // The next frame packet sets the zone up again at the new rate.
        self.zone = None;
        for source in self.sources.values_mut() {
            source.main = None;
            source.one_shots.clear();
            source.completed_shots.clear();
            source.completed_main = None;
            source.pending_position = 0.0;
            source.generator = GeneratorState::restarted(std::mem::take(&mut source.generator));
            source.generator_gain = None;
            if !source.bypass_dry.is_empty() {
                source
                    .bypass_dry
                    .resize(config.buffer_size.max(BLOCK as u32) as usize * 2, 0.0);
                source.reserve_buffers(&config);
            }
            source.chain_tail = None;
            source.filtered.clear();
            source.filtered_offset = 0;
            source.filter_started = false;
            source.filter_paused = false;
            source
                .chain
                .reconfigure(config.sample_rate, config.buffer_size)?;
            source.lanes.clear();
            sync_lanes(
                &mut source.lanes,
                &source.chain,
                config.extra_pairs(),
                &config,
            )?;
            configure_chains(
                &mut source.chain,
                &mut source.lanes,
                &config,
                source.params.has(source_flags::SURROUND_EFFECTS),
            );
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

    fn prepare_source(&mut self, handle: u64) {
        self.sources.entry(handle).or_default();
        let additional = self.sources.len().saturating_sub(self.voice_priority.len());
        self.voice_priority.reserve(additional);
    }

    pub fn update_source(&mut self, handle: u64, mut params: SourceParams) {
        self.prepare_source(handle);
        let rate = self.config.sample_rate;
        let source = self.sources.entry(handle).or_default();
        if params.has(source_flags::FILTER) || !params.filters.is_empty() {
            source
                .bypass_dry
                .resize(self.config.buffer_size.max(BLOCK as u32) as usize * 2, 0.0);
            source.reserve_buffers(&self.config);
        }
        if params.has(source_flags::KEEP_OUTPUT) && source.output.is_none() {
            source.output = Some(Ring::new());
        }
        // A refused chain leaves the previous, valid one (and its state) in place; the rest of
        // the packet still applies.
        if let Some(error) = params.filters_rejected.take() {
            tracing::error!("AudioSource {handle}: filter components rejected: {error}");
            params.filters = source.params.filters.clone();
        } else if params.filters != source.params.filters {
            if let Err(error) =
                FilterChain::validate(&params.filters, rate, self.config.buffer_size)
            {
                tracing::error!("AudioSource {handle}: filter components rejected: {error:#}");
                params.filters = source.params.filters.clone();
            } else if let Err(error) = source.chain.apply(
                &params.filters,
                rate,
                self.config.buffer_size,
                params.has(source_flags::EFFECT_TRANSITIONS),
            ) {
                tracing::error!("AudioSource {handle}: filter components dropped: {error:#}");
                params.filters = Vec::new();
            } else {
                // The extra channel pairs take the same components, each keeping its state;
                // a pair that cannot rebuilds every pair from the front chain.
                let failed = source.lanes.iter_mut().any(|lane| {
                    lane.apply(
                        &params.filters,
                        rate,
                        self.config.buffer_size,
                        params.has(source_flags::EFFECT_TRANSITIONS),
                    )
                    .is_err()
                });
                if failed {
                    source.lanes.clear();
                }
            }
        }
        if let Err(error) = sync_lanes(
            &mut source.lanes,
            &source.chain,
            self.config.extra_pairs(),
            &self.config,
        ) {
            tracing::error!("AudioSource {handle}: surround components dropped: {error:#}");
            source.lanes.clear();
        }
        configure_chains(
            &mut source.chain,
            &mut source.lanes,
            &self.config,
            params.has(source_flags::SURROUND_EFFECTS),
        );
        source.params = params;
    }

    /// The listener's components from the frame packet: an unchanged list keeps the chain and
    /// its state; a refused one (reported once) runs none.
    pub fn update_listener_filters(
        &mut self,
        filters: anyhow::Result<Vec<AudioFilterDescriptor>>,
        transitions: bool,
        surround: bool,
    ) {
        let filters = match filters {
            Ok(filters) => filters,
            Err(error) => {
                if !self.listener_rejected {
                    tracing::error!("AudioListener filter components rejected: {error:#}");
                }
                self.listener_rejected = true;
                self.listener_filters.clear();
                self.listener_chain = FilterChain::default();
                self.listener_lanes.clear();
                return;
            }
        };
        self.listener_rejected = false;
        if filters == self.listener_filters {
            return;
        }
        let rate = self.config.sample_rate;
        if let Err(error) =
            FilterChain::validate(&filters, rate, self.config.buffer_size).and_then(|()| {
                self.listener_chain
                    .apply(&filters, rate, self.config.buffer_size, transitions)
            })
        {
            tracing::error!("AudioListener filter components dropped: {error:#}");
            self.listener_chain = FilterChain::default();
        }
        let failed = self.listener_lanes.iter_mut().any(|lane| {
            lane.apply(&filters, rate, self.config.buffer_size, transitions)
                .is_err()
        });
        if failed {
            self.listener_lanes.clear();
        }
        if sync_lanes(
            &mut self.listener_lanes,
            &self.listener_chain,
            self.config.extra_pairs(),
            &self.config,
        )
        .is_err()
        {
            self.listener_lanes.clear();
        }
        configure_chains(
            &mut self.listener_chain,
            &mut self.listener_lanes,
            &self.config,
            surround,
        );
        self.listener_filters = filters;
        for filter in &mut self.listener_filters {
            filter.stage &= !filter_stage::HISTORY_COMMANDS;
        }
    }

    /// The reverb zones from the frame packet: none (kind 0) removes the zone reverb; the same
    /// `id` keeps its histories and retunes it when its parameters changed.
    pub fn update_zone(&mut self, d: &AudioFilterDescriptor, measured_startup: bool) {
        if d.kind != filter_kind::REVERB {
            self.zone = None;
            return;
        }
        if !d.reverb.iter().all(|v| v.is_finite()) {
            if self.zone_rejected != Some(d.id) {
                tracing::error!(
                    "AudioReverbZone parameters must be finite; the reverb zones are silent"
                );
            }
            self.zone_rejected = Some(d.id);
            self.zone = None;
            return;
        }
        self.zone_rejected = None;
        let spec = sfx_spec(&d.reverb);
        let rate = self.config.sample_rate;
        if let Some(zone) = &mut self.zone
            && zone.id == d.id
            && zone.measured_startup == measured_startup
            && zone.reverb.configuration() == (rate, self.config.buffer_size.max(1))
        {
            if zone.spec != spec {
                if measured_startup {
                    zone.reverb.tune(&spec);
                } else {
                    zone.reverb.tune_immediate(&spec);
                }
                zone.spec = spec;
            }
            return;
        }
        self.zone = match SfxReverb::with_measured_response(
            rate,
            self.config.buffer_size,
            measured_startup,
        ) {
            Ok(mut reverb) => {
                if measured_startup {
                    reverb.initialize_zone_return();
                }
                reverb.tune(&spec);
                Some(ZoneReverb {
                    id: d.id,
                    measured_startup,
                    spec,
                    reverb,
                })
            }
            Err(error) => {
                tracing::error!("AudioReverbZone reverb unavailable: {error:#}");
                None
            }
        };
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
        if let Some(node) = self
            .group_nodes
            .get(&spec.handle)
            .filter(|n| n.rendered > 0)
        {
            fn static_wet_effects(group: &GroupSpec) -> impl Iterator<Item = (usize, &EffectSpec)> {
                group.effects.iter().enumerate().filter(|(_, effect)| {
                    effect.mix_level.is_some()
                        && EffectKind::parse(&effect.kind).is_some_and(EffectKind::static_wet)
                })
            }
            let previous_wet_count = static_wet_effects(&node.spec).count();
            let next_wet_count = static_wet_effects(&spec).count();
            anyhow::ensure!(
                (previous_wet_count == 0 && next_wet_count == 0)
                    || (previous_wet_count == next_wet_count
                        && static_wet_effects(&node.spec)
                            .zip(static_wet_effects(&spec))
                            .all(|((i, previous), (j, next))| i == j
                                && previous.static_wet_settings_match(next))
                        && node.spec.bypass == spec.bypass),
                "AudioMixer wet mixing live parameter, placement and bypass changes are unmeasured"
            );
            for kind in EffectKind::TRANSITION_LIMITED {
                fn units(
                    group: &GroupSpec,
                    kind: EffectKind,
                ) -> impl Iterator<Item = (usize, &EffectSpec)> {
                    group.effects.iter().enumerate().filter(move |(_, e)| {
                        e.kind == kind.name()
                            && (kind.layout_limited() || e.effect_transitions_measured)
                    })
                }
                let mut previous = units(&node.spec, kind);
                let mut next = units(&spec, kind);
                let counts_match = units(&node.spec, kind).count() == units(&spec, kind).count();
                let same = previous
                    .by_ref()
                    .zip(next.by_ref())
                    .all(|((i, a), (j, b))| {
                        i == j && a.live_identity_matches(b) && a.kernel_settings_match(b)
                    });
                anyhow::ensure!(
                    counts_match
                        && same
                        && (units(&node.spec, kind).next().is_none()
                            || node.spec.bypass == spec.bypass),
                    "AudioMixer {} live parameter, placement and bypass changes are unmeasured",
                    kind.name()
                );
            }
        }
        anyhow::ensure!(
            spec.suspend_threshold.is_finite() && spec.suspend_threshold >= 0.0,
            "invalid AudioMixer suspension threshold"
        );
        for effect in &spec.effects {
            effect.validate_in_group(&self.config, spec.bypass)?;
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
        let mut specs: HashMap<u64, &GroupSpec> = self
            .group_nodes
            .iter()
            .map(|(h, n)| (*h, &n.spec))
            .collect();
        specs.insert(handle, &spec);
        let order = processing_order(&specs)?;
        let targets: Vec<String> = spec
            .effects
            .iter()
            .filter_map(|e| e.send_target.clone())
            .collect();
        let node = self.group_nodes.entry(handle).or_insert_with(|| GroupNode {
            spec: spec.clone(),
            rendered: 0,
            input_fade: false,
            effects: Vec::new(),
            buffer: vec![0.0; BLOCK * 2],
            lanes: Vec::new(),
            suspended: false,
            last_active: self.frames,
            energy: 0.0,
        });
        node.update(spec, &self.config)?;
        self.warn_surround_reverb();
        for target in targets {
            for lane in &mut self.sidechain_lanes {
                lane.entry(target.clone())
                    .or_insert_with(|| vec![0.0; BLOCK * 2]);
            }
            self.sidechains
                .entry(target)
                .or_insert_with(|| vec![0.0; BLOCK * 2]);
        }
        self.group_order = order;
        self.active_roots.reserve(
            self.group_nodes
                .len()
                .saturating_sub(self.active_roots.len()),
        );
        self.activated_mixers.reserve(
            self.group_nodes
                .len()
                .saturating_sub(self.activated_mixers.len()),
        );
        Ok(())
    }

    /// Unity 6000.0.71f1 observes suspension on audio updates, using the rendered DSP clock.
    /// Burst renders and no-render updates in tests/projects/mixersuspend constrain both clocks.
    pub fn update_suspension(&mut self) {
        // Public Play can wake a mixer immediately, but its routed voice is submitted by
        // the audio update. Burst renders without updates do not submit a new voice.
        for source in self.sources.values_mut() {
            // Component completion is published on the audio update. A decoded
            // main keeps its final position until that update, while retirement
            // of the real voice and completed one-shots follows the DSP frontier.
            source.completed_shots.retain(|end| self.frames <= *end);
            if let Some(end) = source.completed_main {
                if self.frames >= end {
                    source.pending_position = 0.0;
                }
                if self.frames > end {
                    source.completed_main = None;
                }
            }
            if let Some(voice) = &mut source.main {
                voice.submitted = true;
                voice.start_pending = false;
            }
            for voice in &mut source.one_shots {
                voice.submitted = true;
            }
        }
        self.refresh_active_mixer_roots();
        for (handle, node) in &mut self.group_nodes {
            if node.spec.mixer != *handle {
                continue;
            }
            if self.active_roots.contains(handle) || !node.spec.enable_suspend {
                node.suspended = false;
                node.last_active = self.frames;
            } else if self.frames.saturating_sub(node.last_active) > self.config.sample_rate as u64
            {
                node.suspended = true;
            }
        }
    }

    fn refresh_active_mixer_roots(&mut self) {
        self.active_roots.clear();
        // A playing clipless callback is activity even when it generates silence.
        for source in self.sources.values().filter(|s| s.active()) {
            let mut handle = source.params.group;
            for _ in 0..self.group_nodes.len() {
                let Some(group) = self.group_nodes.get(&handle) else {
                    break;
                };
                // A muted route contributes no activity to its mixer, even while its source
                // remains playing and real. A silent clip on an unmuted route does. The
                // independent lifecycle probe makes this distinction audible with a sibling Echo.
                if group.spec.muted {
                    break;
                }
                if handle == group.spec.mixer && !self.active_roots.insert(handle) {
                    break;
                }
                handle = group.spec.parent;
            }
        }
    }

    /// SFX Reverb in a layout wider than stereo runs one instance per channel pair
    /// (unmeasured). Says so once.
    fn warn_surround_reverb(&mut self) {
        if self.config.extra_pairs() > 0
            && !self.warned_surround_reverb
            && self.group_nodes.values().any(|node| {
                node.effects.iter().any(|e| {
                    e.kind() == Some(EffectKind::SfxReverb) && !e.spec.surround_effects_measured
                })
            })
        {
            self.warned_surround_reverb = true;
            tracing::warn!(
                "audio: SFX Reverb in surround output reverberates each channel pair on its own \
                 (unmeasured)"
            );
        }
    }

    /// Reports admission before a later render can change the source's voice counts.
    pub fn command(&mut self, handle: u64, cmd: &AudioSourceCommand) -> bool {
        self.prepare_source(handle);
        let now = self.frames;
        let rate = self.config.sample_rate;
        let output_rules = self.output_rules;
        let callback = self.callback;
        let Self {
            sources,
            clips,
            group_nodes,
            defer_voice_submission,
            ..
        } = self;
        let source = sources.entry(handle).or_default();
        let deferred = *defer_voice_submission
            || group_nodes
                .get(&source.params.group)
                .is_some_and(|n| n.spec.mixer != 0);
        source.component_playback = deferred;
        let start_frame = |time: f64| -> u64 {
            if time < 0.0 {
                now + output_rules.scheduled_frame(-time, rate)
            } else {
                output_rules.scheduled_frame(time, rate).max(now)
            }
        };
        match cmd.kind {
            command::PLAY => {
                // Play on a paused source resumes it (as UnPause does).
                if let Some(main) = &mut source.main
                    && main.paused
                {
                    main.paused = false;
                    source.filter_paused = false;
                    source.one_shots.iter_mut().for_each(|v| v.paused = false);
                    return true;
                }
                source.played = true;
                source.components_stopped = false;
                source.filter_paused = false;
                source.completed_main = None;
                let clip_handle = source.params.clip;
                if clip_handle == 0 && source.params.has(source_flags::FILTER) {
                    source.generator =
                        GeneratorState::restarted(std::mem::take(&mut source.generator));
                    // Native clipless generators ignore both scheduled boundaries.
                    let mut voice = Voice::new(0, 0.0, now, 1.0);
                    voice.submitted = !deferred;
                    source.main = Some(voice);
                    return true;
                }
                let Some(clip) = clips.get(&clip_handle) else {
                    source.main = None;
                    return false;
                };
                let mut position = source.pending_position;
                source.pending_position = 0.0;
                if position == 0.0 && source.params.pitch < 0.0 && !clip.stream {
                    position = clip.frames as f64;
                }
                // A second Play before submission restarts the pending voice immediately.
                // Unlike replay of an already submitted voice, this includes the explicit
                // Play following playOnAwake on enable. The independent submission probe
                // distinguishes replay, Stop/Play, two Play calls and disable/enable.
                let restart_pending = source.main.as_ref().is_some_and(|v| !v.submitted);
                let mut voice = Voice::new(clip_handle, position, start_frame(cmd.time), 1.0);
                voice.submitted = !deferred || restart_pending;
                voice.start_pending = deferred;
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
                source.completed_shots.clear();
                source.completed_main = None;
                source.pending_position = 0.0;
                source.generator = GeneratorState::restarted(std::mem::take(&mut source.generator));
                source.filtered.clear();
                source.filtered_offset = 0;
                source.filter_started = false;
                source.filter_paused = false;
                source.played = true;
                // A generator played again ramps its source gain up from silence. Unity
                // 6000.0.71f1, tests/projects/audiodistortion script_generator_quiet_restart.
                source.generator_gain = None;
                // Explicit Stop freezes histories even across idle buffers. Natural completion
                // still cuts the tail and clears them. Unity 6000.0.71f1,
                // tests/projects/audiodistortion component_lifecycle controls.
                source.components_stopped = source.params.has(source_flags::COMPONENT_HISTORY);
                source.chain_tail = None;
            }
            command::RESET_COMPONENTS => {
                source.chain.restart();
                source.lanes.iter_mut().for_each(FilterChain::restart);
            }
            command::STOP_MAIN => {
                source.completed_main = None;
                source.main = None;
                source.pending_position = 0.0;
            }
            command::PAUSE => {
                source.filter_paused = true;
                if let Some(main) = &mut source.main {
                    main.paused = true;
                }
                source.one_shots.iter_mut().for_each(|v| v.paused = true);
            }
            command::UNPAUSE => {
                source.filter_paused = false;
                if let Some(main) = &mut source.main {
                    main.paused = false;
                }
                source.one_shots.iter_mut().for_each(|v| v.paused = false);
            }
            command::PLAY_ONE_SHOT => {
                source.components_stopped = false;
                let Some(clip) = clips.get(&cmd.clip) else {
                    return false;
                };
                let position = if source.params.pitch < 0.0 && !clip.stream {
                    clip.frames as f64
                } else {
                    0.0
                };
                source.played = true;
                source.filter_paused = false;
                let mut voice = Voice::new(cmd.clip, position, now, cmd.volume_scale);
                voice.submitted = !deferred;
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
                    && !(output_rules.scheduled_start_times && main.start_pending)
                {
                    main.start_at = start_frame(cmd.time);
                }
            }
            command::SET_SCHEDULED_END => {
                if let Some(main) = &mut source.main
                    && main.clip != 0
                {
                    main.end_at = (cmd.time.max(0.0) * rate as f64) as u64;
                }
            }
            _ => return false,
        }
        true
    }

    pub fn source_state(&self, handle: u64) -> AudioSourceState {
        let Some(source) = self.sources.get(&handle) else {
            return AudioSourceState::default();
        };
        let mut state = AudioSourceState {
            main: voice_state::NONE,
            one_shots: (source.one_shots.len() + source.completed_shots.len()) as i32,
            is_virtual: (source.is_virtual
                || (source.main.is_none()
                    && source.one_shots.is_empty()
                    && source.completed_shots.is_empty()
                    && source.completed_main.is_none()
                    && (source.params.clip != 0 || source.played))) as i32,
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
            length <= capture::MAX_SAMPLES,
            "invalid AudioRenderer buffer length"
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
        // Buffers hold the output layout's channels; the graph renders the front pair.
        let channels = self.config.output_channels();
        let buffer = self.config.buffer_size.max(1) as usize;
        let frames = length / (buffer * channels) * buffer;
        for request in sinks {
            self.capture_sinks.insert(
                request.handle,
                capture::Sink {
                    request,
                    samples: Vec::with_capacity(frames * channels),
                },
            );
        }
        let discarded = self.discard_output;
        self.discard_output = false;
        // Native AudioRenderer advances and writes whole DSP buffers; any partial suffix is left
        // untouched in the caller's NativeArray, including buffers shorter than one DSP block.
        let mut samples = vec![0.0; frames * channels];
        // Preserve the internal DSP chunks and their arithmetic. Native capture delivers the
        // listener at the configured DSP-buffer boundary, as the submission profile records.
        // This renders no extra samples and leaves partial caller buffers untouched.
        for block in samples.chunks_mut(buffer * channels) {
            self.render_capture_block(block);
        }
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

    /// Prepares callback arrays and source scratch on the control thread before closing output.
    pub fn prepare_capture_block(&mut self, token: u64) -> anyhow::Result<()> {
        let frames = self.config.buffer_size.max(1) as usize;
        let samples = frames * 2;
        let prepare = |count: usize| -> anyhow::Result<()> {
            if count > 0
                && let Some(cb) = self.callback
            {
                let mut ready = 0.0;
                cb(
                    callback::PREPARE_RENDER_BUFFER,
                    token,
                    &mut ready,
                    count as i32,
                    1,
                    0,
                );
                anyhow::ensure!(
                    ready == 1.0,
                    "managed audio callback buffer preparation failed"
                );
            }
            Ok(())
        };
        prepare(samples)?;
        prepare(frames.min(BLOCK) * 2)?;
        prepare((frames % BLOCK) * 2)?;
        // Scripts receive the output layout's channels (see `call_in_layout`).
        let channels = self.config.output_channels();
        if channels != 2 {
            prepare(frames * channels)?;
            prepare(frames.min(BLOCK) * channels)?;
            prepare((frames % BLOCK) * channels)?;
        }
        for source in self.sources.values_mut() {
            if source.params.has(source_flags::FILTER) {
                source
                    .filtered
                    .try_reserve(samples.saturating_sub(source.filtered.len()))?;
                let layout = frames * channels;
                source
                    .layout_scratch
                    .try_reserve(layout.saturating_sub(source.layout_scratch.len()))?;
                if source.filter_only() {
                    source
                        .generator
                        .current
                        .try_reserve(layout.saturating_sub(source.generator.current.len()))?;
                    source
                        .generator
                        .next
                        .try_reserve(layout.saturating_sub(source.generator.next.len()))?;
                }
            }
            source.completed_shots.try_reserve(source.one_shots.len())?;
            let pitch = PlaybackPitch {
                source: source.params.pitch,
                group: self
                    .groups
                    .get(&source.params.group)
                    .map_or(1.0, |group| group.pitch),
                doppler: source_spatial(&source.params, &self.listener, self.config, None).pitch,
            };
            for voice in source.main.iter_mut().chain(&mut source.one_shots) {
                if let (Some(stream), Some(clip)) = (&mut voice.stream, self.clips.get(&voice.clip))
                {
                    let channels = clip.channels.max(1);
                    let step = pitch.step(clip.rate, self.config.sample_rate.max(1), false, false);
                    // Reserve the furthest read plus one complete read-ahead chunk. Trimming
                    // can only reduce this bound; preparation performs no PCM reads.
                    let retained = (voice.position - stream.base as f64).max(0.0);
                    let needed = ((retained + frames as f64 * step.abs()).ceil()
                        + STREAM_CHUNK as f64
                        + 2.0)
                        * channels as f64;
                    anyhow::ensure!(
                        needed.is_finite() && needed <= capture::MAX_SAMPLES as f64,
                        "closing streamed output exceeds the supported allocation limit"
                    );
                    stream
                        .buffer
                        .try_reserve((needed as usize).saturating_sub(stream.buffer.len()))?;
                    prepare(STREAM_CHUNK.min(clip.frames).max(1) * channels)?;
                    prepare((clip.frames % STREAM_CHUNK) * channels)?;
                    prepare((clip.frames.saturating_sub(stream.cursor) % STREAM_CHUNK) * channels)?;
                }
            }
        }
        Ok(())
    }

    pub fn capture_buffer_scope(&self, token: u64, enter: bool) {
        if let Some(cb) = self.callback {
            cb(
                callback::RENDER_BUFFER_SCOPE,
                token,
                std::ptr::null_mut(),
                0,
                0,
                i64::from(enter),
            );
        }
    }

    pub fn release_capture_buffers(&self, token: u64) {
        if let Some(cb) = self.callback {
            cb(
                callback::RELEASE_RENDER_BUFFERS,
                token,
                std::ptr::null_mut(),
                0,
                0,
                0,
            );
        }
    }

    // Walk each component and script once, preserving Inspector order across DSP boundaries.
    fn listener_pipeline(&mut self, mut apply: impl FnMut(&mut Self, ListenerStep)) {
        let mut first = 0;
        let mut script = 0;
        while first < self.listener_chain.filters.len() {
            let stage = self.listener_chain.filters[first].stage;
            let before = filter_stage::preceding_scripts(stage);
            if before > script {
                apply(self, ListenerStep::Scripts(script..before));
            }
            let mut end = first + 1;
            while end < self.listener_chain.filters.len()
                && self.listener_chain.filters[end].stage == stage
            {
                end += 1;
            }
            apply(self, ListenerStep::Components(first..end));
            first = end;
            script = before;
        }
        apply(self, ListenerStep::Scripts(script..i32::MAX as usize));
    }

    fn capture_listener_pipeline(&mut self, block: &mut [f32]) {
        let channels = self.config.output_channels();
        self.listener_pipeline(|mixer, step| match step {
            ListenerStep::Scripts(range) => {
                if let Some(callback) = mixer.callback {
                    callback(
                        callback::LISTENER_FILTER,
                        mixer.listener_plan,
                        block.as_mut_ptr(),
                        (block.len() / channels) as i32,
                        channels as i32,
                        listener_script_range(range.start, range.end),
                    );
                }
            }
            ListenerStep::Components(range) => {
                for chunk in block.chunks_mut(BLOCK * channels) {
                    let frames = chunk.len() / channels;
                    let mut lanes = Lanes {
                        samples: &mut mixer.mix_extra,
                        stride: BLOCK * 2,
                        offset: 0,
                        channels: channels.saturating_sub(2),
                    };
                    fold(
                        chunk,
                        &mut mixer.mix[..frames * 2],
                        (channels > 2).then_some(&mut lanes),
                    );
                    mixer.listener_chain.process_range_layout(
                        &mut mixer.listener_lanes,
                        &mut mixer.mix[..frames * 2],
                        (channels > 2).then_some(&mut lanes),
                        range.clone(),
                        None,
                        false,
                    );
                    spread(
                        &mixer.mix[..frames * 2],
                        (channels > 2).then(|| lanes.view()),
                        chunk,
                        channels == 1,
                    );
                }
            }
        });
    }

    fn live_listener_pipeline(&mut self, frames: usize) {
        let channels = self.config.output_channels();
        self.listener_pipeline(|mixer, step| {
            let mut lanes = Lanes {
                samples: &mut mixer.mix_extra,
                stride: BLOCK * 2,
                offset: 0,
                channels: channels.saturating_sub(2),
            };
            match step {
                ListenerStep::Scripts(range) => {
                    if let Some(callback) = mixer.callback {
                        call_in_layout_arg(
                            callback,
                            callback::LISTENER_FILTER,
                            mixer.listener_plan,
                            &mut mixer.mix[..frames * 2],
                            (channels > 2).then_some(&mut lanes),
                            channels,
                            &mut mixer.listener_layout,
                            listener_script_range(range.start, range.end),
                        );
                    }
                }
                ListenerStep::Components(range) => mixer.listener_chain.process_range_layout(
                    &mut mixer.listener_lanes,
                    &mut mixer.mix[..frames * 2],
                    (channels > 2).then_some(&mut lanes),
                    range,
                    None,
                    false,
                ),
            }
        });
    }

    /// Processes one complete offline output block in the output layout's channels, including
    /// the listener's borrowed buffer: its scripts see, and may write, every channel.
    pub fn render_capture_block(&mut self, block: &mut [f32]) {
        let channels = self.config.output_channels();
        assert!(
            block.len() <= self.capture_listener_return.len(),
            "offline block exceeds its prepared DSP period"
        );
        self.capture_listener_return[..block.len()].fill(0.0);
        self.capture_listener_return_used = false;
        self.capture_listener_batch = true;
        self.render(block, channels);
        if self.listener_chain_measured
            && self.listener_filter
            && self.has_listener
            && !self.listener_chain.filters.is_empty()
        {
            self.capture_listener_pipeline(block);
        } else {
            if self.listener_filter
                && self.has_listener
                && let Some(cb) = self.callback
            {
                cb(
                    callback::LISTENER_FILTER,
                    self.listener_plan,
                    block.as_mut_ptr(),
                    (block.len() / channels) as i32,
                    channels as i32,
                    0,
                );
            }
        }
        if self.capture_listener_return_used {
            for (sample, value) in block.iter_mut().zip(&self.capture_listener_return) {
                *sample += *value;
            }
        }
        // The output ring keeps the stereo lanes; the mixing buffer is free between blocks.
        for chunk in block.chunks(BLOCK * channels) {
            let frames = chunk.len() / channels;
            fold(chunk, &mut self.mix[..frames * 2], None);
            self.listener_output.push(&self.mix[..frames * 2]);
        }
        self.capture_listener_batch = false;
    }

    /// Renders `out.len() / channels` frames of interleaved output.
    pub fn render(&mut self, out: &mut [f32], channels: usize) {
        let channels = channels.max(1);
        let frames = out.len() / channels;
        let mut done = 0;
        while done < frames {
            let n = (frames - done).min(BLOCK);
            let deferred = self.render_block(n);
            let mono = self.config.speaker_mode == speaker_mode::MONO;
            let extra = (self.config.extra_pairs() > 0).then(|| Extra {
                samples: &self.mix_extra,
                stride: BLOCK * 2,
                channels: self.config.output_channels() - 2,
            });
            spread(
                &self.mix[..n * 2],
                extra,
                &mut out[done * channels..(done + n) * channels],
                mono,
            );
            if deferred.bypass || deferred.zone {
                self.mix[..n * 2].fill(0.0);
                self.mix_extra.fill(0.0);
                if deferred.bypass {
                    sum_listener_return(
                        &mut self.mix,
                        &mut self.mix_extra,
                        &self.listener_bypass,
                        &self.listener_bypass_extra,
                        n,
                        self.config.extra_pairs(),
                    );
                }
                if deferred.zone {
                    sum_listener_return(
                        &mut self.mix,
                        &mut self.mix_extra,
                        &self.zone_input,
                        &self.zone_extra,
                        n,
                        self.config.extra_pairs(),
                    );
                }
                let extra = (self.config.extra_pairs() > 0).then(|| Extra {
                    samples: &self.mix_extra,
                    stride: BLOCK * 2,
                    channels: self.config.output_channels() - 2,
                });
                spread(
                    &self.mix[..n * 2],
                    extra,
                    &mut self.capture_listener_return[done * channels..(done + n) * channels],
                    mono,
                );
                self.capture_listener_return_used = true;
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

    fn process_zone_return(&mut self, n: usize, pairs: usize) {
        if let Some(zone) = &mut self.zone {
            let input = &mut self.zone_input[..n * 2];
            if self.surround_effects && (pairs > 0 || self.config.output_channels() == 1) {
                let mut lanes = Lanes {
                    samples: &mut self.zone_extra,
                    stride: BLOCK * 2,
                    offset: 0,
                    channels: self.config.output_channels().saturating_sub(2),
                };
                let mut blocks = lanes.pairs(n);
                zone.reverb.process_linked(
                    input,
                    &mut blocks[..pairs],
                    self.config.output_channels(),
                );
            } else {
                zone.reverb.process(input);
            }
        }
    }

    fn add_zone_return(&mut self, n: usize, pairs: usize) {
        self.process_zone_return(n, pairs);
        sum_listener_return(
            &mut self.mix,
            &mut self.mix_extra,
            &self.zone_input,
            &self.zone_extra,
            n,
            pairs,
        );
    }

    fn render_block(&mut self, n: usize) -> DeferredListenerReturn {
        let t0 = self.frames;
        let out_rate = self.config.sample_rate.max(1);
        let callback = self.callback;
        let listener = self.listener;
        let listener_volume = self.listener_volume;
        let listener_paused = self.listener_paused;
        let has_listener = self.has_listener;
        let config = self.config;
        let speakers = self.speakers;
        // A graph first participates when a routed source starts, including a pending
        // voice. Merely loading it or registering a sink does not process its DSP.
        for source in self.sources.values().filter(|s| s.active()) {
            let mut handle = source.params.group;
            for _ in 0..self.group_nodes.len() {
                let Some(node) = self.group_nodes.get(&handle) else {
                    break;
                };
                if handle == node.spec.mixer {
                    self.activated_mixers.insert(handle);
                }
                handle = node.spec.parent;
            }
        }
        // New voices wake immediately, even when no main-thread update intervenes. Waking
        // preserves effect history and the input fade; parameter submissions alone do not wake.
        self.refresh_active_mixer_roots();
        for root in &self.active_roots {
            if let Some(node) = self.group_nodes.get_mut(root)
                && node.suspended
            {
                node.suspended = false;
                node.last_active = self.frames;
            }
        }
        let need_mix = !self.discard_output
            || self.listener_filter
            || !self.listener_chain.filters.is_empty()
            || self.listener_output_wanted
            || !self.group_nodes.is_empty();
        self.mix[..n * 2].fill(0.0);
        for node in self.group_nodes.values_mut() {
            node.buffer[..n * 2].fill(0.0);
            for lane in &mut node.lanes {
                lane.buffer[..n * 2].fill(0.0);
            }
        }
        for sidechain in self.sidechains.values_mut() {
            sidechain[..n * 2].fill(0.0);
        }
        let pairs = config.extra_pairs();
        for (pair, sidechains) in self.sidechain_lanes.iter_mut().enumerate().take(pairs) {
            sidechains.values_mut().for_each(|s| s[..n * 2].fill(0.0));
            self.mix_extra[pair * BLOCK * 2..pair * BLOCK * 2 + n * 2].fill(0.0);
            self.listener_bypass_extra[pair * BLOCK * 2..pair * BLOCK * 2 + n * 2].fill(0.0);
        }

        // Spatialize, then decide which voices are real: the most important `real_voices`.
        let playing = &mut self.voice_priority;
        playing.clear();
        for (&handle, source) in &mut self.sources {
            let p = &source.params;
            let s = source_spatial(p, &listener, config, speakers.as_ref());
            if source.active() {
                playing.push((handle, p.priority, s.audibility * p.volume));
            }
            source.spatial = s;
            source.is_virtual = false;
        }
        if playing.len() > config.real_voices {
            // Partition only: the total ordering selects the same real voices without a
            // full sort or a render-thread allocation. Source summation order is unchanged.
            playing.select_nth_unstable_by(config.real_voices, |a, b| {
                a.1.cmp(&b.1).then(b.2.total_cmp(&a.2)).then(a.0.cmp(&b.0))
            });
            for (index, &(handle, _, _)) in playing.iter().enumerate() {
                self.sources.get_mut(&handle).unwrap().is_virtual = index >= config.real_voices;
            }
        }

        let listener_components = !self.listener_chain.filters.is_empty();
        let script_active = self.listener_filter && has_listener && callback.is_some();
        let measured_script_route = self.surround_effects && script_active;
        let measured_listener_pipeline =
            self.listener_chain_measured && script_active && listener_components;
        let listener_effects = listener_components || measured_script_route;
        let mut listener_bypass_used = false;
        if listener_effects {
            self.listener_bypass[..n * 2].fill(0.0);
        }
        let zone_on = self.zone.is_some() && need_mix;
        let zone_id = self.zone.as_ref().map_or(0, |zone| zone.id);
        if zone_on {
            self.zone_input[..n * 2].fill(0.0);
            self.zone_extra.fill(0.0);
        }
        let Self {
            sources,
            clips,
            groups,
            group_nodes,
            mix,
            source_buffer,
            listener_bypass,
            zone_input,
            zone_extra,
            surround_effects,
            mix_extra,
            listener_bypass_extra,
            source_extra,
            ..
        } = self;
        for (&handle, source) in sources.iter_mut() {
            let flags = source.params.flags;
            let filter = flags & source_flags::FILTER != 0;
            let has_voices = source.main.is_some() || !source.one_shots.is_empty();
            let filter_only = source.filter_only();
            // Native (tests/projects/audioreverb, AudioReverbFilter): after the last voice ends,
            // the components run on to the end of the next DSP buffer, then their tail is cut
            // and the next playback starts them empty.
            let tail = !has_voices && source.chain_tail.is_some_and(|end| t0 < end);
            if !has_voices && !tail && !(filter && source.filter_started) {
                source.chain_tail = None;
                if !source.components_stopped {
                    source.reset_chains();
                }
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
            let s = source.spatial;
            let routed = group_nodes.contains_key(&source.params.group);
            let mut volume = source.params.volume.max(0.0) * if routed { 1.0 } else { group.gain };
            if flags & source_flags::MUTE != 0 || !has_listener {
                volume = 0.0;
            }
            if flags & source_flags::IGNORE_LISTENER_VOLUME == 0 {
                volume *= listener_volume;
            }
            let pitch = PlaybackPitch {
                source: source.params.pitch,
                group: group.pitch,
                doppler: s.pitch,
            };
            let wants_samples = need_mix
                || filter
                || source.output.is_some()
                || flags & source_flags::KEEP_OUTPUT != 0;
            let audible = wants_samples && !source.is_virtual;
            let buffer = &mut source_buffer[..n * 2];
            buffer.fill(0.0);
            // The layout's channels beyond the front pair, beside `buffer`.
            for pair in 0..pairs {
                source_extra[pair * BLOCK * 2..pair * BLOCK * 2 + n * 2].fill(0.0);
            }
            let mut lanes = (pairs > 0).then(|| Lanes {
                samples: &mut source_extra[..],
                stride: BLOCK * 2,
                offset: 0,
                channels: config.output_channels().saturating_sub(2),
            });
            let looping = flags & source_flags::LOOP != 0;
            let layout = config.output_channels();
            let gains = if config.speaker_mode == speaker_mode::MONO {
                VoiceGains::mono_output(&s.mono_output, volume)
            } else if let Some(surround) = &s.surround {
                VoiceGains::surround(surround, volume, layout)
            } else {
                VoiceGains::stereo(
                    s.matrix.map(|g| g * volume),
                    s.mono_matrix.map(|m| m.map(|g| g * volume)),
                    (source.params.spatial_blend <= 0.0
                        && source
                            .params
                            .spatial_blend_curve
                            .iter()
                            .all(|value| *value <= 0.0))
                    .then_some(source.params.pan_stereo),
                )
            };
            let dsp_frames = config.buffer_size.max(1) as u64;
            source.completion_frontier = (t0 + n as u64).div_ceil(dsp_frames) * dsp_frames;
            let generator = filter_only;
            // `AudioSource.bypassEffects`: "Applied from filter components or global listener
            // filters". A paused or not yet submitted source does not advance its components.
            let bypass = flags & source_flags::BYPASS_EFFECTS != 0;
            let separate_return = bypass && flags & source_flags::SPATIAL_STEREO != 0;
            // Inactive components hold their state and silence every unit above them.
            // Unity 6000.0.71f1, tests/projects/audiodistortion live bypass controls.
            let inactive = separate_return && flags & source_flags::COMPONENTS_INACTIVE != 0;
            let last_active = inactive && flags & source_flags::LAST_COMPONENT_ACTIVE != 0;
            let voices_running = source
                .main
                .as_ref()
                .is_some_and(|v| v.submitted && !v.paused)
                || source.one_shots.iter().any(|v| v.submitted && !v.paused);
            if generator {
                // The main generator receives zeros. One-shots retain their own gain
                // and are accumulated downstream of its callback/read-ahead queue.
                if source
                    .main
                    .as_ref()
                    .is_some_and(|v| v.submitted && !v.paused)
                {
                    source.generator.render(
                        handle,
                        buffer,
                        lanes.as_mut(),
                        config.buffer_size.max(1) as usize,
                        layout,
                        callback,
                    );
                    if flags & source_flags::SPATIAL_STEREO != 0 {
                        // Generated channels retain their channel positions; source volume and
                        // distance gain precede the downstream components.
                        source.scale_generator(buffer, lanes.as_mut(), volume * s.audibility);
                    }
                }
                let mut dry = if separate_return {
                    std::mem::take(&mut source.bypass_dry)
                } else {
                    Vec::new()
                };
                if separate_return {
                    dry[..n * 2].fill(0.0);
                }
                let _ = render_source_voices(
                    source,
                    clips,
                    if separate_return {
                        &mut dry[..n * 2]
                    } else {
                        buffer
                    },
                    n,
                    t0,
                    pitch,
                    out_rate,
                    gains,
                    looping,
                    audible,
                    callback,
                    None,
                    // The extra pairs' voices join the generated audio on either route.
                    lanes.as_mut(),
                );
                // Components below the generating script (none may sit above it). Unbound's
                // choice (unmeasured): they also process the source's one-shots.
                if separate_return {
                    for (sample, dry) in buffer.iter_mut().zip(&dry) {
                        *sample += *dry;
                    }
                    source.bypass_dry = dry;
                    // The generated audio is on the dry route; active components run on silence.
                    if voices_running && (!inactive || last_active) {
                        source.run_bypassed_components(
                            buffer,
                            lanes.as_mut(),
                            Some(filter_stage::AFTER_SCRIPTS),
                            last_active,
                        );
                    }
                } else if voices_running {
                    source.process_components(
                        buffer,
                        lanes.as_mut(),
                        Some(filter_stage::AFTER_SCRIPTS),
                        bypass,
                        false,
                    );
                }
            } else if filter {
                if source
                    .main
                    .as_ref()
                    .is_some_and(|v| v.submitted && !v.paused)
                    || source.one_shots.iter().any(|v| v.submitted && !v.paused)
                {
                    source.filter_started = true;
                }
                if source.filter_started && !source.filter_paused {
                    let length = config.buffer_size.max(1) as usize * 2;
                    let mut written = 0;
                    while written < buffer.len() {
                        if source.filtered.len() != length || source.filtered_offset == length {
                            let mut input = std::mem::take(&mut source.filtered);
                            input.resize(length, 0.0);
                            input.fill(0.0);
                            // The extra channel pairs' queue: one buffer length per pair.
                            let mut queue = std::mem::take(&mut source.filtered_extra);
                            queue.resize(pairs * length, 0.0);
                            queue.fill(0.0);
                            source.completion_frontier = t0 + (written + length) as u64 / 2;
                            // Keep voice ramps and interpolation in their existing DSP chunks.
                            for (block, chunk) in input.chunks_mut(BLOCK * 2).enumerate() {
                                let mut chunk_lanes = (pairs > 0).then(|| Lanes {
                                    samples: &mut queue[..],
                                    stride: length,
                                    offset: block * BLOCK * 2,
                                    channels: layout - 2,
                                });
                                let _ = render_source_voices(
                                    source,
                                    clips,
                                    chunk,
                                    chunk.len() / 2,
                                    t0 + (written / 2 + block * BLOCK) as u64,
                                    pitch,
                                    out_rate,
                                    gains,
                                    looping,
                                    audible,
                                    callback,
                                    None,
                                    chunk_lanes.as_mut(),
                                );
                            }
                            let frames = length / 2;
                            let mut queued = (pairs > 0).then(|| Lanes {
                                samples: &mut queue[..],
                                stride: length,
                                offset: 0,
                                channels: layout - 2,
                            });
                            // Bypass keeps the voice on its dry route and supplies silence to
                            // the separate script return. A clipless script remains a generator.
                            // Unity 6000.0.71f1, tests/projects/audiodistortion scripted controls.
                            if separate_return {
                                source.begin_bypass(&mut input);
                                source.bypass_lanes(queued.as_mut(), frames, true);
                            }
                            // The Inspector order is the processing order: components above
                            // the scripts, the scripts, then the components below them. With
                            // inactive components, scripts run (on silence) only below them.
                            // A last component that stays active runs on what reaches it: the
                            // scripts' output when it is their only follower, else silence.
                            let after = source
                                .chain
                                .filters
                                .iter()
                                .filter(|f| f.stage == filter_stage::AFTER_SCRIPTS);
                            let after_count = after.count();
                            let below_inactive =
                                inactive && after_count > 0 && !(last_active && after_count == 1);
                            if !inactive {
                                let bypass = bypass && !separate_return;
                                let stage = Some(filter_stage::BEFORE_SCRIPTS);
                                source.process_components(
                                    &mut input,
                                    queued.as_mut(),
                                    stage,
                                    bypass,
                                    false,
                                );
                            }
                            if let Some(cb) = callback.filter(|_| !below_inactive) {
                                call_in_layout(
                                    cb,
                                    callback::SOURCE_FILTER,
                                    handle,
                                    &mut input,
                                    queued.as_mut(),
                                    layout,
                                    &mut source.layout_scratch,
                                );
                            }
                            let stage = Some(filter_stage::AFTER_SCRIPTS);
                            if !inactive {
                                let bypass = bypass && !separate_return;
                                source.process_components(
                                    &mut input,
                                    queued.as_mut(),
                                    stage,
                                    bypass,
                                    false,
                                );
                            } else if last_active && after_count > 0 {
                                if after_count > 1 {
                                    input.fill(0.0);
                                    if let Some(queued) = queued.as_mut() {
                                        queued.samples.fill(0.0);
                                    }
                                }
                                source.process_components(
                                    &mut input,
                                    queued.as_mut(),
                                    stage,
                                    false,
                                    true,
                                );
                            }
                            if separate_return {
                                source.end_bypass(&mut input);
                                source.bypass_lanes(queued.as_mut(), frames, false);
                            }
                            source.filtered = input;
                            source.filtered_extra = queue;
                            source.filtered_offset = 0;
                        }
                        let count = (buffer.len() - written).min(length - source.filtered_offset);
                        buffer[written..written + count].copy_from_slice(
                            &source.filtered
                                [source.filtered_offset..source.filtered_offset + count],
                        );
                        if let Some(lanes) = lanes.as_mut() {
                            for pair in 0..pairs {
                                let from = pair * length + source.filtered_offset;
                                let to = pair * BLOCK * 2 + written;
                                lanes.samples[to..to + count]
                                    .copy_from_slice(&source.filtered_extra[from..from + count]);
                            }
                        }
                        source.filtered_offset += count;
                        written += count;
                    }
                }
            } else {
                let mut zone_send = ZoneSend {
                    return_id: zone_id,
                    samples: &mut zone_input[..n * 2],
                    extra: (*surround_effects && pairs > 0).then(|| Lanes {
                        samples: zone_extra,
                        stride: BLOCK * 2,
                        offset: 0,
                        channels: layout - 2,
                    }),
                    gain: source.params.reverb_zone_mix,
                    buffer_size: dsp_frames,
                };
                let measured_send = zone_on && flags & source_flags::ZONE_VOICE_SEND != 0;
                let finished_at = render_source_voices(
                    source,
                    clips,
                    buffer,
                    n,
                    t0,
                    pitch,
                    out_rate,
                    gains,
                    looping,
                    audible,
                    callback,
                    measured_send.then_some(&mut zone_send),
                    lanes.as_mut(),
                );
                if voices_running
                    && source.main.is_none()
                    && source.one_shots.is_empty()
                    && !source.chain.filters.is_empty()
                {
                    let frontier = if flags & source_flags::COMPONENT_FILTER_TAIL != 0 {
                        // Device periods may straddle a DSP boundary. Round the last voice's
                        // actual ending sample, not the end of the containing render chunk.
                        finished_at.unwrap_or(t0).div_ceil(dsp_frames) * dsp_frames
                    } else {
                        source.completion_frontier
                    };
                    source.chain_tail = Some(frontier + dsp_frames);
                }
                // Unbound's choice (unmeasured): components see the source's voices after its
                // volume, mute and panning, the signal OnAudioFilterRead receives here.
                if voices_running || tail {
                    if !separate_return {
                        source.process_components(buffer, lanes.as_mut(), None, bypass, false);
                    } else if !inactive || last_active {
                        source.run_bypassed_components(buffer, lanes.as_mut(), None, last_active);
                    }
                }
                if let Some(end) = source.chain_tail.filter(|_| tail) {
                    let keep = (end.saturating_sub(t0) as usize).min(n);
                    buffer[keep * 2..].fill(0.0);
                    if let Some(lanes) = lanes.as_mut() {
                        for pair in 0..pairs {
                            lanes.pair(pair, n)[keep * 2..].fill(0.0);
                        }
                    }
                    if flags & source_flags::COMPONENT_FILTER_TAIL != 0 && t0 + n as u64 >= end {
                        source.reset_chains();
                        source.chain_tail = None;
                    }
                }
            }
            if let Some(ring) = &mut source.output {
                ring.push(buffer);
            }
            // Scripted sources and unmeasured releases retain their earlier send placement.
            // Decoded voices on the measured profile have already sent before components.
            let send = source.params.reverb_zone_mix;
            if zone_on && send > 0.0 && (filter || flags & source_flags::ZONE_VOICE_SEND == 0) {
                for (z, b) in zone_input.iter_mut().zip(buffer.iter()) {
                    *z += *b * send;
                }
                if *surround_effects && let Some(lanes) = lanes.as_mut() {
                    for pair in 0..pairs {
                        let target = &mut zone_extra[pair * BLOCK * 2..pair * BLOCK * 2 + n * 2];
                        for (z, b) in target.iter_mut().zip(lanes.pair(pair, n).iter()) {
                            *z += *b * send;
                        }
                    }
                }
            }
            if need_mix || filter {
                // The managed packet selects the release's listener-bypass rule. Mixer
                // routing joins the final mix and cannot skip the listener's shared chain.
                let bypass_listener =
                    listener_effects && flags & source_flags::BYPASS_LISTENER_EFFECTS != 0;
                let unrouted = if bypass_listener {
                    &mut listener_bypass[..n * 2]
                } else {
                    &mut mix[..n * 2]
                };
                let mut node = group_nodes.get_mut(&source.params.group);
                listener_bypass_used |= bypass_listener && node.is_none();
                if let Some(lanes) = lanes.as_mut() {
                    for pair in 0..pairs {
                        let range = pair * BLOCK * 2..pair * BLOCK * 2 + n * 2;
                        let to = if let Some(node) = node.as_deref_mut() {
                            &mut node.lanes[pair].buffer[..n * 2]
                        } else if bypass_listener {
                            &mut listener_bypass_extra[range]
                        } else {
                            &mut mix_extra[range]
                        };
                        for (m, b) in to.iter_mut().zip(lanes.pair(pair, n).iter()) {
                            *m += *b;
                        }
                    }
                }
                let target = node.map_or(unrouted, |node| &mut node.buffer[..n * 2]);
                for (m, b) in target.iter_mut().zip(buffer.iter()) {
                    *m += *b;
                }
            }
        }
        for &handle in &self.group_order {
            let root = self.group_nodes[&handle].spec.mixer;
            if self
                .group_nodes
                .get(&root)
                .is_some_and(|node| node.suspended)
                || (root != 0 && !self.activated_mixers.contains(&root))
            {
                // A suspended graph does not write registered group sinks. The caller retains
                // its previous block, while the device stream and listener receive silence.
                continue;
            }
            // Frames since the mixer started: its root renders last, so its count is still the
            // block's start. Sidechains stay silent through the mixer's first DSP block, also for
            // groups registered later (tests/projects/mixerduck late source cases).
            let mixer_start = self
                .group_nodes
                .get(&root)
                .map_or(self.group_nodes[&handle].rendered, |r| r.rendered);
            let muted = (config.buffer_size as usize).saturating_sub(mixer_start);
            let node = self.group_nodes.get_mut(&handle).unwrap();
            // The chain's kind/placement/bypass rule is cached when it is configured.
            if node.input_fade {
                let fade_start = if node.spec.input_fade_from_mixer {
                    mixer_start
                } else {
                    node.rendered
                };
                let lanes = node.lanes.iter_mut().map(|lane| &mut lane.buffer);
                for buffer in std::iter::once(&mut node.buffer).chain(lanes) {
                    for (frame, stereo) in buffer[..n * 2].chunks_exact_mut(2).enumerate() {
                        let age = fade_start + frame;
                        let gain = if node.spec.input_fade_from_mixer {
                            self.input_fade_curve.get(age).copied().unwrap_or(1.0)
                        } else {
                            (age as f32 / config.buffer_size.max(1) as f32).min(1.0)
                        };
                        for sample in stereo {
                            *sample *= gain;
                        }
                    }
                }
            }
            node.rendered += n;
            node.process_effects(n, &mut self.sidechains, &mut self.sidechain_lanes, muted);
            if node.spec.muted {
                node.buffer[..n * 2].fill(0.0);
                for lane in &mut node.lanes {
                    lane.buffer[..n * 2].fill(0.0);
                }
            }
            if root == handle {
                let window = config.buffer_size.max(1) as u64;
                let mut start = 0;
                while start < n {
                    let frame = t0 + start as u64;
                    let count = (n - start).min((window - frame % window) as usize);
                    for &sample in &node.buffer[start * 2..(start + count) * 2] {
                        node.energy += sample as f64 * sample as f64;
                    }
                    // Every channel of the layout counts (unmeasured beyond stereo).
                    for lane in &node.lanes {
                        for &sample in &lane.buffer[start * 2..(start + count) * 2] {
                            node.energy += sample as f64 * sample as f64;
                        }
                    }
                    let end = frame + count as u64;
                    if end % window == 0 {
                        // The front pair's two lanes, else every layout channel.
                        let channels = if node.lanes.is_empty() {
                            2
                        } else {
                            config.output_channels() as u64
                        };
                        if node.energy / (window * channels) as f64
                            >= (node.spec.suspend_threshold as f64).powi(2)
                        {
                            node.last_active = end - window;
                        }
                        node.energy = 0.0;
                    }
                    start += count;
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
                // The sink holds the output layout's channels, as the device stream does.
                let channels = config.output_channels();
                let mono = config.speaker_mode == speaker_mode::MONO;
                for frame in 0..n {
                    let (l, r) = (node.buffer[frame * 2], node.buffer[frame * 2 + 1]);
                    if channels == 1 || mono {
                        sink.samples.push((l + r) * 0.5);
                        continue;
                    }
                    sink.samples.extend_from_slice(&[l, r]);
                    for channel in 0..channels - 2 {
                        sink.samples.push(
                            node.lanes
                                .get(channel / 2)
                                .map_or(0.0, |lane| lane.buffer[frame * 2 + channel % 2]),
                        );
                    }
                }
                if sink.request.exclude_from_mix {
                    continue;
                }
            }
            // Remove the buffer temporarily to borrow the parent without copying or allocating.
            let buffer = std::mem::take(&mut node.buffer);
            let lanes = std::mem::take(&mut node.lanes);
            let mut parent_node = self.group_nodes.get_mut(&parent);
            for (pair, lane) in lanes.iter().enumerate() {
                let to = match parent_node.as_deref_mut() {
                    Some(parent) => &mut parent.lanes[pair].buffer[..n * 2],
                    None => &mut self.mix_extra[pair * BLOCK * 2..pair * BLOCK * 2 + n * 2],
                };
                for (m, b) in to.iter_mut().zip(lane.buffer.iter()) {
                    *m += *b;
                }
            }
            let target =
                parent_node.map_or(&mut self.mix[..n * 2], |node| &mut node.buffer[..n * 2]);
            for (m, b) in target.iter_mut().zip(buffer.iter()) {
                *m += *b;
            }
            let node = self.group_nodes.get_mut(&handle).unwrap();
            node.buffer = buffer;
            node.lanes = lanes;
        }
        // The measured return joins after listener components and scripts (audiopitch listener-scripts).
        // Other profiles retain the previous placement until their graph is measured.
        let zone_after_listener = self.zone.as_ref().is_some_and(|zone| zone.measured_startup);
        if zone_on && !zone_after_listener {
            self.add_zone_return(n, pairs);
        }
        if listener_components && !measured_listener_pipeline {
            if has_listener {
                let mut lanes = Lanes {
                    samples: &mut self.mix_extra,
                    stride: BLOCK * 2,
                    offset: 0,
                    channels: self.config.output_channels().saturating_sub(2),
                };
                self.listener_chain.process_layout(
                    &mut self.listener_lanes,
                    &mut self.mix[..n * 2],
                    (pairs > 0).then_some(&mut lanes),
                    None,
                    false,
                    false,
                );
            }
        }
        if listener_components && !measured_script_route {
            sum_listener_return(
                &mut self.mix,
                &mut self.mix_extra,
                &self.listener_bypass,
                &self.listener_bypass_extra,
                n,
                pairs,
            );
        }
        if self.listener_filter
            && !self.capture_listener_batch
            && has_listener
            && let Some(cb) = callback
        {
            if measured_listener_pipeline {
                self.live_listener_pipeline(n);
            } else {
                let mut extra = (pairs > 0).then(|| Lanes {
                    samples: &mut self.mix_extra[..],
                    stride: BLOCK * 2,
                    offset: 0,
                    channels: config.output_channels() - 2,
                });
                call_in_layout(
                    cb,
                    callback::LISTENER_FILTER,
                    self.listener_plan,
                    &mut self.mix[..n * 2],
                    extra.as_mut(),
                    config.output_channels(),
                    &mut self.listener_layout,
                );
            }
        }
        let deferred = DeferredListenerReturn {
            bypass: measured_script_route && listener_bypass_used && self.capture_listener_batch,
            zone: zone_on && zone_after_listener && script_active && self.capture_listener_batch,
        };
        if measured_script_route && listener_bypass_used && !deferred.bypass {
            sum_listener_return(
                &mut self.mix,
                &mut self.mix_extra,
                &self.listener_bypass,
                &self.listener_bypass_extra,
                n,
                pairs,
            );
        }
        if zone_on && zone_after_listener {
            if deferred.zone {
                self.process_zone_return(n, pairs);
            } else {
                self.add_zone_return(n, pairs);
            }
        }
        if !self.capture_listener_batch {
            self.listener_output.push(&self.mix[..n * 2]);
        }
        self.frames += n as u64;
        deferred
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

#[derive(Clone, Copy)]
struct PlaybackPitch {
    source: f32,
    group: f32,
    doppler: f32,
}

impl PlaybackPitch {
    fn step(self, clip_rate: u32, output_rate: u32, measured: bool, resampling: bool) -> f64 {
        if measured {
            // Decoded playback uses a single-precision frequency, limited to 1 MHz,
            // before accumulating phase in f64 (audiodistortion rate/extreme controls).
            const MAX_DECODED_FREQUENCY: f32 = 1_000_000.0;
            let frequency = clip_rate as f32 * self.source * self.doppler * self.group;
            (frequency.clamp(-MAX_DECODED_FREQUENCY, MAX_DECODED_FREQUENCY) / output_rate as f32)
                as f64
        } else {
            let pitch = self.source as f64 * self.group as f64 * self.doppler as f64;
            if resampling {
                pitch * (clip_rate as f32 / output_rate as f32) as f64
            } else {
                pitch * clip_rate as f64 / output_rate as f64
            }
        }
    }
}

#[derive(Debug, Default)]
struct ZoneSendState {
    until: u64,
    next_at: u64,
    next_position: f64,
    end_at: u64,
    looping: bool,
    step: f64,
    include: bool,
    gain: Option<ParameterRamp>,
    return_id: Option<u64>,
}

impl ZoneSendState {
    fn connection_gain(&mut self, gain: f32, return_id: u64, already_playing: bool) -> f32 {
        if self.return_id != Some(return_id) {
            self.return_id = Some(return_id);
            self.gain = Some(ParameterRamp::new(if already_playing { 0.0 } else { gain }));
        }
        self.gain
            .as_mut()
            .expect("prepared zone connection")
            .next(gain, VOICE_GAIN_RAMP_FRAMES)
    }
}

/// Reused zone input receives each decoded voice before source components.
struct ZoneSend<'a> {
    return_id: u64,
    samples: &'a mut [f32],
    extra: Option<Lanes<'a>>,
    gain: f32,
    buffer_size: u64,
}

/// A naturally completing voice omits its entire last DSP buffer from the zone send.
/// Predict with the same additions as playback, including partial device periods, so a
/// fractional cursor near an endpoint cannot select a different buffer.
fn voice_finishes_before(
    voice: &Voice,
    clip: &Clip,
    step: f64,
    start: u64,
    end: u64,
    looping: bool,
) -> bool {
    if looping && voice.end_at >= end {
        return false;
    }
    let mut position = voice.position;
    let origin = voice
        .stream
        .as_ref()
        .map_or(0.0, |stream| stream.origin as f64);
    for dsp in start..end {
        if dsp < voice.start_at {
            continue;
        }
        if dsp >= voice.end_at {
            return true;
        }
        position += if voice.stream.is_some() {
            step.abs()
        } else {
            step
        };
        if !looping && (position + origin >= clip.frames as f64 || position < 0.0) {
            return true;
        }
    }
    false
}

/// Advance decoded voices and accumulate their PCM without invoking the source filter.
#[allow(clippy::too_many_arguments)]
fn render_source_voices(
    source: &mut Source,
    clips: &HashMap<u64, Clip>,
    out: &mut [f32],
    n: usize,
    t0: u64,
    pitch: PlaybackPitch,
    rate: u32,
    gains: VoiceGains,
    looping: bool,
    audible: bool,
    callback: Option<Callback>,
    mut zone_send: Option<&mut ZoneSend<'_>>,
    mut extra: Option<&mut Lanes<'_>>,
) -> Option<u64> {
    let mut finished_at: Option<u64> = None;
    let target_for = |clip: Option<&Clip>| gains.matrix(clip.map_or(2, |c| c.channels));
    let entries = gains.entries;
    let measured_resampling = source.params.has(source_flags::DECODED_RESAMPLING);
    let measured_matrix = source.params.has(source_flags::SPATIAL_STEREO);
    if let Some(main) = &mut source.main
        && main.clip != 0
    {
        if let Some(end) = render_voice(
            main,
            clips.get(&main.clip),
            n,
            t0,
            pitch,
            rate,
            target_for(clips.get(&main.clip)),
            entries,
            looping,
            measured_resampling,
            measured_matrix,
            audible.then_some(&mut *out),
            callback,
            zone_send.as_deref_mut(),
            extra.as_deref_mut().filter(|_| audible),
        ) {
            finished_at = Some(end);
            source.pending_position = if source.component_playback {
                main.position
            } else {
                0.0
            };
            if source.component_playback {
                source.completed_main = Some(source.completion_frontier);
            }
            source.main = None;
        }
    }
    source.one_shots.retain_mut(|voice| {
        let finished = render_voice(
            voice,
            clips.get(&voice.clip),
            n,
            t0,
            pitch,
            rate,
            target_for(clips.get(&voice.clip)),
            entries,
            false,
            measured_resampling,
            measured_matrix,
            audible.then_some(&mut *out),
            callback,
            zone_send.as_deref_mut(),
            extra.as_deref_mut().filter(|_| audible),
        );
        if let Some(end) = finished {
            finished_at = Some(finished_at.map_or(end, |previous| previous.max(end)));
            if source.component_playback {
                source.completed_shots.push(source.completion_frontier);
            }
        }
        finished.is_none()
    });
    finished_at
}

/// Writes the stereo graph's frames into interleaved frames of `out.len() / (stereo.len() / 2)`
/// channels: the front pair, then silent channels; a mono output (or a one-channel buffer)
/// takes the average of the pair. Unity 6000.0.71f1 leaves every other channel silent for
/// stereo clips in Quad, Surround, 5.1 and 7.1 (tests/projects/audiopitch layout controls).
fn spread(stereo: &[f32], extra: Option<Extra<'_>>, out: &mut [f32], mono: bool) {
    let frames = stereo.len() / 2;
    if frames == 0 {
        return;
    }
    let channels = out.len() / frames;
    for (f, (pair, frame)) in stereo
        .chunks_exact(2)
        .zip(out.chunks_exact_mut(channels))
        .enumerate()
    {
        let (l, r) = (pair[0], pair[1]);
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
            for (c, value) in frame.iter_mut().enumerate().skip(2) {
                *value = extra.map_or(0.0, |e| e.get(c - 2, f));
            }
        }
    }
}

/// Extra channel pairs beside a stereo block, read-only: pair `k`'s frames start at
/// `k * stride` (see [`Lanes`]).
#[derive(Clone, Copy)]
struct Extra<'a> {
    samples: &'a [f32],
    stride: usize,
    /// The layout's channels beyond the front pair: wider outputs get silence.
    channels: usize,
}

impl Extra<'_> {
    fn get(&self, channel: usize, frame: usize) -> f32 {
        if channel >= self.channels {
            return 0.0;
        }
        self.samples[(channel / 2) * self.stride + frame * 2 + channel % 2]
    }
}
/// The most channels of any output layout (`Mode7point1`).
pub(crate) const MAX_OUTPUT_CHANNELS: usize = 8;
/// Pairs of channels beyond the front pair in the widest layout.
pub(crate) const MAX_EXTRA_PAIRS: usize = (MAX_OUTPUT_CHANNELS - 1) / 2;

/// The output channels beyond the front pair, as stereo pairs in layout order: channel
/// `2 + 2k` and `3 + 2k` interleaved in pair `k`, whose frames start at `k * stride + offset`.
/// The graph carries them beside the front pair through every stage.
struct Lanes<'a> {
    samples: &'a mut [f32],
    stride: usize,
    offset: usize,
    /// The layout's channels beyond the front pair (pairs may hold one unused slot).
    channels: usize,
}

impl Lanes<'_> {
    fn pairs(&mut self, frames: usize) -> [&mut [f32]; MAX_EXTRA_PAIRS] {
        let mut pairs = self.samples.chunks_mut(self.stride);
        let offset = self.offset;
        std::array::from_fn(|_| {
            pairs
                .next()
                .map_or(&mut [][..], |pair| &mut pair[offset..offset + frames * 2])
        })
    }
    fn index(&self, channel: usize, frame: usize) -> usize {
        (channel / 2) * self.stride + self.offset + frame * 2 + channel % 2
    }
    /// Adds `value` to extra channel `channel` (layout channel `2 + channel`) at `frame`.
    fn add(&mut self, channel: usize, frame: usize, value: f32) {
        let i = self.index(channel, frame);
        self.samples[i] += value;
    }
    fn set(&mut self, channel: usize, frame: usize, value: f32) {
        let i = self.index(channel, frame);
        self.samples[i] = value;
    }
    fn view(&self) -> Extra<'_> {
        Extra {
            samples: &self.samples[self.offset..],
            stride: self.stride,
            channels: self.channels,
        }
    }
    /// The same lanes `frames` frames later.
    fn advanced(&mut self, frames: usize) -> Lanes<'_> {
        Lanes {
            samples: self.samples,
            stride: self.stride,
            offset: self.offset + frames * 2,
            channels: self.channels,
        }
    }
    /// Pair `pair`'s `frames` frames.
    fn pair(&mut self, pair: usize, frames: usize) -> &mut [f32] {
        let start = pair * self.stride + self.offset;
        &mut self.samples[start..start + frames * 2]
    }
}

/// The inverse of [`spread`]: frames of `layout.len() / (stereo.len() / 2)` channels back onto
/// the two graph lanes. A mono layout feeds both lanes; wider layouts keep the front pair (the
/// graph carries no other channel).
fn fold(layout: &[f32], stereo: &mut [f32], mut extra: Option<&mut Lanes<'_>>) {
    let frames = stereo.len() / 2;
    if frames == 0 {
        return;
    }
    let channels = layout.len() / frames;
    for (f, (frame, pair)) in layout
        .chunks_exact(channels)
        .zip(stereo.chunks_exact_mut(2))
        .enumerate()
    {
        pair[0] = frame[0];
        pair[1] = if channels == 1 { frame[0] } else { frame[1] };
        if let Some(lanes) = extra.as_deref_mut() {
            for (c, value) in frame.iter().enumerate().skip(2) {
                lanes.set(c - 2, f, *value);
            }
        }
    }
}

/// Calls an `OnAudioFilterRead` callback on stereo lanes with the output layout's `channels`,
/// as Unity 6000.0.71f1 does for source and listener scripts (tests/projects/audiopitch
/// `context_*_generator` and `context_*_listener`). `scratch` is sized once per layout.
fn call_in_layout(
    cb: Callback,
    kind: i32,
    handle: u64,
    stereo: &mut [f32],
    extra: Option<&mut Lanes<'_>>,
    channels: usize,
    scratch: &mut Vec<f32>,
) {
    call_in_layout_arg(cb, kind, handle, stereo, extra, channels, scratch, 0);
}

#[allow(clippy::too_many_arguments)]
fn call_in_layout_arg(
    cb: Callback,
    kind: i32,
    handle: u64,
    stereo: &mut [f32],
    mut extra: Option<&mut Lanes<'_>>,
    channels: usize,
    scratch: &mut Vec<f32>,
    arg: i64,
) {
    let frames = stereo.len() / 2;
    if channels == 2 {
        cb(kind, handle, stereo.as_mut_ptr(), frames as i32, 2, arg);
        return;
    }
    scratch.resize(frames * channels, 0.0);
    spread(
        stereo,
        extra.as_deref().map(Lanes::view),
        scratch,
        channels == 1,
    );
    cb(
        kind,
        handle,
        scratch.as_mut_ptr(),
        frames as i32,
        channels as i32,
        arg,
    );
    fold(scratch, stereo, extra.as_deref_mut());
}

fn listener_script_range(first: usize, end: usize) -> i64 {
    ((end as i64) << 32) | (first as i64 + 1)
}

/// Gains `[output * 2 + input]` from a clip's (left, right) to up to eight output channels; the
/// first four are the front pair's `[ll, lr, rl, rr]`.
type Matrix = [f32; MATRIX_ENTRIES];

/// A stereo `[ll, lr, rl, rr]` matrix with no other output channel.
fn front_matrix(stereo: [f32; 4]) -> Matrix {
    let mut matrix = [0.0; MATRIX_ENTRIES];
    matrix[..4].copy_from_slice(&stereo);
    matrix
}

/// A source's output gains for this block, before each voice picks its clip's matrix.
#[derive(Clone, Copy)]
struct VoiceGains {
    /// Stereo clips.
    target: Matrix,
    /// Mono clips, when the release's spatial profile measured them separately.
    mono_target: Option<Matrix>,
    /// A 2D source's pan, applied with the equal-power law to mono clips without `mono_target`.
    mono_pan: Option<f32>,
    /// The matrix entries in use: the front pair's four, then two per extra output channel.
    entries: usize,
}

impl VoiceGains {
    /// Front-pair gains (stereo, Prologic).
    fn stereo(target: [f32; 4], mono_target: Option<[f32; 4]>, mono_pan: Option<f32>) -> Self {
        Self {
            target: front_matrix(target),
            mono_target: mono_target.map(front_matrix),
            mono_pan,
            entries: 4,
        }
    }
    /// Every output channel of a surround layout of `channels` channels.
    fn surround(gains: &spatial::SurroundGains, volume: f32, channels: usize) -> Self {
        Self {
            target: gains.stereo.map(|g| g * volume),
            mono_target: Some(gains.mono.map(|g| g * volume)),
            mono_pan: None,
            entries: channels * 2,
        }
    }
    /// A mono output: every voice enters the graph already mono, on both lanes, so
    /// channel-symmetric effects (such as the Pitch Shifter) process it as Unity processes its
    /// single channel and the final `(left + right) / 2` returns it unchanged.
    fn mono_output(gains: &spatial::MonoGains, volume: f32) -> Self {
        let [left, right] = gains.stereo.map(|g| g * volume);
        let mono = gains.mono * volume;
        Self::stereo(
            [left, right, left, right],
            Some([mono, 0.0, mono, 0.0]),
            None,
        )
    }
    /// The matrix of a clip with `channels` channels (a mono clip feeds both inputs).
    fn matrix(&self, channels: usize) -> Matrix {
        let mono = channels == 1;
        let mut target = match self.mono_target {
            Some(matrix) if mono => matrix,
            _ => self.target,
        };
        if mono
            && self.mono_target.is_none()
            && let Some(pan) = self.mono_pan
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
        target
    }
}

/// Mixes `n` frames of a voice into `out` (stereo) when given, and advances it either way.
/// Returns the absolute frame just after the voice ends, or None while it still runs.
#[allow(clippy::too_many_arguments)]
fn render_voice(
    voice: &mut Voice,
    clip: Option<&Clip>,
    n: usize,
    t0: u64,
    pitch: PlaybackPitch,
    out_rate: u32,
    target: Matrix,
    entries: usize,
    looping: bool,
    measured_resampling: bool,
    measured_matrix: bool,
    mut out: Option<&mut [f32]>,
    callback: Option<Callback>,
    mut zone_send: Option<&mut ZoneSend<'_>>,
    mut extra: Option<&mut Lanes<'_>>,
) -> Option<u64> {
    let Some(clip) = clip else {
        return Some(t0);
    };
    if voice.paused || !voice.submitted {
        return None;
    }
    if clip.frames == 0 {
        return Some(t0);
    }
    if voice.fresh {
        voice.matrix = target;
        voice.fade_delta = target;
        voice.fresh = false;
    }
    if measured_matrix && voice.matrix != target {
        for i in 0..entries {
            voice.fade_delta[i] = target[i] - voice.fade_matrix[i];
        }
        voice.fade_frames = 0;
    }
    let from = voice.matrix;
    voice.matrix = target;
    let frames = clip.frames as f64;
    let step = pitch.step(
        clip.rate,
        out_rate,
        measured_matrix && voice.stream.is_none(),
        measured_resampling && voice.stream.is_none(),
    );
    if zone_send.is_some()
        && (voice.zone_send.next_at != t0
            || voice.zone_send.next_position != voice.position
            || voice.zone_send.step != step
            || voice.zone_send.end_at != voice.end_at
            || voice.zone_send.looping != looping)
    {
        voice.zone_send.until = 0;
    }
    let inv_n = 1.0 / n as f32;
    for f in 0..n {
        let dsp = t0 + f as u64;
        if dsp < voice.start_at {
            // The scheduled route has already started processing silence. Its
            // startup matrix ramp elapses before the decoded clip becomes audible.
            voice.advance_fade(measured_matrix, entries);
            continue;
        }
        if dsp >= voice.end_at {
            return Some(dsp);
        }
        if let Some(send) = zone_send.as_ref()
            && dsp >= voice.zone_send.until
        {
            let until = (dsp / send.buffer_size + 1) * send.buffer_size;
            voice.zone_send.include =
                !voice_finishes_before(voice, clip, step, dsp, until, looping);
            voice.zone_send.until = until;
            voice.zone_send.step = step;
            voice.zone_send.end_at = voice.end_at;
            voice.zone_send.looping = looping;
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
                } else if looping || measured_matrix {
                    clip.frame(i.rem_euclid(clip.frames as i64) as usize)
                } else if measured_resampling {
                    clip.frame(i.clamp(0, clip.frames as i64 - 1) as usize)
                } else {
                    (0.0, 0.0)
                }
            };
            let (l0, r0) = fetch(i0);
            let (l1, r1) = fetch(i0 + 1);
            if measured_matrix {
                let inverse = 1.0 - frac;
                (l0 * inverse + l1 * frac, r0 * inverse + r1 * frac)
            } else {
                (l0 + (l1 - l0) * frac, r0 + (r1 - r0) * frac)
            }
        };
        if let Some(out) = out.as_deref_mut() {
            let t = (f + 1) as f32 * inv_n;
            let mut m = if measured_matrix {
                voice.fade_matrix
            } else {
                let mut m = [0.0; MATRIX_ENTRIES];
                for i in 0..entries {
                    m[i] = from[i] + (target[i] - from[i]) * t;
                }
                m
            };
            if !measured_matrix && voice.fade_frames < VOICE_GAIN_RAMP_FRAMES {
                for (i, gain) in m[..entries].iter_mut().enumerate() {
                    *gain = if *gain == voice.fade_delta[i] {
                        voice.fade_matrix[i]
                    } else if voice.fade_delta[i] != 0.0 {
                        (*gain / voice.fade_delta[i]) * voice.fade_matrix[i]
                    } else {
                        *gain * voice.fade_frames as f32 / VOICE_GAIN_RAMP_FRAMES as f32
                    };
                }
            }
            let v = voice.volume_scale;
            let left = (m[0] * l + m[1] * r) * v;
            let right = (m[2] * l + m[3] * r) * v;
            out[f * 2] += left;
            out[f * 2 + 1] += right;
            if let Some(lanes) = extra.as_deref_mut() {
                for channel in 0..(entries - 4) / 2 {
                    let i = 4 + channel * 2;
                    lanes.add(channel, f, (m[i] * l + m[i + 1] * r) * v);
                }
            }
            if let Some(send) = zone_send.as_deref_mut() {
                // Zone send changes use the same 64-frame connection ramp as voice gains.
                // Unity 6000.0.71f1, tests/projects/audioreverb live bypass controls.
                let gain =
                    voice
                        .zone_send
                        .connection_gain(send.gain, send.return_id, voice.rendered);
                if voice.zone_send.include {
                    send.samples[f * 2] += left * gain;
                    send.samples[f * 2 + 1] += right * gain;
                    if let Some(lanes) = send.extra.as_mut() {
                        for channel in 0..lanes.channels {
                            let i = 4 + channel * 2;
                            lanes.add(channel, f, ((m[i] * l + m[i + 1] * r) * v) * gain);
                        }
                    }
                }
            }
        }
        voice.advance_fade(measured_matrix, entries);
        voice.rendered = true;
        if let Some(stream) = &mut voice.stream {
            voice.position += step.abs();
            stream.trim(voice.position, clip.channels);
            // A non-looping stream ends at the end of the clip.
            if !looping && voice.position + stream.origin as f64 >= frames {
                return Some(dsp + 1);
            }
        } else {
            voice.position += step;
            if voice.position >= frames || voice.position < 0.0 {
                if !looping {
                    return Some(dsp + 1);
                }
                voice.position = voice.position.rem_euclid(frames);
            }
        }
    }
    voice.zone_send.next_at = t0 + n as u64;
    voice.zone_send.next_position = voice.position;
    None
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::audio::protocol::AudioSourceCommand;

    #[test]
    fn command_receipt_tracks_play_admission_without_polling_voice_counts() {
        let mut m = mixer();
        source_with(&mut m, 1, 99, |_| {});
        let play = AudioSourceCommand {
            kind: command::PLAY,
            ..Default::default()
        };
        let shot = AudioSourceCommand {
            kind: command::PLAY_ONE_SHOT,
            clip: 99,
            ..Default::default()
        };
        assert!(!m.command(1, &play));
        assert!(!m.command(1, &shot));
        m.add_clip(99, ramp_clip(100, 1000));
        assert!(m.command(1, &play));
        assert!(m.command(1, &shot));
        m.release_clip(99);
        assert!(!m.command(1, &shot));
        assert!(!m.command(1, &play));
    }

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
    fn decoded_rate_ratio_precision_is_selected_before_voice_advance() {
        for (clip_rate, output_rate) in [(48000, 44100), (22050, 32000), (44100, 48000)] {
            for measured in [false, true] {
                let mut m = Mixer::new(MixerConfig {
                    sample_rate: output_rate,
                    ..MixerConfig::default()
                });
                m.add_clip(1, Clip::new(2, clip_rate, vec![0.25; 8192]));
                source_with(&mut m, 7, 1, |p| {
                    p.pitch = 0.7;
                    if measured {
                        p.flags |= source_flags::DECODED_RESAMPLING;
                    }
                });
                play(&mut m, 7, command::PLAY, 0.0);
                m.render(&mut vec![0.0; 1024], 2);
                let ratio = if measured {
                    (clip_rate as f32 / output_rate as f32) as f64
                } else {
                    clip_rate as f64 / output_rate as f64
                };
                let step = if measured {
                    0.7_f32 as f64 * ratio
                } else {
                    0.7_f32 as f64 * clip_rate as f64 / output_rate as f64
                };
                let expected = (0..512).fold(0.0, |position, _| position + step);
                assert_eq!(m.source_state(7).position, expected);
            }
        }
    }

    #[test]
    fn decoded_endpoint_holds_until_end_while_loops_interpolate_across_wrap() {
        for channels in [1, 2] {
            for looping in [false, true] {
                for measured in [false, true] {
                    let mut m = mixer();
                    let pcm = (0..100)
                        .flat_map(|i| std::iter::repeat_n(i as f32 / 100.0, channels))
                        .collect();
                    m.add_clip(1, Clip::new(channels, 1000, pcm));
                    source_with(&mut m, 7, 1, |p| {
                        p.pitch = 0.5;
                        if looping {
                            p.flags |= source_flags::LOOP;
                        }
                        if measured {
                            p.flags |= source_flags::DECODED_RESAMPLING;
                        }
                    });
                    play(&mut m, 7, command::PLAY, 0.0);
                    let mut out = vec![0.0; 202 * 2];
                    m.render(&mut out, 2);
                    let gain = if channels == 1 { mono_gain(199) } else { 1.0 };
                    let last = if measured && !looping { 0.99 } else { 0.495 };
                    assert_eq!(out[199 * 2], last * gain);
                    assert_eq!(out[198 * 2], 0.99 * gain);
                    assert_eq!(out[200 * 2], 0.0);
                    if !looping {
                        assert_eq!(out[201 * 2], 0.0);
                    } else {
                        assert_eq!(out[201 * 2], 0.005 * gain);
                    }
                }
            }
        }
    }

    #[test]
    fn measured_gain_changes_cross_device_periods_and_pick_each_voices_layout() {
        fn setup() -> Mixer {
            let mut m = Mixer::new(MixerConfig {
                sample_rate: 48000,
                ..MixerConfig::default()
            });
            m.add_clip(1, Clip::new(1, 48000, vec![0.5; 4096]));
            m.add_clip(2, Clip::new(2, 48000, [0.25, -0.125].repeat(4096)));
            source_with(&mut m, 7, 1, |p| {
                p.flags |= source_flags::SPATIAL_STEREO | source_flags::DECODED_RESAMPLING;
                p.spatial_blend = 1.0;
                p.position.z = 5.0;
                p.min_distance = 1.0;
                p.max_distance = 20.0;
                p.rolloff_mode = spatial::LINEAR;
            });
            play(&mut m, 7, command::PLAY, 0.0);
            m.render(&mut vec![0.0; 256 * 2], 2);
            m.sources.get_mut(&7).unwrap().params.position.z = 10.0;
            m
        }
        let mut whole = setup();
        let mut expected = vec![0.0; 105 * 2];
        whole.render(&mut expected, 2);
        // Native step-distance Master floats, independent of the render partition.
        assert_eq!(expected[0], 0.2791211);
        assert_eq!(expected[2], 0.27766734);
        assert_eq!(expected[64 * 2], 0.18608074);
        let mut split = setup();
        let mut actual = Vec::new();
        for frames in [17, 31, 5, 11, 41] {
            let mut out = vec![0.0; frames * 2];
            split.render(&mut out, 2);
            actual.extend(out);
        }
        assert_eq!(actual, expected);
        whole.command(
            7,
            &AudioSourceCommand {
                kind: command::PLAY_ONE_SHOT,
                clip: 2,
                volume_scale: 0.5,
                ..Default::default()
            },
        );
        let mut mixed = vec![0.0; 256 * 2];
        whole.render(&mut mixed, 2);
        // The stereo one-shot sums two independent inputs; the main mono voice uses one.
        let target = 0.18608074 + (0.25 - 0.125) * 0.37216148 * 0.5;
        assert_eq!(mixed[200 * 2], target);
        assert_eq!(mixed[200 * 2 + 1], target);
    }

    #[test]
    fn measured_playback_composes_frequency_before_rate_conversion_and_caps_extremes() {
        let pitch = PlaybackPitch {
            source: 1.5,
            group: 1.0,
            doppler: 1.0,
        };
        assert_eq!(pitch.step(44100, 48000, true, true), 1.3781249523162842);
        // An ordinary pitch and a Doppler shift share the same frequency limit.
        for (source, doppler) in [(30.0, 1.0), (1.0, 30.0), (-30.0, 1.0)] {
            let pitch = PlaybackPitch {
                source,
                doppler,
                group: 1.0,
            };
            for rate in [44100, 48000] {
                let mut m = Mixer::new(MixerConfig {
                    sample_rate: rate,
                    ..Default::default()
                });
                let mut data = vec![0.0; 32768];
                data[1024..].fill(0.25);
                m.add_clip(1, Clip::new(1, rate, data));
                source_with(&mut m, 7, 1, |p| {
                    p.flags |= source_flags::SPATIAL_STEREO;
                    p.pitch = source;
                    p.spatial_blend = 1.0;
                    p.position.z = 2000.0;
                    p.velocity.z = -340.0 * (doppler - 1.0);
                    p.rolloff_mode = spatial::CUSTOM;
                });
                assert_eq!(
                    pitch.step(rate, rate, true, false).abs(),
                    (1_000_000.0f32 / rate as f32) as f64
                );
                if source < 0.0 {
                    continue;
                }
                play(&mut m, 7, command::PLAY, 0.0);
                let mut out = vec![0.0; 128 * 2];
                m.render(&mut out, 2);
                let first = out.chunks_exact(2).position(|x| x[0] != 0.0).unwrap();
                assert_eq!(first, if rate == 48000 { 50 } else { 46 });
            }
        }
    }

    #[test]
    fn measured_non_looping_lookahead_reads_the_first_frame_then_stops() {
        let mut m = mixer();
        m.add_clip(1, Clip::new(2, 1000, [0.25, -0.125].repeat(100)));
        m.clips.get_mut(&1).unwrap().samples[0] = 0.75;
        source_with(&mut m, 7, 1, |p| {
            p.flags |= source_flags::SPATIAL_STEREO | source_flags::DECODED_RESAMPLING;
            p.pitch = 0.5;
        });
        play(&mut m, 7, command::PLAY, 0.0);
        let mut out = vec![0.0; 202 * 2];
        m.render(&mut out, 2);
        assert_eq!(out[199 * 2], 0.5);
        assert_eq!(out[200 * 2], 0.0);
        assert_eq!(m.source_state(7).main, voice_state::NONE);
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
        // The complete Native scheduled controls start at full gain after silent lead-in.
        assert_eq!(out[105 * 2], mono_gain(105));
        assert_eq!(out[106 * 2], mono_gain(106));
        assert_eq!(m.source_state(7).main, voice_state::PLAYING);
        assert_eq!(m.source_state(7).position, 5.0);
        // A scheduled end stops it on its frame.
        play(&mut m, 7, command::SET_SCHEDULED_END, 0.120);
        m.render(&mut out[..40], 2);
        assert_eq!(out[9 * 2], mono_gain(119));
        assert_eq!(out[10 * 2], 0.0);
        assert_eq!(m.source_state(7).main, voice_state::NONE);
    }

    #[test]
    fn scheduled_start_edits_follow_audio_update_publication() {
        for measured in [false, true] {
            for restart in [false, true] {
                for edit_after_update in [false, true] {
                    let mut m = Mixer::new(MixerConfig {
                        sample_rate: 32000,
                        buffer_size: 97,
                        ..MixerConfig::default()
                    });
                    if measured {
                        m.output_rules = unbound_compat::AudioOutputRules::for_version(
                            unbound_compat::UnityVersion::new(6000, 0, 71),
                        );
                    }
                    m.defer_voice_submission = true;
                    m.add_clip(1, Clip::new(2, 32000, vec![0.25; 800]));
                    source_with(&mut m, 7, 1, |_| {});
                    play(&mut m, 7, command::PLAY, 300.75 / 32000.0);
                    if restart {
                        play(&mut m, 7, command::PLAY, 300.75 / 32000.0);
                    }
                    if edit_after_update {
                        m.update_suspension();
                    }
                    play(&mut m, 7, command::SET_SCHEDULED_START, 100.75 / 32000.0);
                    play(&mut m, 7, command::SET_SCHEDULED_END, 350.75 / 32000.0);
                    m.update_suspension();
                    let mut out = vec![0.0; 800];
                    m.render(&mut out, 2);
                    let first = out.chunks_exact(2).position(|frame| frame[0] != 0.0);
                    let expected = if !measured {
                        101
                    } else if edit_after_update {
                        100
                    } else {
                        300
                    };
                    assert_eq!(first, Some(expected));
                    assert_eq!(out[expected * 2], 0.25);
                    assert_eq!(out[349 * 2], 0.25);
                    assert_eq!(out[350 * 2], 0.0);
                    assert_eq!(m.source_state(7).main, voice_state::NONE);
                }
            }
        }
    }

    #[test]
    fn fractional_delays_count_whole_frames_from_the_current_clock() {
        for measured in [false, true] {
            let mut m = Mixer::new(MixerConfig {
                sample_rate: 32000,
                buffer_size: 97,
                ..MixerConfig::default()
            });
            if measured {
                m.output_rules = unbound_compat::AudioOutputRules::for_version(
                    unbound_compat::UnityVersion::new(6000, 0, 71),
                );
            }
            m.advance(17);
            m.add_clip(1, Clip::new(2, 32000, vec![0.25; 400]));
            source_with(&mut m, 7, 1, |_| {});
            play(&mut m, 7, command::PLAY, -100.75 / 32000.0);
            let mut out = vec![0.0; 300];
            m.render(&mut out, 2);
            let first = out.chunks_exact(2).position(|frame| frame[0] != 0.0);
            let expected = if measured { 100 } else { 101 };
            assert_eq!(first, Some(expected));
            assert_eq!(out[expected * 2], 0.25);
            assert_eq!(m.source_state(7).position, (150 - expected) as f64);
        }
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
    fn routed_voice_submission_waits_for_an_audio_update_without_shifting_the_clip() {
        let mut m = Mixer::new(MixerConfig::default());
        let mut group = filter_group(3, 0, "Attenuation");
        group.mixer = 3;
        group.effects[0].gain = 1.0;
        m.configure_group(group).unwrap();
        m.add_clip(1, Clip::new(2, 48000, vec![0.125; 4096 * 2]));
        source_with(&mut m, 7, 1, |p| p.group = 3);
        play(&mut m, 7, command::PLAY, 0.0);
        let mut output = vec![0.0; 2048];
        for _ in 0..10 {
            m.render(&mut output, 2);
        }
        assert!(output.iter().all(|x| *x == 0.0));
        assert_eq!(m.source_state(7).main, voice_state::PLAYING);
        assert_eq!(m.source_state(7).position, 0.0);
        m.update_suspension();
        m.render(&mut output, 2);
        assert!(output.iter().any(|x| *x > 0.1));
        assert_eq!(m.source_state(7).position, 1024.0);
    }

    #[test]
    fn captured_listener_delivery_uses_dsp_buffers_and_preserves_pcm_and_partial_suffixes() {
        use std::cell::RefCell;
        thread_local! { static SIZES: RefCell<Vec<i32>> = const { RefCell::new(Vec::new()) }; }
        extern "C" fn listener(
            kind: i32,
            _: u64,
            data: *mut f32,
            frames: i32,
            channels: i32,
            _: i64,
        ) {
            if kind != callback::LISTENER_FILTER {
                return;
            }
            SIZES.with(|sizes| sizes.borrow_mut().push(frames));
            let values =
                unsafe { std::slice::from_raw_parts_mut(data, (frames * channels) as usize) };
            for value in values {
                *value *= 0.5;
            }
        }
        fn prepared() -> Mixer {
            let mut m = Mixer::new(MixerConfig::default());
            m.add_clip(1, Clip::new(2, 48000, vec![0.25; 8192 * 2]));
            source_with(&mut m, 7, 1, |_| {});
            play(&mut m, 7, command::PLAY, 0.0);
            m
        }
        let mut control = prepared();
        let mut expected = vec![0.0; 4096];
        control.render(&mut expected, 2);
        for sample in &mut expected {
            *sample = capture::pcm16(*sample * 0.5);
        }
        let mut m = prepared();
        m.set_callback(Some(listener));
        m.listener_filter = true;
        let output = m.capture(4097, Vec::new()).unwrap();
        assert_eq!(output.samples, expected);
        assert_eq!(m.frames(), 2048);
        SIZES.with(|sizes| assert_eq!(*sizes.borrow(), [1024, 1024]));
        // The capture frontier does not alter the device/direct DSP callback path.
        m.render(&mut vec![0.0; 512], 2);
        SIZES.with(|sizes| assert_eq!(*sizes.borrow(), [1024, 1024, 256]));
    }

    #[test]
    fn listener_components_interleave_three_scripts_without_changing_callback_periods() {
        use std::cell::RefCell;
        thread_local! { static CALLS: RefCell<Vec<(usize, i32)>> = const { RefCell::new(Vec::new()) }; }
        extern "C" fn scripts(
            kind: i32,
            handle: u64,
            data: *mut f32,
            frames: i32,
            channels: i32,
            arg: i64,
        ) {
            if kind != callback::LISTENER_FILTER {
                return;
            }
            assert_eq!(handle, 73);
            let first = if arg == 0 {
                0
            } else {
                (arg as u32 - 1) as usize
            };
            let end = if arg == 0 {
                3
            } else {
                ((arg >> 32) as usize).min(3)
            };
            let output =
                unsafe { std::slice::from_raw_parts_mut(data, (frames * channels) as usize) };
            for script in first..end {
                CALLS.with(|calls| calls.borrow_mut().push((script, frames)));
                match script {
                    0 => {
                        output.fill(0.0);
                        for channel in 0..channels as usize {
                            output[10 * channels as usize + channel] =
                                if channel % 2 == 0 { 1.0 } else { 0.0 };
                        }
                    }
                    1 => output.iter_mut().for_each(|value| *value = -*value + 1.0),
                    2 => output
                        .iter_mut()
                        .for_each(|value| *value = *value * 0.25 - 0.03125),
                    _ => unreachable!(),
                }
            }
        }
        for period in [97_usize, 600] {
            for mode in [
                speaker_mode::MONO,
                speaker_mode::STEREO,
                speaker_mode::MODE_5POINT1,
            ] {
                for capture in [false, true] {
                    CALLS.with(|calls| calls.borrow_mut().clear());
                    let mut mixer = Mixer::new(MixerConfig {
                        sample_rate: 32000,
                        buffer_size: period as u32,
                        speaker_mode: mode,
                        ..Default::default()
                    });
                    mixer.surround_effects = true;
                    mixer.listener_chain_measured = true;
                    mixer.listener_plan = 73;
                    mixer.listener_filter = true;
                    mixer.set_callback(Some(scripts));
                    let descriptor = |id, before| AudioFilterDescriptor {
                        delay: 1.0,
                        decay: 0.0,
                        dry_mix: 0.0,
                        wet_mix: 1.0,
                        ..component(id, filter_kind::ECHO, filter_stage::listener(before))
                    };
                    mixer.update_listener_filters(
                        Ok(vec![descriptor(1, 1), descriptor(2, 2)]),
                        true,
                        true,
                    );
                    let channels = mixer.config.output_channels();
                    let segments: Vec<usize> = if capture {
                        vec![period]
                    } else {
                        (0..period)
                            .step_by(BLOCK)
                            .map(|first| BLOCK.min(period - first))
                            .collect()
                    };
                    let mut output = vec![0.0; period * channels];
                    for repeat in 0..2 {
                        if capture {
                            mixer.render_capture_block(&mut output);
                        } else {
                            mixer.render(&mut output, channels);
                        }
                        let mut first = 0;
                        for &frames in &segments {
                            for frame in 0..frames {
                                for channel in 0..channels {
                                    let baseline = if repeat == 0 && first + frame < 32 {
                                        -0.03125
                                    } else {
                                        0.21875
                                    };
                                    let expected = baseline
                                        - if frame == 74 && channel % 2 == 0 {
                                            0.25
                                        } else {
                                            0.0
                                        };
                                    assert!(
                                        (output[(first + frame) * channels + channel] - expected)
                                            .abs()
                                            < 1e-6,
                                        "period {period}, mode {mode}, capture {capture}, repeat {repeat}, frame {}, channel {channel}: {} != {expected}",
                                        first + frame,
                                        output[(first + frame) * channels + channel]
                                    );
                                }
                            }
                            first += frames;
                        }
                    }
                    let expected: Vec<_> = (0..2)
                        .flat_map(|_| {
                            segments.iter().flat_map(|&frames| {
                                (0..3).map(move |script| (script, frames as i32))
                            })
                        })
                        .collect();
                    CALLS.with(|calls| assert_eq!(*calls.borrow(), expected));
                }
            }
        }
    }

    /// The last output frame of a playing constant clip once the voice ramps are over.
    fn settled(config: MixerConfig, clip: Clip, f: impl FnOnce(&mut SourceParams)) -> Vec<f32> {
        let channels = config.output_channels();
        let mut m = Mixer::new(config);
        m.add_clip(1, clip);
        source_with(&mut m, 7, 1, f);
        play(&mut m, 7, command::PLAY, 0.0);
        let mut out = vec![0.0; 2048 * channels];
        m.render(&mut out, channels);
        out[out.len() - channels..].to_vec()
    }

    #[test]
    fn mono_output_mixes_each_source_into_one_lane_before_the_graph() {
        let mono = MixerConfig {
            speaker_mode: 1,
            ..MixerConfig::default()
        };
        let stereo_clip = || Clip::new(2, 48000, [0.5, 0.25].repeat(4096));
        let mono_clip = || Clip::new(1, 48000, vec![0.5; 4096]);
        let measured = |pan: f32, volume: f32| {
            move |p: &mut SourceParams| {
                p.flags |= source_flags::SPATIAL_STEREO;
                p.pan_stereo = pan;
                p.volume = volume;
            }
        };
        // Stereo clips: 0.707 per channel of the panned pair, not the 1/2 of a final downmix.
        assert_eq!(
            settled(mono, stereo_clip(), measured(0.0, 1.0)),
            [0.707 * 0.5 + 0.707 * 0.25]
        );
        assert_eq!(
            settled(mono, stereo_clip(), measured(-0.5, 1.0)),
            [0.707 * 0.5 + 0.707 * 0.5 * 0.25]
        );
        // Mono clips keep their level whatever the 2D pan, and scale linearly with volume.
        for pan in [-1.0, -0.5, 0.0, 0.5, 1.0] {
            for volume in [1.0, 0.5] {
                let level = settled(mono, mono_clip(), measured(pan, volume))[0];
                assert!(
                    (level - 0.5 * volume).abs() <= 1e-7,
                    "{pan} {volume}: {level}"
                );
                // The unmeasured spatial path pans mono clips by equal power: the same length.
                let level = settled(mono, mono_clip(), |p| {
                    p.pan_stereo = pan;
                    p.volume = volume;
                })[0];
                assert!(
                    (level - 0.5 * volume).abs() <= 1e-6,
                    "{pan} {volume}: {level}"
                );
            }
        }
        // Stereo output is unchanged.
        assert_eq!(
            settled(MixerConfig::default(), stereo_clip(), measured(0.0, 1.0)),
            [0.5, 0.25]
        );
    }

    #[test]
    fn multichannel_outputs_carry_the_front_pair_and_silent_channels() {
        for (mode, channels) in [(3, 4), (4, 5), (5, 6), (6, 8)] {
            let config = MixerConfig {
                speaker_mode: mode,
                ..MixerConfig::default()
            };
            assert_eq!(config.output_channels(), channels);
            let frame = settled(
                config,
                Clip::new(2, 48000, [0.5, 0.25].repeat(4096)),
                |_| {},
            );
            let mut expected = vec![0.0; channels];
            expected[..2].copy_from_slice(&[0.5, 0.25]);
            assert_eq!(frame, expected, "mode {mode}");
        }
    }

    #[test]
    fn audio_renderer_buffers_and_group_sinks_use_the_output_layout() {
        for (mode, channels) in [(1, 1), (2, 2), (3, 4), (5, 6), (6, 8)] {
            let config = MixerConfig {
                speaker_mode: mode,
                ..MixerConfig::default()
            };
            let mut m = Mixer::new(config);
            let mut group = filter_group(3, 0, "Attenuation");
            group.mixer = 3;
            group.effects[0].gain = 1.0;
            m.configure_group(group).unwrap();
            m.add_clip(1, Clip::new(2, 48000, [0.5, 0.25].repeat(8192)));
            source_with(&mut m, 7, 1, |p| {
                p.group = 3;
                p.flags |= source_flags::SPATIAL_STEREO;
            });
            play(&mut m, 7, command::PLAY, 0.0);
            m.update_suspension();
            // One whole DSP buffer plus a partial one, which stays untouched.
            let length = (1024 + 100) * channels;
            let output = m
                .capture(
                    length,
                    vec![capture::SinkRequest {
                        handle: 3,
                        length,
                        exclude_from_mix: false,
                    }],
                )
                .unwrap();
            assert_eq!(output.samples.len(), 1024 * channels, "mode {mode}");
            assert_eq!(
                output.sinks[0].samples.len(),
                1024 * channels,
                "mode {mode}"
            );
            let front = if mode == 1 {
                vec![0.707 * 0.5 + 0.707 * 0.25]
            } else {
                vec![0.5, 0.25]
            };
            let last = &output.sinks[0].samples[1023 * channels..];
            assert_eq!(&last[..front.len()], &front[..], "mode {mode}");
            assert!(last[front.len()..].iter().all(|v| *v == 0.0), "mode {mode}");
            let device = &output.samples[1023 * channels..];
            let pcm: Vec<f32> = front.iter().map(|v| capture::pcm16(*v)).collect();
            assert_eq!(&device[..front.len()], &pcm[..], "mode {mode}");
        }
    }

    #[test]
    fn muted_sibling_does_not_keep_a_quiet_mixer_awake() {
        let mut m = Mixer::new(MixerConfig::default());
        let mut echo = filter_group(3, 4, "Echo");
        echo.mixer = 4;
        echo.effects[0].delay = 5000.0;
        m.configure_group(echo).unwrap();
        let mut master = filter_group(4, 0, "Attenuation");
        master.mixer = 4;
        master.enable_suspend = true;
        master.suspend_threshold = 0.0001;
        master.effects[0].gain = 1.0;
        m.configure_group(master).unwrap();
        let mut muted = filter_group(5, 4, "Attenuation");
        muted.mixer = 4;
        muted.muted = true;
        muted.effects[0].gain = 1.0;
        m.configure_group(muted).unwrap();
        let mut input = vec![0.0; 4096 * 2];
        input[2048 * 2] = 0.25;
        input[3072 * 2 + 1] = -0.125;
        m.add_clip(1, Clip::new(2, 48000, input));
        m.add_clip(2, Clip::new(2, 48000, vec![0.01; 2048]));
        source_with(&mut m, 7, 1, |p| p.group = 3);
        source_with(&mut m, 8, 2, |p| {
            p.group = 5;
            p.flags = source_flags::LOOP;
        });
        play(&mut m, 7, command::PLAY, 0.0);
        play(&mut m, 8, command::PLAY, 0.0);
        for _ in 0..70 {
            m.update_suspension();
            m.advance(1024);
        }
        assert!(m.group_nodes[&4].suspended);
        assert_eq!(m.group_nodes[&3].rendered, 50 * 1024);
        assert_eq!(m.source_state(8).main, voice_state::PLAYING);
        assert_eq!(m.source_state(8).is_virtual, 0);
        assert_eq!(m.source_state(7).is_virtual, 1);
    }

    #[test]
    fn remote_sized_packets_observe_each_dsp_rms_window() {
        fn prepared() -> Mixer {
            let mut mixer = Mixer::new(MixerConfig::default());
            let mut root = filter_group(3, 0, "Echo");
            root.mixer = 3;
            root.enable_suspend = true;
            root.suspend_threshold = 0.01;
            root.effects[0].delay = 100.0;
            root.effects[0].decay = 1.0;
            mixer.configure_group(root).unwrap();
            let mut input = vec![0.0; (1024 + 4800) * 2];
            input[1024 * 2..].fill(0.00999);
            mixer.add_clip(1, Clip::new(2, 48000, input));
            source_with(&mut mixer, 7, 1, |p| p.group = 3);
            play(&mut mixer, 7, command::PLAY, 0.0);
            mixer.update_suspension();
            mixer.render(&mut vec![0.0; 6144 * 2], 2);
            assert!(mixer.sources[&7].main.is_none());
            mixer
        }
        let mut dsp = prepared();
        let mut remote = prepared();
        for (mixer, packet) in [(&mut dsp, 1024), (&mut remote, 480)] {
            for _ in 0..(61440 / packet) {
                mixer.update_suspension();
                mixer.render(&mut vec![0.0; packet * 2], 2);
            }
            assert!(mixer.group_nodes[&3].suspended, "packet={packet}");
        }
        assert_eq!(
            dsp.group_nodes[&3].last_active,
            remote.group_nodes[&3].last_active
        );
    }

    #[test]
    fn scripts_receive_the_output_layout_channels() {
        use std::cell::Cell;
        thread_local! {
            static GENERATED: Cell<i32> = const { Cell::new(0) };
            static HEARD: Cell<i32> = const { Cell::new(0) };
        }
        // A clipless generator writes 0.25/-0.125 on its first two channels; the listener
        // writes 0.125 into channel 4 when the layout has one.
        extern "C" fn script(
            kind: i32,
            _: u64,
            data: *mut f32,
            frames: i32,
            channels: i32,
            _: i64,
        ) {
            let data =
                unsafe { std::slice::from_raw_parts_mut(data, (frames * channels) as usize) };
            let channels = channels as usize;
            if kind == callback::SOURCE_FILTER {
                GENERATED.with(|c| c.set(channels as i32));
                for frame in data.chunks_exact_mut(channels) {
                    frame[0] += 0.25;
                    if channels > 1 {
                        frame[1] -= 0.125;
                    }
                }
            } else if kind == callback::LISTENER_FILTER {
                HEARD.with(|c| c.set(channels as i32));
                if channels > 4 {
                    for frame in data.chunks_exact_mut(channels) {
                        frame[4] += 0.125;
                    }
                }
            }
        }
        for (mode, channels, front) in [
            (speaker_mode::MONO, 1, vec![0.25]),
            (speaker_mode::STEREO, 2, vec![0.25, -0.125]),
            (speaker_mode::MODE_5POINT1, 6, vec![0.25, -0.125]),
        ] {
            let mut m = Mixer::new(MixerConfig {
                speaker_mode: mode,
                ..MixerConfig::default()
            });
            m.set_callback(Some(script));
            m.listener_filter = true;
            source_with(&mut m, 7, 0, |p| p.flags = source_flags::FILTER);
            play(&mut m, 7, command::PLAY, 0.0);
            let output = m.capture(4096 * channels, Vec::new()).unwrap();
            assert_eq!(GENERATED.with(Cell::get), channels as i32, "mode {mode}");
            assert_eq!(HEARD.with(Cell::get), channels as i32, "mode {mode}");
            let last = &output.samples[output.samples.len() - channels..];
            let mut expected = vec![0.0; channels];
            expected[..front.len()].copy_from_slice(&front);
            if channels > 4 {
                expected[4] = 0.125;
            }
            let expected: Vec<f32> = expected.into_iter().map(capture::pcm16).collect();
            assert_eq!(last, &expected[..], "mode {mode}");
        }
    }

    #[test]
    fn extra_channels_run_through_their_groups() {
        // A mono 2D clip in 5.1 reaches LFE at unity; the group's attenuation applies to it
        // through the lane's own effect instance, and the group sink holds all six channels.
        let config = MixerConfig {
            speaker_mode: speaker_mode::MODE_5POINT1,
            ..MixerConfig::default()
        };
        let mut m = Mixer::new(config);
        let mut group = filter_group(3, 0, "Attenuation");
        group.mixer = 3;
        group.effects[0].gain = 0.5;
        m.configure_group(group).unwrap();
        assert_eq!(m.group_nodes[&3].lanes.len(), 2);
        m.add_clip(1, Clip::new(1, 48000, vec![0.5; 8192]));
        source_with(&mut m, 7, 1, |p| {
            p.group = 3;
            p.flags |= source_flags::SPATIAL_STEREO;
        });
        play(&mut m, 7, command::PLAY, 0.0);
        m.update_suspension();
        let length = 2048 * 6;
        let output = m
            .capture(
                length,
                vec![capture::SinkRequest {
                    handle: 3,
                    length,
                    exclude_from_mix: false,
                }],
            )
            .unwrap();
        let sink = &output.sinks[0].samples[length - 6..];
        let front = 0.5f32.sqrt() * 0.5 * 0.5;
        assert!(
            (sink[0] - front).abs() < 1e-6 && (sink[1] - front).abs() < 1e-6,
            "{sink:?}"
        );
        assert_eq!(sink[3], 0.25, "{sink:?}");
        assert_eq!([sink[2], sink[4], sink[5]], [0.0; 3]);
        let device = &output.samples[length - 6..];
        assert_eq!(device[3], capture::pcm16(0.25));
    }

    #[test]
    fn first_script_renders_after_play_allocate_nothing() {
        extern "C" fn script(
            kind: i32,
            _: u64,
            data: *mut f32,
            frames: i32,
            channels: i32,
            _: i64,
        ) {
            if kind == callback::SOURCE_FILTER {
                let data =
                    unsafe { std::slice::from_raw_parts_mut(data, (frames * channels) as usize) };
                data.iter_mut().for_each(|v| *v += 0.125);
            }
        }
        for mode in [
            speaker_mode::STEREO,
            speaker_mode::MONO,
            speaker_mode::MODE_5POINT1,
        ] {
            let mut m = Mixer::new(MixerConfig {
                speaker_mode: mode,
                ..MixerConfig::default()
            });
            m.set_callback(Some(script));
            m.add_clip(1, Clip::new(2, 48000, vec![0.25; 8192 * 2]));
            // A clipless generator and a clip source with a script.
            source_with(&mut m, 7, 0, |p| p.flags = source_flags::FILTER);
            source_with(&mut m, 8, 1, |p| p.flags = source_flags::FILTER);
            let channels = m.config.output_channels();
            let mut out = vec![0.0; 2048 * channels];
            for round in 0..2 {
                play(&mut m, 7, command::PLAY, 0.0);
                play(&mut m, 8, command::PLAY, 0.0);
                crate::audio::tests::allocations::start();
                m.render(&mut out, channels);
                let allocations = crate::audio::tests::allocations::finish();
                assert_eq!(allocations, 0, "mode {mode}, round {round}");
                assert!(out.iter().any(|v| *v != 0.0));
                play(&mut m, 7, command::STOP, 0.0);
                play(&mut m, 8, command::STOP, 0.0);
            }
        }
    }

    #[test]
    fn narrower_layouts_leave_the_other_device_channels_silent() {
        let mut m = Mixer::new(MixerConfig {
            speaker_mode: speaker_mode::MODE_7POINT1,
            ..MixerConfig::default()
        });
        m.add_clip(1, Clip::new(1, 48000, vec![0.5; 48000]));
        source_with(&mut m, 7, 1, |p| {
            p.flags |= source_flags::SPATIAL_STEREO | source_flags::LOOP;
            p.spatial_blend = 1.0;
            p.position = Vector3 {
                x: -2.0,
                y: 0.0,
                z: 0.0,
            };
        });
        play(&mut m, 7, command::PLAY, 0.0);
        let mut out = vec![0.0; 1024 * 8];
        m.render(&mut out, 8);
        assert!(
            out[out.len() - 8..][6] != 0.0,
            "{:?}",
            &out[out.len() - 8..]
        );
        m.reconfigure(MixerConfig {
            speaker_mode: speaker_mode::QUAD,
            ..m.config
        })
        .unwrap();
        // An eight-channel device playing quad: channels 4..8 stay silent.
        m.render(&mut out, 8);
        assert!(
            out.chunks_exact(8)
                .all(|frame| frame[4..].iter().all(|v| *v == 0.0))
        );
    }

    #[test]
    fn linked_effects_apply_one_gain_to_every_channel() {
        // A mono 2D clip in 5.1: front pair at sqrt(1/2), LFE at unity. A compressor over
        // the group keeps that ratio because one gain serves every channel.
        let mut m = Mixer::new(MixerConfig {
            speaker_mode: speaker_mode::MODE_5POINT1,
            ..MixerConfig::default()
        });
        let mut group = filter_group(3, 0, "Compressor");
        group.mixer = 3;
        group.effects[0].compressor = Some(super::super::compressor::CompressorSpec {
            threshold: -30.0,
            attack: 1.0,
            release: 50.0,
            makeup_gain: 0.0,
        });
        m.configure_group(group).unwrap();
        m.add_clip(1, Clip::new(1, 48000, vec![0.5; 48000]));
        source_with(&mut m, 7, 1, |p| {
            p.group = 3;
            p.flags |= source_flags::SPATIAL_STEREO;
        });
        play(&mut m, 7, command::PLAY, 0.0);
        m.update_suspension();
        let mut out = vec![0.0; 4096 * 6];
        m.render(&mut out, 6);
        let frame = &out[out.len() - 6..];
        assert!(frame[3] < 0.25, "compressed: {frame:?}");
        assert!(
            (frame[0] / frame[3] - 0.5f32.sqrt()).abs() < 1e-5,
            "{frame:?}"
        );
    }

    #[test]
    fn duck_volume_detects_a_sidechain_on_any_channel() {
        // Quad: the music (a mono 2D clip) is on the front pair only; the voice (a 3D source
        // behind the listener) reaches the rear pair only and feeds the duck through a send.
        fn group(handle: u64, parent: u64, muted: bool, effects: serde_json::Value) -> GroupSpec {
            serde_json::from_value(serde_json::json!({ "handle": handle, "parent": parent,
                "mixer": 1, "muted": muted, "bypass": false, "effects": effects }))
            .unwrap()
        }
        let render = |voice: bool| {
            let mut m = Mixer::new(MixerConfig {
                speaker_mode: speaker_mode::QUAD,
                ..MixerConfig::default()
            });
            m.configure_group(group(
                1,
                0,
                false,
                serde_json::json!([
                { "id": "duck", "kind": "Duck Volume", "duck": { "threshold": -40.0,
                  "ratio": 4.0, "attackTime": 0.0, "releaseTime": 0.0, "makeupGain": 0.0,
                  "knee": 0.0, "sidechainMix": 1.0 } }]),
            ))
            .unwrap();
            m.configure_group(group(
                2,
                1,
                true,
                serde_json::json!([
                { "id": "send", "kind": "Send", "sendTarget": "duck", "sendLevel": 1.0 }]),
            ))
            .unwrap();
            m.add_clip(1, Clip::new(1, 48000, vec![0.25; 48000]));
            source_with(&mut m, 7, 1, |p| {
                p.group = 1;
                p.flags |= source_flags::SPATIAL_STEREO;
            });
            play(&mut m, 7, command::PLAY, 0.0);
            if voice {
                source_with(&mut m, 8, 1, |p| {
                    p.group = 2;
                    p.flags |= source_flags::SPATIAL_STEREO;
                    p.spatial_blend = 1.0;
                    p.position = Vector3 {
                        x: 0.0,
                        y: 0.0,
                        z: -2.0,
                    };
                });
                play(&mut m, 8, command::PLAY, 0.0);
            }
            m.update_suspension();
            let mut out = vec![0.0; 8192 * 4];
            m.render(&mut out, 4);
            out[out.len() - 4..].to_vec()
        };
        let (quiet, ducked) = (render(false), render(true));
        assert!(ducked[0] < quiet[0] * 0.5, "{quiet:?} {ducked:?}");
        assert_eq!(ducked[0], ducked[1]);
        assert_eq!(&ducked[2..], &[0.0, 0.0]);
    }

    #[test]
    fn fold_returns_layout_frames_to_the_two_lanes() {
        let mut stereo = [0.0; 4];
        fold(&[0.5, -0.25], &mut stereo, None);
        assert_eq!(stereo, [0.5, 0.5, -0.25, -0.25]);
        fold(
            &[0.5, -0.25, 1.0, 1.0, 0.125, 0.25, 1.0, 1.0],
            &mut stereo,
            None,
        );
        assert_eq!(stereo, [0.5, -0.25, 0.125, 0.25]);
    }

    #[test]
    fn clipless_generator_keeps_or_wakes_its_mixer_after_silence() {
        use std::cell::Cell;
        thread_local! { static TONE: Cell<bool> = const { Cell::new(false) }; }
        extern "C" fn generator(
            kind: i32,
            _: u64,
            data: *mut f32,
            frames: i32,
            channels: i32,
            _: i64,
        ) {
            if kind != callback::SOURCE_FILTER {
                return;
            }
            let output =
                unsafe { std::slice::from_raw_parts_mut(data, (frames * channels) as usize) };
            output.fill(TONE.with(|tone| if tone.get() { 0.125 } else { 0.0 }));
        }
        for attach in [0, 80] {
            TONE.with(|tone| tone.set(false));
            let mut mixer = Mixer::new(MixerConfig::default());
            let mut root = filter_group(3, 0, "Attenuation");
            root.mixer = 3;
            root.enable_suspend = true;
            root.suspend_threshold = 0.0001;
            root.effects[0].gain = 1.0;
            mixer.configure_group(root).unwrap();
            mixer.set_callback(Some(generator));
            let mut output = vec![0.0; 2048];
            for block in 0..84 {
                if block == attach {
                    if attach > 0 {
                        assert!(mixer.group_nodes[&3].suspended);
                    }
                    source_with(&mut mixer, 7, 0, |p| {
                        p.group = 3;
                        p.flags = source_flags::FILTER;
                    });
                    play(&mut mixer, 7, command::PLAY, 0.0);
                }
                if block == 82 {
                    TONE.with(|tone| tone.set(true));
                }
                mixer.update_suspension();
                mixer.render(&mut output, 2);
                if block >= attach {
                    assert!(!mixer.group_nodes[&3].suspended);
                }
                assert!(
                    output
                        .iter()
                        .all(|sample| *sample == if block < 83 { 0.0 } else { 0.125 })
                );
            }
        }
    }

    #[test]
    fn clipless_lifecycle_matches_every_native_callback_state_and_sink_sample() {
        use std::cell::RefCell;
        #[derive(Default)]
        struct Trace {
            amplitude: f32,
            calls: u32,
            listener: u32,
            length: usize,
            generated: f32,
            first: f32,
            last: f32,
        }
        thread_local! { static TRACE: RefCell<Trace> = RefCell::new(Trace::default()); }
        extern "C" fn callback(
            kind: i32,
            _: u64,
            data: *mut f32,
            frames: i32,
            channels: i32,
            _: i64,
        ) {
            let values =
                unsafe { std::slice::from_raw_parts_mut(data, (frames * channels) as usize) };
            TRACE.with(|trace| {
                let mut t = trace.borrow_mut();
                if kind == callback::SOURCE_FILTER {
                    t.calls += 1;
                    t.length = values.len();
                    t.generated = t.amplitude;
                    values.fill(t.amplitude);
                } else if kind == callback::LISTENER_FILTER {
                    t.listener += 1;
                    t.first = values[0];
                    t.last = values[values.len() - 1];
                }
            });
        }
        let reference = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../tests/projects/audiogeneratorlifecycle/Reference/unity-6000.0.71f1");
        // Keep the regression self-contained without adding a decompression dependency.
        let unpack =
            |key: &str| -> Vec<f32> {
                let result = std::process::Command::new("python3").args(["-c",
                "import gzip,sys;sys.stdout.buffer.write(gzip.open(sys.argv[1],'rb').read())"])
                .arg(reference.join(format!("{key}.f32.gz"))).output().unwrap();
                assert!(result.status.success());
                result
                    .stdout
                    .chunks_exact(4)
                    .map(|b| f32::from_le_bytes(b.try_into().unwrap()))
                    .collect()
            };
        let expected_output = unpack("output");
        let expected_sink = unpack("master");
        let expected_trace = unpack("trace");
        let mut m = Mixer::new(MixerConfig::default());
        m.defer_voice_submission = true;
        let mut group = filter_group(3, 0, "Attenuation");
        group.mixer = 3;
        group.effects[0].gain = 1.0;
        m.configure_group(group).unwrap();
        m.listener_filter = true;
        m.set_callback(Some(callback));
        source_with(&mut m, 7, 0, |p| {
            p.group = 3;
            p.flags = source_flags::FILTER;
        });
        TRACE.with(|trace| {
            *trace.borrow_mut() = Trace {
                amplitude: 0.0625,
                ..Default::default()
            }
        });
        for step in 0..23 {
            if matches!(step, 0 | 1 | 2 | 5 | 12 | 15 | 18 | 21) {
                m.update_suspension();
            }
            let command = match step {
                3 | 20 => Some(command::PLAY),
                11 => Some(command::PAUSE),
                14 => Some(command::UNPAUSE),
                17 => Some(command::STOP),
                _ => None,
            };
            if let Some(command) = command {
                play(&mut m, 7, command, 0.0);
            }
            if step == 8 {
                TRACE.with(|trace| trace.borrow_mut().amplitude = 0.125);
            }
            if step == 20 {
                TRACE.with(|trace| trace.borrow_mut().amplitude = 0.25);
            }
            let before = TRACE.with(|trace| {
                let t = trace.borrow();
                (t.calls, t.listener)
            });
            let output = m
                .capture(
                    2048,
                    vec![capture::SinkRequest {
                        handle: 3,
                        length: 2048,
                        exclude_from_mix: false,
                    }],
                )
                .unwrap();
            let state = m.source_state(7);
            let observed = TRACE.with(|trace| {
                let t = trace.borrow();
                [
                    (t.calls - before.0) as f32,
                    (t.listener - before.1) as f32,
                    (state.main == voice_state::PLAYING) as u8 as f32,
                    state.is_virtual as f32,
                    state.position as f32,
                    t.length as f32,
                    t.generated,
                    t.first,
                    t.last,
                ]
            });
            assert_eq!(
                &observed,
                &expected_trace[step * 9..(step + 1) * 9],
                "trace step={step}"
            );
            assert_eq!(
                output.samples,
                expected_output[step * 2048..(step + 1) * 2048],
                "output step={step}"
            );
            let mut sink = vec![-3.0; 2048];
            sink[..output.sinks[0].samples.len()].copy_from_slice(&output.sinks[0].samples);
            assert_eq!(
                sink,
                expected_sink[step * 2048..(step + 1) * 2048],
                "sink step={step}"
            );
        }
    }

    #[test]
    fn source_filter_routes_match_complete_native_one_shots_and_scheduled_windows() {
        use std::cell::RefCell;
        #[derive(Default)]
        struct Trace {
            input: Vec<f32>,
            emitted: Vec<f32>,
            lengths: Vec<usize>,
        }
        thread_local! { static TRACE: RefCell<Trace> = RefCell::new(Trace::default()); }
        extern "C" fn filter(
            kind: i32,
            _: u64,
            data: *mut f32,
            frames: i32,
            channels: i32,
            _: i64,
        ) {
            if kind != callback::SOURCE_FILTER {
                return;
            }
            let values =
                unsafe { std::slice::from_raw_parts_mut(data, (frames * channels) as usize) };
            TRACE.with(|trace| {
                let mut trace = trace.borrow_mut();
                trace.input.extend_from_slice(values);
                trace.lengths.push(values.len());
                for value in values.iter_mut() {
                    *value = *value * 0.5 + 0.0625;
                }
                trace.emitted.extend_from_slice(values);
            });
        }
        let reference = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../tests/projects/audiogeneratorboundaries/Reference/unity-6000.0.71f1");
        let unpack =
            |key: &str| -> Vec<f32> {
                let result = std::process::Command::new("python3").args(["-c",
                "import gzip,sys;sys.stdout.buffer.write(gzip.open(sys.argv[1],'rb').read())"])
                .arg(reference.join(format!("{key}.f32.gz"))).output().unwrap();
                assert!(result.status.success());
                result
                    .stdout
                    .chunks_exact(4)
                    .map(|b| f32::from_le_bytes(b.try_into().unwrap()))
                    .collect()
            };
        let metadata: serde_json::Value =
            serde_json::from_slice(&std::fs::read(reference.join("capture.json")).unwrap())
                .unwrap();
        for key in [
            "idle",
            "shotPlain",
            "shotFilter",
            "generator",
            "generatorShot",
            "startInside",
            "endInside",
            "windowInside",
            "startClipInside",
            "endClipInside",
            "windowClipInside",
        ] {
            TRACE.with(|trace| *trace.borrow_mut() = Trace::default());
            let mut m = Mixer::new(MixerConfig::default());
            let mut master = filter_group(3, 0, "Attenuation");
            master.mixer = 3;
            master.effects[0].gain = 1.0;
            let mut child = filter_group(4, 3, "Attenuation");
            child.mixer = 3;
            child.effects[0].gain = 1.0;
            m.configure_group(master).unwrap();
            m.configure_group(child).unwrap();
            m.set_callback(Some(filter));
            let pcm: Vec<f32> = (0..4096).flat_map(|_| [0.125, -0.25]).collect();
            m.add_clip(1, Clip::new(2, 48000, pcm));
            let backed = key.contains("Clip");
            source_with(&mut m, 7, if backed { 1 } else { 0 }, |p| {
                p.group = 4;
                if key != "shotPlain" {
                    p.flags = source_flags::FILTER;
                }
            });
            assert_eq!(
                m.source_state(7).is_virtual != 0,
                metadata[format!("{key}.events")][0]["virtual"]
                    .as_bool()
                    .unwrap(),
                "initial virtual state {key}"
            );
            if key.ends_with("Inside") {
                let (start, end) = if key.starts_with("start") {
                    (8265.25, 12288.75)
                } else if key.starts_with("end") {
                    (8192.25, 10425.75)
                } else {
                    (8265.25, 8377.75)
                };
                play(&mut m, 7, command::PLAY, start / 48000.0);
                play(&mut m, 7, command::SET_SCHEDULED_END, end / 48000.0);
            } else {
                if key.starts_with("generator") {
                    play(&mut m, 7, command::PLAY, 0.0);
                }
                if matches!(key, "shotPlain" | "shotFilter" | "generatorShot") {
                    m.command(
                        7,
                        &AudioSourceCommand {
                            kind: command::PLAY_ONE_SHOT,
                            clip: 1,
                            volume_scale: 1.0,
                            ..Default::default()
                        },
                    );
                }
            }
            let check_state = |m: &Mixer, phase: &str, block: i64| {
                let event = metadata[format!("{key}.events")]
                    .as_array()
                    .unwrap()
                    .iter()
                    .find(|e| e["phase"] == phase && e["block"] == block)
                    .unwrap();
                let state = m.source_state(7);
                assert_eq!(
                    state.main == voice_state::PLAYING
                        || state.main == voice_state::SCHEDULED
                        || state.one_shots > 0,
                    event["playing"].as_bool().unwrap(),
                    "playing {key} {phase} {block}"
                );
                assert_eq!(
                    state.is_virtual != 0,
                    event["virtual"].as_bool().unwrap(),
                    "virtual {key} {phase} {block}"
                );
                assert_eq!(
                    state.position as i64,
                    event["timeSamples"].as_i64().unwrap(),
                    "position {key} {phase} {block}"
                );
            };
            let mut pcm = Vec::new();
            let mut sinks = [Vec::new(), Vec::new()];
            for block in 0..if key.ends_with("Inside") { 88 } else { 12 } {
                m.update_suspension();
                check_state(&m, "beforeRender", block);
                let result = m
                    .capture(
                        2048,
                        [3, 4]
                            .into_iter()
                            .map(|handle| capture::SinkRequest {
                                handle,
                                length: 2048,
                                exclude_from_mix: false,
                            })
                            .collect(),
                    )
                    .unwrap();
                check_state(&m, "afterRender", block);
                pcm.extend(result.samples);
                for (dest, sink) in sinks.iter_mut().zip(result.sinks) {
                    let mut block = vec![-3.0; 2048];
                    block[..sink.samples.len()].copy_from_slice(&sink.samples);
                    dest.extend(block);
                }
            }
            let check = |actual: &[f32], suffix: &str| {
                let expected = unpack(&format!("{key}.{suffix}"));
                assert_eq!(actual.len(), expected.len(), "length {key}.{suffix}");
                for (i, (a, b)) in actual.iter().zip(expected).enumerate() {
                    assert_eq!(*a, b, "{key}.{suffix} sample {i}");
                }
            };
            check(&pcm, "output");
            for (i, suffix) in ["master", "child"].into_iter().enumerate() {
                check(&sinks[i], suffix);
            }
            TRACE.with(|trace| {
                let trace = trace.borrow();
                assert!(
                    trace.lengths.iter().all(|n| *n == 2048),
                    "callback dimensions {key}"
                );
                check(&trace.input, "input");
                check(&trace.emitted, "emitted");
            });
            if key.ends_with("Inside") {
                assert_eq!(
                    m.source_state(7).main == voice_state::PLAYING,
                    !backed,
                    "completion {key}"
                );
            }
        }
    }

    #[test]
    fn generator_accumulates_independent_one_shots_without_changing_their_gain() {
        use std::cell::Cell;
        thread_local! { static INPUT_ZERO: Cell<bool> = const { Cell::new(true) }; }
        extern "C" fn generate(
            kind: i32,
            _: u64,
            data: *mut f32,
            frames: i32,
            channels: i32,
            _: i64,
        ) {
            if kind != callback::SOURCE_FILTER {
                return;
            }
            let pcm = unsafe { std::slice::from_raw_parts_mut(data, (frames * channels) as usize) };
            INPUT_ZERO.with(|zero| zero.set(zero.get() && pcm.iter().all(|x| *x == 0.0)));
            for (i, value) in pcm.iter_mut().enumerate() {
                *value = (i % 17) as f32 / 128.0;
            }
        }
        let render = |generator: bool, shots: bool| {
            let mut m = Mixer::new(MixerConfig {
                buffer_size: 512,
                ..Default::default()
            });
            m.set_callback(Some(generate));
            source_with(&mut m, 7, 0, |p| {
                if generator {
                    p.flags = source_flags::FILTER;
                }
            });
            m.add_clip(
                1,
                Clip::new(
                    2,
                    48000,
                    (0..3500).map(|i| (i % 31) as f32 / 64.0).collect(),
                ),
            );
            if generator {
                play(&mut m, 7, command::PLAY, 0.0);
            }
            if shots {
                for scale in [0.375, 0.8125] {
                    m.command(
                        7,
                        &AudioSourceCommand {
                            kind: command::PLAY_ONE_SHOT,
                            clip: 1,
                            volume_scale: scale,
                            ..Default::default()
                        },
                    );
                }
            }
            let mut out = vec![0.0; 4096];
            // Cross callback boundaries using device-sized packets.
            for chunk in out.chunks_mut(480 * 2) {
                m.render(chunk, 2);
            }
            out
        };
        INPUT_ZERO.with(|zero| zero.set(true));
        let generated = render(true, false);
        let shots = render(false, true);
        let combined = render(true, true);
        for ((g, s), actual) in generated.iter().zip(shots).zip(combined) {
            assert!((actual - (g + s)).abs() < 1e-6);
        }
        INPUT_ZERO.with(|zero| assert!(zero.get()));
    }

    #[test]
    fn component_play_distinguishes_pending_restart_from_active_replay() {
        let mut m = Mixer::new(MixerConfig::default());
        m.defer_voice_submission = true;
        m.add_clip(1, Clip::new(2, 48000, vec![0.125; 4096 * 2]));
        source_with(&mut m, 7, 1, |_| {});
        let mut output = vec![0.0; 2048];
        play(&mut m, 7, command::PLAY, 0.0);
        m.render(&mut output, 2);
        assert_eq!(m.source_state(7).position, 0.0);
        play(&mut m, 7, command::PLAY, 0.0);
        m.render(&mut output, 2);
        assert_eq!(m.source_state(7).position, 1024.0);
        m.update_suspension();
        play(&mut m, 7, command::PLAY, 0.0);
        m.render(&mut output, 2);
        assert_eq!(m.source_state(7).position, 0.0);
        assert!(output.iter().all(|x| *x == 0.0));
        play(&mut m, 7, command::STOP, 0.0);
        play(&mut m, 7, command::PLAY, 0.0);
        m.render(&mut output, 2);
        assert_eq!(m.source_state(7).position, 0.0);
        m.update_suspension();
        m.render(&mut output, 2);
        assert_eq!(m.source_state(7).position, 1024.0);
    }

    #[test]
    fn suspension_uses_dsp_time_at_updates_and_preserves_delay_and_sink_storage() {
        let mut m = Mixer::new(MixerConfig::default());
        let mut group = filter_group(3, 4, "Echo");
        group.mixer = 4;
        group.effects[0].delay = 5000.0;
        m.configure_group(group).unwrap();
        m.configure_group(
            serde_json::from_value(serde_json::json!({
                "handle": 4, "parent": 0, "mixer": 4, "enableSuspend": true,
                "suspendThreshold": 0.0001, "muted": false, "bypass": false, "effects": []
            }))
            .unwrap(),
        )
        .unwrap();
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
        let captured = m
            .capture(
                2048,
                vec![capture::SinkRequest {
                    handle: 4,
                    length: 2048,
                    exclude_from_mix: false,
                }],
            )
            .unwrap();
        assert!(captured.sinks[0].samples.is_empty());
        assert!(captured.samples.iter().all(|x| *x == 0.0));
        m.add_clip(2, Clip::new(2, 48000, vec![0.01; 1024 * 2]));
        source_with(&mut m, 8, 2, |p| {
            p.group = 4;
            p.flags = source_flags::LOOP;
        });
        play(&mut m, 8, command::PLAY, 0.0);
        m.render(&mut block, 2);
        assert!(!m.group_nodes[&4].suspended);
        assert_eq!(m.group_nodes[&3].rendered, 51 * 1024);
        // Neither time without rendering nor repeated graph submissions advance the history.
        for _ in 0..160 {
            m.update_suspension();
        }
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
    fn routing_units_after_attenuation_fade_the_group_input_even_when_bypassed() {
        for kind in ["Send", "Receive"] {
            for after in [false, true] {
                for bypass in [false, true] {
                    let mut m = Mixer::new(MixerConfig::default());
                    let attenuation =
                        serde_json::json!({"id": "att", "kind": "Attenuation", "gain": 1.0});
                    let route = serde_json::json!({"id": "route", "kind": kind, "bypass": bypass});
                    let effects = if after {
                        vec![attenuation, route]
                    } else {
                        vec![route, attenuation]
                    };
                    let spec = serde_json::from_value(
                        serde_json::json!({"handle": 1, "parent": 0, "mixer": 1, "muted": false,
                        "bypass": bypass, "effects": effects}),
                    )
                    .unwrap();
                    m.configure_group(spec).unwrap();
                    m.add_clip(1, Clip::new(2, 48000, vec![1.0; 2048]));
                    source_with(&mut m, 2, 1, |p| p.group = 1);
                    play(&mut m, 2, command::PLAY, 0.0);
                    m.update_suspension();
                    let mut out = vec![0.0; 2048];
                    m.render(&mut out, 2);
                    assert_eq!(out[64 * 2], if after { 64.0 / 1024.0 } else { 1.0 });
                    assert_eq!(out[1023 * 2], if after { 1023.0 / 1024.0 } else { 1.0 });
                }
            }
        }
    }

    #[test]
    fn measured_group_startup_accumulates_gain_across_render_chunks() {
        for period in [97, 480, 1024] {
            for measured in [false, true] {
                let mut m = Mixer::new(MixerConfig {
                    buffer_size: period,
                    ..MixerConfig::default()
                });
                let spec = serde_json::from_value(serde_json::json!({
                    "handle": 1, "parent": 0, "mixer": 1, "muted": false,
                    "bypass": false, "inputFadeFromMixer": measured,
                    "effects": [{"id": "att", "kind": "Attenuation", "gain": 1.0},
                        {"id": "send", "kind": "Send", "sendLevel": 0.0}]
                }))
                .unwrap();
                m.configure_group(spec).unwrap();
                let frames = period as usize * 2;
                m.add_clip(1, Clip::new(2, 48000, vec![1.0; frames * 2]));
                source_with(&mut m, 2, 1, |p| p.group = 1);
                play(&mut m, 2, command::PLAY, 0.0);
                m.update_suspension();
                let mut output = vec![0.0; frames * 2];
                for chunk in output.chunks_mut(47 * 2) {
                    m.render(chunk, 2);
                }
                let mut gain = 0.0_f32;
                for frame in 0..frames {
                    let expected = if frame >= period as usize {
                        1.0
                    } else if measured {
                        gain
                    } else {
                        frame as f32 / period as f32
                    };
                    if frame >= 64 {
                        assert_eq!(output[frame * 2], expected, "{period}/{measured}/{frame}");
                        assert_eq!(output[frame * 2 + 1], expected);
                    }
                    gain += 1.0 / period as f32;
                }
                if period == 480 && measured {
                    // This startup value changes the following delay's Q14 input by one step.
                    assert_eq!((output[220 * 2] * 0.03662109375 * 16384.0) as i32, 274);
                }
            }
        }
    }

    #[test]
    fn measured_startup_age_does_not_fade_a_group_first_used_after_its_mixer() {
        let mut m = Mixer::new(MixerConfig::default());
        let root = serde_json::from_value(serde_json::json!({"handle": 1, "parent": 0, "mixer": 1,
            "muted": false, "bypass": false, "inputFadeFromMixer": true,
            "effects": [{"id": "master", "kind": "Attenuation", "gain": 1.0}]}))
        .unwrap();
        m.configure_group(root).unwrap();
        m.add_clip(1, Clip::new(2, 48000, vec![0.25; 8192]));
        source_with(&mut m, 2, 1, |p| p.group = 1);
        play(&mut m, 2, command::PLAY, 0.0);
        m.update_suspension();
        let mut out = vec![0.0; 2048];
        m.render(&mut out, 2);
        let late = serde_json::from_value(serde_json::json!({"handle": 3, "parent": 1, "mixer": 1,
            "muted": false, "bypass": false, "inputFadeFromMixer": true,
            "effects": [{"id": "att", "kind": "Attenuation", "gain": 1.0},
                {"id": "send", "kind": "Send", "sendLevel": 0.0}]}))
        .unwrap();
        m.configure_group(late).unwrap();
        m.add_clip(4, Clip::new(2, 48000, vec![0.5; 2048]));
        source_with(&mut m, 5, 4, |p| p.group = 3);
        play(&mut m, 5, command::PLAY, 0.0);
        m.update_suspension();
        m.render(&mut out, 2);
        assert_eq!(out[64 * 2], 0.75);
        assert_eq!(out[512 * 2], 0.75);
    }

    #[test]
    fn sends_reach_a_later_sibling_receive_in_the_same_block_after_the_first_dsp_block() {
        fn group(handle: u64, parent: u64, effects: serde_json::Value) -> GroupSpec {
            serde_json::from_value(serde_json::json!({ "handle": handle, "parent": parent,
                "mixer": 1, "muted": false, "bypass": false, "effects": effects }))
            .unwrap()
        }
        let mut m = Mixer::new(MixerConfig::default());
        m.configure_group(group(
            1,
            0,
            serde_json::json!([{ "id": "a1", "kind": "Attenuation", "gain": 1.0 }]),
        ))
        .unwrap();
        m.configure_group(group(
            3,
            1,
            serde_json::json!([{ "id": "recv", "kind": "Receive" },
            { "id": "a3", "kind": "Attenuation", "gain": 1.0 }]),
        ))
        .unwrap();
        // The sender has the larger handle, so only the send edge puts it first.
        m.configure_group(group(
            5,
            1,
            serde_json::json!([
            { "id": "send", "kind": "Send", "sendTarget": "recv", "sendLevel": 0.5 },
            { "id": "a5", "kind": "Attenuation", "gain": 0.0 }]),
        ))
        .unwrap();
        assert_eq!(m.group_order, vec![5, 3, 1]);
        let mut input = vec![0.0; 4096 * 2];
        input[100 * 2] = 1.0;
        input[2000 * 2] = 1.0;
        m.add_clip(1, Clip::new(2, 48000, input));
        source_with(&mut m, 7, 1, |p| p.group = 5);
        play(&mut m, 7, command::PLAY, 0.0);
        m.update_suspension(); // submits the routed voice
        let mut out = vec![0.0; 4096 * 2];
        m.render(&mut out, 2);
        // Nothing reaches a receive during the mixer's first DSP block.
        assert_eq!(out[100 * 2], 0.0);
        assert_eq!(out[2000 * 2], 0.5);
        assert_eq!(out.iter().filter(|x| **x != 0.0).count(), 1);
        // A sending group registered while the mixer runs reaches its target at once.
        m.configure_group(group(
            6,
            1,
            serde_json::json!([
            { "id": "late", "kind": "Send", "sendTarget": "recv", "sendLevel": 0.25 },
            { "id": "a6", "kind": "Attenuation", "gain": 0.0 }]),
        ))
        .unwrap();
        let mut input = vec![0.0; 1024 * 2];
        input[10 * 2] = 1.0;
        m.add_clip(2, Clip::new(2, 48000, input));
        source_with(&mut m, 8, 2, |p| p.group = 6);
        play(&mut m, 8, command::PLAY, 0.0);
        m.update_suspension();
        let mut late = vec![0.0; 1024 * 2];
        m.render(&mut late, 2);
        assert_eq!(late.iter().filter(|x| **x != 0.0).count(), 1);
        // 0.25 times the voice's 64-frame start fade at frame 10.
        assert!(late.chunks_exact(2).any(|f| (0.03..0.05).contains(&f[0])));
        // A send into its own group, or one closing a loop, is refused and keeps the graph.
        assert!(
            m.configure_group(group(
                3,
                1,
                serde_json::json!([{ "id": "recv", "kind": "Receive" },
            { "id": "loop", "kind": "Send", "sendTarget": "recv", "sendLevel": 1.0 }])
            ))
            .is_err()
        );
        assert!(
            m.configure_group(group(
                1,
                0,
                serde_json::json!([{ "id": "back", "kind": "Duck Volume",
            "duck": { "threshold": -10.0, "ratio": 2.0, "attackTime": 0.1, "releaseTime": 0.3,
                "makeupGain": 0.0, "knee": 10.0, "sidechainMix": 1.0 } }])
            ))
            .is_ok()
        );
        assert!(
            m.configure_group(group(
                5,
                1,
                serde_json::json!([
            { "id": "send", "kind": "Send", "sendTarget": "recv", "sendLevel": 0.5 }])
            ))
            .is_ok()
        );
        assert!(
            m.configure_group(group(
                3,
                1,
                serde_json::json!([{ "id": "recv", "kind": "Receive" },
            { "id": "up", "kind": "Send", "sendTarget": "back", "sendLevel": 1.0 }])
            ))
            .is_ok()
        );
        assert!(
            m.configure_group(group(
                1,
                0,
                serde_json::json!([{ "id": "back", "kind": "Duck Volume",
            "duck": { "threshold": -10.0, "ratio": 2.0, "attackTime": 0.1, "releaseTime": 0.3,
                "makeupGain": 0.0, "knee": 10.0, "sidechainMix": 1.0 } },
            { "id": "down", "kind": "Send", "sendTarget": "recv", "sendLevel": 1.0 }])
            ))
            .is_err()
        );
        assert_eq!(m.group_nodes[&1].spec.effects.len(), 1);
    }
    #[test]
    fn mixer_group_cycles_and_unsupported_effects_fail_without_replacing_valid_state() {
        let mut m = Mixer::new(MixerConfig::default());
        m.configure_group(filter_group(3, 4, "Lowpass")).unwrap();
        assert!(m.configure_group(filter_group(4, 3, "Highpass")).is_err());
        assert!(m.configure_group(filter_group(3, 0, "Normalize")).is_err());
        assert_eq!(m.group_nodes[&3].spec.parent, 4);
        assert_eq!(m.group_nodes[&3].spec.effects[0].kind, "Lowpass");
    }
    #[test]
    fn active_simple_filters_need_a_measured_cutoff_and_compressors_their_parameters() {
        let spec = |bypass: bool, group_bypass: bool, effect: serde_json::Value| -> GroupSpec {
            let mut effect = effect;
            effect["bypass"] = bypass.into();
            serde_json::from_value(serde_json::json!({ "handle": 1, "parent": 0, "mixer": 1,
                "muted": false, "bypass": group_bypass, "effects": [effect] }))
            .unwrap()
        };
        let high = serde_json::json!({ "id": "h", "kind": "Highpass Simple", "cutoff": 23000.0 });
        let mut m = Mixer::new(MixerConfig::default());
        assert!(m.configure_group(spec(false, false, high.clone())).is_err());
        m.configure_group(spec(true, false, high.clone())).unwrap();
        m.configure_group(spec(false, true, high)).unwrap();
        let compressor = serde_json::json!({ "id": "c", "kind": "Compressor" });
        let mut m = Mixer::new(MixerConfig::default());
        assert!(
            m.configure_group(spec(false, false, compressor.clone()))
                .is_err()
        );
        m.configure_group(spec(true, false, compressor)).unwrap();
    }
    #[test]
    fn measured_effect_changes_keep_the_unit_and_still_reject_placement_or_bypass() {
        for (kind, a, b) in [
            (
                "Normalize",
                serde_json::json!({"normalize": {"fade": 5000.0, "lowest": 0.1, "maximum": 20.0}}),
                serde_json::json!({"normalize": {"fade": 100.0, "lowest": 0.6, "maximum": 0.5}}),
            ),
            (
                "Pitch Shifter",
                serde_json::json!({"pitchShifter": {"pitch": 0.75}}),
                serde_json::json!({"pitchShifter": {"pitch": 1.5}}),
            ),
        ] {
            let group = |mut parameters: serde_json::Value| {
                parameters["id"] = "live".into();
                parameters["kind"] = kind.into();
                parameters["mixLevel"] = 0.25.into();
                parameters["slotWetMeasured"] = true.into();
                parameters["effectTransitionsMeasured"] = true.into();
                parameters["slotWetTransitionsMeasured"] = true.into();
                serde_json::from_value::<GroupSpec>(serde_json::json!({
                    "handle": 1, "parent": 0, "mixer": 1, "muted": false, "bypass": false, "effects": [parameters]
                }))
                .unwrap()
            };
            let mut mixer = Mixer::new(MixerConfig::default());
            mixer.configure_group(group(a)).unwrap();
            mixer.group_nodes.get_mut(&1).unwrap().rendered = 1024;
            let mut next = group(b);
            next.effects[0].mix_level = Some(0.5);
            mixer.configure_group(next.clone()).unwrap();
            for edit in 0..4 {
                let mut rejected = next.clone();
                match edit {
                    0 => rejected.effects[0].id = "new identity".into(),
                    1 => rejected.effects[0].bypass = true,
                    2 => rejected.effects[0].mix_level = None,
                    _ => rejected.effects.clear(),
                }
                assert!(mixer.configure_group(rejected).is_err());
                assert_eq!(mixer.group_nodes[&1].spec, next);
            }
        }
        // A group without these limited effects keeps its ordinary bypass support.
        let mut mixer = Mixer::new(MixerConfig::default());
        let mut group = filter_group(1, 0, "Echo");
        mixer.configure_group(group.clone()).unwrap();
        mixer.group_nodes.get_mut(&1).unwrap().rendered = 1024;
        group.bypass = true;
        mixer.configure_group(group).unwrap();
    }

    #[test]
    fn static_wet_changes_are_accepted_before_rendering_and_rejected_atomically_after_it() {
        for (kind, parameters) in [
            ("Distortion", serde_json::json!({ "distortion": 0.5 })),
            (
                "Chorus",
                serde_json::json!({ "chorus": { "dry": 0.5, "wet1": 0.5, "wet2": 0.5, "delay": 40.0, "rate": 0.8, "depth": 0.03 } }),
            ),
            (
                "Flange",
                serde_json::json!({ "flange": { "dry": 0.45, "wet": 0.55, "depth": 1.0, "rate": 0.1 } }),
            ),
            (
                "Normalize",
                serde_json::json!({ "normalize": { "fade": 5000.0, "lowest": 0.1, "maximum": 20.0 } }),
            ),
            (
                "Pitch Shifter",
                serde_json::json!({ "pitchShifter": { "pitch": 0.75 } }),
            ),
        ] {
            let mut effect = parameters;
            effect["id"] = "wet".into();
            effect["kind"] = kind.into();
            effect["mixLevel"] = 0.5.into();
            effect["slotWetMeasured"] = true.into();
            let original: GroupSpec = serde_json::from_value(serde_json::json!({
                "handle": 1, "parent": 0, "mixer": 1, "muted": false, "bypass": false, "effects": [effect]
            })).unwrap();
            let mut mixer = Mixer::new(MixerConfig::default());
            mixer.configure_group(original.clone()).unwrap();
            let mut changed = original.clone();
            changed.effects[0].mix_level = Some(0.25);
            mixer.configure_group(changed).unwrap();
            mixer.configure_group(original.clone()).unwrap();
            mixer.group_nodes.get_mut(&1).unwrap().rendered = 1;
            mixer.configure_group(original.clone()).unwrap();
            // These generic fields are unused by these kernels. Group attenuation may
            // update its gain while a slot's wet level and kernel settings remain static.
            let mut unrelated = original.clone();
            unrelated.effects[0].gain = 0.25;
            unrelated.effects[0].cutoff = 1234.0;
            mixer.configure_group(unrelated).unwrap();
            mixer.configure_group(original.clone()).unwrap();
            for edit in 0..4 {
                let mut changed = original.clone();
                match edit {
                    0 => changed.effects[0].mix_level = Some(0.25),
                    1 => changed.effects[0].mix_level = None,
                    2 => changed.effects[0].bypass = true,
                    _ => changed.bypass = true,
                }
                assert!(
                    mixer
                        .configure_group(changed)
                        .unwrap_err()
                        .to_string()
                        .contains("live parameter")
                );
                assert_eq!(mixer.group_nodes[&1].spec, original);
            }
        }
    }

    #[test]
    fn flange_needs_valid_parameters_and_rejects_unmeasured_slot_wet_mixing() {
        let mut mixer = Mixer::new(MixerConfig::default());
        let mut spec = filter_group(3, 0, "Flange");
        assert!(mixer.configure_group(spec.clone()).is_err());
        spec.effects[0].bypass = true;
        mixer.configure_group(spec.clone()).unwrap();
        spec.effects[0].bypass = false;
        spec.effects[0].flange = Some(super::super::flange::FlangeSpec {
            dry: 0.45,
            wet: 0.55,
            depth: 1.0,
            rate: 0.1,
        });
        mixer.configure_group(spec.clone()).unwrap();
        spec.effects[0].mix_level = Some(0.5);
        assert!(
            mixer
                .configure_group(spec.clone())
                .unwrap_err()
                .to_string()
                .contains("wet mixing")
        );
        spec.effects[0].mix_level = None;
        spec.effects[0].flange.as_mut().unwrap().rate = 40.0;
        assert!(mixer.configure_group(spec).is_err());
        let before = mixer.config;
        assert!(
            mixer
                .reconfigure(MixerConfig {
                    sample_rate: u32::MAX,
                    ..before
                })
                .is_err()
        );
        assert_eq!(mixer.config, before);
        assert_eq!(mixer.group_nodes[&3].effects.len(), 1);
    }
    /// An unmeasured layout for every layout-limited effect.
    const PROLOGIC: i32 = speaker_mode::PROLOGIC;
    #[test]
    fn pitch_rejects_unmeasured_contexts_and_preserves_state_on_rejection() {
        let mut mixer = Mixer::new(MixerConfig::default());
        let mut spec = filter_group(3, 0, "Pitch Shifter");
        assert!(mixer.configure_group(spec.clone()).is_err());
        spec.effects[0].pitch_shifter = Some(super::super::pitch::PitchSpec {
            pitch: 0.75,
            fft_size: 1024,
            max_channels: 0,
        });
        mixer.configure_group(spec.clone()).unwrap();
        let before = mixer.config;
        for config in [
            MixerConfig {
                buffer_size: 4096,
                ..before
            },
            MixerConfig {
                sample_rate: 22050,
                ..before
            },
            MixerConfig {
                speaker_mode: PROLOGIC,
                ..before
            },
        ] {
            assert!(mixer.reconfigure(config).is_err());
            assert_eq!(mixer.config, before);
        }
        spec.effects[0].mix_level = Some(0.5);
        assert!(
            mixer
                .configure_group(spec.clone())
                .unwrap_err()
                .to_string()
                .contains("wet mixing")
        );
        spec.effects[0].mix_level = None;
        mixer.group_nodes.get_mut(&3).unwrap().rendered = 1024;
        mixer.configure_group(spec.clone()).unwrap();
        spec.effects[0].pitch_shifter.as_mut().unwrap().pitch = 1.5;
        assert!(
            mixer
                .configure_group(spec.clone())
                .unwrap_err()
                .to_string()
                .contains("live parameter")
        );
        spec.effects[0].pitch_shifter.as_mut().unwrap().pitch = 0.75;
        spec.bypass = true;
        assert!(mixer.configure_group(spec).is_err());
        assert!(!mixer.group_nodes[&3].spec.bypass);
        assert_eq!(
            mixer.group_nodes[&3].spec.effects[0]
                .pitch_shifter
                .as_ref()
                .unwrap()
                .pitch,
            0.75
        );
    }
    #[test]
    fn layout_limited_effects_fail_on_mono_and_other_unmeasured_outputs() {
        let measured = MixerConfig::default();
        let unmeasured = [
            MixerConfig {
                speaker_mode: 1,
                ..measured
            },
            MixerConfig {
                speaker_mode: 3,
                ..measured
            },
            MixerConfig {
                speaker_mode: 4,
                ..measured
            },
            MixerConfig {
                speaker_mode: 5,
                ..measured
            },
            MixerConfig {
                speaker_mode: 6,
                ..measured
            },
            MixerConfig {
                sample_rate: 22050,
                ..measured
            },
            MixerConfig {
                buffer_size: 512,
                ..measured
            },
            MixerConfig {
                speaker_mode: PROLOGIC,
                ..measured
            },
            MixerConfig {
                buffer_size: 4096,
                ..measured
            },
        ];
        // The Pitch Shifter is also measured with these buffers and layouts.
        let pitch_measured =
            |c: &MixerConfig| c.buffer_size == 512 || (1..=6).contains(&c.speaker_mode);
        for kind in ["Normalize", "Pitch Shifter"] {
            let mut spec = filter_group(3, 0, kind);
            spec.effects[0].normalize = Some(super::super::normalize::NormalizeSpec {
                fade: 5000.0,
                lowest: 0.1,
                maximum: 20.0,
            });
            spec.effects[0].pitch_shifter = Some(super::super::pitch::PitchSpec {
                pitch: 0.75,
                fft_size: 1024,
                max_channels: 0,
            });
            for config in unmeasured
                .into_iter()
                .filter(|c| kind != "Pitch Shifter" || !pitch_measured(c))
            {
                let mut mixer = Mixer::new(config);
                let error = mixer.configure_group(spec.clone()).unwrap_err().to_string();
                assert!(
                    error.contains(&format!("{kind} is measured only at")),
                    "{error}"
                );
                assert!(mixer.group_nodes.is_empty());
                let mut bypassed = spec.clone();
                bypassed.effects[0].bypass = true;
                mixer.configure_group(bypassed).unwrap();
            }
            let mut mixer = Mixer::new(measured);
            mixer.configure_group(spec.clone()).unwrap();
            mixer
                .reconfigure(MixerConfig {
                    sample_rate: 44100,
                    ..measured
                })
                .unwrap();
            let before = mixer.config;
            for config in unmeasured
                .into_iter()
                .filter(|c| kind != "Pitch Shifter" || !pitch_measured(c))
            {
                assert!(
                    mixer
                        .reconfigure(config)
                        .unwrap_err()
                        .to_string()
                        .contains("measured only at")
                );
                assert_eq!(mixer.config, before);
            }
        }
        // A control outside these kinds: Flange keeps running on a mono output.
        let mut spec = filter_group(3, 0, "Flange");
        spec.effects[0].flange = Some(super::super::flange::FlangeSpec {
            dry: 0.45,
            wet: 0.55,
            depth: 1.0,
            rate: 0.1,
        });
        Mixer::new(unmeasured[0]).configure_group(spec).unwrap();
    }
    #[test]
    fn normalize_rejects_unmeasured_configuration_and_live_changes_without_mutating_state() {
        let mut mixer = Mixer::new(MixerConfig::default());
        let mut spec = filter_group(3, 0, "Normalize");
        assert!(mixer.configure_group(spec.clone()).is_err());
        spec.effects[0].normalize = Some(super::super::normalize::NormalizeSpec {
            fade: 5000.0,
            lowest: 0.1,
            maximum: 20.0,
        });
        mixer.configure_group(spec.clone()).unwrap();
        let before = mixer.config;
        for config in [
            MixerConfig {
                buffer_size: 512,
                ..before
            },
            MixerConfig {
                sample_rate: 22050,
                ..before
            },
        ] {
            assert!(
                mixer
                    .reconfigure(config)
                    .unwrap_err()
                    .to_string()
                    .contains("measured only")
            );
            assert_eq!(mixer.config, before);
        }
        spec.effects[0].mix_level = Some(0.5);
        assert!(
            mixer
                .configure_group(spec.clone())
                .unwrap_err()
                .to_string()
                .contains("wet mixing")
        );
        spec.effects[0].mix_level = None;
        // A repeated managed submission retains the envelope; changing its settings must fail.
        mixer.group_nodes.get_mut(&3).unwrap().rendered = 1024;
        mixer.configure_group(spec.clone()).unwrap();
        spec.effects[0].normalize.as_mut().unwrap().fade = 100.0;
        assert!(
            mixer
                .configure_group(spec.clone())
                .unwrap_err()
                .to_string()
                .contains("live parameter")
        );
        spec.effects[0].normalize.as_mut().unwrap().fade = 5000.0;
        spec.bypass = true;
        assert!(mixer.configure_group(spec).is_err());
        assert!(!mixer.group_nodes[&3].spec.bypass);
        assert_eq!(
            mixer.group_nodes[&3].spec.effects[0]
                .normalize
                .as_ref()
                .unwrap()
                .fade,
            5000.0
        );
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
            let error = mixer
                .reconfigure(MixerConfig {
                    sample_rate: i32::MAX as u32,
                    ..before
                })
                .unwrap_err();
            assert!(error.to_string().contains("8 MiB"));
            assert_eq!(mixer.config, before);
            assert_eq!(mixer.group_nodes[&3].spec, spec);
            assert_eq!(mixer.group_nodes[&3].effects.len(), 1);
        }
        // The resource limit still admits a five-second stereo delay at 384 kHz.
        let mut mixer = Mixer::new(MixerConfig {
            sample_rate: 384000,
            ..Default::default()
        });
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
                let mut m = Mixer::new(MixerConfig {
                    sample_rate: rate,
                    ..Default::default()
                });
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
                                (0.125
                                    * (2.0
                                        * std::f64::consts::PI
                                        * if channel == 0 { 400.0 } else { 1700.0 }
                                        * frame as f64
                                        / rate as f64)
                                        .sin()) as f32
                            } else {
                                0.0
                            }
                        } else if channel == 0 && (frame == 0 || frame == 2048) {
                            0.25
                        } else if channel == 1 && (frame == 31 || frame == 3072) {
                            -0.125
                        } else {
                            0.0
                        };
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
                    assert!(
                        (native - actual).abs() <= 2e-5 + 2e-4 * native.abs(),
                        "{rate}/{key} sample {i}: native={native}, engine={actual}"
                    );
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

    // ------------------------------------------------------------------ filter components
    // Wiring controls only: the kernels are the AudioMixer ones measured in
    // tests/projects/mixerfilters and tests/projects/mixerecho. No capture measures a filter
    // component on an AudioSource, so these tests do not establish component parity.

    fn component(id: u64, kind: i32, stage: i32) -> AudioFilterDescriptor {
        AudioFilterDescriptor {
            id,
            kind,
            stage,
            cutoff: 1000.0,
            resonance: 1.0,
            delay: 10.0,
            decay: 0.5,
            dry_mix: 1.0,
            wet_mix: 1.0,
            // The AudioReverbFilter defaults (6000.0.71f1, tests/projects/audioreverb).
            reverb: [
                0.0, 0.0, 0.0, 1.0, 0.5, -10000.0, 0.0, 0.0, 0.04, 100.0, 100.0, 5000.0, 0.0, 250.0,
            ],
        }
    }

    #[test]
    fn listener_history_commands_reset_only_the_selected_owner_and_are_not_cached() {
        let mut mixer = Mixer::new(MixerConfig {
            sample_rate: 32000,
            buffer_size: 97,
            ..MixerConfig::default()
        });
        let original = [
            component(7, filter_kind::ECHO, 0),
            component(13, filter_kind::ECHO, filter_stage::AFTER_SCRIPTS),
        ];
        mixer.update_listener_filters(Ok(original.to_vec()), true, false);
        for filter in &mut mixer.listener_chain.filters {
            let mut input = vec![0.25; 2048];
            for block in input.chunks_mut(97 * 2) {
                filter.effect.process(block, false);
            }
        }
        let mut reconnect = original;
        reconnect[0].stage |= filter_stage::RECONNECTED;
        let packet = AudioFramePacket {
            filters: reconnect.as_ptr(),
            filter_count: reconnect.len() as i32,
            ..Default::default()
        };
        // Command bits do not participate in the Inspector-position ordering check.
        let decoded = unsafe { decode_listener_filters(&packet) }.unwrap();
        mixer.update_listener_filters(Ok(decoded), true, false);
        let mut changed = [0.0; 194];
        let mut retained = [0.0; 194];
        mixer.listener_chain.filters[0]
            .effect
            .process(&mut changed, false);
        mixer.listener_chain.filters[1]
            .effect
            .process(&mut retained, false);
        assert!(changed.iter().all(|sample| *sample == 0.0));
        assert!(retained.iter().any(|sample| sample.abs() > 0.1));
        assert_eq!(mixer.listener_filters, original);

        let mut input = vec![0.25; 2048];
        mixer.listener_chain.filters[0]
            .effect
            .process(&mut input, false);
        mixer.update_listener_filters(Ok(original.to_vec()), true, false);
        mixer.listener_chain.filters[0]
            .effect
            .process(&mut changed, false);
        assert!(changed.iter().any(|sample| sample.abs() > 0.1));
    }

    #[test]
    fn listener_reconnection_preserves_lowpass_state_but_owner_recreation_clears_it() {
        let descriptor = component(7, filter_kind::LOWPASS, 0);
        let mut mixer = Mixer::new(MixerConfig {
            sample_rate: 32000,
            buffer_size: 97,
            ..MixerConfig::default()
        });
        mixer.update_listener_filters(Ok(vec![descriptor]), true, false);
        let mut input = vec![0.25; 2048];
        mixer.listener_chain.filters[0]
            .effect
            .process(&mut input, false);
        let command = |stage| AudioFilterDescriptor {
            stage,
            ..descriptor
        };
        mixer.update_listener_filters(Ok(vec![command(filter_stage::RECONNECTED)]), true, false);
        let mut tail = [0.0; 194];
        mixer.listener_chain.filters[0]
            .effect
            .process(&mut tail, false);
        assert!(tail.iter().any(|sample| sample.abs() > 0.1));

        mixer.update_listener_filters(Ok(vec![command(filter_stage::RECREATED)]), true, false);
        tail.fill(0.0);
        mixer.listener_chain.filters[0]
            .effect
            .process(&mut tail, false);
        assert!(tail.iter().all(|sample| *sample == 0.0));
    }

    #[test]
    fn retained_listener_reverb_uses_the_new_layout_after_reconfiguration() {
        let mut mixer = Mixer::new(MixerConfig {
            speaker_mode: speaker_mode::QUAD,
            ..MixerConfig::default()
        });
        mixer.surround_effects = true;
        let descriptor = component(1, filter_kind::REVERB, 0);
        mixer.update_listener_filters(Ok(vec![descriptor]), false, true);
        mixer
            .reconfigure(MixerConfig {
                speaker_mode: speaker_mode::MODE_7POINT1,
                ..MixerConfig::default()
            })
            .unwrap();
        // An identical next frame leaves the existing listener chain in place.
        mixer.update_listener_filters(Ok(vec![descriptor]), false, true);
        let frames = 16384;
        let mut front = vec![0.0; frames * 2];
        let mut extra = vec![0.0; frames * 2 * 3];
        extra[frames * 2 * 2 + 123 * 2 + 1] = 0.25;
        let mut lanes = Lanes {
            samples: &mut extra,
            stride: frames * 2,
            offset: 0,
            channels: 6,
        };
        mixer.listener_chain.process_layout(
            &mut mixer.listener_lanes,
            &mut front,
            Some(&mut lanes),
            None,
            false,
            false,
        );
        assert!(
            front.iter().any(|v| v.abs() > 1e-6),
            "the new side speaker must feed the shared network"
        );
    }

    fn components() -> Vec<AudioFilterDescriptor> {
        vec![
            component(11, filter_kind::LOWPASS, filter_stage::BEFORE_SCRIPTS),
            AudioFilterDescriptor {
                cutoff: 300.0,
                resonance: 3.0,
                ..component(12, filter_kind::HIGHPASS, filter_stage::BEFORE_SCRIPTS)
            },
            component(13, filter_kind::ECHO, filter_stage::BEFORE_SCRIPTS),
        ]
    }

    /// The components run over `input` by the mixer kernels directly, each from a fresh state.
    fn through(input: &[f32], filters: &[AudioFilterDescriptor], rate: u32) -> Vec<f32> {
        let mut out = input.to_vec();
        for d in filters {
            Effect::new(effect_spec(d), rate, 1024)
                .unwrap()
                .process(&mut out, false);
        }
        out
    }

    fn stereo_tone(frames: usize) -> Clip {
        let samples = (0..frames)
            .flat_map(|i| [0.5 * (i as f32 * 0.05).sin(), (i % 7) as f32 / 14.0 - 0.25])
            .collect();
        Clip::new(2, 48000, samples)
    }

    fn render_source(
        filters: &[AudioFilterDescriptor],
        flags: u32,
        clip: Clip,
        frames: usize,
    ) -> Vec<f32> {
        let mut m = Mixer::new(MixerConfig::default());
        m.add_clip(1, clip);
        source_with(&mut m, 7, 1, |p| {
            p.flags = flags;
            p.filters = filters.to_vec();
        });
        play(&mut m, 7, command::PLAY, 0.0);
        let mut out = vec![0.0; frames * 2];
        m.render(&mut out, 2);
        out
    }

    fn packet(clip: u64, flags: u32, filters: &[AudioFilterDescriptor]) -> AudioSourcePacket {
        AudioSourcePacket {
            clip,
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
            reverb_zone_mix: 1.0,
            rolloff_mode: 0,
            priority: 128,
            flags,
            curve_length: 0,
            _pad: 0,
            rolloff_curve: std::ptr::null(),
            spatial_blend_curve: std::ptr::null(),
            spread_curve: std::ptr::null(),
            filter_count: filters.len() as i32,
            _pad2: 0,
            filters: if filters.is_empty() {
                std::ptr::null()
            } else {
                filters.as_ptr()
            },
            listener_position: Vector3::default(),
            listener_forward: Listener::default().forward,
            listener_up: Listener::default().up,
            doppler_listener_position: Vector3::default(),
        }
    }

    #[test]
    fn source_components_run_the_mixer_kernels_in_component_order() {
        let dry = render_source(&[], 0, stereo_tone(8192), 4096);
        assert!(dry.iter().any(|x| x.abs() > 0.1));
        let wet = render_source(&components(), 0, stereo_tone(8192), 4096);
        assert_eq!(wet, through(&dry, &components(), 48000));
        // Another order is another chain.
        let mut reversed = components();
        reversed.reverse();
        let other = render_source(&reversed, 0, stereo_tone(8192), 4096);
        assert_eq!(other, through(&dry, &reversed, 48000));
        assert_ne!(other, wet);
    }

    #[test]
    fn a_source_without_components_or_with_bypassed_effects_is_unchanged() {
        let dry = render_source(&[], 0, stereo_tone(8192), 4096);
        // bypassEffects skips the components; the samples are the dry voice's.
        let bypassed = render_source(
            &components(),
            source_flags::BYPASS_EFFECTS,
            stereo_tone(8192),
            4096,
        );
        assert_eq!(bypassed, dry);
        // A NativeApi 15 style packet (no descriptors) decodes to no components.
        let params = unsafe { SourceParams::from_packet(&packet(1, 0, &[])) };
        assert!(params.filters.is_empty());
        assert_eq!(params.filters_rejected, None);
        // Count 0 never reads the pointer.
        let mut dangling = packet(1, 0, &[]);
        dangling.filters = std::ptr::NonNull::<AudioFilterDescriptor>::dangling().as_ptr();
        let params = unsafe { SourceParams::from_packet(&dangling) };
        assert!(params.filters.is_empty() && params.filters_rejected.is_none());
        // A source with components leaves another source's samples alone.
        let mut m = Mixer::new(MixerConfig::default());
        m.add_clip(1, stereo_tone(8192));
        source_with(&mut m, 7, 1, |p| p.flags = source_flags::KEEP_OUTPUT);
        source_with(&mut m, 8, 1, |p| p.filters = components());
        play(&mut m, 7, command::PLAY, 0.0);
        play(&mut m, 8, command::PLAY, 0.0);
        let mut out = vec![0.0; 4096 * 2];
        m.render(&mut out, 2);
        let mut left = vec![0.0; 4096];
        assert!(m.output_data(7, 0, &mut left, -1));
        let alone: Vec<f32> = dry.chunks_exact(2).map(|f| f[0]).collect();
        assert_eq!(left, alone);
    }

    #[test]
    fn component_state_belongs_to_one_source_and_one_channel() {
        // A left-only impulse: the right channel stays silent through every component.
        let mut impulse = vec![0.0; 4096 * 2];
        impulse[100 * 2] = 0.5;
        let filters = components();
        let alone = render_source(&filters, 0, Clip::new(2, 48000, impulse.clone()), 2048);
        assert!(alone.chunks_exact(2).all(|f| f[1] == 0.0));
        assert!(alone[600 * 2..].chunks_exact(2).any(|f| f[0].abs() > 1e-6));
        // The same component identities on a second, silent source share nothing.
        let mut m = Mixer::new(MixerConfig::default());
        m.add_clip(1, Clip::new(2, 48000, impulse));
        m.add_clip(2, Clip::new(2, 48000, vec![0.0; 4096 * 2]));
        source_with(&mut m, 7, 1, |p| {
            p.flags = source_flags::KEEP_OUTPUT;
            p.filters = filters.clone();
        });
        source_with(&mut m, 8, 2, |p| {
            p.flags = source_flags::KEEP_OUTPUT;
            p.filters = filters.clone();
        });
        play(&mut m, 7, command::PLAY, 0.0);
        play(&mut m, 8, command::PLAY, 0.0);
        let mut out = vec![0.0; 2048 * 2];
        m.render(&mut out, 2);
        let mut silent = vec![1.0; 2048];
        assert!(m.output_data(8, 0, &mut silent, -1));
        assert!(silent.iter().all(|x| *x == 0.0));
        let mut heard = vec![0.0; 2048];
        assert!(m.output_data(7, 0, &mut heard, -1));
        let expected: Vec<f32> = alone.chunks_exact(2).map(|f| f[0]).collect();
        assert_eq!(heard, expected);
    }

    #[test]
    fn unchanged_components_keep_their_state_and_a_new_identity_starts_empty() {
        fn run(update: impl Fn(&mut Mixer, usize)) -> Vec<f32> {
            let mut m = Mixer::new(MixerConfig::default());
            m.add_clip(1, stereo_tone(8192));
            source_with(&mut m, 7, 1, |p| p.filters = components());
            play(&mut m, 7, command::PLAY, 0.0);
            let mut out = vec![0.0; 4096 * 2];
            for (block, chunk) in out.chunks_mut(512 * 2).enumerate() {
                update(&mut m, block);
                m.render(chunk, 2);
            }
            out
        }
        let steady = run(|_, _| {});
        // The engine sends the packet every frame: equal descriptors change nothing.
        assert_eq!(
            run(|m, _| source_with(m, 7, 1, |p| p.filters = components())),
            steady
        );
        // A replaced component (new instance ID) restarts its history.
        let replaced = run(|m, block| {
            let mut filters = components();
            if block >= 4 {
                filters[2].id = 99;
            }
            source_with(m, 7, 1, |p| p.filters = filters);
        });
        assert_eq!(replaced[..2048 * 2], steady[..2048 * 2]);
        assert_ne!(replaced[2048 * 2..], steady[2048 * 2..]);
    }

    #[test]
    fn stop_and_the_end_of_playback_restart_the_components_empty() {
        let first = render_source(&components(), 0, stereo_tone(1024), 2048);
        let mut m = Mixer::new(MixerConfig::default());
        m.add_clip(1, stereo_tone(1024));
        source_with(&mut m, 7, 1, |p| p.filters = components());
        // Natural end: the voice finishes inside the first 2048 frames.
        play(&mut m, 7, command::PLAY, 0.0);
        let mut out = vec![0.0; 2048 * 2];
        m.render(&mut out, 2);
        assert_eq!(out, first);
        assert_eq!(m.source_state(7).main, voice_state::NONE);
        m.render(&mut out, 2);
        play(&mut m, 7, command::PLAY, 0.0);
        m.render(&mut out, 2);
        assert_eq!(out, first);
        // Stop inside the clip; the idle source restarts its components before it plays again.
        // (Stop and Play in one update keep them: stop_and_play_in_one_update_keep_...)
        play(&mut m, 7, command::PLAY, 0.0);
        m.render(&mut out[..512 * 2], 2);
        play(&mut m, 7, command::STOP, 0.0);
        m.render(&mut out[..64 * 2], 2);
        play(&mut m, 7, command::PLAY, 0.0);
        m.render(&mut out, 2);
        assert_eq!(out, first);
    }

    #[test]
    fn explicit_stop_freezes_component_histories_across_idle_device_periods() {
        for rate in [44100, 48000] {
            for shot in [false, true] {
                let run = |idle_period: usize| {
                    let mut m = Mixer::new(MixerConfig {
                        sample_rate: rate,
                        ..Default::default()
                    });
                    let mut clip = stereo_tone(16384);
                    clip.rate = rate;
                    m.add_clip(1, clip);
                    source_with(&mut m, 7, 1, |p| {
                        p.flags = source_flags::COMPONENT_HISTORY;
                        p.filters = components();
                    });
                    let start = |m: &mut Mixer| {
                        if shot {
                            m.command(
                                7,
                                &AudioSourceCommand {
                                    kind: command::PLAY_ONE_SHOT,
                                    clip: 1,
                                    volume_scale: 1.0,
                                    ..Default::default()
                                },
                            );
                        } else {
                            play(m, 7, command::PLAY, 0.0);
                        }
                    };
                    start(&mut m);
                    m.render(&mut vec![0.0; 4096 * 2], 2);
                    play(&mut m, 7, command::STOP, 0.0);
                    if idle_period > 0 {
                        let mut idle = vec![1.0; idle_period * 2];
                        for _ in 0..7 {
                            m.render(&mut idle, 2);
                            assert!(idle.iter().all(|v| *v == 0.0));
                        }
                    }
                    start(&mut m);
                    let mut replay = vec![0.0; 2048 * 2];
                    m.render(&mut replay, 2);
                    replay
                };
                let immediate = run(0);
                for period in [100, 257, 1024, 1300] {
                    assert_eq!(
                        run(period),
                        immediate,
                        "rate={rate} shot={shot} period={period}"
                    );
                }
                let mut fresh_clip = stereo_tone(16384);
                fresh_clip.rate = rate;
                let fresh = render_source(&components(), 0, fresh_clip, 2048);
                assert_ne!(immediate, fresh);
            }
        }
    }

    #[test]
    fn disabled_component_freezes_history_while_other_units_continue() {
        let mut descriptors = components();
        let mut paused = FilterChain::default();
        let mut expected = FilterChain::default();
        for chain in [&mut paused, &mut expected] {
            chain.apply(&descriptors, 48000, 1024, true).unwrap();
        }
        let mut a = stereo_tone(4096).samples;
        let mut b = a.clone();
        paused.process(&mut a[..2048], None, false);
        expected.process(&mut b[..2048], None, false);
        descriptors[2].stage |= filter_stage::BYPASSED;
        paused.apply(&descriptors, 48000, 1024, true).unwrap();
        paused.process(&mut a[2048..4096], None, false);
        // Only the Echo is bypassed. The two preceding filters keep processing.
        for filter in &mut expected.filters[..2] {
            filter.effect.process(&mut b[2048..4096], false);
        }
        assert_eq!(a[..4096], b[..4096]);
        descriptors[2].stage &= !filter_stage::BYPASSED;
        paused.apply(&descriptors, 48000, 1024, true).unwrap();
        paused.process(&mut a[4096..], None, false);
        expected.process(&mut b[4096..], None, false);
        assert_eq!(a, b);
    }

    fn impulse_clip(frames: usize) -> Clip {
        // After the voice's start ramp.
        let mut samples = vec![0.0; frames * 2];
        samples[2 * 200] = 0.5;
        Clip::new(2, 48000, samples)
    }

    /// Native (tests/projects/audioreverb): after the last voice ends the chain runs to the end
    /// of the next DSP buffer, then its tail is cut.
    #[test]
    fn components_run_one_dsp_buffer_past_the_last_voice() {
        // Reverb Delay 0: the late reverb rings through the whole next buffer.
        let mut reverb = component(1, filter_kind::REVERB, filter_stage::BEFORE_SCRIPTS);
        reverb.reverb[8] = 0.0;
        // The 300-frame clip ends in the first 1024-frame buffer: the tail stops at 2048.
        let mut m = Mixer::new(MixerConfig::default());
        m.add_clip(1, impulse_clip(300));
        source_with(&mut m, 7, 1, |p| p.filters = vec![reverb]);
        play(&mut m, 7, command::PLAY, 0.0);
        m.update_suspension();
        let mut out = vec![0.0; 2 * 4096];
        m.render(&mut out, 2);
        assert!(out[2 * 200] != 0.0);
        assert!(out[2 * 2040..2 * 2048].iter().any(|v| v.abs() > 1e-6));
        assert!(out[2 * 2048..].iter().all(|v| *v == 0.0));
    }

    /// Placement is measured independently of the common source path: feed
    /// the captured dry spatial PCM into the shared cores and compare every output sample.
    #[test]
    fn source_filter_cores_follow_captured_spatialization_at_both_rates() {
        use std::io::Read;
        for rate in [48000, 44100] {
            let root = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
                .join("../../tests/projects/audiodistortion/Reference/unity-6000.0.71f1")
                .join(format!("spatial-rate-{rate}"));
            let read = |case: &str| {
                let file = std::fs::File::open(root.join(format!("{case}.master.f32.gz"))).unwrap();
                let mut bytes = Vec::new();
                flate2::read::GzDecoder::new(file)
                    .read_to_end(&mut bytes)
                    .unwrap();
                bytes
                    .chunks_exact(4)
                    .map(|b| f32::from_le_bytes(b.try_into().unwrap()))
                    .collect::<Vec<_>>()
            };
            for mono in [0, 1] {
                for context in [
                    "front", "side", "spread", "blend", "pitch", "doppler", "zone",
                ] {
                    let dry = read(&format!("spatial_dry_{context}_mono{mono}"));
                    for (kind, name) in [
                        (filter_kind::DISTORTION, "dist"),
                        (filter_kind::CHORUS, "chorus"),
                    ] {
                        let case = format!("spatial_{name}_{context}_mono{mono}");
                        let expected = read(&case);
                        let descriptor = AudioFilterDescriptor {
                            cutoff: 0.5,
                            reverb: [
                                0.25, 0.75, 0.375, 0.125, 17.5, 5.0, 0.5, 0.0, 0.0, 0.0, 0.0, 0.0,
                                0.0, 0.0,
                            ],
                            ..component(1, kind, 0)
                        };
                        let mut effect = Effect::new(effect_spec(&descriptor), rate, 1024).unwrap();
                        let mut actual = dry.clone();
                        for chunk in actual.chunks_mut(BLOCK * 2) {
                            effect.process(chunk, false);
                        }
                        let failures: Vec<_> = actual
                            .iter()
                            .zip(&expected)
                            .enumerate()
                            .filter_map(|(i, (a, e))| {
                                ((a - e).abs() > 2e-5 + 2e-4 * e.abs()).then_some((i, *e, *a))
                            })
                            .collect();
                        assert!(failures.is_empty(), "{rate} {case}: {failures:?}");
                    }
                }
            }
        }
    }

    // The exact cutoff frames are captured by tests/projects/audioreverb at both rates.
    // Device callback sizes must not change those frames or the preceding waveform.
    #[test]
    fn component_tail_cutoff_is_independent_of_device_period_and_last_voice_kind() {
        for rate in [48000, 44100] {
            for frames in [11264_usize, 11265, 11519, 11521, 12287, 12289, 12500, 12801] {
                for shot in [false, true] {
                    let render = |period: usize| {
                        let mut m = Mixer::new(MixerConfig {
                            sample_rate: rate,
                            ..Default::default()
                        });
                        let mut clip = impulse_clip(frames);
                        clip.rate = rate;
                        m.add_clip(1, clip);
                        let mut reverb = component(1, filter_kind::REVERB, 0);
                        reverb.reverb[8] = 0.0;
                        source_with(&mut m, 7, 1, |p| {
                            p.flags = source_flags::COMPONENT_FILTER_TAIL;
                            p.filters = vec![reverb];
                        });
                        play(&mut m, 7, command::PLAY, 0.0);
                        if shot {
                            // A main voice ends earlier than the last one-shot.
                            m.command(
                                7,
                                &AudioSourceCommand {
                                    kind: command::SET_SCHEDULED_END,
                                    time: (frames - 100) as f64 / rate as f64,
                                    ..Default::default()
                                },
                            );
                            m.command(
                                7,
                                &AudioSourceCommand {
                                    kind: command::PLAY_ONE_SHOT,
                                    clip: 1,
                                    volume_scale: 0.5,
                                    ..Default::default()
                                },
                            );
                        }
                        let mut out = vec![0.0; 16384 * 2];
                        for chunk in out.chunks_mut(period * 2) {
                            m.render(chunk, 2);
                        }
                        out
                    };
                    let reference = render(1024);
                    let cutoff = frames.div_ceil(1024) * 1024 + 1024;
                    assert!(
                        reference[(cutoff - 16) * 2..cutoff * 2]
                            .iter()
                            .any(|v| v.abs() > 1e-8)
                    );
                    assert!(reference[cutoff * 2..].iter().all(|v| *v == 0.0));
                    for period in [100, 255, 257, 441, 1000, 1300] {
                        assert_eq!(
                            render(period),
                            reference,
                            "rate={rate}, frames={frames}, shot={shot}, period={period}"
                        );
                    }
                }
            }
        }
    }

    #[test]
    fn listener_snapshot_retains_its_sample_when_the_global_listener_changes() {
        for rate in [48000, 44100] {
            let setup = |relative: bool| {
                let mut m = Mixer::new(MixerConfig {
                    sample_rate: rate,
                    ..Default::default()
                });
                m.listener.velocity.z = 20.0;
                m.add_clip(1, ramp_clip(8192, rate));
                source_with(&mut m, 7, 1, |p| {
                    p.flags = source_flags::SPATIAL_STEREO;
                    p.spatial_blend = 1.0;
                    p.position.z = 10.0;
                    if relative {
                        p.listener_snapshot = Some((Listener::default(), Vector3::default()));
                        p.velocity.z = -20.0;
                    }
                });
                play(&mut m, 7, command::PLAY, 0.0);
                m
            };
            let mut absolute = setup(false);
            let mut relative = setup(true);
            let mut expected = vec![0.0; 2048];
            let mut actual = vec![0.0; 2048];
            absolute.render(&mut expected, 2);
            relative.render(&mut actual, 2);
            assert_eq!(actual, expected);
            relative.listener.velocity.z = 40.0;
            relative.listener.position.x = 3.0;
            relative.listener.forward = Vector3 {
                x: 1.0,
                y: 0.0,
                z: 0.0,
            };
            absolute.render(&mut expected, 2);
            relative.render(&mut actual, 2);
            assert_eq!(actual, expected);
            assert_eq!(
                relative.source_state(7).position,
                absolute.source_state(7).position
            );
            absolute.listener.velocity.z = 40.0;
            absolute.render(&mut expected, 2);
            relative.render(&mut actual, 2);
            assert_ne!(actual, expected);
        }
    }

    #[test]
    fn voice_zone_send_omits_completion_buffer_before_components_across_device_periods() {
        for rate in [48000, 44100] {
            for frames in [1024_usize, 1025, 11265, 12288] {
                for shot in [false, true] {
                    let render = |period: usize, filtered: bool, last_sample: f32| {
                        let mut m = Mixer::new(MixerConfig {
                            sample_rate: rate,
                            ..Default::default()
                        });
                        let mut clip = impulse_clip(frames);
                        clip.rate = rate;
                        // The final buffer has a nonzero sample, distinct from an earlier impulse.
                        clip.samples[2 * (frames - 1)] = last_sample;
                        m.add_clip(1, clip);
                        let mut short = impulse_clip(frames / 2);
                        short.rate = rate;
                        m.add_clip(2, short);
                        let mut zone = component(100, filter_kind::REVERB, 0);
                        zone.reverb[0] = -10000.0;
                        zone.reverb[8] = 0.0;
                        m.update_zone(&zone, true);
                        source_with(&mut m, 7, 1, |p| {
                            p.flags = source_flags::ZONE_VOICE_SEND | source_flags::SPATIAL_STEREO;
                            if filtered {
                                p.filters = vec![component(7, filter_kind::DISTORTION, 0)];
                            }
                        });
                        play(&mut m, 7, command::PLAY, 0.0);
                        if shot {
                            m.command(
                                7,
                                &AudioSourceCommand {
                                    kind: command::PLAY_ONE_SHOT,
                                    clip: 2,
                                    volume_scale: 0.5,
                                    ..Default::default()
                                },
                            );
                        }
                        let mut out = vec![0.0; 16384 * 2];
                        for chunk in out.chunks_mut(period * 2) {
                            m.render(chunk, 2);
                        }
                        // Inspect the zone's histories directly: component output cannot affect them.
                        let zone = m.zone.as_mut().unwrap();
                        let mut tail = vec![0.0; 16384 * 2];
                        zone.reverb.process(&mut tail);
                        (out, tail)
                    };
                    let reference = render(1024, false, 0.6);
                    let no_last_sample = render(1024, false, 0.0);
                    assert_eq!(
                        reference.1, no_last_sample.1,
                        "completion sample reached the zone"
                    );
                    assert_ne!(
                        reference.0, no_last_sample.0,
                        "completion sample disappeared from dry output"
                    );
                    if frames > 1024 {
                        assert!(
                            reference.1.iter().any(|v| v.abs() > 1e-8),
                            "earlier voice samples did not reach the zone"
                        );
                    }
                    assert_eq!(
                        reference.1,
                        render(1024, true, 0.6).1,
                        "source components changed the send"
                    );
                    for period in [100, 255, 257, 441, 1000, 1300] {
                        assert_eq!(
                            render(period, false, 0.6),
                            reference,
                            "{rate} {frames} {shot} {period}"
                        );
                    }
                }
            }
        }
    }

    #[test]
    fn listener_reverb_runs_on_the_mix_except_bypassing_unrouted_sources() {
        let listen = |bypass: u32| {
            let mut m = Mixer::new(MixerConfig::default());
            m.add_clip(1, impulse_clip(300));
            m.update_listener_filters(Ok(vec![component(5, filter_kind::REVERB, 0)]), false, false);
            source_with(&mut m, 7, 1, |p| {
                p.flags = bypass;
            });
            play(&mut m, 7, command::PLAY, 0.0);
            m.update_suspension();
            let mut out = vec![0.0; 2 * 4096];
            m.render(&mut out, 2);
            assert!(out[2 * 200] != 0.0);
            out
        };
        // The listener's tail outlives the source.
        assert!(listen(0)[2 * 2048..].iter().any(|v| v.abs() > 1e-6));
        assert!(
            listen(source_flags::BYPASS_LISTENER_EFFECTS)[2 * 201..]
                .iter()
                .all(|v| *v == 0.0)
        );
        assert_eq!(listen(source_flags::BYPASS_EFFECTS), listen(0));
        let mut m = Mixer::new(MixerConfig::default());
        m.update_listener_filters(Err(anyhow::anyhow!("refused")), false, false);
        assert!(m.listener_chain.filters.is_empty());
    }

    #[test]
    fn measured_zone_return_bypasses_listener_components_at_partial_periods() {
        let render = |zone: bool, listener: bool, measured: bool| {
            let mut mixer = Mixer::new(MixerConfig {
                sample_rate: 32000,
                buffer_size: 97,
                ..MixerConfig::default()
            });
            let clip = Clip::new(1, 32000, vec![0.0625; 12000]);
            mixer.add_clip(1, clip);
            if zone {
                let mut descriptor = component(1, filter_kind::REVERB, 0);
                descriptor.reverb = [
                    -10000.0, -500.0, -300.0, 0.3, 0.7, -500.0, 0.007, -500.0, 0.011, 100.0, 100.0,
                    5000.0, 0.0, 250.0,
                ];
                mixer.update_zone(&descriptor, measured);
            }
            if listener {
                let mut descriptor = component(2, filter_kind::DISTORTION, 0);
                descriptor.cutoff = 0.75;
                mixer.update_listener_filters(Ok(vec![descriptor]), false, false);
            }
            source_with(&mut mixer, 7, 1, |_| {});
            play(&mut mixer, 7, command::PLAY, 0.0);
            mixer.update_suspension();
            let mut output = vec![0.0; 8192 * 2];
            mixer.render(&mut output, 2);
            output
        };
        let dry = render(false, false, true);
        let filtered = render(false, true, true);
        let zone = render(true, false, true);
        let combined = render(true, true, true);
        assert!(zone.iter().zip(&dry).any(|(a, b)| (a - b).abs() > 0.001));
        for i in 0..combined.len() {
            assert!((combined[i] - (filtered[i] + zone[i] - dry[i])).abs() < 2e-5);
        }
        let legacy_zone = render(true, false, false);
        let legacy_combined = render(true, true, false);
        assert!(
            legacy_combined
                .iter()
                .enumerate()
                .any(
                    |(i, actual)| (actual - (filtered[i] + legacy_zone[i] - dry[i])).abs() > 0.001
                )
        );
    }

    #[test]
    fn zone_startup_compatibility_changes_the_first_audible_reflection() {
        let mut descriptor = component(1, filter_kind::REVERB, 0);
        descriptor.reverb = [
            -10000.0, 0.0, 0.0, 1.0, 0.5, 0.0, 0.0, -10000.0, 0.0, 100.0, 100.0, 5000.0, 0.0, 250.0,
        ];
        let first = |measured| {
            let mut mixer = Mixer::new(MixerConfig::default());
            mixer.update_zone(&descriptor, measured);
            let mut input = [0.25, 0.25];
            mixer.zone.as_mut().unwrap().reverb.process(&mut input);
            input
        };
        let legacy = first(false);
        let measured = first(true);
        assert!(legacy[0] > measured[0] + 0.01);
        assert!(measured.iter().all(|value| value.abs() < 1e-5));
    }

    #[test]
    fn uncapturable_periods_do_not_prepare_listener_return_storage() {
        for mode in [speaker_mode::MONO, speaker_mode::MODE_7POINT1] {
            let mut mixer = Mixer::new(MixerConfig {
                buffer_size: u32::MAX,
                speaker_mode: mode,
                ..Default::default()
            });
            assert_eq!(mixer.capture_listener_return.capacity(), 0);
            let output = mixer.capture(2048, Vec::new()).unwrap();
            assert!(output.samples.is_empty());
            assert_eq!(mixer.frames(), 0);
            mixer
                .reconfigure(MixerConfig {
                    buffer_size: 97,
                    speaker_mode: mode,
                    ..Default::default()
                })
                .unwrap();
            let output = mixer
                .capture(97 * mixer.config.output_channels(), Vec::new())
                .unwrap();
            assert_eq!(output.samples.len(), 97 * mixer.config.output_channels());
            assert_eq!(mixer.frames(), 97);
        }
    }

    #[test]
    fn listener_scripts_preserve_independent_zone_and_bypass_returns_in_both_render_paths() {
        use std::cell::Cell;
        thread_local! {
            static GAIN: Cell<f32> = const { Cell::new(1.0) };
            static CALLS: Cell<usize> = const { Cell::new(0) };
        }
        extern "C" fn script(
            kind: i32,
            _: u64,
            data: *mut f32,
            frames: i32,
            channels: i32,
            _: i64,
        ) {
            if kind == callback::LISTENER_FILTER {
                CALLS.with(|v| v.set(v.get() + 1));
                let gain = GAIN.with(Cell::get);
                for value in
                    unsafe { std::slice::from_raw_parts_mut(data, (frames * channels) as usize) }
                {
                    *value *= gain;
                }
            }
        }
        for period in [97_usize, 1024] {
            for mode in [
                speaker_mode::MONO,
                speaker_mode::STEREO,
                speaker_mode::SURROUND,
                speaker_mode::MODE_7POINT1,
            ] {
                for capture in [false, true] {
                    let render = |gain, zone_on, bypass, routed, measured| {
                        GAIN.with(|v| v.set(gain));
                        CALLS.with(|v| v.set(0));
                        let config = MixerConfig {
                            buffer_size: period as u32,
                            speaker_mode: mode,
                            ..Default::default()
                        };
                        let channels = config.output_channels();
                        let frames = 8192_usize.div_ceil(period) * period;
                        let mut m = Mixer::new(config);
                        m.surround_effects = measured;
                        m.listener_filter = true;
                        m.set_callback(Some(script));
                        if zone_on {
                            let mut zone = component(1, filter_kind::REVERB, 0);
                            zone.reverb = [
                                -10000.0, -1000.0, -99.0, 1.49, 0.83, -2601.0, 0.007, 200.0, 0.011,
                                100.0, 100.0, 5000.0, 0.0, 250.0,
                            ];
                            m.update_zone(&zone, true);
                        }
                        if routed {
                            let mut group = filter_group(3, 0, "Attenuation");
                            group.mixer = 3;
                            group.effects[0].gain = 1.0;
                            m.configure_group(group).unwrap();
                        }
                        m.add_clip(1, Clip::new(1, 48000, vec![0.125; frames + period]));
                        source_with(&mut m, 7, 1, |p| {
                            if bypass {
                                p.flags |= source_flags::BYPASS_LISTENER_EFFECTS;
                            }
                            if routed {
                                p.group = 3;
                            }
                        });
                        play(&mut m, 7, command::PLAY, 0.0);
                        m.update_suspension();
                        let mut output = vec![0.0; frames * channels];
                        for (index, block) in output.chunks_mut(period * channels).enumerate() {
                            // First activation is warm before measuring the steady audio path.
                            if index > 0 {
                                crate::audio::tests::allocations::start();
                            }
                            if capture {
                                m.render_capture_block(block);
                            } else {
                                m.render(block, channels);
                            }
                            if index > 0 {
                                let allocations = crate::audio::tests::allocations::finish();
                                assert_eq!(allocations, 0, "post-listener render allocated");
                            }
                        }
                        let per_block = if capture { 1 } else { period.div_ceil(BLOCK) };
                        assert_eq!(CALLS.with(Cell::get), frames / period * per_block);
                        output
                    };
                    let zero = render(0.0, true, false, false, true);
                    let one = render(1.0, true, false, false, true);
                    let two = render(2.0, true, false, false, true);
                    assert!(
                        zero.iter().any(|v| v.abs() > 1e-6),
                        "zone return was silenced by the script"
                    );
                    for ((zero, one), two) in zero.iter().zip(&one).zip(&two) {
                        assert!((zero + two - 2.0 * one).abs() < 1e-6);
                    }
                    assert!(
                        render(0.0, false, false, false, true)
                            .iter()
                            .all(|v| *v == 0.0)
                    );
                    assert!(
                        render(0.0, false, true, false, true)
                            .iter()
                            .any(|v| v.abs() > 0.01)
                    );
                    assert!(
                        render(0.0, false, true, true, true)
                            .iter()
                            .all(|v| *v == 0.0)
                    );
                    // Other compatibility contexts retain their previous component-only route.
                    assert!(
                        render(0.0, false, true, false, false)
                            .iter()
                            .all(|v| *v == 0.0)
                    );
                }
            }
        }
    }

    #[test]
    fn reverb_zones_take_each_sources_send_and_restart_empty_with_a_new_id() {
        let zone = |id: u64| AudioFilterDescriptor {
            reverb: [
                -10000.0, -1000.0, -99.0, 1.49, 0.83, -2601.0, 0.007, 200.0, 0.011, 100.0, 100.0,
                5000.0, 0.0, 250.0,
            ],
            ..component(id, filter_kind::REVERB, 0)
        };
        let listen = |send: f32| {
            let mut m = Mixer::new(MixerConfig::default());
            m.add_clip(1, impulse_clip(300));
            m.update_zone(&zone(1), true);
            source_with(&mut m, 7, 1, |p| p.reverb_zone_mix = send);
            play(&mut m, 7, command::PLAY, 0.0);
            m.update_suspension();
            let mut out = vec![0.0; 2 * 4096];
            m.render(&mut out, 2);
            assert!(out[2 * 200] != 0.0);
            (m, out)
        };
        // The tail outlives the source; no send (bypassReverbZones) leaves the dry signal alone.
        let (mut m, out) = listen(1.0);
        assert!(out[2 * 2048..].iter().any(|v| v.abs() > 1e-6));
        assert!(listen(0.0).1[2 * 201..].iter().all(|v| *v == 0.0));
        // The same id keeps the tail, a new one starts empty, kind 0 removes the reverb.
        m.update_zone(&zone(1), true);
        let mut tail = vec![0.0; 2 * 1024];
        m.render(&mut tail, 2);
        assert!(tail.iter().any(|v| v.abs() > 1e-6));
        m.update_zone(&zone(2), true);
        m.render(&mut tail, 2);
        assert!(tail.iter().all(|v| *v == 0.0));
        m.update_zone(&AudioFilterDescriptor::default(), true);
        assert!(m.zone.is_none());
    }

    #[test]
    fn components_sit_above_or_below_the_on_audio_filter_read_scripts() {
        use std::cell::RefCell;
        #[derive(Default)]
        struct Trace {
            input: Vec<f32>,
            emitted: Vec<f32>,
        }
        thread_local! { static TRACE: RefCell<Trace> = RefCell::new(Trace::default()); }
        extern "C" fn script(
            kind: i32,
            _: u64,
            data: *mut f32,
            frames: i32,
            channels: i32,
            _: i64,
        ) {
            if kind != callback::SOURCE_FILTER {
                return;
            }
            let values =
                unsafe { std::slice::from_raw_parts_mut(data, (frames * channels) as usize) };
            TRACE.with(|trace| {
                let mut trace = trace.borrow_mut();
                trace.input.extend_from_slice(values);
                for value in values.iter_mut() {
                    *value *= 0.5;
                }
                trace.emitted.extend_from_slice(values);
            });
        }
        let run = |stage: Option<i32>| -> (Vec<f32>, Trace) {
            TRACE.with(|trace| *trace.borrow_mut() = Trace::default());
            let mut m = Mixer::new(MixerConfig::default());
            m.set_callback(Some(script));
            m.add_clip(1, stereo_tone(8192));
            let filters: Vec<_> = stage.map_or(Vec::new(), |stage| {
                vec![AudioFilterDescriptor {
                    cutoff: 300.0,
                    ..component(12, filter_kind::HIGHPASS, stage)
                }]
            });
            source_with(&mut m, 7, 1, |p| {
                p.flags = source_flags::FILTER;
                p.filters = filters;
            });
            play(&mut m, 7, command::PLAY, 0.0);
            let mut out = vec![0.0; 2048 * 2];
            m.render(&mut out, 2);
            (
                out,
                TRACE.with(|trace| std::mem::take(&mut *trace.borrow_mut())),
            )
        };
        let highpass = [AudioFilterDescriptor {
            cutoff: 300.0,
            ..component(12, filter_kind::HIGHPASS, filter_stage::BEFORE_SCRIPTS)
        }];
        let (dry, plain) = run(None);
        assert_eq!(dry, plain.emitted[..dry.len()]);
        // Above the scripts: they receive the filtered voice.
        let (out, above) = run(Some(filter_stage::BEFORE_SCRIPTS));
        assert_eq!(above.input, through(&plain.input, &highpass, 48000));
        assert_eq!(out, above.emitted[..out.len()]);
        // Below them: they receive the voice, the component filters what they emit.
        let (out, below) = run(Some(filter_stage::AFTER_SCRIPTS));
        assert_eq!(below.input, plain.input);
        assert_eq!(out, through(&below.emitted, &highpass, 48000)[..out.len()]);
    }

    #[test]
    fn a_return_connected_to_an_existing_voice_fades_in_without_restarting_its_clock() {
        let mut state = ZoneSendState::default();
        assert_eq!(state.connection_gain(0.5, 1, false), 0.5);
        // A new return reconnects this running voice; its gain starts at silence.
        assert_eq!(state.connection_gain(0.5, 2, true), 0.0);
        let gains: Vec<_> = (0..64)
            .map(|_| state.connection_gain(0.5, 2, true))
            .collect();
        assert!(gains[0] > 0.0 && gains[31] > 0.2 && gains[31] < 0.3);
        assert_eq!(gains[63], 0.5);
        // A source's own live bypass changes the same connection instead of replacing it.
        assert_eq!(state.connection_gain(0.0, 2, true), 0.5);
    }

    #[test]
    fn a_new_return_fades_in_for_a_playing_voice_whose_gain_changes_in_the_same_block() {
        let zone = |id| {
            let mut zone = component(id, filter_kind::REVERB, 0);
            // Dry level 0 mB and no room: the return outputs exactly its send.
            zone.reverb[0] = 0.0;
            zone.reverb[1] = -10000.0;
            zone
        };
        let mut m = Mixer::new(MixerConfig::default());
        m.add_clip(1, stereo_tone(48000));
        m.update_zone(&zone(1), false);
        let source = |m: &mut Mixer, volume| {
            source_with(m, 7, 1, |p| {
                p.flags = source_flags::ZONE_VOICE_SEND | source_flags::SPATIAL_STEREO;
                p.reverb_zone_mix = 1.0;
                p.volume = volume;
            })
        };
        source(&mut m, 1.0);
        play(&mut m, 7, command::PLAY, 0.0);
        let mut out = vec![0.0; 2 * 1024];
        m.render(&mut out, 2);
        assert!(m.zone_input.iter().any(|sample| *sample != 0.0));
        // The voice's own gain changes while a new return connects it.
        source(&mut m, 0.5);
        m.update_zone(&zone(2), false);
        let mut block = vec![0.0; 2 * BLOCK];
        m.render(&mut block, 2);
        assert_eq!(m.zone_input[..2], [0.0, 0.0]);
        assert!(m.zone_input[2 * 64..].iter().any(|sample| *sample != 0.0));
    }

    #[test]
    fn bypassed_generator_gain_is_independent_of_device_period_and_skips_components() {
        extern "C" fn generate(
            kind: i32,
            _: u64,
            data: *mut f32,
            frames: i32,
            channels: i32,
            _: i64,
        ) {
            if kind == callback::SOURCE_FILTER {
                unsafe { std::slice::from_raw_parts_mut(data, (frames * channels) as usize) }
                    .fill(0.125);
            }
        }
        let render = |period: usize| {
            let mut mixer = Mixer::new(MixerConfig::default());
            mixer.set_callback(Some(generate));
            source_with(&mut mixer, 7, 0, |p| {
                p.flags = source_flags::FILTER
                    | source_flags::SPATIAL_STEREO
                    | source_flags::BYPASS_EFFECTS;
                p.volume = 0.25;
                p.filters = vec![component(
                    1,
                    filter_kind::DISTORTION,
                    filter_stage::AFTER_SCRIPTS,
                )];
            });
            play(&mut mixer, 7, command::PLAY, 0.0);
            let mut out = vec![0.0; 2048 * 2];
            for chunk in out.chunks_mut(period * 2) {
                mixer.render(chunk, 2);
            }
            out
        };
        let expected = render(1024);
        assert_eq!(expected[0], 0.0);
        assert_eq!(expected[2], 0.125 * 0.25 / 64.0);
        assert!(expected[64 * 2..].iter().all(|sample| *sample == 0.03125));
        for period in [74, 255, 257, 1300] {
            assert_eq!(render(period), expected);
        }
    }

    #[test]
    fn a_bypassed_scripted_clip_keeps_dry_audio_and_a_separate_generated_return() {
        use std::cell::Cell;
        thread_local! { static SILENT: Cell<bool> = const { Cell::new(true) }; }
        extern "C" fn script(
            kind: i32,
            _: u64,
            data: *mut f32,
            frames: i32,
            channels: i32,
            _: i64,
        ) {
            if kind != callback::SOURCE_FILTER {
                return;
            }
            let samples =
                unsafe { std::slice::from_raw_parts_mut(data, (frames * channels) as usize) };
            SILENT.with(|silent| silent.set(silent.get() && samples.iter().all(|x| *x == 0.0)));
            for sample in samples {
                *sample = 0.03125;
            }
        }
        let filters = [AudioFilterDescriptor {
            cutoff: 0.4,
            ..component(1, filter_kind::DISTORTION, filter_stage::AFTER_SCRIPTS)
        }];
        let dry = render_source(&[], 0, stereo_tone(8192), 2048);
        let mut mixer = Mixer::new(MixerConfig::default());
        mixer.add_clip(1, stereo_tone(8192));
        mixer.set_callback(Some(script));
        source_with(&mut mixer, 7, 1, |p| {
            p.flags =
                source_flags::FILTER | source_flags::BYPASS_EFFECTS | source_flags::SPATIAL_STEREO;
            p.filters = filters.to_vec();
        });
        play(&mut mixer, 7, command::PLAY, 0.0);
        let mut output = vec![0.0; dry.len()];
        mixer.render(&mut output, 2);
        let generated = through(&vec![0.03125; output.len()], &filters, 48000);
        assert!(SILENT.with(Cell::get));
        for ((actual, dry), generated) in output.iter().zip(dry).zip(generated) {
            assert_eq!(*actual, dry + generated);
        }
        // A larger device buffer after reconfiguration keeps room for the bypassed dry route.
        mixer
            .reconfigure(MixerConfig {
                buffer_size: 4096,
                ..MixerConfig::default()
            })
            .unwrap();
        play(&mut mixer, 7, command::PLAY, 0.0);
        let mut larger = vec![0.0; 2 * 8192];
        mixer.render(&mut larger, 2);
        assert!(larger.iter().any(|sample| *sample != 0.0));
    }

    #[test]
    fn inactive_components_silence_units_above_them_and_a_last_active_one_runs_on_silence() {
        use std::cell::Cell;
        thread_local! { static CALLS: Cell<u32> = const { Cell::new(0) }; }
        extern "C" fn script(
            kind: i32,
            _: u64,
            data: *mut f32,
            frames: i32,
            channels: i32,
            _: i64,
        ) {
            if kind != callback::SOURCE_FILTER {
                return;
            }
            CALLS.with(|calls| calls.set(calls.get() + 1));
            unsafe { std::slice::from_raw_parts_mut(data, (frames * channels) as usize) }
                .fill(0.03125);
        }
        let filters = [AudioFilterDescriptor {
            cutoff: 0.4,
            ..component(1, filter_kind::DISTORTION, filter_stage::AFTER_SCRIPTS)
        }];
        let dry = render_source(&[], 0, stereo_tone(8192), 2048);
        let render = |extra: u32| {
            CALLS.with(|calls| calls.set(0));
            let mut mixer = Mixer::new(MixerConfig::default());
            mixer.add_clip(1, stereo_tone(8192));
            mixer.set_callback(Some(script));
            source_with(&mut mixer, 7, 1, |p| {
                p.flags = source_flags::FILTER
                    | source_flags::BYPASS_EFFECTS
                    | source_flags::SPATIAL_STEREO
                    | source_flags::COMPONENTS_INACTIVE
                    | extra;
                p.filters = filters.to_vec();
            });
            play(&mut mixer, 7, command::PLAY, 0.0);
            let mut output = vec![0.0; dry.len()];
            mixer.render(&mut output, 2);
            (output, CALLS.with(Cell::get))
        };
        // The inactive component below the script silences it: only the dry voice remains.
        let (output, calls) = render(0);
        assert_eq!(calls, 0);
        assert_eq!(output, dry);
        // A last component left active reads the script, which runs on silence.
        let (output, calls) = render(source_flags::LAST_COMPONENT_ACTIVE);
        assert!(calls > 0);
        let generated = through(&vec![0.03125; output.len()], &filters, 48000);
        for ((actual, dry), generated) in output.iter().zip(&dry).zip(generated) {
            assert_eq!(*actual, dry + generated);
        }
    }

    #[test]
    fn a_bypassed_source_without_components_renders_its_dry_voice() {
        let dry = render_source(&[], 0, stereo_tone(8192), 2048);
        for extra in [
            0,
            source_flags::COMPONENTS_INACTIVE,
            source_flags::COMPONENTS_INACTIVE | source_flags::LAST_COMPONENT_ACTIVE,
        ] {
            let mut mixer = Mixer::new(MixerConfig::default());
            mixer.add_clip(1, stereo_tone(8192));
            source_with(&mut mixer, 7, 1, |p| {
                p.flags = source_flags::BYPASS_EFFECTS | source_flags::SPATIAL_STEREO | extra;
            });
            play(&mut mixer, 7, command::PLAY, 0.0);
            let mut output = vec![0.0; dry.len()];
            mixer.render(&mut output, 2);
            assert_eq!(output, dry, "{extra}");
        }
    }

    #[test]
    fn stop_and_play_in_one_update_keep_component_histories() {
        let echo = [AudioFilterDescriptor {
            delay: 30.0,
            ..component(1, filter_kind::ECHO, filter_stage::BEFORE_SCRIPTS)
        }];
        let render = |restart: bool| {
            let mut mixer = Mixer::new(MixerConfig::default());
            mixer.add_clip(1, impulse_clip(4096));
            source_with(&mut mixer, 7, 1, |p| p.filters = echo.to_vec());
            play(&mut mixer, 7, command::PLAY, 0.0);
            let mut first = vec![0.0; 2 * 1024];
            mixer.render(&mut first, 2);
            if restart {
                play(&mut mixer, 7, command::STOP, 0.0);
                play(&mut mixer, 7, command::PLAY, 0.0);
            }
            let mut second = vec![0.0; 2 * 1024];
            mixer.render(&mut second, 2);
            (first, second)
        };
        let (first, _) = render(false);
        let (_, restarted) = render(true);
        // The restarted voice is the same impulse, now joined by the echo of the first one.
        assert!(
            restarted
                .iter()
                .zip(&first)
                .any(|(restarted, fresh)| (restarted - fresh).abs() > 1e-6)
        );
        assert_eq!(restarted[..2], first[..2]);
    }

    #[test]
    fn malformed_filter_descriptors_are_refused_and_keep_the_previous_chain() {
        let valid = components();
        let refused = |p: &AudioSourcePacket| -> String {
            let params = unsafe { SourceParams::from_packet(p) };
            assert!(params.filters.is_empty());
            params.filters_rejected.expect("refused")
        };
        let mut negative = packet(1, 0, &valid);
        negative.filter_count = -1;
        assert!(refused(&negative).contains("outside"));
        let many: Vec<_> = (0..65)
            .map(|id| component(id, filter_kind::LOWPASS, 0))
            .collect();
        assert!(refused(&packet(1, 0, &many)).contains("outside"));
        let mut null = packet(1, 0, &valid);
        null.filters = std::ptr::null();
        assert!(refused(&null).contains("null"));
        let storage = vec![0u64; 16];
        let mut misaligned = packet(1, 0, &valid[..1]);
        misaligned.filters =
            unsafe { (storage.as_ptr() as *const u8).add(4) } as *const AudioFilterDescriptor;
        assert!(refused(&misaligned).contains("misaligned"));
        for kind in [0, filter_kind::CHORUS + 1, -1] {
            assert!(refused(&packet(1, 0, &[component(1, kind, 0)])).contains("kind"));
        }
        let distortion = [component(1, filter_kind::DISTORTION, 0)];
        assert_eq!(
            unsafe { SourceParams::from_packet(&packet(1, 0, &distortion)) }
                .filters
                .len(),
            1
        );
        assert!(refused(&packet(1, 0, &[component(1, filter_kind::ECHO, 4)])).contains("stage"));
        let mut reverb = component(1, filter_kind::REVERB, 0);
        reverb.reverb[3] = f32::NAN;
        assert!(refused(&packet(1, 0, &[reverb])).contains("finite"));
        for bad in [f32::NAN, f32::INFINITY] {
            let lowpass = AudioFilterDescriptor {
                cutoff: bad,
                ..component(1, filter_kind::LOWPASS, 0)
            };
            assert!(refused(&packet(1, 0, &[lowpass])).contains("finite"));
            let echo = AudioFilterDescriptor {
                delay: bad,
                ..component(1, filter_kind::ECHO, 0)
            };
            assert!(refused(&packet(1, 0, &[echo])).contains("finite"));
        }
        let twice = [
            component(5, filter_kind::LOWPASS, 0),
            component(5, filter_kind::ECHO, 0),
        ];
        assert!(refused(&packet(1, 0, &twice)).contains("duplicate"));
        // A clipless generator plays its first script; nothing may sit above it.
        // Packet pointers borrow these arrays through both decoding calls.
        let generator_filters = [component(1, filter_kind::LOWPASS, 0)];
        let generator = packet(0, source_flags::FILTER, &generator_filters);
        assert!(refused(&generator).contains("generator"));
        let below_filters = [component(
            1,
            filter_kind::LOWPASS,
            filter_stage::AFTER_SCRIPTS,
        )];
        let below = packet(0, source_flags::FILTER, &below_filters);
        assert!(
            unsafe { SourceParams::from_packet(&below) }
                .filters_rejected
                .is_none()
        );
        // The resource bound itself is accepted.
        let most: Vec<_> = (0..64)
            .map(|id| component(id, filter_kind::LOWPASS, 0))
            .collect();
        assert_eq!(
            unsafe { SourceParams::from_packet(&packet(1, 0, &most)) }
                .filters
                .len(),
            64
        );

        // A refused packet still updates the source, and its chain stays as it was.
        let mut m = Mixer::new(MixerConfig::default());
        m.update_source(7, unsafe {
            SourceParams::from_packet(&packet(1, 0, &valid))
        });
        let mut bad = packet(1, 0, &valid);
        bad.filter_count = -1;
        bad.volume = 0.25;
        m.update_source(7, unsafe { SourceParams::from_packet(&bad) });
        let source = m.source(7).unwrap();
        assert_eq!(source.params.volume, 0.25);
        assert_eq!(source.params.filters, valid);
        assert_eq!(source.params.filters_rejected, None);
        assert_eq!(source.chain.filters.len(), 3);
    }

    #[test]
    fn echo_components_beyond_the_history_bound_fail_without_replacing_the_chain() {
        let mut m = Mixer::new(MixerConfig {
            sample_rate: i32::MAX as u32,
            ..Default::default()
        });
        let echo = [AudioFilterDescriptor {
            delay: 5000.0,
            ..component(1, filter_kind::ECHO, 0)
        }];
        source_with(&mut m, 7, 1, |p| p.filters = echo.to_vec());
        assert!(m.source(7).unwrap().params.filters.is_empty());
        assert!(m.source(7).unwrap().chain.filters.is_empty());

        let mut m = Mixer::new(MixerConfig::default());
        source_with(&mut m, 7, 1, |p| p.filters = echo.to_vec());
        let before = m.config;
        let error = m
            .reconfigure(MixerConfig {
                sample_rate: i32::MAX as u32,
                ..before
            })
            .unwrap_err();
        assert!(error.to_string().contains("8 MiB"));
        assert_eq!(m.config, before);
        assert_eq!(m.source(7).unwrap().params.filters, echo);
    }

    #[test]
    fn a_new_output_rate_restarts_the_components_at_that_rate() {
        let clip = || Clip::new(2, 44100, stereo_tone(8192).samples);
        let fresh = {
            let mut m = Mixer::new(MixerConfig {
                sample_rate: 44100,
                ..Default::default()
            });
            m.add_clip(1, clip());
            source_with(&mut m, 7, 1, |p| p.filters = components());
            play(&mut m, 7, command::PLAY, 0.0);
            let mut out = vec![0.0; 2048 * 2];
            m.render(&mut out, 2);
            out
        };
        let mut m = Mixer::new(MixerConfig::default());
        m.add_clip(1, clip());
        source_with(&mut m, 7, 1, |p| p.filters = components());
        play(&mut m, 7, command::PLAY, 0.0);
        let mut out = vec![0.0; 2048 * 2];
        m.render(&mut out, 2);
        m.reconfigure(MixerConfig {
            sample_rate: 44100,
            ..m.config
        })
        .unwrap();
        play(&mut m, 7, command::PLAY, 0.0);
        m.render(&mut out, 2);
        assert_eq!(out, fresh);
    }
}
