//! Unity releases and the compatibility profiles that select per-release behaviour.
mod profile;
mod shader_keywords;
mod version;

pub use profile::{CompileMode, Feature, InputHandling, Profile, ProfileData};
pub use shader_keywords::ShaderKeywordRules;
pub use version::{Stage, UnityVersion};
