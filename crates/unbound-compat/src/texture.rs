//! Texture encoders measured in tests/projects/texturecompression.
use crate::{MeasuredFamily, UnityVersion};
use serde::{Deserialize, Serialize};

/// A release family's measured encoders for imports and Texture2D.Compress. Other families
/// retain their existing approximations until their settings are measured.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Hash, Serialize, Deserialize)]
pub struct TextureCompressionRules {
    pub measured_import_encoders: bool,
    #[serde(default)]
    pub measured_runtime_encoder: bool,
    #[serde(default)]
    #[serde(alias = "measured_astc_fast")]
    pub measured_astc_encoders: bool,
}

impl TextureCompressionRules {
    pub fn for_version(version: UnityVersion) -> Self {
        let measured = version.measured_family() == Some(MeasuredFamily::Unity6000_0);
        Self {
            measured_import_encoders: measured,
            measured_runtime_encoder: measured,
            measured_astc_encoders: measured,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn measured_encoders_are_scoped_to_the_reference_family() {
        for version in [
            UnityVersion::new(6000, 0, 71),
            UnityVersion::new(6000, 0, 0),
            UnityVersion::new(6000, 0, 67),
        ] {
            let rules = TextureCompressionRules::for_version(version);
            assert!(rules.measured_import_encoders);
            assert!(rules.measured_runtime_encoder);
            assert!(rules.measured_astc_encoders);
        }
        for version in [
            UnityVersion::new(6000, 3, 0),
            UnityVersion::new(2022, 3, 71),
        ] {
            assert!(!TextureCompressionRules::for_version(version).measured_import_encoders);
            assert!(!TextureCompressionRules::for_version(version).measured_runtime_encoder);
            assert!(!TextureCompressionRules::for_version(version).measured_astc_encoders);
        }
        assert!(
            TextureCompressionRules::for_version(UnityVersion::new(6000, 0, 71))
                .measured_runtime_encoder
        );
    }
}
