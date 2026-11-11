//! The AudioMixer Compressor unit, as measured from Unity 6000.0.71f1
//! (tests/projects/mixercompressor, 48000 and 44100 Hz): a linked peak limiter. The detector, its
//! starting state and the gain law were fitted to step, ramp, tone and noise captures.
use serde::Deserialize;

/// Parameters as the mixer stores them: dB and milliseconds.
#[derive(Debug, Clone, PartialEq, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CompressorSpec {
    pub threshold: f32,
    pub attack: f32,
    pub release: f32,
    pub makeup_gain: f32,
}
impl CompressorSpec {
    pub(crate) fn validate(&self, id: &str) -> anyhow::Result<()> {
        let values = [self.threshold, self.attack, self.release, self.makeup_gain];
        anyhow::ensure!(
            values.iter().all(|v| v.is_finite()),
            "AudioMixer Compressor '{id}' has a non-finite parameter"
        );
        // The Editor's ranges keep both times positive; zero or less is unmeasured.
        anyhow::ensure!(
            self.attack > 0.0 && self.release > 0.0,
            "AudioMixer Compressor '{id}' attack {} or release {} is unsupported",
            self.attack,
            self.release
        );
        Ok(())
    }
}

/// Peak-hold slope of the detector, in linear amplitude per second.
const PEAK_DECAY: f64 = 10.0;

/// Single precision throughout: the fitted rounding (a pole rounded from double precision and
/// its complement) reproduces the measured asymptote and attack curves of long time constants.
pub struct Compressor {
    /// Peak detector: jumps to the louder channel, decays linearly.
    peak: f32,
    /// Follower state in amplitude or threshold-relative units; initialized on its first frame.
    env: Option<f32>,
}
impl Default for Compressor {
    fn default() -> Self {
        Self {
            peak: 0.0,
            env: None,
        }
    }
}
impl Compressor {
    /// The pole of a one-pole smoother with a time constant of `ms` milliseconds, and its
    /// complement.
    fn pole(ms: f32, rate: u32) -> (f32, f32) {
        let pole = (-1000.0 / (ms as f64 * rate as f64)).exp() as f32;
        (pole, 1.0 - pole)
    }
    /// Scales interleaved stereo `samples` by the limiter gain: `threshold / envelope` once the
    /// envelope exceeds the threshold, times Make up gain. The envelope uses the attack time
    /// while the peak is above the threshold and the release time otherwise.
    #[cfg(test)]
    pub fn process(&mut self, samples: &mut [f32], spec: &CompressorSpec, rate: u32) {
        self.process_linked(samples, &mut [], spec, rate, false);
    }

    /// [`Compressor::process`] over a layout wider than stereo: the louder of every channel
    /// drives the gain of all of them, as within a stereo pair; `extra` are the other pairs.
    /// Measured transitions retain the follower relative to the threshold.
    pub fn process_linked(
        &mut self,
        samples: &mut [f32],
        extra: &mut [&mut [f32]],
        spec: &CompressorSpec,
        rate: u32,
        normalized: bool,
    ) {
        let threshold = 10_f64.powf(spec.threshold as f64 / 20.0) as f32;
        let makeup = 10_f64.powf(spec.makeup_gain as f64 / 20.0) as f32;
        let attack = Self::pole(spec.attack, rate);
        let release = Self::pole(spec.release, rate);
        let decay = (PEAK_DECAY / rate as f64) as f32;
        // A threshold change retains the envelope in units of the threshold, so its
        // gain changes through the attack/release follower. Unity 6000.0.71f1,
        // tests/projects/audiopitch transitions. Other profiles retain amplitude units.
        let limit = if normalized { 1.0 } else { threshold };
        let mut env = self.env.unwrap_or(limit);
        for (frame, stereo) in samples.chunks_exact_mut(2).enumerate() {
            let mut level = stereo[0].abs().max(stereo[1].abs());
            for lane in extra.iter() {
                level = level
                    .max(lane[frame * 2].abs())
                    .max(lane[frame * 2 + 1].abs());
            }
            self.peak = level.max(self.peak - decay);
            let (pole, step) = if self.peak > threshold {
                attack
            } else {
                release
            };
            env = if normalized {
                let target = self.peak * threshold.recip();
                (env - target) * pole + target
            } else {
                env * pole + self.peak * step
            };
            let gain = (limit / env).min(1.0) * makeup;
            for x in stereo {
                *x *= gain;
            }
            for lane in extra.iter_mut() {
                lane[frame * 2] *= gain;
                lane[frame * 2 + 1] *= gain;
            }
        }
        self.env = Some(env);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn spec() -> CompressorSpec {
        CompressorSpec {
            threshold: -20.0,
            attack: 10.0,
            release: 50.0,
            makeup_gain: 0.0,
        }
    }

    #[test]
    fn signals_below_the_threshold_pass_and_a_held_step_is_limited_to_it() {
        let mut c = Compressor::default();
        let mut quiet = vec![0.05_f32; 2048];
        c.process(&mut quiet, &spec(), 48000);
        assert!(quiet.iter().all(|&x| x == 0.05));
        let mut loud = vec![0.5_f32; 48000];
        c.process(&mut loud, &spec(), 48000);
        // Ten attack time constants: the output settles at the -20 dB threshold (within the
        // single-precision asymptote, as Native: about 2e-5 relative).
        assert!((loud[loud.len() - 1] - 0.1).abs() < 1e-5);
    }

    #[test]
    fn the_louder_channel_drives_both_and_make_up_gain_scales_the_result() {
        let mut c = Compressor::default();
        let s = CompressorSpec {
            makeup_gain: 20.0,
            ..spec()
        };
        let mut x = [0.05_f32, 0.0, 0.05, 0.0];
        c.process(&mut x, &s, 48000);
        assert!((x[0] - 0.5).abs() < 1e-6 && x[1] == 0.0);
        let mut c = Compressor::default();
        let mut x: Vec<f32> = (0..9600).flat_map(|_| [0.0, 0.5]).collect();
        c.process(&mut x, &spec(), 48000);
        assert!(x[x.len() - 2] == 0.0 && (x[x.len() - 1] - 0.1).abs() < 1e-5);
    }

    #[test]
    fn live_threshold_changes_retain_the_follower_and_link_every_channel() {
        // Include a rate and partial period outside the recorded device configurations.
        for rate in [32000, 44100, 48000] {
            let mut compressor = Compressor::default();
            let mut parameters = CompressorSpec {
                threshold: -18.0,
                attack: 20.0,
                release: 80.0,
                makeup_gain: 0.0,
            };
            let loud: Vec<f32> = (0..rate).flat_map(|_| [0.75, 0.015625]).collect();
            let mut steady = loud.clone();
            compressor.process_linked(&mut steady, &mut [], &parameters, rate, true);
            let old = steady[steady.len() - 2];
            parameters.threshold = -30.0;
            let mut front = vec![0.015625; 97 * 2];
            let mut rear = vec![0.015625; 97 * 2];
            for frame in rear.chunks_exact_mut(2) {
                frame[0] = 0.75;
            }
            compressor.process_linked(&mut front, &mut [&mut rear], &parameters, rate, true);
            // Lowering the target does not jump to the new static gain on the first sample.
            assert!((rear[0] - old).abs() < 0.001);
            assert!(rear[192] > 0.08 && rear[192] < old);
            for frame in 0..97 {
                for value in [front[frame * 2], front[frame * 2 + 1], rear[frame * 2 + 1]] {
                    assert!((value * 48.0 - rear[frame * 2]).abs() < 1e-6);
                }
            }
            let mut settled = loud.clone();
            compressor.process_linked(&mut settled, &mut [], &parameters, rate, true);
            assert!((settled[settled.len() - 2] - 10_f32.powf(-1.5)).abs() < 2e-5);
            parameters.threshold = -12.0;
            let mut raised = vec![0.75; 97 * 2];
            compressor.process_linked(&mut raised, &mut [], &parameters, rate, true);
            assert!(raised[0] < 0.04 && raised[192] > raised[0] && raised[192] < 0.08);
            let mut silent = vec![0.0; rate as usize * 2];
            compressor.process_linked(&mut silent, &mut [], &parameters, rate, true);
            assert!(silent.iter().all(|value| *value == 0.0));
        }
    }
}
