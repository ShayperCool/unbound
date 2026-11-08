//! Accumulated linear parameter ramps. Mixer attenuation retains its DSP-buffer cadence;
//! effect-slot gains use the same arithmetic over their shorter measured transition.

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
        // Unity 6000.0.71f1, tests/projects/audiodistortion live gain-delta controls.
        const MIN_GAIN_CHANGE: f32 = 0.00032;
        if target == 1.0 {
            *self = Self::new(1.0);
            1.0
        } else if self.position == 0 && (target - self.value).abs() <= MIN_GAIN_CHANGE {
            self.value
        } else {
            let value = self.next(target, frames);
            if self.position == 0 {
                self.value = target;
            }
            value
        }
    }

    /// Returns the preceding value, then advances one frame. At the next boundary an unchanged
    /// target snaps to its endpoint, bounding accumulated roundoff. Repeated descriptor
    /// submissions never restart the slope. `frames` must be positive and fixed between resets.
    pub(crate) fn next(&mut self, target: f32, frames: u32) -> f32 {
        debug_assert!(frames > 0 && self.position < frames);
        if self.position == 0 {
            if self.target == target {
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
