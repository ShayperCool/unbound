//! Asset importers: turn a source asset and its `.meta` import settings into engine-ready data, the
//! way Unity's importers do, plus the content-addressed cache that keeps their outputs.
//!
//! * [`texture`]: png/jpg/tga/bmp/gif/psd/tiff/hdr/exr with `TextureImporter` settings (alpha
//!   source, NPOT scaling, max size, mipmaps, sampler state) and the [`sprite`]s it defines.
//! * [`audio`]: wav/mp3/ogg/aiff/flac with `AudioImporter` settings.
//! * [`model`]: FBX/OBJ with `ModelImporter` settings, converted to Unity's coordinate system, with
//!   the sub-asset fileIDs the project's scenes and prefabs reference.
//! * [`cache`]: imports keyed by the content hash of the source, its `.meta`, the importer kind and
//!   version, stored under `<project>/Library/Unbound/Imports/`.
//!
//! The library has no engine dependency: the runtime converts these outputs to its own types.
pub mod audio;
pub mod cache;
pub mod meta;
pub mod model;
pub mod sprite;
pub mod texture;

pub use audio::{AudioImporter, AudioSettings, ImportedAudio};
pub use cache::{AssetCache, Outcome};
pub use meta::{FileIdSource, FileIdTable};
pub use model::{ImportedModel, ModelImporter, ModelSettings};
pub use sprite::{SpriteDef, SpriteMeshType, SpriteMode};
pub use texture::{ImportedTexture, TextureImporter, TextureSettings};

use anyhow::Result;
use serde::{Serialize, de::DeserializeOwned};
use std::path::Path;
use unbound_yaml::Value;

/// Bumped whenever the layout of any cached output changes; part of every cache key.
pub const IMPORTER_FORMAT_VERSION: u32 = 1;

/// An importer: a pure function of the source bytes, the parsed `.meta` and its own options.
pub trait Importer: Sync {
    type Output: Serialize + DeserializeOwned + Send + Sync + 'static;
    /// Directory name in the cache and part of the cache key: `textures`, `audio`, `models`.
    const KIND: &'static str;
    /// Bumped whenever this importer's output for the same inputs changes.
    const VERSION: u32;

    /// `source` is only used for its name and extension; the content is `bytes`. `meta` is the
    /// parsed `.meta` file, or [`Value::Null`] when the asset has none (defaults apply).
    fn import(&self, source: &Path, bytes: &[u8], meta: &Value) -> Result<Self::Output>;

    /// Options of this importer instance that change its output (the build target, ...). They are
    /// part of the cache key.
    fn options_key(&self) -> String {
        String::new()
    }
}

/// What kind of asset a file is, by extension, as far as these importers are concerned.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum AssetKind {
    Texture,
    Audio,
    Model,
}

impl AssetKind {
    pub fn from_path(path: &Path) -> Option<Self> {
        let ext = path.extension()?.to_str()?.to_ascii_lowercase();
        if texture::EXTENSIONS.contains(&ext.as_str()) {
            Some(Self::Texture)
        } else if audio::EXTENSIONS.contains(&ext.as_str()) {
            Some(Self::Audio)
        } else if model::EXTENSIONS.contains(&ext.as_str()) {
            Some(Self::Model)
        } else {
            None
        }
    }
}

/// The asset name Unity gives a main object: the file name without its extension.
pub(crate) fn asset_name(path: &Path) -> String {
    path.file_stem()
        .map(|s| s.to_string_lossy().into_owned())
        .unwrap_or_default()
}
