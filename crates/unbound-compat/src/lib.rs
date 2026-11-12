//! Unity releases and the compatibility profiles that select per-release behaviour.
mod graphics;
mod mtl;
mod profile;
mod shader_keywords;
mod texture;
mod version;

pub use graphics::{
    Conventions, DeviceRequest, GraphicsDevice, HostOs, candidates, forced_by_option,
    parse_api_list, select as select_device,
};
pub use mtl::{MtlColor, MtlRules, MtlTransparency};
pub use profile::{CompileMode, Feature, InputHandling, Profile, ProfileData};
pub use shader_keywords::ShaderKeywordRules;
pub use texture::TextureCompressionRules;
pub use version::{Stage, UnityVersion};
