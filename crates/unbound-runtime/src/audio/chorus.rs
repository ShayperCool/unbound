//! Chorus matching the Unity 6000.0.71f1 Editor, tests/projects/audiochorus, on AudioMixer
//! groups and `AudioChorusFilter`. It is the shared [`ModulatedDelay`] with a 100 ms history and
//! one tap per output channel, the right channel's sweep a quarter period after the left's.
//! The left channel takes Wet mix tap 1 and the right tap 2; tap 3 has no measured effect on
//! stereo output, and wider layouts are unmeasured. Feedback is obsolete and has no measured
//! effect. These are measurements of one release, not a documented Unity algorithm.
use super::modulated_delay::{ModulatedDelay, Taps};
use serde::Deserialize;

#[derive(Debug, Clone, PartialEq, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ChorusSpec {
    pub dry: f32,
    pub wet1: f32,
    pub wet2: f32,
    /// `None`: a Delay above 100 ms, which keeps the previous valid control value.
    pub delay: Option<f32>,
    pub rate: f32,
    pub depth: f32,
}
impl ChorusSpec {
    pub fn validate(&self) -> anyhow::Result<()> {
        anyhow::ensure!(
            [self.dry, self.wet1, self.wet2, self.depth]
                .iter()
                .all(|v| v.is_finite() && (0.0..=1.0).contains(v))
                && self
                    .delay
                    .is_none_or(|v| v.is_finite() && (0.0..=100.0).contains(&v))
                && self.rate.is_finite()
                && (0.0..=20.0).contains(&self.rate),
            "AudioMixer Chorus has invalid parameters"
        );
        Ok(())
    }
}

/// The Delay parameter's default, in milliseconds.
const DEFAULT_DELAY_MS: f32 = 40.0;

pub struct Chorus {
    line: ModulatedDelay,
    /// Latest valid delay control; a delay change alone does not rebuild the span target.
    delay: f32,
    depth: Option<f32>,
    span: f32,
}
impl Chorus {
    /// 100 ms of history.
    fn reach(rate: u32) -> usize {
        rate as usize / 10
    }
    pub(crate) fn frames(rate: u32, buffer: u32) -> anyhow::Result<usize> {
        ModulatedDelay::frames("Chorus", rate, buffer, Self::reach)
    }
    pub fn new(rate: u32, buffer: u32) -> anyhow::Result<Self> {
        Ok(Self {
            line: ModulatedDelay::new("Chorus", rate, buffer, Self::reach)?,
            delay: DEFAULT_DELAY_MS,
            depth: None,
            span: 0.0,
        })
    }
    pub fn configuration(&self) -> (u32, u32) {
        self.line.configuration()
    }
    /// Restarts history and sweep phase as a reconnected unit; the span keeps slewing from its
    /// current value. Unity 6000.0.71f1, tests/projects/audiodistortion live bypass controls.
    pub fn restart(&mut self) {
        self.line.restart();
    }
    /// Clears history and sweep, rebuilding the span from retained controls on next use.
    pub fn clear(&mut self) {
        self.line.clear();
        self.depth = None;
        self.span = 0.0;
    }
    pub fn process(&mut self, samples: &mut [f32], spec: &ChorusSpec) {
        if let Some(delay) = spec.delay {
            self.delay = delay;
        }
        if self.depth != Some(spec.depth) {
            // Delay alone leaves this target unchanged; a depth change uses the latest valid
            // delay. Unity 6000.0.71f1, tests/projects/audiodistortion live controls.
            let rate = self.line.rate() as f32;
            self.span = ((self.delay * spec.depth) * rate / 1000.0) * spec.depth;
            self.depth = Some(spec.depth);
        }
        self.line.process(
            samples,
            self.span,
            spec.rate,
            Taps::Quadrature,
            spec.dry,
            [spec.wet1, spec.wet2],
        );
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    fn spec() -> ChorusSpec {
        ChorusSpec {
            dry: 0.0,
            wet1: 1.0,
            wet2: 1.0,
            delay: Some(40.0),
            rate: 0.8,
            depth: 0.03,
        }
    }
    #[test]
    fn live_delay_setters_keep_the_span_until_a_depth_update_or_recreation() {
        let input: Vec<f32> = (0..32768)
            .map(|i| ((i * 37) % 997) as f32 / 2048.0 - 0.25)
            .collect();
        let mut baseline = input.clone();
        let mut changed = input.clone();
        let mut parameters = spec();
        parameters.delay = Some(17.5);
        parameters.depth = 0.5;
        Chorus::new(44100, 1024)
            .unwrap()
            .process(&mut baseline, &parameters);
        let mut effect = Chorus::new(44100, 1024).unwrap();
        effect.process(&mut changed[..12288], &parameters);
        parameters.delay = Some(2.0);
        effect.process(&mut changed[12288..], &parameters);
        assert_eq!(changed, baseline);
        effect.clear();
        let mut recreated = input.clone();
        effect.process(&mut recreated, &parameters);
        let mut fresh = input;
        Chorus::new(44100, 1024)
            .unwrap()
            .process(&mut fresh, &parameters);
        assert_eq!(recreated, fresh);
        assert_ne!(recreated, baseline);
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
    fn smaller_mixer_chunks_keep_the_dsp_buffer_coordinates() {
        let mut seed = 12345u32;
        let mut whole: Vec<_> = (0..262144 * 2)
            .map(|_| {
                seed = seed.wrapping_mul(1664525).wrapping_add(1013904223);
                0.25 * ((seed >> 8) as f32 / 8388608.0 - 1.0)
            })
            .collect();
        let mut chunks = whole.clone();
        let mut spec = spec();
        spec.depth = 1.0;
        Chorus::new(48000, 1024).unwrap().process(&mut whole, &spec);
        let mut chorus = Chorus::new(48000, 1024).unwrap();
        for chunk in chunks.chunks_mut(256 * 2) {
            chorus.process(chunk, &spec);
        }
        assert_eq!(chunks, whole);
    }
    #[test]
    fn delay_above_the_range_keeps_the_previous_dsp_delay() {
        let input: Vec<f32> = (0..16384)
            .map(|i| ((i * 13) % 101) as f32 / 404.0)
            .collect();
        let mut spec = spec();
        spec.depth = 0.5;
        let (mut kept, mut default) = (input.clone(), input);
        let mut chorus = Chorus::new(44100, 1024).unwrap();
        spec.delay = None;
        chorus.process(&mut kept, &spec);
        spec.delay = Some(DEFAULT_DELAY_MS);
        Chorus::new(44100, 1024)
            .unwrap()
            .process(&mut default, &spec);
        assert_eq!(kept, default);
    }
}
