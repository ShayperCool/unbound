//! Textures and cubemaps stored in Unity's serialized files (the built-in default reflection
//! cubemap), read by their type trees.
use crate::{Importer, serialized::SerializedFile};
use anyhow::{Context, Result, bail};
use serde::{Deserialize, Serialize};
use std::path::Path;
use unbound_yaml::Value;

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct SerializedTexture {
    pub name: String,
    pub width: i32,
    pub height: i32,
    /// Unity's TextureFormat.
    pub format: i32,
    pub mip_count: i32,
    /// Images (6 for a cubemap), each with its mip chain in turn.
    pub image_count: i32,
    /// Unity's TextureDimension.
    pub dimension: i32,
    pub filter_mode: i32,
    pub wrap_u: i32,
    pub wrap_v: i32,
    pub aniso: i32,
    /// `m_ColorSpace`: 1 linear (sampled as stored), 0 gamma (sRGB).
    pub color_space: i32,
    /// `m_LightmapFormat` (5: an RGBM encoded reflection).
    pub lightmap_format: i32,
    #[serde(with = "serde_bytes")]
    pub data: Vec<u8>,
}

fn int(v: &Value, key: &str) -> i32 {
    v.get(key)
        .and_then(Value::as_str)
        .and_then(|s| s.parse().ok())
        .unwrap_or(0)
}

/// A Texture2D or Cubemap's fields with its image data in the file (not streamed).
pub fn read_texture(object: &Value) -> Result<SerializedTexture> {
    let hex = object
        .get("image data")
        .and_then(Value::as_str)
        .context("a texture without image data")?;
    if hex.is_empty() {
        bail!("the texture's image data is streamed from another file, which is not read yet");
    }
    let data = (0..hex.len() / 2)
        .map(|i| u8::from_str_radix(&hex[2 * i..2 * i + 2], 16))
        .collect::<Result<Vec<u8>, _>>()
        .context("image data")?;
    let settings = object.get("m_TextureSettings");
    let setting = |key: &str| settings.map_or(0, |s| int(s, key));
    Ok(SerializedTexture {
        name: object.get("m_Name").and_then(Value::as_str).unwrap_or("").to_string(),
        width: int(object, "m_Width"),
        height: int(object, "m_Height"),
        format: int(object, "m_TextureFormat"),
        mip_count: int(object, "m_MipCount").max(1),
        image_count: int(object, "m_ImageCount").max(1),
        dimension: int(object, "m_TextureDimension"),
        filter_mode: setting("m_FilterMode"),
        wrap_u: setting("m_WrapU"),
        wrap_v: setting("m_WrapV"),
        aniso: setting("m_Aniso"),
        color_space: int(object, "m_ColorSpace"),
        lightmap_format: int(object, "m_LightmapFormat"),
        data,
    })
}

/// The texture `path_id` of a serialized file.
pub struct SerializedTextureImporter {
    pub path_id: i64,
}

impl Importer for SerializedTextureImporter {
    type Output = SerializedTexture;
    const KIND: &'static str = "serialized-texture";
    const VERSION: u32 = 2;

    fn import(&self, _source: &Path, bytes: &[u8], _meta: &Value) -> Result<SerializedTexture> {
        let file = SerializedFile::parse(bytes)?;
        let mut texture = read_texture(&file.read(self.path_id)?)?;
        // A Cubemap object (class 89) is a cube whatever its stored dimension says.
        if file.info(self.path_id)?.class_id == 89 {
            texture.dimension = 4;
        }
        Ok(texture)
    }

    fn options_key(&self) -> String {
        self.path_id.to_string()
    }
}
