//! What a sprite atlas asset says: a Sprite Atlas V1 `.spriteatlas` (its `m_EditorData`) or a
//! Sprite Atlas V2 `.spriteatlasv2` (the asset's packables and master, the importer's settings
//! in its `.meta`).
use anyhow::{Context, Result, bail};
use unbound_yaml::{Guid, Map, Value};

/// A sprite atlas's settings and packables.
#[derive(Debug, Clone, PartialEq)]
pub struct AtlasDefinition {
    /// `SpriteAtlas.name`: V1's `m_Name`, V2's file name.
    pub name: String,
    /// `SpriteAtlas.tag`: V1's `m_Tag` (a variant's is its master's), else the name.
    pub tag: String,
    pub v2: bool,
    /// Sprites, textures and folders, as (GUID, file id).
    pub packables: Vec<(Guid, i64)>,
    /// "Include in Build" (`bindAsDefault`): off, the atlas binds late (atlasRequested).
    pub include_in_build: bool,
    /// A variant's master atlas and scale.
    pub variant: Option<(Guid, f32)>,
    pub padding: u32,
    pub block_offset: u32,
    pub allow_rotation: bool,
    pub tight: bool,
    pub alpha_dilation: bool,
    pub alpha_splitting: bool,
    /// The page textures' settings as a TextureImporter `.meta` would hold them (texture
    /// settings and platform settings), for the texture importer's format selection.
    pub texture_meta: Value,
}

fn int(v: &Value, key: &str) -> Option<i64> {
    v.get(key).and_then(Value::as_i64)
}

fn float(v: &Value, key: &str) -> Option<f64> {
    v.get(key).and_then(Value::as_f64)
}

fn scalar(v: impl ToString) -> Value {
    Value::Scalar(v.to_string())
}

fn map(entries: Vec<(&str, Value)>) -> Value {
    Value::Map(Map::from(
        entries
            .into_iter()
            .map(|(k, v)| (k.to_string(), v))
            .collect::<Vec<_>>(),
    ))
}

/// `m_BuildTarget` → `buildTarget`: V1 prefixes its platform settings' fields.
fn unprefixed(key: &str) -> String {
    match key.strip_prefix("m_") {
        Some(rest) => {
            let mut chars = rest.chars();
            chars
                .next()
                .map(|c| c.to_ascii_lowercase().to_string() + chars.as_str())
                .unwrap_or_default()
        }
        None => key.to_string(),
    }
}

/// The TextureImporter settings of an atlas's pages: sRGB, mipmaps, readability, filtering and
/// compression from the atlas's texture settings, its platform settings, wrap Clamp (measured on
/// the 6000.0.71f1 Editor, tests/projects/spriteatlas). The pages are already powers of two
/// within the maximum size, so no resizing applies.
fn texture_meta(texture: &Value, platforms: &[Value]) -> Value {
    let get = |key: &str, default: i64| int(texture, key).unwrap_or(default);
    let platforms = platforms
        .iter()
        .map(|p| match p.as_map() {
            Some(entries) => Value::Map(Map::from(
                entries
                    .0
                    .iter()
                    .map(|(k, v)| (unprefixed(k), v.clone()))
                    .collect::<Vec<_>>(),
            )),
            None => p.clone(),
        })
        .collect();
    map(vec![(
        "TextureImporter",
        map(vec![
            ("textureType", scalar(0)),
            ("textureShape", scalar(1)),
            (
                "mipmaps",
                map(vec![
                    ("sRGBTexture", scalar(get("sRGB", 1))),
                    ("enableMipMap", scalar(get("generateMipMaps", 0))),
                ]),
            ),
            ("isReadable", scalar(get("readable", 0))),
            ("alphaUsage", scalar(1)),
            ("nPOTScale", scalar(0)),
            (
                "textureSettings",
                map(vec![
                    ("filterMode", scalar(get("filterMode", 1))),
                    ("aniso", scalar(get("anisoLevel", 1))),
                    ("wrapU", scalar(1)),
                    ("wrapV", scalar(1)),
                    ("wrapW", scalar(1)),
                ]),
            ),
            ("maxTextureSize", scalar(get("maxTextureSize", 2048))),
            ("textureCompression", scalar(get("textureCompression", 1))),
            ("compressionQuality", scalar(get("compressionQuality", 50))),
            ("crunchedCompression", scalar(get("crunchedCompression", 0))),
            ("platformSettings", Value::Seq(platforms)),
        ]),
    )])
}

fn packables(list: Option<&Value>) -> Vec<(Guid, i64)> {
    list.and_then(Value::as_seq)
        .unwrap_or_default()
        .iter()
        .filter_map(Value::as_ref)
        .filter(|r| !r.is_null())
        .map(|r| (r.guid, r.file_id))
        .collect()
}

fn master(body: &Value) -> Option<Guid> {
    body.get("m_MasterAtlas")
        .and_then(Value::as_ref)
        .filter(|r| !r.is_null())
        .map(|r| r.guid)
}

/// Reads a V1 atlas (`.spriteatlas`: one SpriteAtlas document).
pub fn read_v1(text: &str) -> Result<AtlasDefinition> {
    let docs = unbound_yaml::parse_documents(text).context("parsing the sprite atlas")?;
    let Some(doc) = docs.into_iter().find(|d| d.class_id == 687078895) else {
        bail!("no SpriteAtlas document");
    };
    let body = &doc.body;
    let data = body.get("m_EditorData").context("no m_EditorData")?;
    let packing = data.get("packingSettings").cloned().unwrap_or_default();
    let name = body
        .get("m_Name")
        .and_then(Value::as_str)
        .unwrap_or_default()
        .to_string();
    let is_variant = int(body, "m_IsVariant") == Some(1);
    Ok(AtlasDefinition {
        tag: body
            .get("m_Tag")
            .and_then(Value::as_str)
            .filter(|t| !t.is_empty())
            .unwrap_or(&name)
            .to_string(),
        name,
        v2: false,
        packables: packables(data.get("packables")),
        include_in_build: int(data, "bindAsDefault").unwrap_or(1) != 0,
        variant: is_variant
            .then(|| master(body))
            .flatten()
            .map(|m| (m, float(data, "variantMultiplier").unwrap_or(1.0) as f32)),
        padding: int(&packing, "padding").unwrap_or(4).max(0) as u32,
        block_offset: int(&packing, "blockOffset").unwrap_or(1).max(0) as u32,
        allow_rotation: int(&packing, "enableRotation").unwrap_or(1) != 0,
        tight: int(&packing, "enableTightPacking").unwrap_or(1) != 0,
        alpha_dilation: int(&packing, "enableAlphaDilation").unwrap_or(0) != 0,
        alpha_splitting: int(&packing, "allowAlphaSplitting").unwrap_or(0) != 0,
        texture_meta: texture_meta(
            data.get("textureSettings").unwrap_or(&Value::Null),
            data.get("platformSettings")
                .and_then(Value::as_seq)
                .unwrap_or_default(),
        ),
    })
}

/// Reads a V2 atlas: the `.spriteatlasv2` (one SpriteAtlasAsset document) and its `.meta`
/// (`SpriteAtlasImporter`).
pub fn read_v2(text: &str, meta: &Value, file_name: &str) -> Result<AtlasDefinition> {
    let docs = unbound_yaml::parse_documents(text).context("parsing the sprite atlas")?;
    let Some(doc) = docs.into_iter().find(|d| d.class_id == 612988286) else {
        bail!("no SpriteAtlasAsset document");
    };
    let body = &doc.body;
    let importer = crate::meta::importer(meta, "SpriteAtlasImporter");
    let packing = importer.get("packingSettings").cloned().unwrap_or_default();
    let is_variant = int(body, "m_IsVariant") == Some(1);
    Ok(AtlasDefinition {
        name: file_name.to_string(),
        tag: file_name.to_string(),
        v2: true,
        packables: packables(body.get("m_ImporterData").and_then(|d| d.get("packables"))),
        include_in_build: int(importer, "bindAsDefault").unwrap_or(1) != 0,
        variant: is_variant.then(|| master(body)).flatten().map(|m| {
            (
                m,
                float(importer, "variantMultiplier").unwrap_or(1.0) as f32,
            )
        }),
        padding: int(&packing, "padding").unwrap_or(4).max(0) as u32,
        block_offset: int(&packing, "blockOffset").unwrap_or(1).max(0) as u32,
        allow_rotation: int(&packing, "enableRotation").unwrap_or(1) != 0,
        tight: int(&packing, "enableTightPacking").unwrap_or(1) != 0,
        alpha_dilation: int(&packing, "enableAlphaDilation").unwrap_or(0) != 0,
        alpha_splitting: int(&packing, "allowAlphaSplitting").unwrap_or(0) != 0,
        texture_meta: texture_meta(
            importer.get("textureSettings").unwrap_or(&Value::Null),
            importer
                .get("platformSettings")
                .and_then(Value::as_seq)
                .unwrap_or_default(),
        ),
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    const V1: &str = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!687078895 &4343727234628468602\nSpriteAtlas:\n  m_Name: Color\n  m_EditorData:\n    serializedVersion: 2\n    textureSettings:\n      serializedVersion: 2\n      anisoLevel: 1\n      compressionQuality: 50\n      maxTextureSize: 2048\n      textureCompression: 0\n      filterMode: 1\n      generateMipMaps: 0\n      readable: 0\n      crunchedCompression: 0\n      sRGB: 1\n    platformSettings:\n    - serializedVersion: 4\n      m_BuildTarget: Android\n      m_MaxTextureSize: 4096\n      m_TextureFormat: 47\n      m_TextureCompression: 1\n      m_Overridden: 1\n    packingSettings:\n      serializedVersion: 2\n      padding: 4\n      blockOffset: 1\n      allowAlphaSplitting: 0\n      enableRotation: 1\n      enableTightPacking: 0\n      enableAlphaDilation: 0\n    variantMultiplier: 1\n    packables:\n    - {fileID: 102900000, guid: 47fcad8e38f23b645b170273b0c9d189, type: 3}\n    bindAsDefault: 1\n  m_MasterAtlas: {fileID: 0}\n  m_Tag: Color\n  m_IsVariant: 0\n";

    #[test]
    fn reads_v1_settings_and_packables() {
        let atlas = read_v1(V1).unwrap();
        assert_eq!(
            (atlas.name.as_str(), atlas.tag.as_str()),
            ("Color", "Color")
        );
        assert_eq!(
            atlas.packables,
            [(
                Guid::parse("47fcad8e38f23b645b170273b0c9d189").unwrap(),
                102900000
            )]
        );
        assert!(atlas.include_in_build && atlas.allow_rotation && !atlas.tight);
        assert_eq!(
            (atlas.padding, atlas.block_offset, atlas.variant),
            (4, 1, None)
        );
        let settings = crate::texture::TextureSettings::read(&atlas.texture_meta, "Android");
        assert_eq!(settings.max_size, 4096);
        assert_eq!(settings.explicit_format, Some(47));
        assert!(!settings.mipmaps && settings.srgb);
        assert_eq!(settings.wrap_u, crate::texture::WrapMode::Clamp);
        let settings = crate::texture::TextureSettings::read(&atlas.texture_meta, "Standalone");
        assert_eq!((settings.max_size, settings.compression), (2048, 0));
    }

    #[test]
    fn reads_v2_importer_settings_and_variants() {
        let asset = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!612988286 &1\nSpriteAtlasAsset:\n  m_Name: \n  serializedVersion: 2\n  m_MasterAtlas: {fileID: 4343727234628468602, guid: 98f19659dd3fcda4da749fbebe03ae23, type: 2}\n  m_ImporterData:\n    packables:\n    - {fileID: 102900000, guid: 6b863381e87673249b3afc843315ecd9, type: 3}\n  m_IsVariant: 1\n";
        let meta = unbound_yaml::parse_value("fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\nSpriteAtlasImporter:\n  textureSettings:\n    serializedVersion: 2\n    maxTextureSize: 2048\n    filterMode: 0\n    generateMipMaps: 1\n  platformSettings:\n  - serializedVersion: 3\n    buildTarget: DefaultTexturePlatform\n    maxTextureSize: 1024\n    textureCompression: 1\n  packingSettings:\n    serializedVersion: 2\n    padding: 2\n    blockOffset: 1\n    enableRotation: 0\n    enableTightPacking: 0\n  variantMultiplier: 0.5\n  bindAsDefault: 0\n").unwrap();
        let atlas = read_v2(asset, &meta, "Tmp").unwrap();
        assert_eq!((atlas.name.as_str(), atlas.v2), ("Tmp", true));
        assert!(!atlas.include_in_build && !atlas.allow_rotation && !atlas.tight);
        assert_eq!(
            atlas.variant,
            Some((
                Guid::parse("98f19659dd3fcda4da749fbebe03ae23").unwrap(),
                0.5
            ))
        );
        let settings = crate::texture::TextureSettings::read(&atlas.texture_meta, "Standalone");
        assert_eq!(settings.max_size, 1024);
        assert!(settings.mipmaps);
        assert_eq!(settings.filter, crate::texture::FilterMode::Point);
        assert!(read_v1(asset).is_err());
    }
}
