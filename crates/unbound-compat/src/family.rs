//! Release families with measured behaviour.
//!
//! Unity releases are grouped by `MAJOR.MINOR`: every release of a family (`6000.0.0f1`,
//! `6000.0.67f1`, `6000.0.71f1`, its betas and patch releases) is one version for compatibility
//! purposes. A family's answers are measured on one Editor release of it and serve every release
//! of the family; another family (`6000.1`, `6000.3`) is a different version and gets its own
//! answers once it is measured. A release outside every measured family keeps each gate's
//! unmeasured behaviour (a fallback, or a loud failure).
use crate::version::UnityVersion;

/// A release family whose behaviour Unbound has measured.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum MeasuredFamily {
    /// Unity 6000.0, measured on the 6000.0.71f1 Editor.
    Unity6000_0,
}

/// Each measured family with the release its answers were measured on.
const MEASURED: &[(MeasuredFamily, UnityVersion)] =
    &[(MeasuredFamily::Unity6000_0, UnityVersion::new(6000, 0, 71))];

impl MeasuredFamily {
    /// The measured family `version` belongs to; None outside every measured family.
    pub fn of(version: UnityVersion) -> Option<Self> {
        MEASURED
            .iter()
            .find(|(_, release)| release.family() == version.family())
            .map(|(family, _)| *family)
    }

    /// The Editor release the family's answers were measured on (for messages and references).
    pub fn release(self) -> UnityVersion {
        MEASURED
            .iter()
            .find(|(family, _)| *family == self)
            .map(|(_, release)| *release)
            .expect("every measured family names its release")
    }
}

impl UnityVersion {
    /// The measured family of this release (see [`MeasuredFamily`]).
    pub fn measured_family(&self) -> Option<MeasuredFamily> {
        MeasuredFamily::of(*self)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn family(v: &str) -> Option<MeasuredFamily> {
        v.parse::<UnityVersion>().unwrap().measured_family()
    }

    #[test]
    fn every_release_of_a_family_is_one_version() {
        for release in [
            "6000.0.0f1",
            "6000.0.0b16",
            "6000.0.62f1",
            "6000.0.67f1",
            "6000.0.71f1",
            "6000.0.71f2",
            "6000.0.71p1",
            "6000.0.99f1",
        ] {
            assert_eq!(
                family(release),
                Some(MeasuredFamily::Unity6000_0),
                "{release}"
            );
        }
        assert_eq!(
            MeasuredFamily::Unity6000_0.release().to_string(),
            "6000.0.71f1"
        );
    }

    #[test]
    fn other_families_are_other_versions() {
        for release in [
            "6000.1.0f1",
            "6000.3.0f1",
            "6000.6.3f1",
            "2022.3.71f1",
            "6001.0.0f1",
        ] {
            assert_eq!(family(release), None, "{release}");
        }
    }
}
