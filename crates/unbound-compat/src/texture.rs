//! Import encoders measured in tests/projects/texturecompression.
use crate::UnityVersion;
use serde::{Deserialize, Serialize};

/// A release's measured import-encoder selection. Other releases retain the existing ISPC
/// approximation until their settings are measured.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub struct TextureCompressionRules {
    pub measured_import_encoders: bool,
}

impl TextureCompressionRules {
    pub fn for_version(version: UnityVersion) -> Self {
        Self {
            measured_import_encoders: version == UnityVersion::new(6000, 0, 71),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn measured_encoders_are_scoped_to_the_reference_release() {
        assert!(
            TextureCompressionRules::for_version(UnityVersion::new(6000, 0, 71))
                .measured_import_encoders
        );
        for version in [
            UnityVersion::new(6000, 0, 70),
            UnityVersion::new(2022, 3, 71),
        ] {
            assert!(!TextureCompressionRules::for_version(version).measured_import_encoders);
        }
    }
}
