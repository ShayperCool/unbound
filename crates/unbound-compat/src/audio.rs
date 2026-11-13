//! Output configuration measured by tests/projects/audiopitch.
use crate::UnityVersion;

// Unity 6000.0.71f1, tests/projects/audiopitch configuration controls.
const DEFAULT_BUFFER: u32 = 1024;
const LEGACY_MINIMUM_BUFFER: u32 = 64;

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct AudioOutputRules {
    pub requested_buffer_sizes: bool,
    pub scheduled_start_times: bool,
}

impl AudioOutputRules {
    /// Largest buffer selected by the measured output configuration controls.
    pub const MAXIMUM_BUFFER: u32 = 8192;
    pub fn for_version(version: UnityVersion) -> Self {
        Self {
            requested_buffer_sizes: version == UnityVersion::new(6000, 0, 71),
            scheduled_start_times: version == UnityVersion::new(6000, 0, 71),
        }
    }

    pub fn buffer_size(self, requested: u32) -> u32 {
        if self.requested_buffer_sizes {
            if requested == 0 {
                DEFAULT_BUFFER
            } else {
                requested.min(Self::MAXIMUM_BUFFER)
            }
        } else {
            // Other releases retain the previous unmeasured minimum.
            requested.max(LEGACY_MINIMUM_BUFFER)
        }
    }

    /// Start times and delays select the preceding whole sample frame. Other releases
    /// retain the previous, unmeasured nearest-frame conversion.
    pub fn scheduled_frame(self, seconds: f64, rate: u32) -> u64 {
        let frames = seconds.max(0.0) * rate as f64;
        if self.scheduled_start_times {
            frames as u64
        } else {
            frames.round() as u64
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn buffer_selection_matches_the_editor_configuration_reference() {
        let rows: serde_json::Value = serde_json::from_str(include_str!(
            "../../../tests/projects/audiopitch/Reference/unity-6000.0.71f1/wide/configurations.json"
        ))
        .unwrap();
        let rules = AudioOutputRules::for_version(UnityVersion::new(6000, 0, 71));
        for row in rows.as_array().unwrap() {
            assert_eq!(row["ok"], true);
            assert_eq!(
                rules.buffer_size(row["buffer"].as_u64().unwrap() as u32),
                row["actual_buffer"].as_u64().unwrap() as u32
            );
        }
    }

    #[test]
    fn positive_buffer_requests_are_preserved_only_on_the_measured_release() {
        let measured = AudioOutputRules::for_version(UnityVersion::new(6000, 0, 71));
        assert!(measured.scheduled_start_times);
        for size in [1, 16, 31, 32, 33, 64, 340, 8192] {
            assert_eq!(measured.buffer_size(size), size);
        }
        assert_eq!(measured.buffer_size(0), 1024);
        assert_eq!(measured.buffer_size(16384), 8192);
        for version in [
            UnityVersion::new(6000, 0, 70),
            UnityVersion::new(6000, 0, 72),
        ] {
            let other = AudioOutputRules::for_version(version);
            assert!(!other.scheduled_start_times);
            assert_eq!(other.buffer_size(32), 64);
            assert_eq!(other.buffer_size(340), 340);
        }
    }
}
