//! Mixer Flange matching the Unity 6000.0.71f1 Editor (tests/projects/audioflange): the
//! shared [`ModulatedDelay`] of Chorus with one tap for both channels, a 40 ms history and a
//! delay span of 20 ms times depth squared. These are release-specific measurements, not a
//! documented Unity DSP contract.
use super::modulated_delay::{ModulatedDelay, Taps};
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
/// The Delay span at Depth 1, in milliseconds (no exposed Delay parameter).
const SPAN_MS: f32 = 40.0;

pub struct Flange {
    line: ModulatedDelay,
}
impl Flange {
    /// 40 ms of history; the milliseconds are truncated to frames before rounding up to DSP
    /// buffers.
    fn reach(rate: u32) -> usize {
        (rate as f32 * SPAN_MS) as usize / 1000
    }
    pub(crate) fn frames(rate: u32, buffer: u32) -> anyhow::Result<usize> {
        ModulatedDelay::frames("Flange", rate, buffer, Self::reach)
    }
    pub fn new(rate: u32, buffer: u32) -> anyhow::Result<Self> {
        Ok(Self {
            line: ModulatedDelay::new("Flange", rate, buffer, Self::reach)?,
        })
    }
    pub fn configuration(&self) -> (u32, u32) {
        self.line.configuration()
    }
    pub fn clear(&mut self) {
        self.line.clear();
    }
    pub fn process(&mut self, samples: &mut [f32], spec: &FlangeSpec) {
        let rate = self.line.rate() as f32;
        let target = ((SPAN_MS * spec.depth) * rate / 1000.0) * (spec.depth * 0.5);
        self.line.process(
            samples,
            target,
            spec.rate,
            Taps::Shared,
            spec.dry,
            [spec.wet; 2],
        );
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
