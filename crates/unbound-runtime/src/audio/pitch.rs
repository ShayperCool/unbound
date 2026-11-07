//! Unity 6000.0.71f1 default Pitch Shifter profile. See tests/projects/audiopitch.
use serde::Deserialize;
use std::f32::consts::{PI, TAU};

#[derive(Debug, Clone, PartialEq, Deserialize)]
pub struct PitchSpec {
    pub pitch: f32,
}
impl PitchSpec {
    pub fn validate(&self) -> anyhow::Result<()> {
        anyhow::ensure!(
            self.pitch.is_finite() && (0.5..=2.0).contains(&self.pitch),
            "AudioMixer Pitch Shifter has invalid Pitch"
        );
        Ok(())
    }
}
const N: usize = 1024;
const HOP: usize = 256;
const LATENCY: usize = N - HOP;
struct Channel {
    input: Vec<f32>,
    output: Vec<f32>,
    overlap: Vec<f32>,
    last: Vec<f32>,
    phase: Vec<f32>,
    position: usize,
}
impl Default for Channel {
    fn default() -> Self {
        Self {
            input: vec![0.0; N],
            output: vec![0.0; HOP],
            overlap: vec![0.0; N],
            last: vec![0.0; N / 2 + 1],
            phase: vec![0.0; N / 2 + 1],
            position: LATENCY,
        }
    }
}
pub struct PitchShifter {
    channels: [Channel; 2],
    window: Vec<f32>,
    cosine: Vec<f32>,
    twiddle: Vec<f32>,
    work: Vec<f32>,
    magnitudes: Vec<f32>,
    frequencies: Vec<f32>,
}
impl PitchShifter {
    pub fn validate_configuration(rate: u32, buffer: u32) -> anyhow::Result<()> {
        anyhow::ensure!(
            [48000, 44100].contains(&rate) && buffer == 1024,
            "AudioMixer Pitch Shifter is measured only at 48000/44100 Hz with 1024-frame stereo output"
        );
        Ok(())
    }
    pub fn new() -> Self {
        let cosine: Vec<_> = (0..8192)
            .map(|i| (i as f32 * std::f32::consts::FRAC_PI_2 * (1.0 / 8192.0)).cos())
            .collect();
        let mut twiddle = vec![0.0; N / 2];
        twiddle[0] = 1.0;
        twiddle[N / 4] = cosine[4096];
        twiddle[N / 4 + 1] = cosine[4096];
        for j in (2..N / 4).step_by(2) {
            let turns = j as f32 / (2 * N) as f32;
            let c = lookup(&cosine, turns);
            let s = lookup(&cosine, turns - 0.25);
            twiddle[j] = c;
            twiddle[j + 1] = s;
            twiddle[N / 2 - j] = s;
            twiddle[N / 2 - j + 1] = c;
        }
        reverse(&mut twiddle);
        Self {
            channels: Default::default(),
            window: (0..N)
                .map(|i| {
                    ((0.5 - 0.5 * (std::f64::consts::TAU * i as f64 / N as f64).cos()) as f32)
                        / 32.0
                })
                .collect(),
            cosine,
            twiddle,
            work: vec![0.0; 2 * N],
            magnitudes: vec![0.0; N / 2 + 1],
            frequencies: vec![0.0; N / 2 + 1],
        }
    }
    pub fn clear(&mut self) {
        self.channels = Default::default();
    }
    pub fn process(&mut self, samples: &mut [f32], spec: &PitchSpec, rate: u32) {
        let bin = rate as f32 / N as f32;
        let expected = HOP as f32 * TAU / N as f32;
        let deviation = bin * 4.0 / TAU;
        for (channel, state) in self.channels.iter_mut().enumerate() {
            for frame in samples.chunks_exact_mut(2) {
                state.input[state.position] = frame[channel];
                frame[channel] = state.output[state.position - LATENCY];
                state.position += 1;
                if state.position < N {
                    continue;
                }
                state.position = LATENCY;
                for i in 0..N {
                    self.work[2 * i] = state.input[i] * self.window[i];
                    self.work[2 * i + 1] = 0.0;
                }
                transform(&mut self.work, &self.twiddle, true);
                self.magnitudes.fill(0.0);
                self.frequencies.fill(0.0);
                for k in 0..=N / 2 {
                    let re = self.work[2 * k];
                    let im = self.work[2 * k + 1];
                    let phase = if im == 0.0 {
                        0.0
                    } else if re == 0.0 {
                        if im > 0.0 { PI / 2.0 } else { -PI / 2.0 }
                    } else {
                        im.atan2(re)
                    };
                    let magnitude = 2.0 * (re * re + im * im + 0.0).sqrt();
                    let mut delta = phase - (state.last[k] + k as f32 * expected);
                    state.last[k] = phase;
                    let mut q = (delta * std::f32::consts::FRAC_1_PI) as i32;
                    q += if q >= 0 { q & 1 } else { -(q & 1) };
                    delta -= q as f32 * PI;
                    let frequency = k as f32 * bin + delta * deviation;
                    let index = (k as f32 * spec.pitch + 0.5) as usize;
                    if index <= N / 2 {
                        self.magnitudes[index] += magnitude;
                        self.frequencies[index] = frequency * spec.pitch;
                    }
                }
                self.work.fill(0.0);
                for k in 0..=N / 2 {
                    let delta =
                        (self.frequencies[k] / bin - k as f32) * (TAU / 4.0) + k as f32 * expected;
                    state.phase[k] += delta;
                    if state.phase[k] > PI || state.phase[k] < -PI {
                        state.phase[k] += (state.phase[k] * (1.0 / TAU)) as i32 as f32 * -TAU;
                    }
                    let turns = state.phase[k] * (1.0 / TAU);
                    self.work[2 * k] = self.magnitudes[k] * lookup(&self.cosine, turns);
                    self.work[2 * k + 1] = self.magnitudes[k] * lookup(&self.cosine, turns - 0.25);
                }
                transform(&mut self.work, &self.twiddle, false);
                for i in 0..N {
                    state.overlap[i] += self.window[i] * self.work[2 * i];
                }
                state.output.copy_from_slice(&state.overlap[..HOP]);
                state.overlap.copy_within(HOP..N, 0);
                state.overlap[LATENCY..].fill(0.0);
                state.input.copy_within(HOP..N, 0);
            }
        }
    }
}
// The measured quarter-wave lookup uses reflected endpoint indices, without interpolation.
fn lookup(table: &[f32], turns: f32) -> f32 {
    let index = ((turns * 32768.0) as i32).unsigned_abs() as usize & 32767;
    match index / 8192 {
        0 => table[index],
        1 => -table[16383 - index],
        2 => -table[index - 16384],
        _ => table[32767 - index],
    }
}
fn reverse(a: &mut [f32]) {
    let count = a.len() / 2;
    let bits = count.trailing_zeros();
    for i in 0..count {
        let j = i.reverse_bits() >> (usize::BITS - bits);
        if i < j {
            a.swap(2 * i, 2 * j);
            a.swap(2 * i + 1, 2 * j + 1);
        }
    }
}
// Radix-4/2 butterflies, following Ooura fft4g's operation order in single precision.
// Copyright Takuya OOURA, 1996-2001. https://www.kurims.kyoto-u.ac.jp/~ooura/fft.html
// Permission: use, copy, modify and distribute for any purpose, including commercial, without fee.
fn transform(a: &mut [f32], w: &[f32], negative: bool) {
    reverse(a);
    let n = a.len();
    let mut l = 2;
    while 4 * l < n {
        let m = 4 * l;
        for j in (0..l).step_by(2) {
            butterfly(a, j, l, None);
        }
        for j in (m..m + l).step_by(2) {
            diagonal(a, j, l, w[2]);
        }
        for (iteration, k) in (2 * m..n).step_by(2 * m).enumerate() {
            let k1 = 2 * (iteration + 1);
            let k2 = 2 * k1;
            let (r2, i2) = (w[k1], w[k1 + 1]);
            let (r1, i1) = (w[k2], w[k2 + 1]);
            let r3 = r1 - 2.0 * i2 * i1;
            let i3 = 2.0 * i2 * r1 - i1;
            for j in (k..k + l).step_by(2) {
                butterfly(a, j, l, Some([r1, i1, r2, i2, r3, i3]));
            }
            let (r1, i1) = (w[k2 + 2], w[k2 + 3]);
            let r3 = r1 - 2.0 * r2 * i1;
            let i3 = 2.0 * r2 * r1 - i1;
            for j in (k + m..k + m + l).step_by(2) {
                butterfly(a, j, l, Some([r1, i1, -i2, r2, r3, i3]));
            }
        }
        l *= 4;
    }
    if 4 * l == n {
        for j in (0..l).step_by(2) {
            if negative {
                final_negative(a, j, l);
            } else {
                butterfly(a, j, l, None);
            }
        }
    } else {
        for j in (0..l).step_by(2) {
            let r = a[j] - a[j + l];
            let i = a[j + 1] - a[j + l + 1];
            a[j] += a[j + l];
            if negative {
                a[j + 1] = -a[j + 1] - a[j + l + 1];
                a[j + l + 1] = -i;
            } else {
                a[j + 1] += a[j + l + 1];
                a[j + l + 1] = i;
            }
            a[j + l] = r;
        }
    }
}
fn butterfly(a: &mut [f32], j: usize, l: usize, twiddle: Option<[f32; 6]>) {
    let j1 = j + l;
    let j2 = j1 + l;
    let j3 = j2 + l;
    let (r0, i0) = (a[j] + a[j1], a[j + 1] + a[j1 + 1]);
    let (r1, i1) = (a[j] - a[j1], a[j + 1] - a[j1 + 1]);
    let (r2, i2) = (a[j2] + a[j3], a[j2 + 1] + a[j3 + 1]);
    let (r3, i3) = (a[j2] - a[j3], a[j2 + 1] - a[j3 + 1]);
    a[j] = r0 + r2;
    a[j + 1] = i0 + i2;
    let values = [(r1 - i3, i1 + r3), (r0 - r2, i0 - i2), (r1 + i3, i1 - r3)];
    for (k, (r, i)) in values.into_iter().enumerate() {
        let index = j + (k + 1) * l;
        let (r, i) = if let Some(t) = twiddle {
            (
                t[2 * k] * r - t[2 * k + 1] * i,
                t[2 * k] * i + t[2 * k + 1] * r,
            )
        } else {
            (r, i)
        };
        a[index] = r;
        a[index + 1] = i;
    }
}

fn diagonal(a: &mut [f32], j: usize, l: usize, w: f32) {
    let j1 = j + l;
    let j2 = j1 + l;
    let j3 = j2 + l;
    let (r0, i0) = (a[j] + a[j1], a[j + 1] + a[j1 + 1]);
    let (r1, i1) = (a[j] - a[j1], a[j + 1] - a[j1 + 1]);
    let (r2, i2) = (a[j2] + a[j3], a[j2 + 1] + a[j3 + 1]);
    let (r3, i3) = (a[j2] - a[j3], a[j2 + 1] - a[j3 + 1]);
    a[j] = r0 + r2;
    a[j + 1] = i0 + i2;
    a[j2] = i2 - i0;
    a[j2 + 1] = r0 - r2;
    let (r, i) = (r1 - i3, i1 + r3);
    a[j1] = w * (r - i);
    a[j1 + 1] = w * (r + i);
    let (r, i) = (i3 + r1, r3 - i1);
    a[j3] = w * (i - r);
    a[j3 + 1] = w * (i + r);
}
fn final_negative(a: &mut [f32], j: usize, l: usize) {
    let j1 = j + l;
    let j2 = j1 + l;
    let j3 = j2 + l;
    let (r0, i0) = (a[j] + a[j1], -a[j + 1] - a[j1 + 1]);
    let (r1, i1) = (a[j] - a[j1], -a[j + 1] + a[j1 + 1]);
    let (r2, i2) = (a[j2] + a[j3], a[j2 + 1] + a[j3 + 1]);
    let (r3, i3) = (a[j2] - a[j3], a[j2 + 1] - a[j3 + 1]);
    a[j] = r0 + r2;
    a[j + 1] = i0 - i2;
    a[j2] = r0 - r2;
    a[j2 + 1] = i0 + i2;
    a[j1] = r1 - i3;
    a[j1 + 1] = i1 - r3;
    a[j3] = r1 + i3;
    a[j3 + 1] = i1 + r3;
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn reset_and_packet_boundaries_preserve_native_phase_history() {
        let spec = PitchSpec { pitch: 0.75 };
        let signal: Vec<f32> = (0..16384)
            .map(|i| if i % 1531 == 0 { 0.5 } else { 0.0 })
            .collect();
        let mut whole = signal.clone();
        let mut effect = PitchShifter::new();
        effect.process(&mut whole, &spec, 44100);
        let mut split = signal.clone();
        let mut effect = PitchShifter::new();
        for chunk in split.chunks_mut(480 * 2) {
            effect.process(chunk, &spec, 44100);
        }
        assert_eq!(whole, split);
        effect.clear();
        let mut reset = signal;
        effect.process(&mut reset, &spec, 44100);
        assert_eq!(whole, reset);
    }
    #[test]
    fn native_unity_pitch_has_a_delayed_nonidentity_impulse() {
        let mut samples = vec![0.0; 8192];
        samples[2048 * 2] = 0.5;
        PitchShifter::new().process(&mut samples, &PitchSpec { pitch: 1.0 }, 48000);
        assert_eq!(samples[2048 * 2], 0.0);
        assert_eq!(samples[3072 * 2], 0.12646484);
        assert!(
            samples
                .iter()
                .enumerate()
                .all(|(i, x)| i % 2 == 0 || *x == 0.0)
        );
    }
}
