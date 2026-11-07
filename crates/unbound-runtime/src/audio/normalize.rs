//! Normalize's linked peak envelope, measured in Unity 6000.0.71f1.
//! See tests/projects/audionormalize for the captured context and contract limits; the mixer
//! checks the measured output layout ([`super::effects::EffectKind::layout_limited`]).
use serde::Deserialize;

#[derive(Debug, Clone, PartialEq, Deserialize)]
pub struct NormalizeSpec {
    pub fade: f32,
    pub lowest: f32,
    pub maximum: f32,
}
impl NormalizeSpec {
    pub fn validate(&self) -> anyhow::Result<()> {
        anyhow::ensure!(
            self.fade.is_finite()
                && (0.0..=20000.0).contains(&self.fade)
                && self.lowest.is_finite()
                && (0.0..=1.0).contains(&self.lowest)
                && self.maximum.is_finite()
                && (0.0..=100000.0).contains(&self.maximum),
            "AudioMixer Normalize has invalid parameters"
        );
        Ok(())
    }
}
pub struct Normalize {
    peak: f32,
}
impl Default for Normalize {
    fn default() -> Self {
        Self { peak: 1.0 }
    }
}
impl Normalize {
    pub fn process(&mut self, samples: &mut [f32], spec: &NormalizeSpec, rate: u32) {
        let decay = if spec.fade == 0.0 {
            1.0
        } else {
            1.0 / (spec.fade * rate as f32 / 1000.0)
        };
        for frame in samples.chunks_exact_mut(2) {
            self.peak = (self.peak - decay)
                .max(spec.lowest)
                .max(frame[0].abs())
                .max(frame[1].abs());
            let gain = spec.maximum.min(1.0 / self.peak);
            for x in frame {
                *x *= gain;
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn native_default_peak_response_and_linked_channels() {
        // Captured default_dc_quarter: silent first 1024 frames, then [0.25, -0.125].
        let spec = NormalizeSpec {
            fade: 5000.0,
            lowest: 0.1,
            maximum: 20.0,
        };
        let mut effect = Normalize::default();
        let mut silent = vec![0.0; 2048];
        effect.process(&mut silent, &spec, 48000);
        let mut signal = vec![0.25, -0.125];
        effect.process(&mut signal, &spec, 48000);
        assert_eq!(signal, [0.25107375, -0.12553687]);
        let mut rest = vec![0.25; 48000 * 2];
        effect.process(&mut rest, &spec, 48000);
        assert!(rest[rest.len() - 1] > signal[0]);
    }
    #[test]
    fn zero_floor_silence_and_gain_cap_stay_finite() {
        let spec = NormalizeSpec {
            fade: 0.0,
            lowest: 0.0,
            maximum: 10.0,
        };
        let mut x = [0.0, 0.0, 0.01, -0.005, 0.5, -0.25, 0.0, 0.0];
        Normalize::default().process(&mut x, &spec, 44100);
        assert_eq!(
            x,
            [0.0, 0.0, 0.099999994, -0.049999997, 1.0, -0.5, 0.0, 0.0]
        );
    }
}
