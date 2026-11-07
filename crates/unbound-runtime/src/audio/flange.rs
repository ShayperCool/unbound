//! Mixer Flange measured on Unity 6000.0.71f1 (tests/projects/audioflange).
//! Q14 input history and cosine interpolation match Chorus, but both channels use one
//! phase, the history covers 40 ms, and the delay span is 20 ms times depth squared.
//! These are release-specific measurements, not a documented Unity DSP contract.
use super::chorus::Chorus;
use serde::Deserialize;

#[derive(Debug, Clone, PartialEq, Deserialize)]
pub struct FlangeSpec {
    pub dry: f32,
    pub wet: f32,
    pub depth: f32,
    pub rate: f32,
}
impl FlangeSpec {
    pub fn validate(&self) -> anyhow::Result<()> {
        anyhow::ensure!(
            [self.dry, self.wet]
                .iter()
                .all(|v| v.is_finite() && (0.0..=1.0).contains(v))
                && self.depth.is_finite()
                && (0.01..=1.0).contains(&self.depth)
                && self.rate.is_finite()
                && (0.0..=20.0).contains(&self.rate),
            "AudioMixer Flange has invalid parameters"
        );
        Ok(())
    }
}
pub struct Flange {
    history: Vec<[i16; 2]>,
    position: usize,
    offset: usize,
    buffer: usize,
    rate: u32,
    phase: f32,
    span: f32,
}
impl Flange {
    pub(crate) fn frames(rate: u32, buffer: u32) -> anyhow::Result<usize> {
        anyhow::ensure!(rate > 0, "AudioMixer Flange output rate must be positive");
        let buffer = buffer.max(1) as usize;
        // Native truncates milliseconds to samples before rounding up to DSP buffers.
        let delay = (rate as f32 * 40.0) as usize / 1000;
        (delay / buffer + 1)
            .max(2)
            .checked_mul(buffer)
            .filter(|n| *n <= 8 * 1024 * 1024 / 4)
            .ok_or_else(|| {
                anyhow::anyhow!("AudioMixer Flange history exceeds the supported 8 MiB limit")
            })
    }
    pub fn new(rate: u32, buffer: u32) -> anyhow::Result<Self> {
        let frames = Self::frames(rate, buffer)?;
        let mut history = Vec::new();
        history.try_reserve_exact(frames)?;
        history.resize(frames, [0; 2]);
        Ok(Self {
            history,
            position: 0,
            offset: 0,
            buffer: buffer.max(1) as usize,
            rate,
            phase: 0.0,
            span: 0.0,
        })
    }
    pub fn configuration(&self) -> (u32, u32) {
        (self.rate, self.buffer as u32)
    }
    pub fn clear(&mut self) {
        self.history.fill([0; 2]);
        self.position = 0;
        self.offset = 0;
        self.phase = 0.0;
        self.span = 0.0;
    }
    fn wrap(&self, value: f32) -> f32 {
        let frames = self.history.len() as f32;
        if value < 0.0 {
            value + frames
        } else if value >= frames {
            value - frames
        } else {
            value
        }
    }
    fn sample(&self, index: usize, channel: usize) -> f32 {
        if index == self.history.len() {
            0.0
        } else {
            self.history[index][channel] as f32 * (1.0 / 16384.0)
        }
    }
    pub fn process(&mut self, samples: &mut [f32], spec: &FlangeSpec) {
        let target =
            (((40.0 * spec.depth) * self.rate as f32 / 1000.0) * (spec.depth * 0.5)).max(4.0);
        let increment = spec.rate / self.rate as f32;
        let mut done = 0;
        while done < samples.len() / 2 {
            let count = (samples.len() / 2 - done)
                .min(self.buffer - self.offset)
                .min(self.history.len() - self.position);
            for (history, frame) in self.history[self.position..self.position + count]
                .iter_mut()
                .zip(samples[done * 2..(done + count) * 2].chunks_exact(2))
            {
                for c in 0..2 {
                    history[c] = (frame[c] * 16384.0) as i32 as i16;
                }
            }
            let base = (self.position - self.offset) as f32;
            for f in 0..count {
                self.span = if target - 0.015625 > self.span {
                    self.span + 0.015625
                } else if self.span > target + 0.015625 {
                    self.span - 0.015625
                } else {
                    target
                };
                let wave = (Chorus::cosine(self.phase) + 1.0) * 0.5;
                let shift = wave * self.span - self.span;
                let read = self.wrap((base + shift) + (self.offset + f) as f32);
                let next = self.wrap(read + 1.0);
                let fraction = read - read as usize as f32;
                for c in 0..2 {
                    let a = self.sample(read as usize, c);
                    let b = self.sample(next as usize, c);
                    let x = &mut samples[(done + f) * 2 + c];
                    *x = *x * spec.dry + (a * (1.0 - fraction) + b * fraction) * spec.wet;
                }
                self.phase += increment;
                if self.phase > 1.0 {
                    self.phase -= 1.0;
                }
            }
            self.position = (self.position + count) % self.history.len();
            self.offset = (self.offset + count) % self.buffer;
            done += count;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    fn spec() -> FlangeSpec {
        FlangeSpec {
            dry: 0.45,
            wet: 0.55,
            depth: 1.0,
            rate: 0.1,
        }
    }
    #[test]
    fn native_default_impulses_and_reset() {
        let mut input = vec![0.0; 32768 * 2];
        input[2048 * 2] = 0.5;
        input[3072 * 2 + 1] = -0.5;
        let mut output = input.clone();
        let mut flange = Flange::new(48000, 1024).unwrap();
        flange.process(&mut output, &spec());
        assert_eq!(output[2048 * 2], 0.49842224);
        assert_eq!(output[2049 * 2], 0.0015641212);
        assert_eq!(output[3072 * 2 + 1], -0.49469605);
        assert_eq!(output[3073 * 2 + 1], -0.005303955);
        flange.clear();
        flange.process(&mut input, &spec());
        assert_eq!(input, output);
    }
    #[test]
    fn chunks_preserve_phase_and_both_channels_share_it() {
        let input: Vec<_> = (0..262144)
            .flat_map(|f| {
                let value = (f % 8192) as f32 / 32768.0;
                [value, value]
            })
            .collect();
        let mut whole = input.clone();
        let mut chunks = input;
        let mut spec = spec();
        spec.rate = 5.0;
        Flange::new(44100, 1024).unwrap().process(&mut whole, &spec);
        let mut flange = Flange::new(44100, 1024).unwrap();
        for chunk in chunks.chunks_mut(512) {
            flange.process(chunk, &spec);
        }
        assert_eq!(chunks, whole);
        assert!(whole.chunks_exact(2).all(|f| f[0] == f[1]));
    }
    #[test]
    fn configuration_rejects_unbounded_histories_and_invalid_parameters() {
        assert!(Flange::new(0, 1024).is_err());
        assert!(Flange::new(48000, u32::MAX).is_err());
        assert!(Flange::new(u32::MAX, 1024).is_err());
        let mut value = spec();
        value.rate = f32::NAN;
        assert!(value.validate().is_err());
    }
}
