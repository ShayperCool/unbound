//! The modulated delay line shared by mixer Chorus and Flange and by `AudioChorusFilter`,
//! matching the Unity 6000.0.71f1 Editor (tests/projects/audiochorus,
//! tests/projects/audioflange). The input is kept as Q14 stereo history; a read tap trails the
//! input by a span that slews toward its target and is swept by the table cosine, and the two
//! history frames around the tap are interpolated linearly. Each effect chooses the history
//! length, the target span and how the channels' taps relate ([`Taps`]). These are
//! measurements of one release, not a documented Unity DSP contract.
use super::wavetable;

/// A backend resource limit on one delay history, not a restriction of Unity's parameters.
pub(crate) const MAX_HISTORY_BYTES: usize = 8 * 1024 * 1024;
/// Fixed-point scale of the history (Q14).
const Q14: f32 = 16384.0;
/// The span moves toward its target by at most this many frames per frame.
const SPAN_STEP: f32 = 0.015625;
/// The shortest target span, in frames.
const MIN_SPAN: f32 = 4.0;

/// How the two channels read the history.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) enum Taps {
    /// Both channels read at one coordinate (Flange).
    Shared,
    /// The right channel's sweep runs a quarter period after the left's (Chorus).
    Quadrature,
}

pub(crate) struct ModulatedDelay {
    history: Vec<[i16; 2]>,
    position: usize,
    /// Frames into the current DSP buffer: the read coordinates keep the configured buffer's
    /// origin when the mixer calls with smaller chunks.
    offset: usize,
    buffer: usize,
    rate: u32,
    phase: f32,
    span: f32,
}

impl ModulatedDelay {
    /// History frames for an effect reaching `reach(rate)` frames back: whole DSP buffers
    /// beyond the reach, at least two.
    pub(crate) fn frames(
        effect: &str,
        rate: u32,
        buffer: u32,
        reach: fn(u32) -> usize,
    ) -> anyhow::Result<usize> {
        anyhow::ensure!(rate > 0, "AudioMixer {effect} output rate must be positive");
        let buffer = buffer.max(1) as usize;
        ((reach(rate) / buffer + 1).max(2))
            .checked_mul(buffer)
            .filter(|n| *n <= MAX_HISTORY_BYTES / std::mem::size_of::<[i16; 2]>())
            .ok_or_else(|| {
                anyhow::anyhow!("AudioMixer {effect} history exceeds the supported 8 MiB limit")
            })
    }

    pub(crate) fn new(
        effect: &str,
        rate: u32,
        buffer: u32,
        reach: fn(u32) -> usize,
    ) -> anyhow::Result<Self> {
        let frames = Self::frames(effect, rate, buffer, reach)?;
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

    pub(crate) fn rate(&self) -> u32 {
        self.rate
    }

    pub(crate) fn configuration(&self) -> (u32, u32) {
        (self.rate, self.buffer as u32)
    }

    pub(crate) fn clear(&mut self) {
        self.restart();
        self.span = 0.0;
    }

    /// Clears the history and restarts the write position and sweep phase; the current span
    /// continues from where it was.
    pub(crate) fn restart(&mut self) {
        self.history.fill([0; 2]);
        self.position = 0;
        self.offset = 0;
        self.phase = 0.0;
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
        // A tiny negative coordinate plus the history length can round to the end. Native's
        // Chorus rate20 capture reads zero there, rather than wrapping to the current input frame.
        if index == self.history.len() {
            0.0
        } else {
            self.history[index][channel] as f32 * (1.0 / Q14)
        }
    }

    /// The history indices around the tap and the fraction between them, for the read at
    /// `frame` of the current DSP buffer whose origin is `base`.
    fn tap(&self, base: f32, frame: f32, phase: f32) -> (usize, usize, f32) {
        let wave = (wavetable::cosine(phase) + 1.0) * 0.5;
        let shift = wave * self.span - self.span;
        let read = self.wrap((base + shift) + frame);
        // Round the next floating coordinate before converting either to an index.
        // Replacing this with `index + 1` changes measured near-zero-delay samples.
        let next = self.wrap(read + 1.0);
        (read as usize, next as usize, read - read as usize as f32)
    }

    /// Mixes `dry` input with the delayed signal at `wet[channel]` in place. `target` is the
    /// span in frames before its lower limit, `sweep` the modulation rate in Hz.
    pub(crate) fn process(
        &mut self,
        samples: &mut [f32],
        target: f32,
        sweep: f32,
        taps: Taps,
        dry: f32,
        wet: [f32; 2],
    ) {
        let target = target.max(MIN_SPAN);
        let increment = sweep / self.rate as f32;
        let mut done = 0;
        while done < samples.len() / 2 {
            let count = (samples.len() / 2 - done)
                .min(self.buffer - self.offset)
                .min(self.history.len() - self.position);
            // Copy this input chunk before reading: rounding at a nearly zero delay can read
            // the following input frame.
            for (history, frame) in self.history[self.position..self.position + count]
                .iter_mut()
                .zip(samples[done * 2..(done + count) * 2].chunks_exact(2))
            {
                for c in 0..2 {
                    history[c] = (frame[c] * Q14) as i32 as i16;
                }
            }
            let base = (self.position - self.offset) as f32;
            for f in 0..count {
                self.span = if target - SPAN_STEP > self.span {
                    self.span + SPAN_STEP
                } else if self.span > target + SPAN_STEP {
                    self.span - SPAN_STEP
                } else {
                    target
                };
                let frame = (self.offset + f) as f32;
                let left = self.tap(base, frame, self.phase);
                let right = match taps {
                    Taps::Shared => left,
                    Taps::Quadrature => self.tap(base, frame, self.phase + 0.25),
                };
                for (c, (read, next, fraction)) in [left, right].into_iter().enumerate() {
                    let a = self.sample(read, c);
                    let b = self.sample(next, c);
                    let x = &mut samples[(done + f) * 2 + c];
                    *x = *x * dry + (a * (1.0 - fraction) + b * fraction) * wet[c];
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

    fn reach(rate: u32) -> usize {
        rate as usize / 10
    }

    #[test]
    fn restart_clears_history_position_and_phase_but_keeps_the_span() {
        let mut line = ModulatedDelay::new("Chorus", 48000, 1024, reach).unwrap();
        let mut samples = vec![0.25; 2 * 3000];
        line.process(
            &mut samples,
            210.0,
            5.0,
            Taps::Quadrature,
            0.25,
            [0.75, 0.375],
        );
        let span = line.span;
        assert!(span > 0.0 && span < 210.0);
        line.restart();
        assert_eq!(line.span, span);
        assert_eq!((line.position, line.offset, line.phase), (0, 0, 0.0));
        assert!(line.history.iter().all(|frame| *frame == [0, 0]));
        line.clear();
        assert_eq!(line.span, 0.0);
    }

    #[test]
    fn rounded_history_endpoint_is_zero_extended() {
        let line = ModulatedDelay::new("Chorus", 48000, 1024, reach).unwrap();
        for read in [-f32::EPSILON, -0.001, 0.0, 5119.9995, 5120.0, 5121.0] {
            let read = line.wrap(read);
            assert!((read as usize) <= line.history.len());
            assert!((line.wrap(read + 1.0) as usize) <= line.history.len());
            if read as usize == line.history.len() {
                assert_eq!(line.sample(read as usize, 0), 0.0);
            }
        }
    }

    #[test]
    fn shared_taps_equal_quadrature_taps_without_the_quarter_offset() {
        // With equal channels, a shared tap gives both channels the left channel's output.
        let input: Vec<f32> = (0..8192)
            .flat_map(|f| {
                let v = ((f * 37) % 997) as f32 / 2048.0 - 0.25;
                [v, v]
            })
            .collect();
        let mut shared = input.clone();
        let mut quadrature = input;
        ModulatedDelay::new("Flange", 44100, 1024, reach)
            .unwrap()
            .process(&mut shared, 300.0, 3.0, Taps::Shared, 0.5, [0.5, 0.5]);
        ModulatedDelay::new("Chorus", 44100, 1024, reach)
            .unwrap()
            .process(
                &mut quadrature,
                300.0,
                3.0,
                Taps::Quadrature,
                0.5,
                [0.5, 0.5],
            );
        assert!(shared.chunks_exact(2).all(|f| f[0] == f[1]));
        assert!(
            shared
                .chunks_exact(2)
                .zip(quadrature.chunks_exact(2))
                .all(|(s, q)| s[0] == q[0])
        );
        assert!(
            shared
                .chunks_exact(2)
                .zip(quadrature.chunks_exact(2))
                .any(|(s, q)| s[1] != q[1])
        );
    }

    #[test]
    fn histories_are_bounded_and_need_a_positive_rate() {
        assert!(ModulatedDelay::frames("Chorus", 0, 1024, reach).is_err());
        assert!(ModulatedDelay::frames("Chorus", 48000, u32::MAX, reach).is_err());
        assert!(ModulatedDelay::frames("Chorus", u32::MAX, 1024, reach).is_err());
        assert_eq!(
            ModulatedDelay::frames("Chorus", 48000, 1024, reach).unwrap(),
            5 * 1024
        );
        assert_eq!(
            ModulatedDelay::frames("Chorus", 100, 1024, reach).unwrap(),
            2 * 1024
        );
    }
}
