//! Persistent DSP on summed mixer groups. Native captures establish a fourth-order Butterworth
//! response for Lowpass and a second-order response for Highpass. The lowpass cascade's startup
//! modulation still differs; see tests/projects/mixerfilters for complete buffers and limits.
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
}

#[derive(Default)]
struct Biquad {
    b: [f32; 3],
    a: [f32; 2],
    z: [[f32; 2]; 2],
    history: [[f32; 4]; 2],
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

pub struct Effect {
    pub spec: EffectSpec,
    stages: [Biquad; 2],
    cutoff: f32,
    resonance: f32,
    rate: u32,
    gain: f32,
}
impl Effect {
    pub fn new(spec: EffectSpec, rate: u32) -> Self {
        let mut effect = Self {
            spec: spec.clone(),
            stages: Default::default(),
            cutoff: 5000.0,
            resonance: 1.0,
            rate,
            gain: spec.gain,
        };
        effect.update(spec, rate);
        effect
    }
    pub fn update(&mut self, spec: EffectSpec, rate: u32) {
        self.rate = rate;
        self.spec = spec;
        self.tune();
    }
    fn tune(&mut self) {
        match self.spec.kind.as_str() {
            "Lowpass" => {
                for (i, stage) in self.stages.iter_mut().enumerate() {
                    let q = self.resonance
                        / (2.0 * (((2 * i + 1) as f32) * std::f32::consts::PI / 8.0).cos());
                    stage.tune(false, self.cutoff, q, self.rate);
                }
            }
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
                    *x = self.stages[0].sample(*x, channel, self.spec.kind == "Highpass");
                    if self.spec.kind == "Lowpass" {
                        *x = self.stages[1].sample(*x, channel, false);
                    }
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
}
impl GroupNode {
    pub fn update(&mut self, spec: GroupSpec, rate: u32) {
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
                    effect.update(s.clone(), rate);
                    effect
                } else {
                    Effect::new(s.clone(), rate)
                }
            })
            .collect();
        self.spec = spec;
    }
}
