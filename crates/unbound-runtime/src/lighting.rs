//! Scene lighting data (`LightmapSettings.m_LightingDataAsset`): a project's baked LightingData
//! asset or the built-in one (`unity_builtin_extra`) that scenes without baked lighting use. Read
//! by their type trees, cached by content.
use anyhow::{Context, Result};
use serde_json::{Value as Json, json};
use std::path::{Path, PathBuf};
use unbound_assets::{
    AssetCache, lighting::BuiltinLightingImporter,
    serialized_texture::SerializedTextureImporter,
};
use unbound_project::assetdb::AssetDb;
use unbound_yaml::Guid;

/// Unity's built-in resources (`unity_builtin_extra`).
const BUILTIN_EXTRA: &str = "0000000000000000f000000000000000";

/// The file a `{guid}` names: an asset of the project, or the install's built-in resources.
fn serialized_file(db: &AssetDb, unity_data: &Path, guid: &str) -> Result<PathBuf> {
    Ok(if guid.eq_ignore_ascii_case(BUILTIN_EXTRA) {
        unity_data.join("Resources/unity_builtin_extra")
    } else if guid.eq_ignore_ascii_case(crate::editor_resources::EDITOR_GUID) {
        unity_data.join("Resources/unity editor resources")
    } else if guid.eq_ignore_ascii_case(crate::editor_resources::DEFAULT_GUID) {
        unity_data.join("Resources/unity default resources")
    } else {
        db.by_guid(Guid::parse(guid).context("bad guid")?)
            .with_context(|| format!("no asset with guid {guid}"))?
            .file
            .clone()
    })
}

fn meta_of(file: &Path) -> PathBuf {
    PathBuf::from(format!("{}.meta", file.display()))
}

/// The lighting data `{fileID, guid}` names: `{ name, lightmapsMode, ambientProbe: [27],
/// defaultReflection: {guid, fileId} | null }`.
pub fn lighting_data(db: &AssetDb, unity_data: &Path, guid: &str, file_id: i64) -> Result<Json> {
    let file = serialized_file(db, unity_data, guid)?;
    let cache = AssetCache::new(unbound_project::cache_root().join("builtin"));
    let importer = BuiltinLightingImporter { path_id: file_id };
    let data = cache
        .get_or_import(&importer, &file, &meta_of(&file))
        .with_context(|| format!("reading lighting data {file_id} of {}", file.display()))?;
    if let Err(e) = cache.save() {
        tracing::debug!("saving the built-in cache index: {e:#}");
    }
    Ok(json!({
        "name": data.name,
        "lightmapsMode": data.lightmaps_mode,
        "ambientProbe": data.ambient_probe,
        // A reference of the same file (file id 0).
        "defaultReflection": data.default_reflection.filter(|(f, _)| *f == 0).map(|(_, path)| json!({ "guid": guid, "fileId": path })),
    }))
}

/// Uploads a texture or cubemap of a serialized file under `handle`: `{ name, width, height,
/// mipCount, dimension, rgbm }`. RGBA32 data only (what the built-in reflection holds).
pub fn load_texture_object(
    db: &AssetDb,
    unity_data: &Path,
    guid: &str,
    file_id: i64,
    handle: u64,
    upload: impl FnOnce(u64, crate::render_protocol::owned::Texture),
) -> Result<Json> {
    let file = serialized_file(db, unity_data, guid)?;
    let cache = AssetCache::new(unbound_project::cache_root().join("builtin"));
    let t = cache
        .get_or_import(&SerializedTextureImporter { path_id: file_id }, &file, &meta_of(&file))
        .with_context(|| format!("reading texture {file_id} of {}", file.display()))?;
    if let Err(e) = cache.save() {
        tracing::debug!("saving the built-in cache index: {e:#}");
    }
    if t.format != 4 {
        anyhow::bail!(
            "texture `{}` has TextureFormat {}, which Unbound does not read from serialized files yet",
            t.name,
            t.format
        );
    }
    upload(
        handle,
        crate::render_protocol::owned::Texture {
            width: t.width,
            height: t.height,
            // Linear data samples as stored; gamma data may be read through an sRGB view.
            format: if t.color_space == 1 { 1 } else { 0 },
            mip_count: t.mip_count,
            data: t.data.clone(),
            filter_mode: t.filter_mode,
            wrap_u: t.wrap_u,
            wrap_v: t.wrap_v,
            aniso_level: t.aniso,
            render_target: false,
            random_write: false,
            depth_bits: 0,
            layers: t.image_count,
            dimension: t.dimension,
        },
    );
    Ok(json!({
        "name": t.name,
        "width": t.width,
        "height": t.height,
        "mipCount": t.mip_count,
        "dimension": t.dimension,
        "rgbm": t.lightmap_format == 5,
    }))
}
