//! Texture encoders measured in tests/projects/texturecompression.
use crate::UnityVersion;
use serde::{Deserialize, Serialize};

/// A release's measured encoders for imports and Texture2D.Compress. Other releases retain
/// their existing approximations until their settings are measured.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub struct TextureCompressionRules {
    pub measured_import_encoders: bool,
    #[serde(default)]
    pub measured_runtime_encoder: bool,
    #[serde(default)]
    pub measured_astc_fast: bool,
}

impl TextureCompressionRules {
    pub fn for_version(version: UnityVersion) -> Self {
        let measured = version == UnityVersion::new(6000, 0, 71);
        Self {
            measured_import_encoders: measured,
            measured_runtime_encoder: measured,
            measured_astc_fast: measured,
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
            assert!(!TextureCompressionRules::for_version(version).measured_runtime_encoder);
            assert!(!TextureCompressionRules::for_version(version).measured_astc_fast);
        }
        assert!(
            TextureCompressionRules::for_version(UnityVersion::new(6000, 0, 71))
                .measured_runtime_encoder
        );
    }
}
