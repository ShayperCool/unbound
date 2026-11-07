//! Persistent DSP on summed mixer groups. Native captures establish a fourth-order Butterworth
//! response for Lowpass and a second-order response for Highpass. The independently measured
//! startup kernels also constrain parameter-dependent delay-history normalization.
use super::chorus::{Chorus, ChorusSpec};
use super::compressor::{Compressor, CompressorSpec};
use super::duck::{Duck, DuckSpec};
use super::flange::{Flange, FlangeSpec};
use super::mixer::MixerConfig;
use super::modulated_delay::MAX_HISTORY_BYTES;
use super::normalize::{Normalize, NormalizeSpec};
use super::pitch::{PitchShifter, PitchSpec};
use super::reverb::{SfxReverb, SfxReverbSpec};
use serde::Deserialize;
use std::collections::HashMap;

/// The group effect kinds the mixer runs, by the AudioMixer effect name the managed side sends.
/// Every per-kind rule of the mixer is a method here.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) enum EffectKind {
    Attenuation,
    Lowpass,
    Highpass,
    Echo,
    SfxReverb,
    Send,
    Receive,
    DuckVolume,
    Compressor,
    ParamEq,
    LowpassSimple,
    HighpassSimple,
    Distortion,
    Chorus,
    Flange,
    Normalize,
    PitchShifter,
}
impl EffectKind {
    const NAMES: [(Self, &'static str); 17] = [
        (Self::Attenuation, "Attenuation"),
        (Self::Lowpass, "Lowpass"),
        (Self::Highpass, "Highpass"),
        (Self::Echo, "Echo"),
        (Self::SfxReverb, "SFX Reverb"),
        (Self::Send, "Send"),
        (Self::Receive, "Receive"),
        (Self::DuckVolume, "Duck Volume"),
        (Self::Compressor, "Compressor"),
        (Self::ParamEq, "ParamEQ"),
        (Self::LowpassSimple, "Lowpass Simple"),
        (Self::HighpassSimple, "Highpass Simple"),
        (Self::Distortion, "Distortion"),
        (Self::Chorus, "Chorus"),
        (Self::Flange, "Flange"),
        (Self::Normalize, "Normalize"),
        (Self::PitchShifter, "Pitch Shifter"),
    ];
    /// None for a kind the mixer does not run.
    pub(crate) fn parse(name: &str) -> Option<Self> {
        Self::NAMES
            .iter()
            .find(|(_, n)| *n == name)
            .map(|(kind, _)| *kind)
    }
    pub(crate) fn name(self) -> &'static str {
        Self::NAMES
            .iter()
            .find(|(kind, _)| *kind == self)
            .map_or("", |(_, name)| name)
    }
    /// Kernels with their own state that a bypass skips, leaving the state as it is.
    fn bypass_keeps_state(self) -> bool {
        use EffectKind::*;
        matches!(
            self,
            Compressor
                | ParamEq
                | Distortion
                | Chorus
                | Flange
                | Normalize
                | PitchShifter
                | LowpassSimple
                | HighpassSimple
        )
    }
    /// Kinds whose group input keeps the measured startup fade also while bypassed
    /// (tests/projects/mixerduck, tests/projects/mixercompressor). Lowpass and Highpass fade
    /// only while active; Attenuation, Send and Receive never.
    pub(crate) fn fades_in_when_bypassed(self) -> bool {
        use EffectKind::*;
        matches!(self, Echo | SfxReverb | DuckVolume) || self.bypass_keeps_state()
    }
    /// The fade of Lowpass and Highpass, which apply it only while active.
    pub(crate) fn fades_in_when_active(self) -> bool {
        matches!(self, Self::Lowpass | Self::Highpass)
    }
    /// The one-pole filters, whose cutoff is measured only from 10 to 22000 Hz.
    fn simple_filter(self) -> bool {
        matches!(self, Self::LowpassSimple | Self::HighpassSimple)
    }
    /// Kinds measured only on [`MEASURED_LAYOUT`] output, whose settings, placement and bypass
    /// are also unmeasured to change once the group renders.
    pub(crate) fn layout_limited(self) -> bool {
        Self::LAYOUT_LIMITED.contains(&self)
    }
    pub(crate) const LAYOUT_LIMITED: [Self; 2] = [Self::PitchShifter, Self::Normalize];
}

/// The output the Normalize and Pitch Shifter captures cover (Unity 6000.0.71f1 Linux Editor,
/// tests/projects/audionormalize and tests/projects/audiopitch): these rates, DSP buffers of
/// this many frames and the stereo speaker mode. Other layouts fail rather than run unmeasured.
pub(crate) const MEASURED_LAYOUT: MeasuredLayout = MeasuredLayout {
    rates: [48000, 44100],
    buffer: 1024,
    speaker_mode: 2,
};
pub(crate) struct MeasuredLayout {
    rates: [u32; 2],
    buffer: u32,
    /// `AudioSpeakerMode.Stereo`.
    speaker_mode: i32,
}
impl MeasuredLayout {
    pub(crate) fn ensure(&self, effect: &str, config: &MixerConfig) -> anyhow::Result<()> {
        anyhow::ensure!(
            self.rates.contains(&config.sample_rate)
                && config.buffer_size == self.buffer
                && config.speaker_mode == self.speaker_mode,
            "AudioMixer {effect} is measured only at 48000/44100 Hz with 1024-frame stereo output"
        );
        Ok(())
    }
}

#[derive(Debug, Clone, PartialEq, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct EffectSpec {
    pub id: String,
    pub kind: String,
    #[serde(default)]
    pub bypass: bool,
    #[serde(default)]
    pub gain: f32,
    #[serde(default)]
    pub cutoff: f32,
    #[serde(default = "one")]
    pub resonance: f32,
    #[serde(default = "echo_delay")]
    pub delay: f32,
    #[serde(default)]
    pub decay: f32,
    /// Measured channel limits 0, 1, 2 and 16 all retain both stereo channels.
    #[serde(default)]
    pub max_channels: f32,
    #[serde(default = "one")]
    pub dry_mix: f32,
    #[serde(default = "one")]
    pub wet_mix: f32,
    /// SFX Reverb parameters; required for that kind.
    #[serde(default)]
    pub sfx: Option<SfxReverbSpec>,
    /// The effect slot's linear wet level when wet mixing is enabled (measured for SFX Reverb).
    #[serde(default)]
    pub mix_level: Option<f32>,
    /// False when the wet level is low enough that Native drops the effect's output.
    #[serde(default = "yes")]
    pub mix_audible: bool,
    /// Duck Volume parameters; required for that kind.
    #[serde(default)]
    pub duck: Option<DuckSpec>,
    /// A Send's target: the id of a Receive or Duck Volume unit (none: the send goes nowhere).
    #[serde(default)]
    pub send_target: Option<String>,
    /// A Send's linear level (the mixer's gain approximation of its dB setting).
    #[serde(default)]
    pub send_level: f32,
    /// Compressor parameters; required for that kind unless bypassed.
    #[serde(default)]
    pub compressor: Option<CompressorSpec>,
    /// ParamEQ parameters; required for that kind unless bypassed.
    #[serde(default)]
    pub eq: Option<ParamEqSpec>,
    /// Distortion Level, clamped for DSP while the managed API keeps the exposed value.
    #[serde(default)]
    pub distortion: Option<f32>,
    #[serde(default)]
    pub chorus: Option<ChorusSpec>,
    #[serde(default)]
    pub flange: Option<FlangeSpec>,
    #[serde(default)]
    pub normalize: Option<NormalizeSpec>,
    #[serde(default)]
    pub pitch_shifter: Option<PitchSpec>,
}

/// ParamEQ parameters as the mixer stores them: Hz, octaves and a linear gain.
#[derive(Debug, Clone, PartialEq, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ParamEqSpec {
    pub center: f32,
    pub octaves: f32,
    pub gain: f32,
}
impl EffectSpec {
    /// Parameter and resource checks that hold wherever the effect runs (group or source).
    pub(crate) fn validate(&self, rate: u32, buffer: u32) -> anyhow::Result<()> {
        if self.kind == "Echo" {
            Echo::frames(self.delay, rate)?;
        }
        if self.chorus.is_some() {
            Chorus::frames(rate, buffer)?;
        }
        if self.flange.is_some() {
            Flange::frames(rate, buffer)?;
        }
        anyhow::ensure!(
            self.kind != "SFX Reverb" || self.sfx.is_some(),
            "AudioMixer SFX Reverb '{}' has no parameters",
            self.id
        );
        if let Some(sfx) = &self.sfx {
            let values = [
                sfx.dry_level,
                sfx.room,
                sfx.room_hf,
                sfx.decay_time,
                sfx.decay_hf_ratio,
                sfx.reflections,
                sfx.reflect_delay,
                sfx.reverb,
                sfx.reverb_delay,
                sfx.diffusion,
                sfx.density,
                sfx.hf_reference,
                sfx.room_lf,
                sfx.lf_reference,
            ];
            anyhow::ensure!(
                values.iter().all(|v| v.is_finite()),
                "AudioMixer SFX Reverb '{}' has a non-finite parameter",
                self.id
            );
        }
        anyhow::ensure!(
            self.kind != "Duck Volume" || self.duck.is_some(),
            "AudioMixer Duck Volume '{}' has no parameters",
            self.id
        );
        if let Some(duck) = &self.duck {
            duck.validate(&self.id)?;
        }
        if let Some(compressor) = &self.compressor {
            compressor.validate(&self.id)?;
        }
        if let Some(chorus) = &self.chorus {
            chorus.validate()?;
        }
        if let Some(flange) = &self.flange {
            flange.validate()?;
        }
        if let Some(pitch) = &self.pitch_shifter {
            pitch.validate()?;
        }
        if let Some(normalize) = &self.normalize {
            normalize.validate()?;
        }
        if let Some(level) = self.distortion {
            anyhow::ensure!(
                level.is_finite() && (0.0..=1.0).contains(&level),
                "AudioMixer Distortion has an invalid Level {level}"
            );
        }
        if let Some(eq) = &self.eq {
            // The Editor's ranges; outside them the response is unmeasured.
            anyhow::ensure!(
                eq.center.is_finite()
                    && eq.center > 0.0
                    && eq.octaves > 0.0
                    && eq.octaves.is_finite()
                    && eq.gain > 0.0
                    && eq.gain.is_finite(),
                "AudioMixer ParamEQ '{}' has unsupported parameters",
                self.id
            );
        }
        anyhow::ensure!(
            self.send_level.is_finite() && self.send_level >= 0.0,
            "invalid AudioMixer send level {}",
            self.send_level
        );
        if let Some(level) = self.mix_level {
            anyhow::ensure!(
                self.kind == "SFX Reverb",
                "AudioMixer wet mixing is unsupported for effect '{}'",
                self.kind
            );
            anyhow::ensure!(
                level.is_finite() && (0.0..=1.0).contains(&level),
                "invalid AudioMixer wet level {level}"
            );
        }
        Ok(())
    }

    /// Everything a group effect needs on this output, checked before the mixer changes any
    /// state: the checks of [`EffectSpec::validate`], and for an active effect (neither it nor
    /// its group bypassed) a supported kind, its parameters and its measured context.
    pub(crate) fn validate_in_group(
        &self,
        config: &MixerConfig,
        group_bypass: bool,
    ) -> anyhow::Result<()> {
        self.validate(config.sample_rate, config.buffer_size)?;
        if self.bypass || group_bypass {
            return Ok(());
        }
        let Some(kind) = EffectKind::parse(&self.kind) else {
            anyhow::bail!("unsupported AudioMixer effect '{}'", self.kind);
        };
        let parameters = match kind {
            EffectKind::Compressor => self.compressor.is_some(),
            EffectKind::ParamEq => self.eq.is_some(),
            EffectKind::Distortion => self.distortion.is_some(),
            EffectKind::Chorus => self.chorus.is_some(),
            EffectKind::Flange => self.flange.is_some(),
            EffectKind::Normalize => self.normalize.is_some(),
            EffectKind::PitchShifter => self.pitch_shifter.is_some(),
            _ => true,
        };
        anyhow::ensure!(
            parameters,
            "AudioMixer {} '{}' has no parameters",
            self.kind,
            self.id
        );
        anyhow::ensure!(
            !kind.simple_filter() || (10.0..=22000.0).contains(&self.cutoff),
            "AudioMixer {} '{}' cutoff {} is outside the measured 10 to 22000 Hz",
            self.kind,
            self.id,
            self.cutoff
        );
        if kind.layout_limited() {
            MEASURED_LAYOUT.ensure(&self.kind, config)?;
        }
        Ok(())
    }
}
fn yes() -> bool {
    true
}
fn echo_delay() -> f32 {
    500.0
}
fn one() -> f32 {
    1.0
}

#[derive(Debug, Clone, PartialEq, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct GroupSpec {
    pub handle: u64,
    pub parent: u64,
    pub muted: bool,
    pub bypass: bool,
    pub effects: Vec<EffectSpec>,
    #[serde(default)]
    pub observe: bool,
    #[serde(default)]
    pub mixer: u64,
    #[serde(default)]
    pub enable_suspend: bool,
    /// Linear RMS boundary, converted from the asset's dB setting by the managed mixer.
    #[serde(default)]
    pub suspend_threshold: f32,
}

#[derive(Default)]
struct Biquad {
    b: [f32; 3],
    a: [f32; 2],
    z: [[f32; 2]; 2],
    history: [[f32; 4]; 2],
}

#[derive(Default)]
struct Lowpass {
    a: [[f32; 2]; 2],
    gain: [f32; 2],
    delay: [[f32; 4]; 2],
}
impl Lowpass {
    fn tune(&mut self, cutoff: f32, resonance: f32, rate: u32) {
        let rate = rate as f32;
        let cutoff = cutoff.min(rate * 0.5 - 1.0);
        let warped = (cutoff * std::f32::consts::PI / rate).tan() * (rate + rate);
        let squared = warped * warped;
        // Rounded analog Butterworth prototype damping, confirmed by the complete Native kernels.
        for (stage, damping) in [0.765367_f32, 1.847759_f32].into_iter().enumerate() {
            let inverse_square = 1.0 / squared;
            let linear = (damping / resonance) / warped;
            let quadratic = ((inverse_square * 4.0) * rate) * rate;
            let linear = (linear + linear) * rate;
            let denominator = (linear + quadratic) + 1.0;
            let gain = 1.0 / denominator;
            self.a[stage] = [
                (2.0 - ((inverse_square * 8.0) * rate) * rate) / denominator,
                ((quadratic - linear) + 1.0) / denominator,
            ];
            if self.gain[stage] != 0.0 {
                let ratio = gain / self.gain[stage];
                // Each stage's normalization rescales one of the first section's two histories.
                // Rescaling both histories identically loses the measured first-buffer response.
                for channel in &mut self.delay {
                    channel[stage] *= ratio;
                }
            }
            self.gain[stage] = gain;
        }
    }
    fn sample(&mut self, input: f32, channel: usize) -> f32 {
        let d = &mut self.delay[channel];
        let first =
            input * (self.gain[0] * self.gain[1]) - self.a[0][0] * d[0] - self.a[0][1] * d[1];
        let second = ((2.0 * d[0] + d[1]) - self.a[1][0] * d[2] - self.a[1][1] * d[3]) + first;
        let output = second + (2.0 * d[2] + d[3]);
        *d = [first, d[0], second, d[2]];
        output
    }
}
impl Biquad {
    fn tune(&mut self, high: bool, cutoff: f32, q: f32, rate: u32) {
        let w = 2.0 * std::f32::consts::PI * cutoff.min(rate as f32 * 0.5) / rate as f32;
        let c = w.cos();
        let alpha = w.sin() / (2.0 * q);
        let inv = 1.0 / (1.0 + alpha);
        let b0 = if high {
            (1.0 + c) * 0.5
        } else {
            (1.0 - c) * 0.5
        } * inv;
        self.b = [b0, if high { -2.0 * b0 } else { 2.0 * b0 }, b0];
        self.a = [-2.0 * c * inv, (1.0 - alpha) * inv];
    }
    fn sample(&mut self, input: f32, channel: usize, direct: bool) -> f32 {
        let z = &mut self.z[channel];
        if direct {
            let iir = input - self.a[0] * z[0] - self.a[1] * z[1];
            let output = self.b[0] * iir + self.b[1] * z[0] + self.b[2] * z[1];
            z[1] = z[0];
            z[0] = iir;
            output
        } else {
            let h = &mut self.history[channel];
            let output = self.b[0] * input + self.b[1] * h[0] + self.b[2] * h[1]
                - self.a[0] * h[2]
                - self.a[1] * h[3];
            *h = [input, h[0], output, h[2]];
            output
        }
    }
}

/// Lowpass Simple and Highpass Simple (tests/projects/mixercompressor): the low pass is two
/// identical one-pole stages, the high pass the input minus one such stage. The coefficient is
/// `w / (w + rate)` with `w = 2 pi cutoff` up to `rate / pi` (where it is 2/3), then rises
/// linearly to 1 at the 22000 Hz maximum (pass-through, and silence for the high pass).
#[derive(Default)]
struct SimpleFilter {
    z: [[f64; 2]; 2],
}
impl SimpleFilter {
    fn coefficient(cutoff: f32, rate: u32) -> f64 {
        let (cutoff, rate) = (cutoff as f64, rate as f64);
        let knee = rate / std::f64::consts::PI;
        if cutoff <= knee {
            let w = 2.0 * std::f64::consts::PI * cutoff;
            w / (w + rate)
        } else {
            (2.0 + (cutoff - knee) / (22000.0 - knee)).min(3.0) / 3.0
        }
    }
    fn process(&mut self, samples: &mut [f32], high: bool, cutoff: f32, rate: u32) {
        let a = Self::coefficient(cutoff, rate);
        for stereo in samples.chunks_exact_mut(2) {
            for (x, z) in stereo.iter_mut().zip(&mut self.z) {
                let input = *x as f64;
                z[0] += a * (input - z[0]);
                *x = if high {
                    input - z[0]
                } else {
                    z[1] += a * (z[0] - z[1]);
                    z[1]
                } as f32;
            }
        }
    }
}

/// ParamEQ (tests/projects/mixercompressor): a peaking biquad with `A = gain` (so the center
/// gain is `gain^2`), `alpha = sin(w0) * octaves / 2` and the center limited to
/// `rate / 2 - 100` Hz. Native's single-precision arithmetic is not reproduced.
#[derive(Default)]
struct ParamEq {
    b: [f64; 3],
    a: [f64; 2],
    h: [[f64; 4]; 2],
}
impl ParamEq {
    fn tune(&mut self, spec: &ParamEqSpec, rate: u32) {
        let center = (spec.center as f64).min(rate as f64 / 2.0 - 100.0);
        let w0 = 2.0 * std::f64::consts::PI * center / rate as f64;
        let alpha = w0.sin() * spec.octaves as f64 / 2.0;
        let (gain, c) = (spec.gain as f64, w0.cos());
        let a0 = 1.0 + alpha / gain;
        self.b = [
            (1.0 + alpha * gain) / a0,
            -2.0 * c / a0,
            (1.0 - alpha * gain) / a0,
        ];
        self.a = [-2.0 * c / a0, (1.0 - alpha / gain) / a0];
    }
    fn process(&mut self, samples: &mut [f32]) {
        let (b, a) = (self.b, self.a);
        for stereo in samples.chunks_exact_mut(2) {
            for (x, h) in stereo.iter_mut().zip(&mut self.h) {
                let input = *x as f64;
                let y = b[0] * input + b[1] * h[0] + b[2] * h[1] - a[0] * h[2] - a[1] * h[3];
                *h = [input, h[0], y, h[2]];
                *x = y as f32;
            }
        }
    }
}

/// Unity 6000.0.71f1 Echo kernels, independently captured at both output rates.
#[derive(Default)]
struct Echo {
    history: Vec<[i16; 2]>,
    position: usize,
}
impl Echo {
    fn frames(delay: f32, rate: u32) -> anyhow::Result<usize> {
        // MAX_HISTORY_BYTES is a backend resource limit, not a restriction of Unity's range.
        anyhow::ensure!(delay.is_finite(), "AudioMixer Echo delay must be finite");
        // Truncate fractional frames, then round up to the measured eight-frame boundary.
        let frames = (delay.clamp(1.0, 5000.0) * rate as f32 / 1000.0) as usize;
        let frames = frames.checked_add(7).map(|n| (n & !7).max(8));
        let frames = frames.filter(|n| *n <= MAX_HISTORY_BYTES / std::mem::size_of::<[i16; 2]>());
        frames.ok_or_else(|| {
            anyhow::anyhow!(
                "AudioMixer Echo delay buffer exceeds the supported 8 MiB limit at {rate} Hz"
            )
        })
    }
    fn tune(&mut self, delay: f32, rate: u32) -> anyhow::Result<()> {
        let frames = Self::frames(delay, rate)?;
        if self.history.len() != frames {
            let mut history = Vec::new();
            history.try_reserve_exact(frames)?;
            history.resize(frames, [0; 2]);
            self.history = history;
            self.position = 0;
        }
        Ok(())
    }
    fn process(&mut self, samples: &mut [f32], spec: &EffectSpec) {
        let decay = spec.decay.clamp(0.0, 1.0);
        let dry = spec.dry_mix.clamp(0.0, 1.0);
        let wet = spec.wet_mix.clamp(0.0, 1.0);
        for stereo in samples.chunks_exact_mut(2) {
            let history = &mut self.history[self.position];
            for (channel, input) in stereo.iter_mut().enumerate() {
                // The reciprocal multiply is observable in the tone feedback tail.
                let delayed = history[channel] as f32 * (1.0 / 32767.0);
                history[channel] = ((*input + delayed * decay) * 32767.0) as i16;
                *input = *input * dry + delayed * wet;
            }
            self.position = (self.position + 1) % self.history.len();
        }
    }
}

/// Unity 6000.0.71f1 static waveshaper (tests/projects/audiodistortion). The endpoint
/// stops at 0.9999 to keep the rational curve finite; channels and rates are independent.
fn distortion(samples: &mut [f32], level: f32) {
    let level = level.min(0.9999);
    let k = 2.0 * level / (1.0 - level);
    for x in samples {
        *x = ((1.0 + k) * *x) / (1.0 + k * x.abs());
    }
}

pub struct Effect {
    pub spec: EffectSpec,
    /// `spec.kind`, parsed once; None for a kind the mixer does not run (bypassed only).
    kind: Option<EffectKind>,
    stages: [Biquad; 2],
    lowpass: Lowpass,
    echo: Echo,
    reverb: Option<Box<SfxReverb>>,
    duck: Duck,
    compressor: Compressor,
    chorus: Option<Chorus>,
    flange: Option<Flange>,
    normalize: Normalize,
    pitch: Option<PitchShifter>,
    simple: SimpleFilter,
    eq: ParamEq,
    /// Frames since the effect started, for the slot's startup ramp.
    frames: u32,
    scratch: Vec<f32>,
    cutoff: f32,
    resonance: f32,
    rate: u32,
    gain: f32,
}
impl Effect {
    pub fn new(spec: EffectSpec, rate: u32, buffer: u32) -> anyhow::Result<Self> {
        let mut effect = Self {
            spec: spec.clone(),
            kind: None,
            stages: Default::default(),
            lowpass: Default::default(),
            echo: Default::default(),
            reverb: None,
            duck: Duck::default(),
            compressor: Compressor::default(),
            chorus: None,
            flange: None,
            normalize: Normalize::default(),
            pitch: None,
            simple: SimpleFilter::default(),
            eq: ParamEq::default(),
            frames: 0,
            scratch: Vec::new(),
            cutoff: 5000.0,
            resonance: 1.0,
            rate,
            gain: spec.gain,
        };
        effect.update(spec, rate, buffer)?;
        Ok(effect)
    }
    pub fn update(&mut self, spec: EffectSpec, rate: u32, buffer: u32) -> anyhow::Result<()> {
        if spec.chorus.is_some()
            && self
                .chorus
                .as_ref()
                .is_none_or(|c| c.configuration() != (rate, buffer.max(1)))
        {
            self.chorus = Some(Chorus::new(rate, buffer)?);
        }
        if spec.flange.is_some()
            && self
                .flange
                .as_ref()
                .is_none_or(|c| c.configuration() != (rate, buffer.max(1)))
        {
            self.flange = Some(Flange::new(rate, buffer)?);
        }
        if spec.pitch_shifter.is_some() && self.pitch.is_none() {
            self.pitch = Some(PitchShifter::new());
        }
        let kind = EffectKind::parse(&spec.kind);
        if kind == Some(EffectKind::Echo) {
            self.echo.tune(spec.delay, rate)?;
        }
        if let Some(sfx) = &spec.sfx {
            let reverb = match &mut self.reverb {
                Some(reverb) if reverb.rate() == rate => reverb,
                slot => slot.insert(Box::new(SfxReverb::new(rate)?)),
            };
            reverb.tune(sfx);
        }
        self.kind = kind;
        self.rate = rate;
        self.spec = spec;
        self.tune();
        Ok(())
    }
    /// Forgets the histories and the parameter ramp, leaving the effect as [`Effect::new`] makes
    /// it at the current rate. The Echo buffer keeps its allocation (zeroed).
    pub fn reset(&mut self) {
        self.stages = Default::default();
        self.lowpass = Default::default();
        self.echo.history.fill([0; 2]);
        self.echo.position = 0;
        if let Some(reverb) = &mut self.reverb {
            reverb.clear();
        }
        self.duck = Duck::default();
        self.compressor = Compressor::default();
        if let Some(chorus) = &mut self.chorus {
            chorus.clear();
        }
        if let Some(flange) = &mut self.flange {
            flange.clear();
        }
        self.normalize = Normalize::default();
        if let Some(pitch) = &mut self.pitch {
            pitch.clear();
        }
        self.simple = SimpleFilter::default();
        self.eq.h = Default::default();
        self.frames = 0;
        self.cutoff = 5000.0;
        self.resonance = 1.0;
        self.gain = self.spec.gain;
        self.tune();
    }
    fn tune(&mut self) {
        match self.kind {
            Some(EffectKind::Lowpass) => self.lowpass.tune(self.cutoff, self.resonance, self.rate),
            Some(EffectKind::Highpass) => {
                self.stages[0].tune(true, self.cutoff, self.resonance, self.rate)
            }
            Some(EffectKind::ParamEq) => {
                if let Some(eq) = &self.spec.eq {
                    self.eq.tune(eq, self.rate);
                }
            }
            _ => {}
        }
    }
    pub(crate) fn kind(&self) -> Option<EffectKind> {
        self.kind
    }
    // Measured sample-by-sample slew: clamp the step to one percent of the endpoint sum.
    fn slew(current: f32, target: f32) -> f32 {
        let limit = (current.abs() + target.abs()) * 0.01;
        current + (target - current).clamp(-limit, limit)
    }
    /// Units that exchange signal between groups, in chain order: a Send adds a copy at its
    /// level to its target's sidechain, a Receive mixes its sidechain in and a Duck Volume
    /// ducks by it. Native ignores bypass on them (the Editor disallows it), and nothing reaches
    /// a sidechain during the mixer's first DSP block: `muted` leading frames of this chunk still
    /// belong to it. False for any other kind.
    pub fn process_routed(
        &mut self,
        samples: &mut [f32],
        sidechains: &mut HashMap<String, Vec<f32>>,
        muted: usize,
    ) -> bool {
        match self.kind {
            Some(EffectKind::Send) => {
                let target = self
                    .spec
                    .send_target
                    .as_ref()
                    .and_then(|t| sidechains.get_mut(t));
                if let Some(sidechain) = target {
                    let skip = muted.min(samples.len() / 2);
                    for (s, x) in sidechain[skip * 2..samples.len()]
                        .iter_mut()
                        .zip(&samples[skip * 2..])
                    {
                        *s += x * self.spec.send_level;
                    }
                }
            }
            Some(EffectKind::Receive) => {
                if let Some(sidechain) = sidechains.get(&self.spec.id) {
                    for (x, s) in samples.iter_mut().zip(sidechain) {
                        *x += s;
                    }
                }
            }
            Some(EffectKind::DuckVolume) => {
                // validate() requires the parameters for this kind.
                if let Some(duck) = &self.spec.duck {
                    let sidechain = sidechains.get(&self.spec.id).map(|s| &s[..samples.len()]);
                    self.duck.process(samples, sidechain, duck, self.rate);
                }
            }
            _ => return false,
        }
        self.frames = self.frames.saturating_add((samples.len() / 2) as u32);
        true
    }
    pub fn process(&mut self, samples: &mut [f32], bypass: bool) {
        let Some(kind) = self.kind else { return };
        if kind == EffectKind::Attenuation {
            let from = self.gain;
            let frames = samples.len() / 2;
            for (frame, stereo) in samples.chunks_exact_mut(2).enumerate() {
                let gain = from + (self.spec.gain - from) * (frame + 1) as f32 / frames as f32;
                for x in stereo {
                    *x *= gain;
                }
            }
            self.gain = self.spec.gain;
        } else if kind == EffectKind::Echo {
            if !bypass && !self.spec.bypass {
                self.echo.process(samples, &self.spec);
            }
        } else if kind.bypass_keeps_state() {
            // A bypassed unit keeps its state; resuming after bypass is unmeasured.
            if bypass || self.spec.bypass {
                return;
            }
            // An active unit has its parameters (validate_in_group), and its kernel exists.
            let rate = self.rate;
            match kind {
                EffectKind::Compressor => {
                    if let Some(spec) = &self.spec.compressor {
                        self.compressor.process(samples, spec, rate);
                    }
                }
                EffectKind::ParamEq => self.eq.process(samples),
                EffectKind::Distortion => {
                    if let Some(level) = self.spec.distortion {
                        distortion(samples, level);
                    }
                }
                EffectKind::PitchShifter => {
                    if let (Some(pitch), Some(spec)) = (&mut self.pitch, &self.spec.pitch_shifter) {
                        pitch.process(samples, spec, rate);
                    }
                }
                EffectKind::Normalize => {
                    if let Some(spec) = &self.spec.normalize {
                        self.normalize.process(samples, spec, rate);
                    }
                }
                EffectKind::Flange => {
                    if let (Some(flange), Some(spec)) = (&mut self.flange, &self.spec.flange) {
                        flange.process(samples, spec);
                    }
                }
                EffectKind::Chorus => {
                    if let (Some(chorus), Some(spec)) = (&mut self.chorus, &self.spec.chorus) {
                        chorus.process(samples, spec);
                    }
                }
                _ => self.simple.process(
                    samples,
                    kind == EffectKind::HighpassSimple,
                    self.spec.cutoff,
                    rate,
                ),
            }
        } else if let Some(reverb) = &mut self.reverb {
            if !bypass && !self.spec.bypass {
                match self.spec.mix_level {
                    None => reverb.process(samples),
                    Some(wet) => {
                        // Native: wet * effect + (1 - wet) * input, the slot ramping in over
                        // its first 64 frames; an inaudible wet level drops the effect output.
                        let processed = &mut self.scratch;
                        processed.clear();
                        processed.extend_from_slice(samples);
                        reverb.process(processed);
                        let wet = if self.spec.mix_audible { wet } else { 0.0 };
                        let dry = 1.0 - self.spec.mix_level.unwrap_or(0.0);
                        for (frame, (out, effect)) in samples
                            .chunks_exact_mut(2)
                            .zip(processed.chunks_exact(2))
                            .enumerate()
                        {
                            let ramp = ((self.frames as usize + frame) as f32 / 64.0).min(1.0);
                            for (x, e) in out.iter_mut().zip(effect) {
                                *x = (wet * e + dry * *x) * ramp;
                            }
                        }
                    }
                }
                self.frames = self.frames.saturating_add((samples.len() / 2) as u32);
            }
        } else if kind.fades_in_when_active() && !bypass && !self.spec.bypass {
            for stereo in samples.chunks_exact_mut(2) {
                let cutoff = Self::slew(self.cutoff, self.spec.cutoff.clamp(10.0, 22000.0));
                let resonance = Self::slew(self.resonance, self.spec.resonance.clamp(1.0, 10.0));
                if cutoff != self.cutoff || resonance != self.resonance {
                    self.cutoff = cutoff;
                    self.resonance = resonance;
                    self.tune();
                }
                for (channel, x) in stereo.iter_mut().enumerate() {
                    *x = if kind == EffectKind::Lowpass {
                        self.lowpass.sample(*x, channel)
                    } else {
                        self.stages[0].sample(*x, channel, true)
                    };
                }
            }
        }
    }
}

pub struct GroupNode {
    pub spec: GroupSpec,
    pub effects: Vec<Effect>,
    pub buffer: Vec<f32>,
    pub rendered: usize,
    pub suspended: bool,
    pub last_active: u64,
    pub energy: f64,
}
impl GroupNode {
    pub fn update(&mut self, spec: GroupSpec, rate: u32, buffer: u32) -> anyhow::Result<()> {
        for effect in &spec.effects {
            effect.validate(rate, buffer)?;
        }
        let mut old = std::mem::take(&mut self.effects);
        self.effects = spec
            .effects
            .iter()
            .map(|s| {
                if let Some(index) = old
                    .iter()
                    .position(|e| e.spec.id == s.id && e.spec.kind == s.kind)
                {
                    let mut effect = old.remove(index);
                    effect.update(s.clone(), rate, buffer)?;
                    Ok(effect)
                } else {
                    Effect::new(s.clone(), rate, buffer)
                }
            })
            .collect::<anyhow::Result<_>>()?;
        self.spec = spec;
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_kind_table_round_trips_and_keeps_the_former_kind_lists() {
        for (kind, name) in EffectKind::NAMES {
            assert_eq!(EffectKind::parse(name), Some(kind));
            assert_eq!(kind.name(), name);
        }
        assert_eq!(EffectKind::parse("Unmeasured Effect"), None);
        let names = |rule: fn(EffectKind) -> bool| -> Vec<&str> {
            EffectKind::NAMES
                .iter()
                .filter(|(kind, _)| rule(*kind))
                .map(|(_, name)| *name)
                .collect()
        };
        assert_eq!(
            names(EffectKind::bypass_keeps_state),
            [
                "Compressor",
                "ParamEQ",
                "Lowpass Simple",
                "Highpass Simple",
                "Distortion",
                "Chorus",
                "Flange",
                "Normalize",
                "Pitch Shifter"
            ]
        );
        assert_eq!(
            names(EffectKind::fades_in_when_bypassed),
            [
                "Echo",
                "SFX Reverb",
                "Duck Volume",
                "Compressor",
                "ParamEQ",
                "Lowpass Simple",
                "Highpass Simple",
                "Distortion",
                "Chorus",
                "Flange",
                "Normalize",
                "Pitch Shifter"
            ]
        );
        assert_eq!(
            names(EffectKind::fades_in_when_active),
            ["Lowpass", "Highpass"]
        );
        assert_eq!(
            names(EffectKind::layout_limited),
            ["Normalize", "Pitch Shifter"]
        );
    }

    #[test]
    fn kernels_without_parameters_leave_the_samples_instead_of_panicking() {
        // validate_in_group rejects these when active; the audio thread must not panic anyway.
        for kind in [
            "Distortion",
            "Chorus",
            "Flange",
            "Normalize",
            "Pitch Shifter",
            "Compressor",
            "Duck Volume",
        ] {
            let spec: EffectSpec =
                serde_json::from_value(serde_json::json!({ "id": "e", "kind": kind })).unwrap();
            let mut effect = Effect::new(spec, 48000, 1024).unwrap();
            let mut samples = [0.25_f32, -0.5, 0.125, 0.0];
            effect.process(&mut samples, false);
            effect.process_routed(&mut samples, &mut HashMap::new(), 0);
            assert_eq!(samples, [0.25, -0.5, 0.125, 0.0], "{kind}");
        }
    }

    #[test]
    fn distortion_matches_native_dc_and_large_bipolar_samples() {
        let mut x = [0.0, 0.00001, -0.000005, 0.5, -0.25, 4.0, -2.0];
        distortion(&mut x, 0.5);
        let native = [
            0.0,
            0.000029999399,
            -0.0000149998496,
            0.75,
            -0.5,
            1.3333334,
            -1.2,
        ];
        for (actual, expected) in x.into_iter().zip(native) {
            assert!((actual - expected).abs() < 1e-7);
        }
        let mut endpoint = [0.06451613, -0.032258064];
        distortion(&mut endpoint, 1.0);
        assert!((endpoint[0] - 0.9992754).abs() < 1e-7);
        assert!((endpoint[1] + 0.99850196).abs() < 1e-7);
        let mut dry = [0.5, -2.0];
        distortion(&mut dry, 0.0);
        assert_eq!(dry, [0.5, -2.0]);
    }

    #[test]
    fn simple_filter_coefficients_follow_the_measured_two_segment_curve() {
        // Native impulse responses (tests/projects/mixercompressor): first sample a^2.
        for (cutoff, rate, expected) in [
            (5000.0, 48000, 0.3955872),
            (14000.0, 44100, 0.6660725),
            (17000.0, 48000, 0.7520257),
            (21999.0, 44100, 0.9999582),
        ] {
            assert!(
                (SimpleFilter::coefficient(cutoff, rate) - expected).abs() < 2e-7,
                "{cutoff} {rate}"
            );
        }
        assert_eq!(SimpleFilter::coefficient(22000.0, 48000), 1.0);
        let mut high = SimpleFilter::default();
        let mut x = [0.5_f32, -0.5, 0.0, 0.0];
        high.process(&mut x, true, 22000.0, 44100);
        assert_eq!(x, [0.0; 4]);
    }

    #[test]
    fn param_eq_boosts_its_center_by_the_squared_gain_and_passes_dc() {
        let mut eq = ParamEq::default();
        eq.tune(
            &ParamEqSpec {
                center: 1000.0,
                octaves: 1.0,
                gain: 3.0,
            },
            48000,
        );
        // Native's first impulse sample for this setting (eq_boost).
        assert!((eq.b[0] - 1.1703296).abs() < 1e-6);
        let dc: f64 = eq.b.iter().sum::<f64>() / (1.0 + eq.a[0] + eq.a[1]);
        assert!((dc - 1.0).abs() < 1e-12);
        // |H(e^jw0)| = gain^2 at the center.
        let w = 2.0 * std::f64::consts::PI * 1000.0 / 48000.0;
        let response = |c: [f64; 3]| {
            let re: f64 = (0..3).map(|i| c[i] * (i as f64 * w).cos()).sum();
            let im: f64 = (0..3).map(|i| -c[i] * (i as f64 * w).sin()).sum();
            re.hypot(im)
        };
        let gain = response(eq.b) / response([1.0, eq.a[0], eq.a[1]]);
        assert!((gain - 9.0).abs() < 1e-9);
    }
}
