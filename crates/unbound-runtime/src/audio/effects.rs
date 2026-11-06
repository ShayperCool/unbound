//! Persistent DSP on summed mixer groups. Native captures establish a fourth-order Butterworth
//! response for Lowpass and a second-order response for Highpass. The independently measured
//! startup kernels also constrain parameter-dependent delay-history normalization.
use super::reverb::{SfxReverb, SfxReverbSpec};
use serde::Deserialize;

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
}
impl EffectSpec {
    pub(crate) fn validate(&self, rate: u32) -> anyhow::Result<()> {
        if self.kind == "Echo" {
            Echo::frames(self.delay, rate)?;
        }
        anyhow::ensure!(self.kind != "SFX Reverb" || self.sfx.is_some(),
            "AudioMixer SFX Reverb '{}' has no parameters", self.id);
        if let Some(sfx) = &self.sfx {
            let values = [sfx.dry_level, sfx.room, sfx.room_hf, sfx.decay_time, sfx.decay_hf_ratio,
                sfx.reflections, sfx.reflect_delay, sfx.reverb, sfx.reverb_delay, sfx.diffusion,
                sfx.density, sfx.hf_reference, sfx.room_lf, sfx.lf_reference];
            anyhow::ensure!(values.iter().all(|v| v.is_finite()),
                "AudioMixer SFX Reverb '{}' has a non-finite parameter", self.id);
        }
        if let Some(level) = self.mix_level {
            anyhow::ensure!(self.kind == "SFX Reverb",
                "AudioMixer wet mixing is unsupported for effect '{}'", self.kind);
            anyhow::ensure!(level.is_finite() && (0.0..=1.0).contains(&level),
                "invalid AudioMixer wet level {level}");
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

/// Unity 6000.0.71f1 Echo kernels, independently captured at both output rates.
#[derive(Default)]
struct Echo {
    history: Vec<[i16; 2]>,
    position: usize,
}
impl Echo {
    fn frames(delay: f32, rate: u32) -> anyhow::Result<usize> {
        // A backend resource limit, not a restriction on Unity's parameter range.
        const MAX_HISTORY_BYTES: usize = 8 * 1024 * 1024;
        anyhow::ensure!(delay.is_finite(), "AudioMixer Echo delay must be finite");
        // Truncate fractional frames, then round up to the measured eight-frame boundary.
        let frames = (delay.clamp(1.0, 5000.0) * rate as f32 / 1000.0) as usize;
        let frames = frames.checked_add(7).map(|n| (n & !7).max(8));
        let frames = frames.filter(|n| *n <= MAX_HISTORY_BYTES / std::mem::size_of::<[i16; 2]>());
        frames.ok_or_else(|| anyhow::anyhow!(
            "AudioMixer Echo delay buffer exceeds the supported 8 MiB limit at {rate} Hz"
        ))
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

pub struct Effect {
    pub spec: EffectSpec,
    stages: [Biquad; 2],
    lowpass: Lowpass,
    echo: Echo,
    reverb: Option<Box<SfxReverb>>,
    /// Frames since the effect started, for the wet-mix slot's startup ramp.
    frames: u32,
    scratch: Vec<f32>,
    cutoff: f32,
    resonance: f32,
    rate: u32,
    gain: f32,
}
impl Effect {
    pub fn new(spec: EffectSpec, rate: u32) -> anyhow::Result<Self> {
        let mut effect = Self {
            spec: spec.clone(),
            stages: Default::default(),
            lowpass: Default::default(),
            echo: Default::default(),
            reverb: None,
            frames: 0,
            scratch: Vec::new(),
            cutoff: 5000.0,
            resonance: 1.0,
            rate,
            gain: spec.gain,
        };
        effect.update(spec, rate)?;
        Ok(effect)
    }
    pub fn update(&mut self, spec: EffectSpec, rate: u32) -> anyhow::Result<()> {
        if spec.kind == "Echo" {
            self.echo.tune(spec.delay, rate)?;
        }
        if let Some(sfx) = &spec.sfx {
            if self.reverb.as_ref().is_none_or(|r| r.rate() != rate) {
                self.reverb = Some(Box::new(SfxReverb::new(rate)?));
            }
            self.reverb.as_mut().unwrap().tune(sfx);
        }
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
        self.frames = 0;
        self.cutoff = 5000.0;
        self.resonance = 1.0;
        self.gain = self.spec.gain;
        self.tune();
    }
    fn tune(&mut self) {
        match self.spec.kind.as_str() {
            "Lowpass" => self.lowpass.tune(self.cutoff, self.resonance, self.rate),
            "Highpass" => self.stages[0].tune(true, self.cutoff, self.resonance, self.rate),
            _ => {}
        }
    }
    // Measured sample-by-sample slew: clamp the step to one percent of the endpoint sum.
    fn slew(current: f32, target: f32) -> f32 {
        let limit = (current.abs() + target.abs()) * 0.01;
        current + (target - current).clamp(-limit, limit)
    }
    pub fn process(&mut self, samples: &mut [f32], bypass: bool) {
        if self.spec.kind == "Attenuation" {
            let from = self.gain;
            let frames = samples.len() / 2;
            for (frame, stereo) in samples.chunks_exact_mut(2).enumerate() {
                let gain = from + (self.spec.gain - from) * (frame + 1) as f32 / frames as f32;
                for x in stereo {
                    *x *= gain;
                }
            }
            self.gain = self.spec.gain;
        } else if self.spec.kind == "Echo" {
            if !bypass && !self.spec.bypass {
                self.echo.process(samples, &self.spec);
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
                        for (frame, (out, effect)) in samples.chunks_exact_mut(2)
                            .zip(processed.chunks_exact(2)).enumerate()
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
        } else if !bypass && !self.spec.bypass {
            for stereo in samples.chunks_exact_mut(2) {
                let cutoff = Self::slew(self.cutoff, self.spec.cutoff.clamp(10.0, 22000.0));
                let resonance = Self::slew(self.resonance, self.spec.resonance.clamp(1.0, 10.0));
                if cutoff != self.cutoff || resonance != self.resonance {
                    self.cutoff = cutoff;
                    self.resonance = resonance;
                    self.tune();
                }
                for (channel, x) in stereo.iter_mut().enumerate() {
                    *x = if self.spec.kind == "Lowpass" {
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
    pub fn update(&mut self, spec: GroupSpec, rate: u32) -> anyhow::Result<()> {
        for effect in &spec.effects {
            effect.validate(rate)?;
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
                    effect.update(s.clone(), rate)?;
                    Ok(effect)
                } else {
                    Effect::new(s.clone(), rate)
                }
            })
            .collect::<anyhow::Result<_>>()?;
        self.spec = spec;
        Ok(())
    }
}
