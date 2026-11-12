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
        self.process_linked_with(samples, &mut [], 2, spec, rate, |_, _, _| {});
    }

    /// One peak envelope drives every active speaker, including the LFE channel.
    pub(crate) fn process_linked_with(
        &mut self,
        front: &mut [f32],
        extra: &mut [&mut [f32]],
        channels: usize,
        spec: &NormalizeSpec,
        rate: u32,
        mut finish: impl FnMut(&mut [f32], &[f32], usize),
    ) {
        let decay = if spec.fade == 0.0 {
            1.0
        } else {
            1.0 / (spec.fade * rate as f32 / 1000.0)
        };
        super::channels::process_frames(front, extra, channels, |frame, index| {
            let mut dry = [0.0; 8];
            dry[..channels].copy_from_slice(frame);
            self.peak = (self.peak - decay).max(spec.lowest);
            for x in frame.iter() {
                self.peak = self.peak.max(x.abs());
            }
            let gain = spec.maximum.min(1.0 / self.peak);
            for x in frame.iter_mut() {
                *x *= gain;
            }
            finish(frame, &dry[..channels], index);
        });
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn one_envelope_includes_rear_speakers_but_excludes_an_unused_pair_slot() {
        let spec = NormalizeSpec {
            fade: 0.0,
            lowest: 0.01,
            maximum: 10.0,
        };
        let mut front = vec![0.125; 97 * 2];
        let mut middle = vec![0.25; 97 * 2];
        let mut rear = vec![0.5; 97 * 2];
        for pair in rear.chunks_exact_mut(2) {
            pair[1] = 100.0;
        }
        Normalize::default().process_linked_with(
            &mut front,
            &mut [&mut middle, &mut rear],
            5,
            &spec,
            32000,
            |_, _, _| {},
        );
        assert!(front.iter().all(|&x| x == 0.25));
        assert!(middle.iter().all(|&x| x == 0.5));
        assert!(rear.chunks_exact(2).all(|pair| pair == [1.0, 100.0]));

        // The envelope decays once per frame, independent of the number of speakers.
        let spec = NormalizeSpec {
            fade: 200.0,
            lowest: 0.01,
            maximum: 10.0,
        };
        let mut stereo = Normalize::default();
        let mut wide = Normalize::default();
        for _ in 0..11 {
            let mut pair = [0.0625; 97 * 2];
            let mut front = pair;
            let mut rear = pair;
            stereo.process(&mut pair, &spec, 32000);
            wide.process_linked_with(&mut front, &mut [&mut rear], 4, &spec, 32000, |_, _, _| {});
            assert_eq!(front, pair);
            assert_eq!(rear, pair);
        }
    }

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
