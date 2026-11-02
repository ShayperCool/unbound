//! Which rules answer shader keyword metadata (`Shader.keywordSpace`, `ComputeShader.keywordSpace`)
//! for a release.
//!
//! The order Unity lists a project shader's declared keywords in, which `#pragma` sets expand to
//! which keywords, and the four implicit stereo keywords every space starts with are engine
//! behaviour that can change between releases. They are measured for exactly one release
//! (tests/projects/shaderkeywords `Reference/unity-6000.0.71f1.jsonl`, a batchmode GPU Linux
//! Editor on OpenGLCore). Other patches of the family are not covered by that reference, so they
//! get no rules: their project shaders' keyword spaces are reported as unknown and the managed
//! side keeps its name-based behaviour, with a warning. The managed side selects its own tables
//! (built-in global keywords, implicit keyword types) for the same release in `Compat.cs`.
use crate::version::UnityVersion;

/// A set of keyword-space rules, named by the release that measured it.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ShaderKeywordRules {
    /// 6000.0.71f1: stereo keywords first, then each pass's keywords stage by stage (global
    /// declarations, the fog/instancing/particle sets, local declarations, the light pass sets);
    /// compute shaders list local declarations before global ones.
    Unity6000_0_71f1,
}

/// The releases a rule set was measured on, exactly.
const MEASURED: &[(UnityVersion, ShaderKeywordRules)] = &[(
    UnityVersion::new(6000, 0, 71),
    ShaderKeywordRules::Unity6000_0_71f1,
)];

impl ShaderKeywordRules {
    /// The rules measured on exactly this release (None: not measured, keyword spaces unknown).
    pub fn for_release(version: UnityVersion) -> Option<ShaderKeywordRules> {
        MEASURED
            .iter()
            .find(|(release, _)| *release == version)
            .map(|(_, rules)| *rules)
    }

    /// The release the rules were measured on (for messages).
    pub fn release(self) -> UnityVersion {
        MEASURED
            .iter()
            .find(|(_, rules)| *rules == self)
            .map(|(release, _)| *release)
            .expect("every rule set names its release")
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn rules(v: &str) -> Option<ShaderKeywordRules> {
        ShaderKeywordRules::for_release(v.parse().unwrap())
    }

    #[test]
    fn only_the_measured_release_has_rules() {
        assert_eq!(
            rules("6000.0.71f1"),
            Some(ShaderKeywordRules::Unity6000_0_71f1)
        );
        assert_eq!(
            ShaderKeywordRules::Unity6000_0_71f1.release().to_string(),
            "6000.0.71f1"
        );
        // Other patches, builds and stages of the family are not measured: no family-wide rule.
        for other in [
            "6000.0.70f1",
            "6000.0.72f1",
            "6000.0.71f2",
            "6000.0.71b1",
            "6000.0.71p1",
            "6000.1.0f1",
            "2022.3.10f1",
        ] {
            assert_eq!(rules(other), None, "{other}");
        }
    }
}
