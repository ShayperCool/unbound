//! Package resolution measured in tests/projects/packageresolve.
use crate::UnityVersion;

/// How a release's Package Manager resolves a project without `Packages/packages-lock.json`.
/// Other releases keep the highest version asked for until they are measured.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct PackageResolutionRules {
    /// A registry package another package asks for below the version the Editor's own manifest
    /// (`Resources/PackageManager/Editor/manifest.json`) names as its minimum resolves to that
    /// minimum; the project's own dependencies keep the version they name.
    pub editor_minimum_versions: bool,
}

impl PackageResolutionRules {
    pub fn for_version(version: UnityVersion) -> Self {
        Self {
            editor_minimum_versions: version == UnityVersion::new(6000, 0, 71),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn editor_minimums_are_scoped_to_the_measured_release() {
        assert!(
            PackageResolutionRules::for_version(UnityVersion::new(6000, 0, 71))
                .editor_minimum_versions
        );
        for version in [
            UnityVersion::new(6000, 0, 70),
            UnityVersion::new(2022, 3, 71),
        ] {
            assert!(!PackageResolutionRules::for_version(version).editor_minimum_versions);
        }
    }
}
