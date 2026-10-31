//! Persistent DSP on summed mixer groups. The measured built-in Lowpass is a fourth-order
//! Butterworth cascade, while Highpass is second order. See tests/projects/mixerfilters.
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
}

#[derive(Default)]
struct Biquad {
    b: [f32; 3],
    a: [f32; 2],
    z: [[f32; 2]; 2],
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
    fn process(&mut self, samples: &mut [f32]) {
        for (i, x) in samples.iter_mut().enumerate() {
            let z = &mut self.z[i % 2];
            let y = self.b[0] * *x + z[0];
            z[0] = self.b[1] * *x - self.a[0] * y + z[1];
            z[1] = self.b[2] * *x - self.a[1] * y;
            *x = y;
        }
    }
}

pub struct Effect {
    pub spec: EffectSpec,
    stages: [Biquad; 2],
}
impl Effect {
    pub fn new(spec: EffectSpec, rate: u32) -> Self {
        let mut effect = Self {
            spec: spec.clone(),
            stages: Default::default(),
        };
        effect.update(spec, rate);
        effect
    }
    pub fn update(&mut self, spec: EffectSpec, rate: u32) {
        let cutoff = spec.cutoff.clamp(10.0, 22000.0);
        let resonance = spec.resonance.clamp(1.0, 10.0);
        match spec.kind.as_str() {
            "Lowpass" => {
                for (i, stage) in self.stages.iter_mut().enumerate() {
                    let q = resonance
                        / (2.0 * (((2 * i + 1) as f32) * std::f32::consts::PI / 8.0).cos());
                    stage.tune(false, cutoff, q, rate);
                }
            }
            "Highpass" => self.stages[0].tune(true, cutoff, resonance, rate),
            _ => {}
        }
        self.spec = spec;
    }
    pub fn process(&mut self, samples: &mut [f32], bypass: bool) {
        if self.spec.kind == "Attenuation" {
            for x in samples {
                *x *= self.spec.gain;
            }
        } else if !bypass && !self.spec.bypass {
            self.stages[0].process(samples);
            if self.spec.kind == "Lowpass" {
                self.stages[1].process(samples);
            }
        }
    }
}

pub struct GroupNode {
    pub spec: GroupSpec,
    pub effects: Vec<Effect>,
    pub buffer: Vec<f32>,
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
