//! Unity releases and the compatibility profiles that select per-release behaviour.
mod audio;
mod family;
mod lighting;
mod mtl;
mod packages;
mod profile;
mod shader_keywords;
mod sort;
mod texture;
mod version;

pub use audio::AudioOutputRules;
pub use family::MeasuredFamily;
pub use lighting::{HdrEncoding, LightingEncodings};
pub use mtl::{MtlColor, MtlRules, MtlTransparency};
pub use packages::PackageResolutionRules;
pub use profile::{CompileMode, Feature, InputHandling, Profile, ProfileData};
pub use shader_keywords::ShaderKeywordRules;
pub use sort::EditorSort;
pub use texture::TextureCompressionRules;
pub use unbound_graphics::{
    Conventions, DeviceRequest, GraphicsDevice, HostOs, candidates, forced_by_option,
    parse_api_list, select as select_device,
};
pub use version::{Stage, UnityVersion};
