//! Unity 6000.0.71f1 Pitch Shifter profile. See tests/projects/audiopitch.
//! The output layouts it is measured for are checked by the mixer
//! ([`super::effects::EffectKind::layout_limited`]). The kernel runs each channel
//! independently; the engine's group graph carries the front pair of every layout.
use super::wavetable;
use serde::Deserialize;
use std::f32::consts::{PI, TAU};

#[derive(Debug, Clone, PartialEq, Deserialize)]
pub struct PitchSpec {
    pub pitch: f32,
    #[serde(default = "default_fft_size")]
    pub fft_size: usize,
    /// The DSP value of `Max channels` (0: the output layout's channel count).
    #[serde(default)]
    pub max_channels: u32,
}
impl PitchSpec {
    pub fn validate(&self) -> anyhow::Result<()> {
        anyhow::ensure!(
            self.pitch.is_finite() && (0.5..=2.0).contains(&self.pitch),
            "AudioMixer Pitch Shifter has invalid Pitch"
        );
        anyhow::ensure!(
            matches!(self.fft_size, 256 | 512 | 1024 | 2048 | 4096),
            "AudioMixer Pitch Shifter has invalid FFT size"
        );
        anyhow::ensure!(
            self.max_channels <= MAX_CHANNEL_LIMIT,
            "AudioMixer Pitch Shifter has invalid Max channels"
        );
        Ok(())
    }
}
fn default_fft_size() -> usize {
    1024
}
// Unity 6000.0.71f1 uses four windows regardless of the exposed Overlap value.
// tests/projects/audiopitch overlap controls cover the parameter range.
const OVERLAP: usize = 4;
// Window-table spacing measured by the FFT-size controls in tests/projects/audiopitch.
const WINDOW_INTERVALS: usize = 1024;
/// The largest `Max channels` value, from the effect's parameter range (0..16).
pub const MAX_CHANNEL_LIMIT: u32 = 16;
struct Channel {
    input: Vec<f32>,
    output: Vec<f32>,
    overlap: Vec<f32>,
    last: Vec<f32>,
    phase: Vec<f32>,
    position: usize,
}
impl Channel {
    fn new(n: usize) -> Self {
        let hop = n / OVERLAP;
        let latency = n - hop;
        Self {
            input: vec![0.0; n],
            output: vec![0.0; hop],
            overlap: vec![0.0; n],
            last: vec![0.0; n / 2 + 1],
            phase: vec![0.0; n / 2 + 1],
            position: latency,
        }
    }
}
impl Channel {
    /// Reset the FIFO and phase history, keeping the allocations.
    fn clear(&mut self, latency: usize) {
        for buffer in [
            &mut self.input,
            &mut self.output,
            &mut self.overlap,
            &mut self.last,
            &mut self.phase,
        ] {
            buffer.fill(0.0);
        }
        self.position = latency;
    }
}
pub struct PitchShifter {
    channels: Vec<Channel>,
    /// How many channels the effect processes: `Max channels`, or the layout's count when that
    /// is zero. A group carrying more channels passes through unchanged (Unity 6000.0.71f1,
    /// tests/projects/audiopitch `maxch_*` and layout controls).
    limit: usize,
    fft_size: usize,
    pitch: f32,
    window: Vec<f32>,
    twiddle: Vec<f32>,
    work: Vec<f32>,
    magnitudes: Vec<f32>,
    frequencies: Vec<f32>,
}
impl PitchShifter {
    pub fn new(n: usize, channels: usize) -> Self {
        let mut result = Self {
            channels: (0..channels).map(|_| Channel::new(n)).collect(),
            limit: channels,
            fft_size: 0,
            pitch: 1.0,
            window: Vec::new(),
            twiddle: Vec::new(),
            work: Vec::new(),
            magnitudes: Vec::new(),
            frequencies: Vec::new(),
        };
        result.prepare(n);
        result
    }
    fn prepare(&mut self, n: usize) {
        if self.fft_size == n {
            return;
        }
        // cos(pi / 4), the table's middle entry.
        let diagonal = wavetable::table()[wavetable::QUARTER / 2];
        let mut twiddle = vec![0.0; n / 2];
        twiddle[0] = 1.0;
        twiddle[n / 4] = diagonal;
        twiddle[n / 4 + 1] = diagonal;
        for j in (2..n / 4).step_by(2) {
            let turns = j as f32 / (2 * n) as f32;
            let c = wavetable::cosine(turns);
            let s = wavetable::cosine(turns - 0.25);
            twiddle[j] = c;
            twiddle[j + 1] = s;
            twiddle[n / 2 - j] = s;
            twiddle[n / 2 - j + 1] = c;
        }
        reverse(&mut twiddle);
        self.fft_size = n;
        self.twiddle = twiddle;
        self.window = (0..n)
            .map(|i| {
                // A reflected 1024-point Hann curve, linearly interpolated at larger FFT sizes.
                // Scale both analysis and synthesis by sqrt(FFT size).
                let x = (i * WINDOW_INTERVALS) as f32 / n as f32;
                let descending = x > (WINDOW_INTERVALS / 2) as f32;
                let position = if descending {
                    WINDOW_INTERVALS as f32 - x
                } else {
                    x
                };
                let index = position as usize;
                let hann = |j: usize| {
                    (0.5 - 0.5 * (std::f64::consts::TAU * j as f64 / WINDOW_INTERVALS as f64).cos())
                        as f32
                };
                let a = hann(index);
                // Fractional positions keep their positive remainder on the descending half;
                // the adjacent table entry follows that half's backward direction.
                let next = if descending {
                    index.checked_sub(1)
                } else {
                    Some(index + 1)
                };
                let value = if position == index as f32 {
                    a
                } else if let Some(next) = next {
                    a + (hann(next) - a) * (position - index as f32)
                } else {
                    a
                };
                value / (n as f32).sqrt()
            })
            .collect();
        self.work.resize(2 * n, 0.0);
        self.magnitudes.resize(n / 2 + 1, 0.0);
        self.frequencies.resize(n / 2 + 1, 0.0);
    }
    pub fn fft_size(&self) -> usize {
        self.fft_size
    }
    pub fn clear(&mut self) {
        let latency = self.fft_size - self.fft_size / OVERLAP;
        for state in &mut self.channels {
            state.clear(latency);
        }
    }
    /// Applies the channel limit for an output layout of `layout` channels. A changed count
    /// restarts every history, as a reallocation would; an unchanged one keeps them (the
    /// `maxch_live_*` controls).
    pub(crate) fn set_channel_limit(&mut self, layout: usize, max_channels: u32) {
        let limit = if max_channels == 0 {
            layout
        } else {
            max_channels as usize
        };
        if limit != self.limit {
            self.clear();
            self.limit = limit;
        }
    }
    pub(crate) fn set_pitch(&mut self, pitch: f32) {
        if self.pitch != pitch {
            // Live ratios restart phase tracking while keeping the FIFO and overlap audio.
            // Unity 6000.0.71f1, tests/projects/audiodistortion live Pitch controls.
            for state in &mut self.channels {
                state.last.fill(0.0);
                state.phase.fill(0.0);
            }
            self.pitch = pitch;
        }
    }
    /// Processes interleaved `samples` of a group carrying `layout` channels.
    pub fn process(&mut self, samples: &mut [f32], spec: &PitchSpec, rate: u32, layout: usize) {
        if layout > self.limit {
            // More input channels than the effect handles: the input passes unchanged and
            // the histories stay as they are.
            return;
        }
        self.set_pitch(spec.pitch);
        let n = self.fft_size;
        let hop = n / OVERLAP;
        let latency = n - hop;
        let channels = self.channels.len();
        let bin = rate as f32 / n as f32;
        let expected = hop as f32 * TAU / n as f32;
        let deviation = bin * OVERLAP as f32 / TAU;
        for (channel, state) in self.channels.iter_mut().enumerate() {
            for frame in samples.chunks_exact_mut(channels) {
                state.input[state.position] = frame[channel];
                frame[channel] = state.output[state.position - latency];
                state.position += 1;
                if state.position < n {
                    continue;
                }
                state.position = latency;
                for i in 0..n {
                    self.work[2 * i] = state.input[i] * self.window[i];
                    self.work[2 * i + 1] = 0.0;
                }
                transform(&mut self.work, &self.twiddle, true);
                self.magnitudes.fill(0.0);
                self.frequencies.fill(0.0);
                for k in 0..=n / 2 {
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
                    if index <= n / 2 {
                        self.magnitudes[index] += magnitude;
                        self.frequencies[index] = frequency * spec.pitch;
                    }
                }
                self.work.fill(0.0);
                for k in 0..=n / 2 {
                    let delta = (self.frequencies[k] / bin - k as f32) * (TAU / OVERLAP as f32)
                        + k as f32 * expected;
                    state.phase[k] += delta;
                    if state.phase[k] > PI || state.phase[k] < -PI {
                        state.phase[k] += (state.phase[k] * (1.0 / TAU)) as i32 as f32 * -TAU;
                    }
                    let turns = state.phase[k] * (1.0 / TAU);
                    self.work[2 * k] = self.magnitudes[k] * wavetable::cosine(turns);
                    self.work[2 * k + 1] = self.magnitudes[k] * wavetable::cosine(turns - 0.25);
                }
                transform(&mut self.work, &self.twiddle, false);
                for i in 0..n {
                    state.overlap[i] += self.window[i] * self.work[2 * i];
                }
                state.output[..hop].copy_from_slice(&state.overlap[..hop]);
                state.overlap.copy_within(hop..n, 0);
                state.overlap[latency..n].fill(0.0);
                state.input.copy_within(hop..n, 0);
            }
        }
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
    fn fft_sizes_keep_channels_independent_and_ignore_render_chunk_boundaries() {
        for n in [256, 512, 1024, 2048, 4096] {
            for channels in [1, 2, 4, 6, 8] {
                let spec = PitchSpec {
                    pitch: 1.375,
                    fft_size: n,
                    max_channels: 0,
                };
                let signal: Vec<f32> = (0..(n * 3 + 37) * channels)
                    .map(|i| {
                        if i % channels == 0 {
                            ((i * 37) % 997) as f32 / 2048.0 - 0.25
                        } else {
                            0.0
                        }
                    })
                    .collect();
                let mut whole = signal.clone();
                let mut split = signal.clone();
                let mut effect = PitchShifter::new(n, channels);
                let mut chunked = PitchShifter::new(n, channels);
                effect.process(&mut whole, &spec, 48000, channels);
                for part in split.chunks_mut(37 * channels) {
                    chunked.process(part, &spec, 48000, channels);
                }
                assert_eq!(whole, split, "FFT {n}, channels {channels}");
                assert!(whole.iter().any(|v| v.abs() > 0.01));
                assert!(
                    whole
                        .chunks_exact(channels)
                        .all(|f| f[1..].iter().all(|v| *v == 0.0))
                );
                effect.clear();
                let mut reset = signal;
                effect.process(&mut reset, &spec, 48000, channels);
                assert_eq!(whole, reset);
            }
        }
    }

    #[test]
    fn submitted_ratios_restart_phase_even_without_processing_between_edits() {
        let signal: Vec<f32> = (0..4096)
            .map(|i| ((i * 37) % 997) as f32 / 2048.0 - 0.25)
            .collect();
        let parameters = PitchSpec {
            pitch: 0.75,
            fft_size: 1024,
            max_channels: 0,
        };
        let mut edited = PitchShifter::new(1024, 2);
        let mut held = PitchShifter::new(1024, 2);
        edited.process(&mut signal.clone(), &parameters, 44100, 2);
        held.process(&mut signal.clone(), &parameters, 44100, 2);
        edited.set_pitch(1.5);
        edited.set_pitch(parameters.pitch);
        let mut a = signal.clone();
        let mut b = signal;
        edited.process(&mut a, &parameters, 44100, 2);
        held.process(&mut b, &parameters, 44100, 2);
        // Already queued output survives the two edits; newly synthesized audio changes.
        let hop = parameters.fft_size / OVERLAP;
        assert_eq!(&a[..hop * 2], &b[..hop * 2]);
        assert_ne!(&a[hop * 2..], &b[hop * 2..]);
    }

    #[test]
    fn live_ratios_retain_fifo_audio_across_arbitrary_render_chunks() {
        let input: Vec<f32> = (0..2200)
            .map(|i| ((i * 37) % 997) as f32 / 2048.0 - 0.25)
            .collect();
        let mut whole = PitchShifter::new(1024, 2);
        let mut chunks = PitchShifter::new(1024, 2);
        for pitch in [0.75, 1.5, 0.625, 1.0] {
            let parameters = PitchSpec {
                pitch,
                fft_size: 1024,
                max_channels: 0,
            };
            let mut a = input.clone();
            let mut b = input.clone();
            whole.process(&mut a, &parameters, 44100, 2);
            for part in b.chunks_mut(14) {
                chunks.process(part, &parameters, 44100, 2);
            }
            assert_eq!(a, b);
            assert!(a.iter().any(|v| v.abs() > 1e-3));
        }
    }

    #[test]
    fn reset_and_packet_boundaries_preserve_native_phase_history() {
        let spec = PitchSpec {
            pitch: 0.75,
            fft_size: 1024,
            max_channels: 0,
        };
        let signal: Vec<f32> = (0..16384)
            .map(|i| if i % 1531 == 0 { 0.5 } else { 0.0 })
            .collect();
        let mut whole = signal.clone();
        let mut effect = PitchShifter::new(1024, 2);
        effect.process(&mut whole, &spec, 44100, 2);
        let mut split = signal.clone();
        let mut effect = PitchShifter::new(1024, 2);
        for chunk in split.chunks_mut(480 * 2) {
            effect.process(chunk, &spec, 44100, 2);
        }
        assert_eq!(whole, split);
        effect.clear();
        let mut reset = signal;
        effect.process(&mut reset, &spec, 44100, 2);
        assert_eq!(whole, reset);
    }
    #[test]
    fn native_unity_pitch_has_a_delayed_nonidentity_impulse() {
        let mut samples = vec![0.0; 8192];
        samples[2048 * 2] = 0.5;
        PitchShifter::new(1024, 2).process(
            &mut samples,
            &PitchSpec {
                pitch: 1.0,
                fft_size: 1024,
                max_channels: 0,
            },
            48000,
            2,
        );
        assert_eq!(samples[2048 * 2], 0.0);
        assert_eq!(samples[3072 * 2], 0.12646484);
        assert!(
            samples
                .iter()
                .enumerate()
                .all(|(i, x)| i % 2 == 0 || *x == 0.0)
        );
    }

    fn noise(frames: usize) -> Vec<f32> {
        (0..frames * 2)
            .map(|i| ((i * 37) % 997) as f32 / 2048.0 - 0.25)
            .collect()
    }
    fn spec(max_channels: u32) -> PitchSpec {
        PitchSpec {
            pitch: 0.75,
            fft_size: 1024,
            max_channels,
        }
    }
    fn run(effect: &mut PitchShifter, input: &[f32], spec: &PitchSpec, layout: usize) -> Vec<f32> {
        let mut output = input.to_vec();
        effect.process(&mut output, spec, 48000, layout);
        output
    }

    #[test]
    fn channel_limits_below_the_layout_pass_the_input_through() {
        let input = noise(4096);
        let mut reference = PitchShifter::new(1024, 2);
        reference.set_channel_limit(2, 0);
        let shifted = run(&mut reference, &input, &spec(0), 2);
        assert_ne!(shifted, input);
        // Stereo groups: limits 0, 2 and anything larger process; 1 passes through.
        for (layout, limit, processes) in [
            (2, 1, false),
            (2, 2, true),
            (2, 16, true),
            (1, 1, true),
            (6, 2, false),
            (6, 6, true),
            (8, 6, false),
            (8, 0, true),
        ] {
            let mut effect = PitchShifter::new(1024, 2);
            effect.set_channel_limit(layout, limit);
            let output = run(&mut effect, &input, &spec(limit), layout);
            if processes {
                assert_eq!(output, shifted, "layout {layout}, limit {limit}");
            } else {
                assert_eq!(output, input, "layout {layout}, limit {limit}");
            }
        }
    }

    #[test]
    fn changing_the_processed_channel_count_restarts_the_histories() {
        let input = noise(4096);
        let mut fresh = PitchShifter::new(1024, 2);
        fresh.set_channel_limit(2, 0);
        let fresh_output = run(&mut fresh, &input, &spec(0), 2);
        for (limit, restarts) in [(2, false), (4, true), (16, true)] {
            let mut effect = PitchShifter::new(1024, 2);
            effect.set_channel_limit(2, 0);
            run(&mut effect, &input, &spec(0), 2);
            effect.set_channel_limit(2, limit);
            let output = run(&mut effect, &input, &spec(limit), 2);
            assert_eq!(output == fresh_output, restarts, "limit {limit}");
        }
        // A bypassing limit leaves the histories untouched until the count changes again.
        let mut effect = PitchShifter::new(1024, 2);
        effect.set_channel_limit(2, 0);
        run(&mut effect, &input, &spec(0), 2);
        effect.set_channel_limit(2, 1);
        assert_eq!(run(&mut effect, &input, &spec(1), 2), input);
        effect.set_channel_limit(2, 0);
        assert_eq!(run(&mut effect, &input, &spec(0), 2), fresh_output);
    }
}
