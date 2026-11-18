//! Which rules answer shader keyword metadata (`Shader.keywordSpace`, `ComputeShader.keywordSpace`)
//! for a release.
//!
//! The order Unity lists a project shader's declared keywords in, which `#pragma` sets expand to
//! which keywords, and the four implicit stereo keywords every space starts with are engine
//! behaviour that can change between release families. They are measured for the 6000.0 family on
//! its 6000.0.71f1 Editor (tests/projects/shaderkeywords `Reference/unity-6000.0.71f1.jsonl`, a
//! batchmode GPU Linux Editor on OpenGLCore), and every release of the family gets them. Other
//! families get no rules: their project shaders' keyword spaces are reported as unknown and the
//! managed side keeps its name-based behaviour, with a warning. The managed side selects its own
//! tables (built-in global keywords, implicit keyword types) for the same family in `Compat.cs`.
use crate::{family::MeasuredFamily, version::UnityVersion};

/// A set of keyword-space rules, named by the release family that measured it.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ShaderKeywordRules {
    /// 6000.0 (measured on 6000.0.71f1): stereo keywords first, then each pass's keywords stage by
    /// stage (global declarations, the fog/instancing/particle sets, local declarations, the light
    /// pass sets); compute shaders list local declarations before global ones.
    Unity6000_0,
}

impl ShaderKeywordRules {
    /// The rules of the release's family (None: not measured, keyword spaces unknown).
    pub fn for_release(version: UnityVersion) -> Option<ShaderKeywordRules> {
        match version.measured_family()? {
            MeasuredFamily::Unity6000_0 => Some(ShaderKeywordRules::Unity6000_0),
        }
    }

    /// The release the rules were measured on (for messages).
    pub fn release(self) -> UnityVersion {
        match self {
            ShaderKeywordRules::Unity6000_0 => MeasuredFamily::Unity6000_0.release(),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn rules(v: &str) -> Option<ShaderKeywordRules> {
        ShaderKeywordRules::for_release(v.parse().unwrap())
    }

    #[test]
    fn every_release_of_the_measured_family_has_its_rules() {
        assert_eq!(
            ShaderKeywordRules::Unity6000_0.release().to_string(),
            "6000.0.71f1"
        );
        for release in [
            "6000.0.71f1",
            "6000.0.0f1",
            "6000.0.67f1",
            "6000.0.72f1",
            "6000.0.71f2",
            "6000.0.71b1",
            "6000.0.71p1",
        ] {
            assert_eq!(
                rules(release),
                Some(ShaderKeywordRules::Unity6000_0),
                "{release}"
            );
        }
        // Other families are other versions: no rules until they are measured.
        for other in ["6000.1.0f1", "6000.3.0f1", "2022.3.10f1"] {
            assert_eq!(rules(other), None, "{other}");
        }
    }
}
