//! Chorus measured on Unity 6000.0.71f1, tests/projects/audiochorus. The captured wet path
//! uses Q14 history, a quarter-wave cosine table, and one tap per channel (cycling over three
//! taps). The equations and state below are an implementation of those measurements, not a
//! documented Unity algorithm. Feedback is obsolete and has no measured effect.
use serde::Deserialize;
use std::sync::OnceLock;

#[derive(Debug, Clone, PartialEq, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ChorusSpec {
    pub dry: f32,
    pub wet1: f32,
    pub wet2: f32,
    pub wet3: f32,
    pub delay: f32,
    pub rate: f32,
    pub depth: f32,
}

#[cfg(test)]
mod tests {
    use super::*;
    fn spec() -> ChorusSpec {
        ChorusSpec { dry: 0.0, wet1: 1.0, wet2: 1.0, wet3: 1.0,
            delay: 40.0, rate: 0.8, depth: 0.03 }
    }
    #[test]
    fn native_impulses_constrain_interpolation_and_channel_phase() {
        let mut x = vec![0.0; 32768 * 2];
        x[2048 * 2] = 0.5;
        x[3072 * 2 + 1] = -0.5;
        let original = x.clone();
        let mut chorus = Chorus::new(48000, 1024).unwrap();
        chorus.process(&mut x, &spec());
        assert_eq!(x[2048 * 2], 0.47711181640625);
        assert_eq!(x[2049 * 2], 0.02294921875);
        assert_eq!(x[3072 * 2 + 1], 0.0);
        assert_eq!(x[3074 * 2 + 1], -0.18359375);
        assert_eq!(x[3075 * 2 + 1], -0.316650390625);
        chorus.clear();
        let mut reset = original;
        chorus.process(&mut reset, &spec());
        assert_eq!(reset, x);
    }
    #[test]
    fn rounding_at_the_history_boundary_keeps_both_indices_in_bounds() {
        let chorus = Chorus::new(48000, 1024).unwrap();
        for read in [-f32::EPSILON, -0.001, 0.0, 5119.9995, 5120.0, 5121.0] {
            let read = chorus.wrap(read);
            assert!((read as usize) < chorus.history.len());
            assert!((chorus.wrap(read + 1.0) as usize) < chorus.history.len());
        }
    }
    #[test]
    fn smaller_mixer_chunks_keep_the_dsp_buffer_coordinates() {
        let mut seed = 12345u32;
        let mut whole: Vec<_> = (0..262144 * 2).map(|_| {
            seed = seed.wrapping_mul(1664525).wrapping_add(1013904223);
            0.25 * ((seed >> 8) as f32 / 8388608.0 - 1.0)
        }).collect();
        let mut chunks = whole.clone();
        let mut spec = spec(); spec.depth = 1.0;
        Chorus::new(48000, 1024).unwrap().process(&mut whole, &spec);
        let mut chorus = Chorus::new(48000, 1024).unwrap();
        for chunk in chunks.chunks_mut(256 * 2) { chorus.process(chunk, &spec); }
        assert_eq!(chunks, whole);
    }
}
impl ChorusSpec {
    pub fn validate(&self) -> anyhow::Result<()> {
        anyhow::ensure!([self.dry, self.wet1, self.wet2, self.wet3, self.depth].iter()
            .all(|v| v.is_finite() && (0.0..=1.0).contains(v))
            && self.delay.is_finite() && (0.0..=100.0).contains(&self.delay)
            && self.rate.is_finite() && (0.0..=20.0).contains(&self.rate),
            "AudioMixer Chorus has invalid parameters");
        Ok(())
    }
}

pub struct Chorus {
    history: Vec<[i16; 2]>,
    position: usize,
    offset: usize,
    buffer: usize,
    rate: u32,
    phase: f32,
    span: f32,
}
impl Chorus {
    pub fn new(rate: u32, buffer: u32) -> anyhow::Result<Self> {
        let buffer = buffer.max(1) as usize;
        let frames = (((rate as usize / 10) / buffer + 1).max(2))
            .checked_mul(buffer).filter(|n| *n <= 8 * 1024 * 1024 / 4)
            .ok_or_else(|| anyhow::anyhow!("AudioMixer Chorus history exceeds the supported 8 MiB limit"))?;
        let mut history = Vec::new();
        history.try_reserve_exact(frames)?;
        history.resize(frames, [0; 2]);
        Ok(Self { history, position: 0, offset: 0, buffer, rate, phase: 0.0, span: 0.0 })
    }
    pub fn configuration(&self) -> (u32, u32) { (self.rate, self.buffer as u32) }
    pub fn clear(&mut self) {
        self.history.fill([0; 2]);
        self.position = 0;
        self.offset = 0;
        self.phase = 0.0;
        self.span = 0.0;
    }
    fn cosine(phase: f32) -> f32 {
        static TABLE: OnceLock<Box<[f32]>> = OnceLock::new();
        let table = TABLE.get_or_init(|| (0..8192).map(|i|
            ((i as f32 / 8192.0) * std::f32::consts::FRAC_PI_2).cos()).collect());
        let index = ((phase * 32768.0) as i32).unsigned_abs() as usize & 32767;
        match index >> 13 {
            0 => table[index],
            1 => -table[16383 - index],
            2 => -table[index - 16384],
            _ => table[32767 - index],
        }
    }
    fn wrap(&self, value: f32) -> f32 {
        let frames = self.history.len() as f32;
        let value = if value < 0.0 { value + frames } else { value };
        // Adding a tiny negative coordinate can round to exactly `frames`.
        if value >= frames { value - frames } else { value }
    }
    pub fn process(&mut self, samples: &mut [f32], spec: &ChorusSpec) {
        let target = (((spec.delay * spec.depth) * self.rate as f32 / 1000.0) * spec.depth).max(4.0);
        let increment = spec.rate / self.rate as f32;
        let mut done = 0;
        while done < samples.len() / 2 {
            let count = (samples.len() / 2 - done).min(self.buffer - self.offset)
                .min(self.history.len() - self.position);
            // Copy this input chunk before reading: rounding at a nearly zero delay can read
            // the following input frame. Keep the configured DSP block's coordinate origin
            // when the mixer calls us with smaller chunks.
            for (history, frame) in self.history[self.position..self.position + count].iter_mut()
                .zip(samples[done * 2..(done + count) * 2].chunks_exact(2)) {
                for c in 0..2 { history[c] = (frame[c] * 16384.0) as i32 as i16; }
            }
            let base = (self.position - self.offset) as f32;
            for f in 0..count {
                self.span = if target - 0.015625 > self.span { self.span + 0.015625 }
                    else if self.span > target + 0.015625 { self.span - 0.015625 } else { target };
                for c in 0..2 {
                    let wave = (Self::cosine(self.phase + c as f32 * 0.25) + 1.0) * 0.5;
                    let shift = wave * self.span - self.span;
                    let read = self.wrap((base + shift) + (self.offset + f) as f32);
                    // Round the next floating coordinate before converting either to an index.
                    // Replacing this with `index + 1` changes measured near-zero-delay samples.
                    let next = self.wrap(read + 1.0);
                    let fraction = read - read as usize as f32;
                    let a = self.history[read as usize][c] as f32 * (1.0 / 16384.0);
                    let b = self.history[next as usize][c] as f32 * (1.0 / 16384.0);
                    let wet = if c == 0 { spec.wet1 } else { spec.wet2 };
                    let x = &mut samples[(done + f) * 2 + c];
                    *x = *x * spec.dry + (a * (1.0 - fraction) + b * fraction) * wet;
                }
                self.phase += increment;
                if self.phase > 1.0 { self.phase -= 1.0; }
            }
            self.position = (self.position + count) % self.history.len();
            self.offset = (self.offset + count) % self.buffer;
            done += count;
        }
    }
}
