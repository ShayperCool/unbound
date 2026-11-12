//! Accumulated linear parameter ramps. Mixer attenuation retains its DSP-buffer cadence;
//! effect-slot gains use the same arithmetic over their shorter measured transition.

#[derive(Debug)]
pub(crate) struct ParameterRamp {
    pub(crate) value: f32,
    target: f32,
    step: f32,
    position: u32,
}
impl ParameterRamp {
    pub(crate) fn new(value: f32) -> Self {
        Self {
            value,
            target: value,
            step: 0.0,
            position: 0,
        }
    }

    /// Slot gain controls keep changes below the measured gain deadband and bypass the
    /// multiplication at unity. Both wet and dry paths share this operation.
    pub(crate) fn gain(&mut self, target: f32, frames: u32) -> f32 {
        self.gain_channels(target, frames, 2)
    }

    pub(crate) fn gain_change_audible(
        from: f32,
        target: f32,
        frames: u32,
        channels: usize,
    ) -> bool {
        const MIN_SUM_STEP: f32 = 0.00001;
        let step = ((target - from) / frames as f32).abs();
        let mut sum = 0.0;
        for _ in 0..channels {
            sum += step;
        }
        sum >= MIN_SUM_STEP
    }

    /// The connection deadband sums the absolute gain step over the active speakers.
    pub(crate) fn gain_channels(&mut self, target: f32, frames: u32, channels: usize) -> f32 {
        // Stereo boundary controls: audiodistortion; wider layouts: audiopitch wet profile.
        if target == 1.0 {
            *self = Self::new(1.0);
            1.0
        } else if self.position == 0
            && !Self::gain_change_audible(self.value, target, frames, channels)
        {
            self.value
        } else {
            let value = self.next(target, frames);
            if self.position == 0 {
                self.value = target;
            }
            value
        }
    }

    /// Connection matrices recalculate their slope at each DSP period, including when
    /// a period ends before the connection's transition does (audiopitch wide profile).
    pub(crate) fn gain_period(
        &mut self,
        target: f32,
        frames: u32,
        channels: usize,
        beginning: bool,
    ) -> f32 {
        if target == 1.0 {
            *self = Self::new(1.0);
            return 1.0;
        }
        if beginning || target != self.target {
            self.step = (target - self.value) / frames as f32;
            if Self::gain_change_audible(self.value, target, frames, channels) {
                self.position = frames;
            }
            self.target = target;
        }
        let value = self.value;
        if self.position > 0 {
            self.value += self.step;
            self.position -= 1;
            if self.position == 0 {
                self.value = target;
            }
        }
        value
    }

    /// Returns the preceding value, then advances one frame. At the next boundary an unchanged
    /// target snaps to its endpoint, bounding accumulated roundoff. Repeated descriptor
    /// submissions never restart the slope. `frames` must be positive and fixed between resets.
    pub(crate) fn next(&mut self, target: f32, frames: u32) -> f32 {
        self.advance(target, frames, true)
    }

    /// Send gains retain their accumulated endpoint and correct its remaining error next period.
    pub(crate) fn next_retained(&mut self, target: f32, frames: u32) -> f32 {
        self.advance(target, frames, false)
    }

    fn advance(&mut self, target: f32, frames: u32, snap: bool) -> f32 {
        debug_assert!(frames > 0 && self.position < frames);
        if self.position == 0 {
            if snap && self.target == target {
                self.value = target;
                self.step = 0.0;
            } else {
                self.step = (target - self.value) / frames as f32;
            }
            self.target = target;
        }
        let value = self.value;
        self.value += self.step;
        self.position += 1;
        if self.position == frames {
            self.position = 0;
        }
        value
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn connection_gain_recalculates_from_its_current_value_at_short_periods() {
        let mut ramp = ParameterRamp::new(0.0);
        let mut values = Vec::new();
        for frame in 0..160 {
            values.push(ramp.gain_period(0.5, 64, 8, frame % 32 == 0));
        }
        assert_eq!(values[0], 0.0);
        assert_eq!(values[32], 0.25);
        assert_eq!(values[64], 0.375);
        assert_eq!(values[96], 0.4375);
        assert_eq!(values[128], 0.46875);
        assert_eq!(ramp.gain_period(1.0, 64, 8, true), 1.0);

        for period in [17, 32, 64, 97] {
            let render = |chunks: &[usize]| {
                let mut ramp = ParameterRamp::new(0.0);
                let mut values = Vec::new();
                let mut frame = 0;
                while frame < period * 10 {
                    let count = chunks[values.len() % chunks.len()].min(period * 10 - frame);
                    for _ in 0..count {
                        let target = if frame < period * 3 { 0.5 } else { 0.25 };
                        values.push(ramp.gain_period(target, 64, 5, frame % period == 0));
                        frame += 1;
                    }
                }
                values
            };
            assert_eq!(render(&[period]), render(&[1, 7, 53, 3]));
            if period >= 64 {
                assert_eq!(render(&[period])[64], 0.5);
            }
        }
    }

    #[test]
    fn wider_connections_apply_a_small_change_that_stereo_retains() {
        for channels in [1, 2, 4, 5, 6, 8] {
            let boundary = 0.00064 / channels as f32;
            let mut below = ParameterRamp::new(0.0);
            for _ in 0..128 {
                assert_eq!(below.gain_channels(boundary * 0.999, 64, channels), 0.0);
            }
            let target = boundary * 1.001;
            let mut above = ParameterRamp::new(0.0);
            assert_eq!(above.gain_channels(target, 64, channels), 0.0);
            assert!(above.gain_channels(target, 64, channels) > 0.0);
            for _ in 2..64 {
                above.gain_channels(target, 64, channels);
            }
            assert_eq!(above.gain_channels(target, 64, channels), target);
        }
        let mut stereo = ParameterRamp::new(0.0);
        let mut quad = ParameterRamp::new(0.0);
        for _ in 0..128 {
            assert_eq!(stereo.gain_channels(0.0003163, 64, 2), 0.0);
            quad.gain_channels(0.0003163, 64, 4);
        }
        assert_eq!(quad.value, 0.0003163);
    }

    #[test]
    fn gain_changes_at_the_floor_are_applied_and_changes_below_it_are_retained() {
        // Unity 6000.0.71f1, audiodistortion generator gain boundary controls.
        for target in [0.000319, 0.00032, 0.000321] {
            let mut gain = ParameterRamp::new(0.0);
            assert_eq!(gain.gain(target, 64), 0.0);
            if target < 0.00032 {
                assert!((0..128).all(|_| gain.gain(target, 64) == 0.0));
            } else {
                assert!(gain.gain(target, 64) > 0.0);
                for _ in 0..63 {
                    gain.gain(target, 64);
                }
                assert_eq!(gain.gain(target, 64), target);
            }
        }
    }

    #[test]
    fn gain_reaches_endpoint_and_repeated_targets_preserve_accumulation() {
        let mut ramp = ParameterRamp::new(1.0);
        let mut expected = 1.0;
        let step = (0.25106102 - expected) / 64.0;
        for _ in 0..64 {
            assert_eq!(ramp.next(0.25106102, 64), expected);
            expected += step;
        }
        assert_eq!(ramp.next(0.25106102, 64), 0.25106102);
    }

    #[test]
    fn slot_gains_skip_small_changes_and_unity_bypasses_the_ramp() {
        let mut wet = ParameterRamp::new(0.25);
        assert_eq!(wet.gain(1.0, 64), 1.0);
        assert_eq!(wet.gain(0.25, 64), 1.0);
        for _ in 1..64 {
            wet.gain(0.25, 64);
        }
        let settled = wet.value;
        assert_eq!(wet.gain(settled + 0.0001, 64), settled);
        assert_eq!(wet.gain(1.0, 64), 1.0);
        assert_eq!(wet.gain(0.0, 64), 1.0);
    }
}
