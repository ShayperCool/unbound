//! Unity releases and the compatibility profiles that select per-release behaviour.
mod profile;
mod version;

pub use profile::{CompileMode, Feature, InputHandling, Profile, ProfileData};
pub use version::{Stage, UnityVersion};
