//! The AudioMixer SFX Reverb effect of Unity 6000.0.71f1, identified from Native impulse
//! responses at 48000 and 44100 Hz (tests/projects/mixerreverb). The input is summed to mono and
//! passes the Room HF and Room LF filters. Early reflections pass two allpasses, then four taps
//! per channel. The late reverb is a ring of eight damped lines that feeds back through a
//! three-stage rotation butterfly whose angle follows Diffusion; the butterfly also fans each
//! line out to eight output taps alternating between the channels. The line damping uses the
//! DMO-style I3DL2 coefficients (as in OpenMPT's BSD-licensed `I3DL2Reverb.cpp`).
use serde::Deserialize;

// Parameter and tap changes crossfade through one complete DSP buffer. Equal endpoints
// retain their exact arithmetic. Unity 6000.0.71f1, tests/projects/audioreverb live controls.
fn blend(from: f32, to: f32, phase: usize, size: usize) -> f32 {
    if from == to {
        to
    } else {
        (from * (size - phase) as f32 + to * phase as f32) / size as f32
    }
}

impl Coefficients {
    fn at(self, target: Self, phase: usize, size: usize) -> Self {
        let value = |from, to| blend(from, to, phase, size);
        let mut c = target;
        c.hf = value(self.hf, target.hf);
        c.early_gain = value(self.early_gain, target.early_gain);
        c.late_gain = value(self.late_gain, target.late_gain);
        c.rotation.0 = value(self.rotation.0, target.rotation.0);
        c.rotation.1 = value(self.rotation.1, target.rotation.1);
        for i in 0..8 {
            c.damping[i] = value(self.damping[i], target.damping[i]);
        }
        c
    }
}

/// Parameters in the Editor's units (mB, seconds, percent, Hz), clamped to the Editor ranges.
#[derive(Debug, Clone, PartialEq, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SfxReverbSpec {
    pub dry_level: f32,
    pub room: f32,
    pub room_hf: f32,
    pub decay_time: f32,
    pub decay_hf_ratio: f32,
    pub reflections: f32,
    pub reflect_delay: f32,
    pub reverb: f32,
    pub reverb_delay: f32,
    pub diffusion: f32,
    pub density: f32,
    pub hf_reference: f32,
    pub room_lf: f32,
    pub lf_reference: f32,
}

// Tap times in milliseconds; the measured sample offsets are floor(ms * rate / 1000) at both rates.
// Line 0's offset includes the frame its base subtracts.
const EARLY_MS: [[f32; 4]; 2] = [[0.0, 7.926, 20.075, 50.781], [4.984, 12.616, 31.932, 80.76]];
const EARLY_ALLPASS_MS: [f32; 2] = [5.885, 7.676];
const EARLY_ALLPASS_GAIN: f32 = 0.63;
// Late input taps use the sum of Reflect Delay and Reverb Delay before truncation.
// Output delays are a separate geometric series; their histories stay separate on retuning.
const LATE_TAP_S: f32 = 0.0187;
const LATE_TAP_RATIO: f32 = 1.29;
const OUTPUT_TAP_SECONDS: f32 = 0.0015;
const OUTPUT_TAP_RATIO: f32 = 1.47;
/// The nominal length of line k is 61 ms * 1.32^k at Density 100.
const LINE_MS: f32 = 61.0;
const LINE_RATIO: f32 = 1.32;
/// A backend resource limit for the delay histories, not a Unity restriction.
const MAX_HISTORY_BYTES: usize = 8 * 1024 * 1024;

fn frames(ms: f32, rate: u32) -> usize {
    (ms as f64 * rate as f64 / 1000.0) as usize
}
fn level(mb: f32) -> f32 {
    10f32.powf(mb / 2000.0)
}

fn ring(len: usize) -> anyhow::Result<Vec<f32>> {
    let mut v = Vec::new();
    v.try_reserve_exact(len)?;
    v.resize(len, 0.0);
    Ok(v)
}

#[derive(Default, Clone, Copy, PartialEq)]
struct Coefficients {
    dry: f32,
    hf: f32,
    lf_b: [f32; 3],
    lf_a: [f32; 2],
    lf: bool,
    early_gain: f32,
    early: [[usize; 4]; 2],
    late_gain: f32,
    late: [usize; 8],
    feedback: [usize; 8],
    decay: [f32; 8],
    damping: [f32; 8],
    rotation: (f32, f32),
    fan_out: [usize; 8],
}

pub struct SfxReverb {
    rate: u32,
    c: Coefficients,
    previous: Coefficients,
    pending: Coefficients,
    configured: bool,
    /// Set at a DSP-buffer boundary when the pending parameters differ from the previous ones.
    transitioning: bool,
    buffer_size: usize,
    buffer_position: usize,
    input: Vec<f32>,
    early_output: Vec<f32>,
    lines: Vec<f32>,
    line_len: usize,
    allpass: [Vec<f32>; 2],
    allpass_pos: [usize; 2],
    pos: usize,
    hf: [f32; 2],
    lf: [f32; 4],
    damped: [f32; 8],
}

impl SfxReverb {
    pub fn new(rate: u32, buffer_size: u32) -> anyhow::Result<Self> {
        // The longest input read is the late network at maximal Reflect and Reverb Delay.
        let input = frames(400.0 + LATE_TAP_S * 1000.0 * LATE_TAP_RATIO.powi(6), rate) + 3;
        let line_len = (LINE_MS * LINE_RATIO.powi(7) * rate as f32 / 1000.0).round() as usize + 2;
        let bytes = (input + 8 * line_len) * std::mem::size_of::<f32>();
        anyhow::ensure!(
            bytes <= MAX_HISTORY_BYTES,
            "AudioMixer SFX Reverb history exceeds the supported 8 MiB limit at {rate} Hz"
        );
        let allpass = |i: usize| ring(frames(EARLY_ALLPASS_MS[i], rate).max(1));
        Ok(Self {
            rate,
            c: Coefficients::default(),
            previous: Coefficients::default(),
            pending: Coefficients::default(),
            configured: false,
            transitioning: false,
            buffer_size: buffer_size.max(1) as usize,
            buffer_position: 0,
            input: ring(input)?,
            early_output: ring(frames(EARLY_MS[1][3], rate) + 1)?,
            lines: ring(8 * line_len)?,
            line_len,
            allpass: [allpass(0)?, allpass(1)?],
            allpass_pos: [0; 2],
            pos: 0,
            hf: [0.0; 2],
            lf: [0.0; 4],
            damped: [0.0; 8],
        })
    }

    /// Starts an empty shared zone return before its first active parameter update.
    /// Existing source and mixer effects keep their ordinary initial parameter application.
    pub fn initialize_zone_return(&mut self) {
        self.tune(&SfxReverbSpec {
            dry_level: -10000.0,
            room: -10000.0,
            room_hf: 0.0,
            decay_time: 1.0,
            decay_hf_ratio: 0.5,
            reflections: -10000.0,
            reflect_delay: 0.04,
            reverb: 0.0,
            reverb_delay: 0.02,
            diffusion: 100.0,
            density: 100.0,
            hf_reference: 5000.0,
            room_lf: 0.0,
            lf_reference: 250.0,
        });
        // Measured startup values, tests/projects/audioreverb lifecycle controls.
        const INITIAL_INPUT_POLE: f32 = 0.02;
        const INITIAL_LINE_POLE: f32 = 0.6;
        const INITIAL_FEEDBACK_GAIN: f32 = 0.32;
        self.c.hf = INITIAL_INPUT_POLE;
        self.c.early_gain = 0.0;
        self.c.late_gain = 0.0;
        self.c.rotation = (1.0, 1.0);
        self.c.damping.fill(INITIAL_LINE_POLE);
        self.c.decay.fill(INITIAL_FEEDBACK_GAIN);
        self.previous = self.c;
        self.transitioning = false;
        self.pending = self.c;
    }

    pub fn configuration(&self) -> (u32, u32) {
        (self.rate, self.buffer_size as u32)
    }

    /// Forgets every history; coefficients stay.
    pub fn clear(&mut self) {
        self.input.fill(0.0);
        self.early_output.fill(0.0);
        self.lines.fill(0.0);
        for line in self.allpass.iter_mut() {
            line.fill(0.0);
        }
        self.allpass_pos = [0; 2];
        self.pos = 0;
        self.hf = [0.0; 2];
        self.lf = [0.0; 4];
        self.damped = [0.0; 8];
        self.buffer_position = 0;
        self.c = self.pending;
        self.previous = self.c;
        self.transitioning = false;
    }

    /// The first parameters apply at once; later ones take effect at the next DSP-buffer
    /// boundary and crossfade through that buffer, as measured on the zone return.
    pub fn tune(&mut self, p: &SfxReverbSpec) {
        let rate = self.rate as f32;
        let mut c = Coefficients {
            dry: level(p.dry_level.clamp(-10000.0, 0.0)),
            ..Default::default()
        };
        let room = p.room.clamp(-10000.0, 0.0);
        // Room HF: two identical one-poles; their combined gain at HF Reference is the Room HF level.
        // The cosine is used as is down to the 20 Hz reference; only the discriminant is limited
        // at zero (tests/projects/audiodistortion live sfxreverb_hfref20).
        let g = level(p.room_hf.clamp(-10000.0, 0.0));
        let w = 2.0 * std::f32::consts::PI * p.hf_reference.clamp(20.0, 20000.0) / rate;
        if g < 1.0 {
            let k = 1.0 - g * w.cos();
            c.hf = (k - (k * k - (1.0 - g) * (1.0 - g)).max(0.0).sqrt()) / (1.0 - g);
            c.hf = c.hf.clamp(0.0, 0.99);
        }
        // Room LF: G (s^2 + sqrt2 s + 1) / (G s^2 + sqrt(2G) s + 1), bilinear at LF Reference.
        let g = level(p.room_lf.clamp(-10000.0, 0.0));
        if g < 1.0 {
            let k = 1.0 / (std::f32::consts::PI * p.lf_reference.clamp(20.0, 1000.0) / rate).tan();
            let s = std::f32::consts::SQRT_2;
            let digital = |n2: f32, n1: f32, n0: f32| {
                [
                    n2 * k * k + n1 * k + n0,
                    2.0 * (n0 - n2 * k * k),
                    n2 * k * k - n1 * k + n0,
                ]
            };
            let b = digital(g, g * s, g);
            let a = digital(g, g.sqrt() * s, 1.0);
            c.lf = true;
            c.lf_b = [b[0] / a[0], b[1] / a[0], b[2] / a[0]];
            c.lf_a = [a[1] / a[0], a[2] / a[0]];
        }
        // The Native rounding is float32: 0.02 * 48000 must give 960.
        let reflect = p.reflect_delay.clamp(0.0, 0.3);
        let early_base = ((reflect * rate) as usize).saturating_sub(1);
        for (taps, ms) in c.early.iter_mut().zip(EARLY_MS) {
            for (tap, ms) in taps.iter_mut().zip(ms) {
                *tap = early_base + frames(ms, self.rate);
            }
        }
        c.early_gain = std::f32::consts::FRAC_1_SQRT_2
            * 0.5
            * level(room + p.reflections.clamp(-10000.0, 1000.0));
        // Input and output delays retain their separate histories during parameter changes.
        // Their sum gives the measured static onsets at both output rates.
        let reverb_delay = p.reverb_delay.clamp(0.0, 0.1);
        let late_delay = reflect + reverb_delay;
        let mut tap = LATE_TAP_S;
        let density = p.density.clamp(0.0, 100.0) / 100.0;
        let scale = 0.1 + 0.9 * density * density * density;
        let decay = p.decay_time.clamp(0.1, 20.0);
        // Decay HF Ratio above 1 measures as 1 (no damping).
        let ratio = p.decay_hf_ratio.clamp(0.1, 1.0);
        let mut energy = 0.0;
        for k in 0..8 {
            let m = LINE_MS * rate / 1000.0 * scale * LINE_RATIO.powi(k as i32);
            c.late[k] = if k == 0 {
                ((late_delay * rate) as usize).max(1)
            } else {
                let frames = ((late_delay + tap) * rate) as usize;
                tap *= LATE_TAP_RATIO;
                frames
            };
            c.late[k] = c.late[k].saturating_sub(1);
            c.feedback[k] = (m.round() as usize).saturating_sub(1).max(1);
            let c1 = 10f32.powf(m / rate * -60.0 / decay / 20.0);
            c.decay[k] = c1;
            c.damping[k] = damping(c1, ratio, w).min(0.99);
            energy += 1.0 - c1 * c1;
        }
        c.late_gain =
            (energy / 8.0).sqrt() / 8f32.sqrt() * level(room + p.reverb.clamp(-10000.0, 2000.0));
        let angle = std::f32::consts::FRAC_PI_4 * p.diffusion.clamp(0.0, 100.0) / 100.0;
        // A three-stage butterfly uses its tangent and a separate normalization gain.
        // Both values follow the DSP-buffer transition.
        c.rotation = (angle.tan(), (angle.cos() as f64).powi(3) as f32);
        let mut output_delay = OUTPUT_TAP_SECONDS * rate;
        for delay in &mut c.fan_out {
            *delay = ((output_delay as f64 + 0.5) as usize).saturating_sub(1);
            output_delay *= OUTPUT_TAP_RATIO;
        }
        self.pending = c;
        if !self.configured {
            self.c = c;
            self.previous = self.c;
            self.transitioning = false;
            self.configured = true;
        }
    }

    /// Applies parameters at once. Mixer and AudioReverbFilter SFX Reverb live changes and
    /// unmeasured zone profiles keep this path: the DSP-buffer crossfade of [`Self::tune`] is
    /// measured only on the zone return (tests/projects/audioreverb live controls).
    pub fn tune_immediate(&mut self, p: &SfxReverbSpec) {
        self.tune(p);
        self.c = self.pending;
        self.previous = self.c;
        self.transitioning = false;
    }

    fn input_at(&self, delay: usize) -> f32 {
        let n = self.input.len();
        self.input[(self.pos + n - delay % n) % n]
    }

    fn line_at(&self, line: usize, delay: usize) -> f32 {
        let n = self.line_len;
        self.lines[line * n + (self.pos % n + n - delay) % n]
    }

    fn input_tap(&self, old: usize, new: usize, phase: usize, size: usize) -> f32 {
        if old == new {
            self.input_at(new)
        } else {
            blend(self.input_at(old), self.input_at(new), phase, size)
        }
    }

    /// Processes interleaved stereo in place: dry level plus early and late reverb.
    pub fn process(&mut self, samples: &mut [f32]) {
        for stereo in samples.chunks_exact_mut(2) {
            if self.buffer_position == 0 {
                self.previous = self.c;
                self.c = self.pending;
                self.transitioning = self.previous != self.c;
            }
            let phase = self.buffer_position;
            let size = self.buffer_size;
            let c = if self.transitioning {
                self.previous.at(self.c, phase, size)
            } else {
                self.c
            };

            let mut mono = stereo[0] + stereo[1];
            if c.hf != 0.0 {
                self.hf[0] = (self.hf[0] - mono) * c.hf + mono;
                self.hf[1] = (self.hf[1] - self.hf[0]) * c.hf + self.hf[0];
                mono = self.hf[1];
            } else {
                self.hf.fill(mono);
            }
            if c.lf {
                let [x1, x2, y1, y2] = self.lf;
                let y = c.lf_b[0] * mono + c.lf_b[1] * x1 + c.lf_b[2] * x2
                    - c.lf_a[0] * y1
                    - c.lf_a[1] * y2;
                self.lf = [mono, x1, y, y1];
                mono = y;
            }
            let n = self.input.len();
            self.input[self.pos % n] = mono;
            let mut out = [0.0f32; 2];
            let mut x = self.input_tap(self.previous.early[0][0], c.early[0][0], phase, size)
                * c.early_gain;
            for (line, pos) in self.allpass.iter_mut().zip(&mut self.allpass_pos) {
                let delayed = line[*pos];
                let v = x + EARLY_ALLPASS_GAIN * delayed;
                x = delayed - EARLY_ALLPASS_GAIN * v;
                line[*pos] = v;
                *pos = (*pos + 1) % line.len();
            }
            let early_len = self.early_output.len();
            self.early_output[self.pos % early_len] = x;
            for (ch, taps) in c.early.iter().enumerate() {
                for delay in taps {
                    let offset = delay - c.early[0][0];
                    out[ch] += self.early_output[(self.pos + early_len - offset) % early_len];
                }
            }
            let mut y = [0.0f32; 8];
            for (k, y) in y.iter_mut().enumerate() {
                let input = self.input_tap(self.previous.late[k], c.late[k], phase, size);
                let line = (k + 1) % 8;
                let feedback = if self.previous.feedback[k] == c.feedback[k]
                    && self.previous.decay[k] == c.decay[k]
                {
                    c.decay[k] * self.line_at(line, c.feedback[k])
                } else {
                    blend(
                        self.previous.decay[k] * self.line_at(line, self.previous.feedback[k]),
                        c.decay[k] * self.line_at(line, c.feedback[k]),
                        phase,
                        size,
                    )
                };
                let v = (input * c.late_gain + feedback) * c.rotation.1;
                self.damped[k] = (self.damped[k] - v) * c.damping[k] + v;
                *y = self.damped[k];
            }
            // Rotate the eight feedback lines through three unnormalized butterfly stages.
            let (rotation, _) = c.rotation;
            for bit in [1, 2, 4] {
                for i in (0..8).filter(|i| i & bit == 0) {
                    let (a, b) = (y[i], y[i | bit]);
                    y[i] = a + rotation * b;
                    y[i | bit] = b - rotation * a;
                }
            }
            let len = self.line_len;
            let at = self.pos % len;
            for (u, value) in y.iter().enumerate() {
                self.lines[u * len + at] = *value;
            }
            let mut late = [0.0f32; 2];
            for (j, &delay) in c.fan_out.iter().enumerate() {
                late[j % 2] += self.line_at((j + 1) % 8, delay);
            }
            for ch in 0..2 {
                stereo[ch] = c.dry * stereo[ch] + out[ch] + late[ch];
            }
            self.pos = self.pos.wrapping_add(1);
            self.buffer_position += 1;
            if self.buffer_position == self.buffer_size {
                self.buffer_position = 0;
            }
        }
    }
}

/// DMO-style high-frequency damping pole for a line whose broadband gain per pass is `c1`.
fn damping(c1: f32, ratio: f32, w: f32) -> f32 {
    let c21 = (c1.powf(2.0 - 2.0 / ratio) - 1.0) / (1.0 - w.cos());
    // A short decay with a low ratio overflows the power: the pole is then at its limit, as it
    // already is for every finite c21 large enough to overflow c22 * c22 (Native PaddedCell,
    // tests/projects/audioreverb).
    if c21 == f32::INFINITY {
        return 1.0;
    }
    if c21 == 0.0 || !c21.is_finite() {
        return 0.0;
    }
    let c22 = -2.0 * c21 - 2.0;
    let c23 = (c22 * c22 - c21 * c21 * 4.0).max(0.0).sqrt();
    let mut c2 = (c23 - c22) / (c21 + c21);
    if c2.abs() > 1.0 {
        c2 = (-c22 - c23) / (c21 + c21);
    }
    if c2.is_finite() { c2 } else { 0.0 }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::audio::effects::{Effect, EffectSpec};

    #[test]
    fn room_hf_at_the_lowest_references_reaches_the_pole_limit() {
        // Unity 6000.0.71f1, tests/projects/audiodistortion live sfxreverb_hfref20/50: the
        // cosine is not limited, so very low references give the steepest allowed pole.
        for (room_hf, reference) in [(-100.0, 20.0), (-1000.0, 50.0)] {
            let mut reverb = SfxReverb::new(48000, 1024).unwrap();
            reverb.tune(&spec(&[("room_hf", room_hf), ("hf_reference", reference)]));
            assert_eq!(reverb.c.hf, 0.99, "{room_hf} {reference}");
        }
        let mut reverb = SfxReverb::new(48000, 1024).unwrap();
        reverb.tune(&spec(&[("room_hf", -1200.0), ("hf_reference", 5000.0)]));
        assert!(reverb.c.hf > 0.0 && reverb.c.hf < 0.99);
    }

    fn spec(overrides: &[(&str, f32)]) -> SfxReverbSpec {
        let mut p = SfxReverbSpec {
            dry_level: 0.0,
            room: -10000.0,
            room_hf: 0.0,
            decay_time: 1.0,
            decay_hf_ratio: 0.5,
            reflections: -10000.0,
            reflect_delay: 0.02,
            reverb: 0.0,
            reverb_delay: 0.04,
            diffusion: 100.0,
            density: 100.0,
            hf_reference: 5000.0,
            room_lf: 0.0,
            lf_reference: 250.0,
        };
        for (name, value) in overrides {
            let field = match *name {
                "dry" => &mut p.dry_level,
                "room" => &mut p.room,
                "reflections" => &mut p.reflections,
                "reverb" => &mut p.reverb,
                "decay" => &mut p.decay_time,
                "ratio" => &mut p.decay_hf_ratio,
                "diffusion" => &mut p.diffusion,
                "room_hf" => &mut p.room_hf,
                "hf_reference" => &mut p.hf_reference,
                _ => panic!("{name}"),
            };
            *field = *value;
        }
        p
    }

    /// Response to a unit left impulse at frame 0.
    fn impulse(rate: u32, p: &SfxReverbSpec, frames: usize) -> Vec<f32> {
        let mut reverb = SfxReverb::new(rate, 1024).unwrap();
        reverb.tune(p);
        let mut x = vec![0.0; frames * 2];
        x[0] = 1.0;
        reverb.process(&mut x);
        x
    }

    fn close(a: f32, b: f32) {
        assert!((a - b).abs() <= 2e-6, "{a} != {b}");
    }

    #[test]
    fn early_taps_match_the_native_impulse_response_at_both_rates() {
        let p = spec(&[
            ("room", 0.0),
            ("reflections", 0.0),
            ("reverb", -10000.0),
            ("dry", -10000.0),
        ]);
        // Tap gain 1/(2 sqrt 2) times the allpass chain's first sample 0.63^2.
        for (rate, left, right) in [(48000, 959, 1198), (44100, 881, 1100)] {
            let y = impulse(rate, &p, 2000);
            assert!(y[..2 * left].iter().skip(2).all(|v| v.abs() < 1e-6));
            close(y[2 * left], 0.140_325_34);
            close(y[2 * right + 1], 0.140_325_34);
        }
        // The allpass chain at 48 kHz: loops of 282 and 368 frames.
        let y = impulse(48000, &p, 2000);
        close(y[2 * (959 + 282)], -0.134_333_67);
        close(y[2 * (959 + 368)], -0.134_333_67);
    }

    #[test]
    fn late_onset_gain_and_diffusion_split_match_native() {
        let flat = [
            ("room", 0.0),
            ("dry", -10000.0),
            ("ratio", 1.0),
            ("decay", 20.0),
        ];
        let y = impulse(
            48000,
            &spec(&[flat.as_slice(), &[("diffusion", 0.0)]].concat()),
            4000,
        );
        close(y[2 * 3847], 0.124_290_4);
        close(y[2 * 3946 + 1], 0.124_290_4);
        let y = impulse(
            48000,
            &spec(&[
                ("room", 0.0),
                ("dry", -10000.0),
                ("decay", 1.0),
                ("diffusion", 0.0),
                ("ratio", 1.0),
            ]),
            4000,
        );
        close(y[2 * 3847], 0.324_532);
        // Diffusion 100 splits each first pass into eight taps of 1/sqrt(8).
        let y = impulse(48000, &spec(&flat), 4000);
        close(y[2 * 2950], -0.043_943_3);
        close(y[2 * 2984 + 1], -0.043_943_3);
        close(y[2 * 3847], 0.043_943_3);
    }

    /// Native onsets (tests/projects/audioreverb and mixerreverb). A Generic zone at weight 0.8
    /// scales its delays to a sum of 691.2 frames at 48 kHz.
    #[test]
    fn late_lines_truncate_the_delay_and_tap_sum() {
        let cases: [(u32, f32, f32, [usize; 8]); 3] = [
            (
                48000,
                0.007 * 0.8,
                0.011 * 0.8,
                [761, 1658, 1919, 2254, 2688, 3246, 3967, 4897],
            ),
            (
                48000,
                0.007,
                0.011,
                [933, 1831, 2091, 2427, 2860, 3419, 4140, 5070],
            ),
            (
                44100,
                0.0,
                0.011,
                [549, 1373, 1612, 1921, 2319, 2832, 3495, 4349],
            ),
        ];
        for (rate, reflect, delay, late) in cases {
            let mut reverb = SfxReverb::new(rate, 1024).unwrap();
            let mut p = spec(&[]);
            p.reflect_delay = reflect;
            p.reverb_delay = delay;
            reverb.tune(&p);
            assert_eq!(
                reverb.c.late.map(|tap| tap + reverb.c.fan_out[0]),
                late,
                "{rate} {reflect} {delay}"
            );
        }
    }

    #[test]
    fn immediate_parameter_application_changes_a_running_early_return_without_a_ramp() {
        let mut parameters = spec(&[]);
        parameters.dry_level = -10000.0;
        parameters.room = 0.0;
        parameters.room_hf = 0.0;
        parameters.room_lf = 0.0;
        parameters.reflections = 0.0;
        parameters.reflect_delay = 0.0;
        parameters.reverb = -10000.0;
        let mut reverb = SfxReverb::new(48000, 32).unwrap();
        reverb.tune(&parameters);
        let mut before = [0.25, 0.25];
        reverb.process(&mut before);
        parameters.room = -600.0;
        reverb.tune_immediate(&parameters);
        let mut after = [0.25, 0.25];
        reverb.process(&mut after);
        let dry = 0.25 * level(parameters.dry_level);
        assert!(((after[0] - dry) / (before[0] - dry) - level(-600.0)).abs() < 1e-6);
    }

    #[test]
    fn a_flat_room_hf_keeps_current_input_memory_for_the_next_parameter_transition() {
        let mut parameters = spec(&[]);
        parameters.room_hf = -1000.0;
        let mut reverb = SfxReverb::new(48000, 32).unwrap();
        reverb.tune(&parameters);
        reverb.process(&mut vec![0.25; 128]);
        parameters.room_hf = 0.0;
        reverb.tune(&parameters);
        reverb.process(&mut vec![0.5; 128]);
        reverb.process(&mut vec![0.125; 128]);
        assert_eq!(reverb.hf, [0.25, 0.25]);
        parameters.room_hf = -1000.0;
        reverb.tune(&parameters);
        reverb.process(&mut [0.0; 4]);
        assert_eq!(reverb.hf, [0.0, 0.0]);
    }

    #[test]
    fn parameter_transitions_keep_histories_and_dsp_coordinates_across_device_periods() {
        for (rate, buffer) in [(48000, 1024), (32000, 256)] {
            let mut active = spec(&[("room", -1000.0), ("reflections", -2602.0)]);
            let off = spec(&[
                ("room", -10000.0),
                ("reflections", -10000.0),
                ("diffusion", 0.0),
                ("decay", 0.1),
            ]);
            let mut whole = SfxReverb::new(rate, buffer).unwrap();
            let mut split = SfxReverb::new(rate, buffer).unwrap();
            whole.tune(&active);
            split.tune(&active);
            let input: Vec<f32> = (0..buffer as usize * 16 * 2)
                .map(|i| ((i * 37 % 997) as f32 - 498.0) / 2048.0)
                .collect();
            for (block, input) in input.chunks(buffer as usize * 2).enumerate() {
                if block == 8 {
                    whole.tune(&off);
                    split.tune(&off);
                }
                if block == 12 {
                    active.reflect_delay = 0.0;
                    active.density = 23.0;
                    whole.tune(&active);
                    split.tune(&active);
                }
                let mut a = input.to_vec();
                let mut b = a.clone();
                whole.process(&mut a);
                for period in b.chunks_mut(74) {
                    split.process(period);
                }
                assert_eq!(a, b, "{rate} {buffer} block {block}");
                if block == 9 {
                    assert!(a.iter().zip(input).any(|(a, b)| (a - b).abs() > 0.0001));
                }
            }
        }
    }

    #[test]
    fn dry_level_and_wet_mixing_follow_the_measured_slot() {
        let mut reverb = SfxReverb::new(48000, 1024).unwrap();
        reverb.tune(&spec(&[("dry", -600.0)]));
        let mut x = vec![0.25, -0.125];
        reverb.process(&mut x);
        close(x[0], 0.125_296_8);
        // Wet mixing: wet * effect + (1 - wet) * input, ramped in over 64 frames.
        let json = serde_json::json!({ "id": "r", "kind": "SFX Reverb", "mixLevel": 0.5,
            "sfx": { "dryLevel": -10000.0, "room": -10000.0, "roomHf": 0.0, "decayTime": 1.0,
                "decayHfRatio": 0.5, "reflections": -10000.0, "reflectDelay": 0.02, "reverb": 0.0,
                "reverbDelay": 0.04, "diffusion": 100.0, "density": 100.0, "hfReference": 5000.0,
                "roomLf": 0.0, "lfReference": 250.0 } });
        let mut effect = Effect::new(
            serde_json::from_value::<EffectSpec>(json.clone()).unwrap(),
            48000,
            1024,
        )
        .unwrap();
        let mut x = vec![1.0f32; 2 * 128];
        effect.process(&mut x, false);
        // The effect's own dry level is -100 dB (1e-5).
        close(x[2 * 32], 32.0 / 64.0 * (0.5 * 1e-5 + 0.5));
        close(x[2 * 100], 0.5 * 1e-5 + 0.5);
        let mut lowpass: EffectSpec = serde_json::from_value(json).unwrap();
        lowpass.kind = "Lowpass".into();
        assert!(lowpass.validate(48000, 1024).is_err());
    }
}
