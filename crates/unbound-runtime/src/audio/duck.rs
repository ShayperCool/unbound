//! The AudioMixer Duck Volume unit: a sidechain compressor, as measured from Unity 6000.0.71f1
//! (tests/projects/mixerduck, 48000 and 44100 Hz). The level detector and gain curve are those of
//! the compressor in Unity's public NativeAudioPlugins (`Plugin_Multiband.cpp`); the captures fix
//! the time constants' accuracy, the channel link and the 8-frame gain interpolation.
use serde::Deserialize;

/// Parameters as the mixer stores them: dB, a ratio (2 = 2:1), seconds and a 0..1 mix.
#[derive(Debug, Clone, PartialEq, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct DuckSpec {
    pub threshold: f32,
    pub ratio: f32,
    pub attack_time: f32,
    pub release_time: f32,
    pub makeup_gain: f32,
    pub knee: f32,
    pub sidechain_mix: f32,
}
impl DuckSpec {
    pub(crate) fn validate(&self, id: &str) -> anyhow::Result<()> {
        let values = [
            self.threshold,
            self.ratio,
            self.attack_time,
            self.release_time,
            self.makeup_gain,
            self.knee,
            self.sidechain_mix,
        ];
        anyhow::ensure!(
            values.iter().all(|v| v.is_finite()),
            "AudioMixer Duck Volume '{id}' has a non-finite parameter"
        );
        // The Editor's range is 0.2 to 10; a ratio of zero or below is unmeasured.
        anyhow::ensure!(
            self.ratio > 0.0,
            "AudioMixer Duck Volume '{id}' ratio {} is unsupported",
            self.ratio
        );
        Ok(())
    }
}

/// Frames per gain evaluation; the gain is interpolated linearly across them.
const CHUNK: u64 = 8;

#[derive(Default)]
pub struct Duck {
    /// Mean-square level detector.
    env: f32,
    /// The gain reached at the end of the current chunk, and the one it started from.
    gain: f32,
    from: f32,
    frames: u64,
}
impl Duck {
    /// One-pole coefficient reaching 90 % of a step in `time` seconds (zero: immediate).
    fn coefficient(time: f32, rate: u32) -> f32 {
        let samples = time * rate as f32;
        if samples <= 0.0 {
            1.0
        } else {
            1.0 - (1.0_f32 - 0.9).powf(1.0 / samples)
        }
    }
    fn curve(spec: &DuckSpec, level: f32) -> f32 {
        let slope = 0.05 * (1.0 / spec.ratio - 1.0);
        let t = level - spec.threshold;
        // Soft knee spanning threshold ± knee, quadratic in dB.
        if t.abs() < spec.knee {
            let t = t + spec.knee;
            10_f32.powf(slope * 0.25 / spec.knee).powf(t * t)
        } else if t > 0.0 {
            10_f32.powf(slope).powf(t)
        } else {
            1.0
        }
    }
    /// Scales interleaved stereo `samples` by the gain detected from `sidechain` (silence when
    /// no send reaches the unit), mixed with the unit's own input by Sidechain Mix.
    pub fn process(
        &mut self,
        samples: &mut [f32],
        sidechain: Option<&[f32]>,
        spec: &DuckSpec,
        rate: u32,
    ) {
        self.process_linked(samples, &mut [], sidechain, &[], spec, rate);
    }

    /// [`Duck::process`] over a layout wider than stereo: the louder of every channel drives
    /// the gain of all of them, as within a stereo pair. `extra` are the other channel pairs
    /// and `extra_sidechains` their sidechain blocks (empty: silence).
    pub fn process_linked(
        &mut self,
        samples: &mut [f32],
        extra: &mut [&mut [f32]],
        sidechain: Option<&[f32]>,
        extra_sidechains: &[&[f32]],
        spec: &DuckSpec,
        rate: u32,
    ) {
        let attack = Self::coefficient(spec.attack_time, rate);
        let release = Self::coefficient(spec.release_time, rate);
        let makeup = 10_f32.powf(spec.makeup_gain / 20.0);
        let mix = spec.sidechain_mix;
        for (frame, stereo) in samples.chunks_exact_mut(2).enumerate() {
            let side = sidechain.map_or([0.0; 2], |s| [s[frame * 2], s[frame * 2 + 1]]);
            let x = [
                mix * side[0] + (1.0 - mix) * stereo[0],
                mix * side[1] + (1.0 - mix) * stereo[1],
            ];
            // Linked stereo: the louder channel drives both.
            let mut s = (x[0] * x[0]).max(x[1] * x[1]);
            for (pair, lane) in extra.iter().enumerate() {
                for channel in 0..2 {
                    let i = frame * 2 + channel;
                    let side = extra_sidechains
                        .get(pair)
                        .and_then(|s| s.get(i))
                        .copied()
                        .unwrap_or(0.0);
                    let x = mix * side + (1.0 - mix) * lane[i];
                    s = s.max(x * x);
                }
            }
            let s = s.clamp(1e-11, 100.0);
            let k = if s > self.env { attack } else { release };
            self.env += (s - self.env) * k + 1e-16;
            let phase = self.frames % CHUNK;
            if phase == 0 {
                self.from = self.gain;
                self.gain = Self::curve(spec, 10.0 * self.env.log10()) * makeup;
            }
            let gain = self.from + (self.gain - self.from) * ((phase + 1) as f32 / CHUNK as f32);
            for x in stereo {
                *x *= gain;
            }
            for lane in extra.iter_mut() {
                lane[frame * 2] *= gain;
                lane[frame * 2 + 1] *= gain;
            }
            self.frames += 1;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn spec() -> DuckSpec {
        DuckSpec {
            threshold: -10.0,
            ratio: 2.0,
            attack_time: 0.0,
            release_time: 0.0,
            makeup_gain: 0.0,
            knee: 10.0,
            sidechain_mix: 1.0,
        }
    }

    #[test]
    fn instant_detector_interpolates_each_eight_frame_chunk_from_the_first_frame() {
        let mut duck = Duck::default();
        let mut samples = vec![1.0; 32];
        let mut side = vec![0.0; 32];
        side[16..].fill(0.5);
        duck.process(&mut samples, Some(&side), &spec(), 48000);
        // The unit starts from zero gain, then holds unity until the sidechain chunk.
        assert_eq!(samples[0], 1.0 / 8.0);
        assert_eq!(samples[14], 1.0);
        // -6.02 dB in the knee: 10^(-0.025 (13.98)^2 / 10) = 0.75485 after eight frames.
        assert!((samples[16] - (1.0 - (1.0 - 0.754849) / 8.0)).abs() < 1e-6);
        assert!((samples[30] - 0.754849).abs() < 1e-6);
    }

    #[test]
    fn the_curve_is_unity_below_the_knee_and_follows_the_ratio_above_it() {
        let s = spec();
        assert_eq!(Duck::curve(&s, -20.5), 1.0);
        assert_eq!(Duck::curve(&s, f32::NEG_INFINITY), 1.0);
        assert!((20.0 * Duck::curve(&s, 10.0).log10() + 10.0).abs() < 1e-4);
    }
}
