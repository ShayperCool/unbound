//! Persistent DSP on summed mixer groups. Native captures establish a fourth-order Butterworth
//! response for Lowpass and a second-order response for Highpass. The independently measured
//! startup kernels also constrain parameter-dependent delay-history normalization.
use super::chorus::{Chorus, ChorusSpec};
use super::compressor::{Compressor, CompressorSpec};
use super::duck::{Duck, DuckSpec};
use super::flange::{Flange, FlangeSpec};
use super::mixer::{MixerConfig, speaker_mode};
use super::modulated_delay::MAX_HISTORY_BYTES;
use super::normalize::{Normalize, NormalizeSpec};
use super::pitch::{PitchShifter, PitchSpec};
use super::ramp::ParameterRamp;
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
#[derive(Clone, Copy)]
struct SlotWet {
    maximum: f32,
    static_only: bool,
}
const STATIC_SLOT_WET: Option<SlotWet> = Some(SlotWet {
    maximum: f32::MAX,
    static_only: true,
});
// Static wet startup controls in the five effect probes and mixerreverb.
const SLOT_STARTUP_FRAMES: f32 = 64.0;
// Live wet/dry gain controls in tests/projects/audiodistortion, Unity 6000.0.71f1.
const SLOT_TRANSITION_FRAMES: u32 = 64;
const UNIT_SLOT_WET: Option<SlotWet> = Some(SlotWet {
    maximum: 1.0,
    static_only: false,
});

impl EffectKind {
    const NAMES: [(Self, &'static str, Option<SlotWet>); 17] = [
        (Self::Attenuation, "Attenuation", None),
        (Self::Lowpass, "Lowpass", None),
        (Self::Highpass, "Highpass", None),
        (Self::Echo, "Echo", None),
        (Self::SfxReverb, "SFX Reverb", UNIT_SLOT_WET),
        (Self::Send, "Send", None),
        (Self::Receive, "Receive", None),
        (Self::DuckVolume, "Duck Volume", None),
        (Self::Compressor, "Compressor", None),
        (Self::ParamEq, "ParamEQ", None),
        (Self::LowpassSimple, "Lowpass Simple", None),
        (Self::HighpassSimple, "Highpass Simple", None),
        (Self::Distortion, "Distortion", STATIC_SLOT_WET),
        (Self::Chorus, "Chorus", STATIC_SLOT_WET),
        (Self::Flange, "Flange", STATIC_SLOT_WET),
        (Self::Normalize, "Normalize", STATIC_SLOT_WET),
        (Self::PitchShifter, "Pitch Shifter", STATIC_SLOT_WET),
    ];
    /// None for a kind the mixer does not run.
    pub(crate) fn parse(name: &str) -> Option<Self> {
        Self::NAMES
            .iter()
            .find(|(_, n, _)| *n == name)
            .map(|(kind, _, _)| *kind)
    }
    pub(crate) fn name(self) -> &'static str {
        Self::NAMES
            .iter()
            .find(|(kind, _, _)| *kind == self)
            .map_or("", |(_, name, _)| name)
    }
    fn wet_policy(self) -> Option<SlotWet> {
        Self::NAMES
            .iter()
            .find(|(kind, _, _)| *kind == self)
            .and_then(|(_, _, wet)| *wet)
    }
    pub(crate) fn static_wet(self) -> bool {
        self.wet_policy().is_some_and(|policy| policy.static_only)
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
    /// only while active; Send and Receive use the placement rule below.
    pub(crate) fn fades_in_when_bypassed(self) -> bool {
        use EffectKind::*;
        matches!(self, Echo | SfxReverb | DuckVolume) || self.bypass_keeps_state()
    }
    /// Routing units following Attenuation retain the group input fade. Before it, they
    /// do not request a fade (tests/projects/mixerduck, startup placement controls).
    pub(crate) fn fades_in_after_attenuation(self) -> bool {
        matches!(self, Self::Send | Self::Receive)
    }
    /// The fade of Lowpass and Highpass, which apply it only while active.
    pub(crate) fn fades_in_when_active(self) -> bool {
        matches!(self, Self::Lowpass | Self::Highpass)
    }
    /// The one-pole filters, whose cutoff is measured only from 10 to 22000 Hz.
    fn simple_filter(self) -> bool {
        matches!(self, Self::LowpassSimple | Self::HighpassSimple)
    }
    /// Kinds with explicit output-context checks. Normalize uses [`MEASURED_LAYOUT`];
    /// Pitch Shifter also covers other DSP buffers and output layouts (`PITCH_SPEAKER_MODES`).
    pub(crate) fn layout_limited(self) -> bool {
        Self::LAYOUT_LIMITED.contains(&self)
    }
    pub(crate) const TRANSITION_LIMITED: [Self; 5] = [
        Self::Distortion,
        Self::Chorus,
        Self::Flange,
        Self::Normalize,
        Self::PitchShifter,
    ];
    pub(crate) const LAYOUT_LIMITED: [Self; 2] = [Self::PitchShifter, Self::Normalize];
}

/// The output contexts of the Pitch Shifter captures (Unity 6000.0.71f1 Linux Editor,
/// tests/projects/audiopitch): output rates, DSP buffer lengths and `AudioSpeakerMode` values
/// Mono, Stereo, Quad, Surround, Mode5point1 and Mode7point1. Prologic stays unmeasured.
const PITCH_RATES: [u32; 2] = [48000, 44100];
const PITCH_BUFFERS: [u32; 4] = [256, 512, 1024, 2048];
const PITCH_SPEAKER_MODES: [i32; 6] = [
    speaker_mode::MONO,
    speaker_mode::STEREO,
    speaker_mode::QUAD,
    speaker_mode::SURROUND,
    speaker_mode::MODE_5POINT1,
    speaker_mode::MODE_7POINT1,
];

/// The output covered by Normalize and static-wet captures (Unity 6000.0.71f1 Linux Editor,
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
    /// The compatibility profile selects block-constant mixer parameter submission.
    #[serde(default)]
    pub gain_immediate: bool,
    /// Interior attenuation keeps an accumulated per-frame gain (tests/projects/mixerduck).
    #[serde(default)]
    pub gain_ramp_measured: bool,
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
    /// The effect slot's linear wet level when wet mixing is enabled.
    #[serde(default)]
    pub mix_level: Option<f32>,
    /// Compat selects the static slot mechanism measured by the five effect probes.
    #[serde(default)]
    pub slot_wet_measured: bool,
    /// Compat selects block-constant parameter changes using each kernel's state rules.
    #[serde(default)]
    pub effect_transitions_measured: bool,
    /// Compat selects the shared 64-frame effect-slot gain transition.
    #[serde(default)]
    pub slot_wet_transitions_measured: bool,
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
    /// The measured live path changes values, keeping identity, placement and bypass fixed.
    /// Generic payloads owned by other units do not constrain supported group updates.
    pub(crate) fn live_identity_matches(&self, other: &Self) -> bool {
        self.id == other.id
            && self.kind == other.kind
            && self.bypass == other.bypass
            && self.slot_wet_measured == other.slot_wet_measured
            && self.effect_transitions_measured == other.effect_transitions_measured
            && self.slot_wet_transitions_measured == other.slot_wet_transitions_measured
    }
    pub(crate) fn kernel_settings_match(&self, other: &Self) -> bool {
        (self.effect_transitions_measured && other.effect_transitions_measured)
            || (self.distortion == other.distortion
                && self.chorus == other.chorus
                && self.flange == other.flange
                && self.normalize == other.normalize
                && self.pitch_shifter == other.pitch_shifter)
    }
    pub(crate) fn static_wet_settings_match(&self, other: &Self) -> bool {
        self.live_identity_matches(other)
            && ((self.slot_wet_transitions_measured && other.slot_wet_transitions_measured)
                || (self.mix_level == other.mix_level && self.mix_audible == other.mix_audible))
            && self.kernel_settings_match(other)
    }

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
            let policy = EffectKind::parse(&self.kind).and_then(EffectKind::wet_policy);
            anyhow::ensure!(
                policy.is_some(),
                "AudioMixer wet mixing is unsupported for effect '{}'",
                self.kind
            );
            let policy = policy.unwrap();
            anyhow::ensure!(
                !policy.static_only || self.slot_wet_measured,
                "AudioMixer static wet mixing is measured only for Unity 6000.0.71f1"
            );
            anyhow::ensure!(
                level.is_finite() && (0.0..=policy.maximum).contains(&level),
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
        if kind == EffectKind::PitchShifter {
            anyhow::ensure!(
                PITCH_RATES.contains(&config.sample_rate)
                    && PITCH_BUFFERS.contains(&config.buffer_size)
                    && PITCH_SPEAKER_MODES.contains(&config.speaker_mode),
                "AudioMixer Pitch Shifter is measured only at 48000/44100 Hz with 256/512/1024/2048-frame mono, stereo, quad, 5.0, 5.1 or 7.1 output"
            );
        }
        if (kind.layout_limited() && kind != EffectKind::PitchShifter)
            || (self.mix_level.is_some() && kind.wet_policy().is_some_and(|p| p.static_only))
        {
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
    /// The measured mixer graph startup profile; false keeps the unmeasured legacy group age.
    #[serde(default)]
    pub input_fade_from_mixer: bool,
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
    gain_ramp: ParameterRamp,
    wet_ramp: ParameterRamp,
    dry_ramp: ParameterRamp,
    send_from: f32,
    send_frames: u32,
    buffer: u32,
    /// Channels of the output layout the group carries (see [`Effect::set_layout`]).
    layout: usize,
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
            send_from: spec.send_level,
            send_frames: 0,
            gain_ramp: ParameterRamp::new(spec.gain),
            wet_ramp: ParameterRamp::new(if spec.mix_audible {
                spec.mix_level.unwrap_or(1.0)
            } else {
                0.0
            }),
            dry_ramp: ParameterRamp::new((1.0 - spec.mix_level.unwrap_or(1.0)).max(-1.0)),
            buffer: buffer.max(1),
            layout: 2,
            gain: if spec.kind == EffectKind::Send.name() {
                spec.send_level
            } else {
                spec.gain
            },
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
        if let Some(parameters) = &spec.pitch_shifter {
            if self
                .pitch
                .as_ref()
                .is_none_or(|p| p.fft_size() != parameters.fft_size)
            {
                self.pitch = Some(PitchShifter::new(parameters.fft_size, 2));
            }
        }
        if let (Some(pitch), Some(parameters)) = (&mut self.pitch, &spec.pitch_shifter) {
            // Parameter side effects apply even when a zero-wet slot pauses audio processing.
            pitch.set_channel_limit(self.layout, parameters.max_channels);
            pitch.set_pitch(parameters.pitch);
        }
        if self.buffer != buffer.max(1)
            || self.spec.gain_ramp_measured != spec.gain_ramp_measured
            || self.spec.gain_immediate != spec.gain_immediate
        {
            self.gain_ramp = ParameterRamp::new(self.gain);
        }
        let kind = EffectKind::parse(&spec.kind);
        if kind == Some(EffectKind::Echo) {
            self.echo.tune(spec.delay, rate)?;
        }
        if let Some(sfx) = &spec.sfx {
            let reverb = match &mut self.reverb {
                Some(reverb) if reverb.configuration() == (rate, buffer.max(1)) => reverb,
                slot => slot.insert(Box::new(SfxReverb::new(rate, buffer)?)),
            };
            reverb.tune_immediate(sfx);
        }
        if kind == Some(EffectKind::Send) && spec.send_level != self.spec.send_level {
            self.send_from = self.gain;
            self.send_frames = 0;
        }
        if spec.mix_level.is_some() {
            // Group processing splits at BLOCK; reserve also for direct DSP-buffer callers.
            self.scratch
                .resize(buffer.max(super::mixer::BLOCK as u32) as usize * 2, 0.0);
        }
        self.buffer = buffer.max(1);
        self.kind = kind;
        self.rate = rate;
        self.spec = spec;
        self.tune();
        Ok(())
    }
    /// Sets the output layout's channel count. Group effects learn it when they are created;
    /// a layout change (`AudioSettings.Reset`) recreates every group effect.
    pub(crate) fn set_layout(&mut self, channels: usize) {
        self.layout = channels;
        if let (Some(pitch), Some(parameters)) = (&mut self.pitch, &self.spec.pitch_shifter) {
            pitch.set_channel_limit(channels, parameters.max_channels);
        }
    }

    /// A reconnected source component restarts empty like [`Effect::reset`], except that the
    /// Chorus sweep span continues and the resonant lowpass keeps its state: that unit has no
    /// restart of its own. Unity 6000.0.71f1, tests/projects/audiodistortion live bypass
    /// controls.
    pub fn restart(&mut self) {
        self.clear_state(true);
    }

    /// Forgets the histories and the parameter ramp, leaving the effect as [`Effect::new`] makes
    /// it at the current rate. The Echo buffer keeps its allocation (zeroed).
    pub fn reset(&mut self) {
        self.clear_state(false);
    }

    fn clear_state(&mut self, restart: bool) {
        self.stages = Default::default();
        if !restart {
            self.lowpass = Default::default();
        }
        self.echo.history.fill([0; 2]);
        self.echo.position = 0;
        if let Some(reverb) = &mut self.reverb {
            reverb.clear();
        }
        self.duck = Duck::default();
        self.compressor = Compressor::default();
        if let Some(chorus) = &mut self.chorus {
            if restart {
                chorus.restart();
            } else {
                chorus.clear();
            }
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
        self.send_from = self.spec.send_level;
        self.send_frames = 0;
        self.gain_ramp = ParameterRamp::new(self.spec.gain);
        self.wet_ramp = ParameterRamp::new(if self.spec.mix_audible {
            self.spec.mix_level.unwrap_or(1.0)
        } else {
            0.0
        });
        self.dry_ramp = ParameterRamp::new((1.0 - self.spec.mix_level.unwrap_or(1.0)).max(-1.0));
        if !restart {
            self.cutoff = 5000.0;
            self.resonance = 1.0;
        }
        self.gain = if self.kind == Some(EffectKind::Send) {
            self.spec.send_level
        } else {
            self.spec.gain
        };
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
                    let frames = samples.len() / 2;
                    let skip = muted.min(frames);
                    // tests/projects/mixerduck: a live level change interpolates linear gain
                    // across interleaved samples of one DSP block, starting at the preceding level.
                    for frame in skip..frames {
                        let position = self.send_frames.saturating_add(frame as u32);
                        for channel in 0..2 {
                            let gain = self.send_gain_at(position, channel);
                            let i = frame * 2 + channel;
                            sidechain[i] += samples[i] * gain;
                        }
                    }
                }
                self.send_frames = self
                    .send_frames
                    .saturating_add((samples.len() / 2) as u32)
                    .min(self.buffer);
                self.gain = self.send_gain_at(self.send_frames, 0);
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
    fn send_gain_at(&self, position: u32, channel: usize) -> f32 {
        if position >= self.buffer {
            self.spec.send_level
        } else {
            self.send_from
                + (self.spec.send_level - self.send_from)
                    * ((position as f32 + channel as f32 * 0.5) / self.buffer as f32)
        }
    }
    pub fn process(&mut self, samples: &mut [f32], bypass: bool) {
        let wet = self.spec.mix_level.filter(|_| !bypass && !self.spec.bypass);
        if wet.is_some() {
            self.scratch[..samples.len()].copy_from_slice(samples);
        }
        let live_wet = self.spec.slot_wet_transitions_measured
            && self.kind.is_some_and(EffectKind::static_wet);
        // A fully dry live slot skips the kernel while its gains can still be finishing.
        if !(live_wet && wet == Some(0.0)) {
            self.process_effect(samples, bypass);
        }
        if wet == Some(0.0)
            && self.spec.slot_wet_measured
            && (!live_wet || (self.wet_ramp.value == 0.0 && self.dry_ramp.value == 1.0))
        {
            // The fully dry slot is an identity, including its startup frames. Keep the
            // static kernel history advancing; the live path above pauses it at zero wet.
            samples.copy_from_slice(&self.scratch[..samples.len()]);
        } else if let Some(level) = wet {
            let wet = if self.spec.mix_audible { level } else { 0.0 };
            let dry = (1.0 - level).max(-1.0);
            // The slot adds its own 64-frame startup fade around the processed/dry sum.
            for (frame, (out, input)) in samples
                .chunks_exact_mut(2)
                .zip(self.scratch.chunks_exact(2))
                .enumerate()
            {
                let (wet, dry) = if live_wet {
                    (
                        self.wet_ramp.gain(level, SLOT_TRANSITION_FRAMES),
                        self.dry_ramp.gain(dry, SLOT_TRANSITION_FRAMES),
                    )
                } else {
                    (wet, dry)
                };
                let ramp = ((self.frames as usize + frame) as f32 / SLOT_STARTUP_FRAMES).min(1.0);
                for (x, input) in out.iter_mut().zip(input) {
                    *x = (wet * *x + dry * input) * ramp;
                }
            }
        }
        if !bypass && !self.spec.bypass {
            self.frames = self.frames.saturating_add((samples.len() / 2) as u32);
        }
    }
    fn process_effect(&mut self, samples: &mut [f32], bypass: bool) {
        let Some(kind) = self.kind else { return };
        if kind == EffectKind::Attenuation {
            let from = self.gain;
            let frames = samples.len() / 2;
            let measured_ramp = self.spec.gain_ramp_measured && !self.spec.gain_immediate;
            for (frame, stereo) in samples.chunks_exact_mut(2).enumerate() {
                let gain = if self.spec.gain_immediate {
                    self.spec.gain
                } else if measured_ramp {
                    self.gain_ramp.next(self.spec.gain, self.buffer)
                } else {
                    from + (self.spec.gain - from) * (frame + 1) as f32 / frames as f32
                };
                for x in stereo {
                    *x *= gain;
                }
            }
            self.gain = if measured_ramp {
                self.gain_ramp.value
            } else {
                self.spec.gain
            };
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
                        pitch.process(samples, spec, rate, self.layout);
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
                reverb.process(samples);
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
    pub input_fade: bool,
    pub suspended: bool,
    pub last_active: u64,
    pub energy: f64,
}
impl GroupNode {
    pub fn update(&mut self, spec: GroupSpec, config: &MixerConfig) -> anyhow::Result<()> {
        let (rate, buffer) = (config.sample_rate, config.buffer_size);
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
                    let mut effect = Effect::new(s.clone(), rate, buffer)?;
                    effect.set_layout(config.output_channels());
                    Ok(effect)
                }
            })
            .collect::<anyhow::Result<_>>()?;
        let mut after_attenuation = false;
        self.input_fade = self.effects.iter().any(|effect| {
            let Some(kind) = effect.kind() else {
                return false;
            };
            after_attenuation |= kind == EffectKind::Attenuation;
            // The static wet wrapper before Attenuation supplies the startup fade itself.
            // Moving it after Attenuation retains the group's input fade as well.
            let wet_before_attenuation = effect.spec.slot_wet_measured
                && effect.spec.mix_level.is_some()
                && !after_attenuation;
            !wet_before_attenuation
                && (kind.fades_in_when_bypassed()
                    || (after_attenuation && kind.fades_in_after_attenuation())
                    || (!spec.bypass && !effect.spec.bypass && kind.fades_in_when_active()))
        });
        self.spec = spec;
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn wet_transitions_survive_partial_chunks_and_repeated_descriptors() {
        let spec = |wet| {
            serde_json::from_value::<EffectSpec>(serde_json::json!({
                "id": "live", "kind": "Distortion", "distortion": 0.5,
                "mixLevel": wet, "slotWetMeasured": true, "slotWetTransitionsMeasured": true,
                "effectTransitionsMeasured": true
            }))
            .unwrap()
        };
        let mut whole = Effect::new(spec(0.25), 44100, 1024).unwrap();
        let mut chunks = Effect::new(spec(0.25), 44100, 1024).unwrap();
        for target in [0.25, 0.5, 1.0, 0.0, 0.25] {
            whole.update(spec(target), 44100, 1024).unwrap();
            let mut a: Vec<f32> = (0..2048).map(|i| (i % 31) as f32 / 128.0 - 0.125).collect();
            let mut b = a.clone();
            whole.process(&mut a, false);
            for part in b.chunks_mut(14) {
                chunks.update(spec(target), 44100, 1024).unwrap();
                chunks.process(part, false);
            }
            assert_eq!(a, b, "wet {target}");
        }
    }

    #[test]
    fn group_sfx_reverb_live_changes_apply_at_once_inside_a_dsp_buffer() {
        // The DSP-buffer crossfade is measured only on the zone return; mixer and filter
        // SFX Reverb live changes keep immediate application.
        let spec = |dry: f32| {
            serde_json::from_value::<EffectSpec>(serde_json::json!({
                "id": "reverb", "kind": "SFX Reverb",
                "sfx": {
                    "dryLevel": dry, "room": -10000.0, "roomHf": 0.0, "decayTime": 1.0,
                    "decayHfRatio": 0.5, "reflections": -10000.0, "reflectDelay": 0.02,
                    "reverb": 0.0, "reverbDelay": 0.04, "diffusion": 100.0, "density": 100.0,
                    "hfReference": 5000.0, "roomLf": 0.0, "lfReference": 250.0
                }
            }))
            .unwrap()
        };
        let mut effect = Effect::new(spec(0.0), 48000, 1024).unwrap();
        let mut before = vec![0.25; 2 * 100];
        effect.process(&mut before, false);
        effect.update(spec(-600.0), 48000, 1024).unwrap();
        let mut after = vec![0.25; 2];
        effect.process(&mut after, false);
        let ratio = after[0] / before[199];
        assert!(
            (ratio - 10f32.powf(-600.0 / 2000.0)).abs() < 1e-6,
            "{ratio}"
        );
    }

    #[test]
    fn attenuation_parameter_submission_applies_across_the_whole_packet() {
        for packet in [64, 256, 480, 1024] {
            for immediate in [false, true] {
                let spec = |gain| {
                    serde_json::from_value::<EffectSpec>(serde_json::json!({
                        "id": "level", "kind": "Attenuation", "gain": gain,
                        "gainImmediate": immediate
                    }))
                    .unwrap()
                };
                let mut effect = Effect::new(spec(1.0), 48000, 1024).unwrap();
                let mut samples = vec![1.0; packet * 2];
                effect.process(&mut samples, false);
                for gain in [0.5, 0.25, 0.0] {
                    effect.update(spec(gain), 48000, 1024).unwrap();
                    samples.fill(1.0);
                    effect.process(&mut samples, false);
                    if immediate {
                        assert!(samples.iter().all(|v| *v == gain));
                    } else {
                        assert!(samples[0] > gain);
                        assert_eq!(samples[packet * 2 - 1], gain);
                    }
                }
            }
        }
    }

    #[test]
    fn interior_attenuation_carries_its_accumulated_gain_between_buffers() {
        // mixerduck/control_volume_nosend, first changing block at 48000 Hz.
        let spec = |gain| {
            serde_json::from_value::<EffectSpec>(serde_json::json!({
                "id": "level", "kind": "Attenuation", "gain": gain,
                "gainRampMeasured": true
            }))
            .unwrap()
        };
        let mut effect = Effect::new(spec(1.0), 48000, 1024).unwrap();
        effect.update(spec(0.644_302_37), 48000, 1024).unwrap();
        let mut samples = vec![1.0; 2048];
        for right in samples.iter_mut().skip(1).step_by(2) {
            *right = -0.5;
        }
        effect.process(&mut samples, false);
        assert_eq!(&samples[..4], &[1.0, -0.5, 0.999_652_6, -0.499_826_3]);
        assert_eq!(samples[2046], 0.644_634_5);
        assert_eq!(samples[2047], -0.322_317_24);
        // A different target starts from the preceding accumulated gain, including rounding.
        effect.update(spec(0.415_118_93), 48000, 1024).unwrap();
        samples.fill(1.0);
        effect.process(&mut samples, false);
        assert_eq!(samples[0], 0.644_287_1);
        assert_eq!(samples[2046], 0.415_335_2);
        // An unchanged request is held exactly on the next DSP buffer.
        samples.fill(1.0);
        effect.process(&mut samples, false);
        assert!(samples.iter().all(|gain| *gain == 0.415_118_93));
        // A different packet size and increasing gain retain the same first-frame rule.
        effect.update(spec(1.0), 44100, 256).unwrap();
        let mut shorter = vec![1.0; 512];
        effect.process(&mut shorter, false);
        assert_eq!(shorter[0], 0.415_118_93);
        assert!(shorter[510] > shorter[0] && shorter[510] < 1.0);
    }

    #[test]
    fn measured_attenuation_ramp_is_independent_of_render_chunk_boundaries() {
        let spec = |gain| {
            serde_json::from_value::<EffectSpec>(serde_json::json!({
                "id": "level", "kind": "Attenuation", "gain": gain,
                "gainRampMeasured": true
            }))
            .unwrap()
        };
        for bypass in [false, true] {
            let render = |chunks: &[usize]| {
                let mut effect = Effect::new(spec(1.0), 48000, 1024).unwrap();
                effect.update(spec(0.644_302_37), 48000, 1024).unwrap();
                let mut output = vec![1.0; 4096];
                let mut offset = 0;
                for &frames in chunks {
                    effect.process(&mut output[offset..offset + frames * 2], bypass);
                    offset += frames * 2;
                }
                assert_eq!(offset, output.len());
                output
            };
            let whole = render(&[1024, 1024]);
            assert_eq!(render(&[256; 8]), whole);
            assert_eq!(render(&[63, 1, 192, 256, 512, 480, 480, 64]), whole);
            assert_eq!(whole[2048], 0.644_302_37);
        }
    }

    #[test]
    fn live_send_gain_changes_ramp_without_resetting_or_muting_the_dry_signal() {
        for rate in [48000, 44100] {
            let spec = |level| {
                serde_json::from_value::<EffectSpec>(serde_json::json!({
                    "id": "send", "kind": "Send", "sendTarget": "receive", "sendLevel": level
                }))
                .unwrap()
            };
            let mut effect = Effect::new(spec(0.25), rate, 32).unwrap();
            let mut sidechains = HashMap::from([("receive".to_string(), vec![0.0; 32])]);
            let mut dry = vec![1.0; 32];
            effect.process_routed(&mut dry, &mut sidechains, 0);
            assert_eq!(sidechains["receive"], vec![0.25; 32]);
            for (from, to) in [(0.25, 0.75), (0.75, 0.125)] {
                for chunk in 0..3 {
                    // Equal descriptors are submitted repeatedly, including midway through a ramp.
                    effect.update(spec(to), rate, 32).unwrap();
                    sidechains.get_mut("receive").unwrap().fill(0.0);
                    effect.process_routed(&mut dry, &mut sidechains, 0);
                    for frame in 0..16 {
                        for channel in 0..2 {
                            let position = ((chunk * 16 + frame) * 2 + channel).min(64);
                            let expected = from + (to - from) * position as f32 / 64.0;
                            assert_eq!(sidechains["receive"][frame * 2 + channel], expected);
                        }
                    }
                    assert_eq!(dry, vec![1.0; 32]);
                }
            }
            effect.reset();
            sidechains.get_mut("receive").unwrap().fill(0.0);
            effect.process_routed(&mut dry, &mut sidechains, 0);
            assert_eq!(sidechains["receive"], vec![0.125; 32]);
        }
    }

    #[test]
    fn the_kind_table_round_trips_and_keeps_the_former_kind_lists() {
        for (kind, name, _) in EffectKind::NAMES {
            assert_eq!(EffectKind::parse(name), Some(kind));
            assert_eq!(kind.name(), name);
        }
        assert_eq!(EffectKind::parse("Unmeasured Effect"), None);
        let names = |rule: fn(EffectKind) -> bool| -> Vec<&str> {
            EffectKind::NAMES
                .iter()
                .filter(|(kind, _, _)| rule(*kind))
                .map(|(_, name, _)| *name)
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
    fn slot_wet_blends_independent_dry_and_processed_signals_without_growing_scratch() {
        for rate in [48000, 44100] {
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
                for level in [0.0, 0.25, 0.5, 1.0, 2.5, 31.674294] {
                    let mut json = parameters.clone();
                    json["id"] = "wet".into();
                    json["kind"] = kind.into();
                    let dry_spec: EffectSpec = serde_json::from_value(json).unwrap();
                    let mut wet_spec = dry_spec.clone();
                    wet_spec.mix_level = Some(level);
                    wet_spec.slot_wet_measured = true;
                    wet_spec
                        .validate_in_group(&MixerConfig::default(), false)
                        .unwrap();
                    let mut processed = Effect::new(dry_spec, rate, 1024).unwrap();
                    let mut wet = Effect::new(wet_spec, rate, 1024).unwrap();
                    let allocation = wet.scratch.as_ptr();
                    let capacity = wet.scratch.capacity();
                    let mut frame = 0;
                    // Irregular chunks verify the fade is a continuous frame clock.
                    for frames in [17, 31, 256, 1024, 1024] {
                        let input: Vec<f32> = (0..frames * 2)
                            .map(|i| ((i * 17 % 127) as f32 - 63.0) / 128.0)
                            .collect();
                        let mut expected = input.clone();
                        let mut actual = input.clone();
                        processed.process(&mut expected, false);
                        wet.process(&mut actual, false);
                        for i in 0..actual.len() {
                            let ramp = ((frame + i / 2) as f32 / 64.0).min(1.0);
                            assert_eq!(
                                actual[i],
                                if level == 0.0 {
                                    input[i]
                                } else {
                                    (level * expected[i] + (1.0 - level).max(-1.0) * input[i])
                                        * ramp
                                },
                                "{kind}, {level}, {frame}, {i}"
                            );
                        }
                        assert_eq!(allocation, wet.scratch.as_ptr());
                        assert_eq!(capacity, wet.scratch.capacity());
                        frame += frames;
                    }
                    let mut bypass = [0.25, -0.5];
                    wet.process(&mut bypass, true);
                    assert_eq!(bypass, [0.25, -0.5]);
                }
            }
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
