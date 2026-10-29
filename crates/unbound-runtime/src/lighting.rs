//! Scene lighting data (`LightmapSettings.m_LightingDataAsset`): a project's baked LightingData
//! asset or the built-in one (`unity_builtin_extra`) that scenes without baked lighting use. Read
//! by their type trees, cached by content.
use anyhow::{Context, Result};
use serde_json::{Value as Json, json};
use std::path::{Path, PathBuf};
use unbound_assets::{
    AssetCache,
    lighting::{BuiltinLightingImporter, ObjectPtr},
    serialized_mesh::SerializedMeshImporter,
    serialized_texture::SerializedTextureImporter,
};
use unbound_project::assetdb::AssetDb;
use unbound_yaml::Guid;

/// Unity's built-in resources (`unity_builtin_extra`, `unity default resources`).
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
/// defaultReflection, lightmaps: [{ color, dir, shadowMask }], renderers: [{ object, prefab,
/// index, st: [4], indexDynamic, stDynamic: [4] }], lights: [{ object, prefab,
/// probeOcclusionLightIndex, occlusionMaskChannel, lightmapBakeType, mixedLightingMode, isBaked }],
/// probes: { positions: [3n], coefficients: [27n], tetrahedra: [{ indices, neighbors, matrix }],
/// hullRays: [3n] } | null }`, references as `{guid, fileId}` or null.
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
    let reference = |p: &Option<ObjectPtr>| match p {
        Some(p) => json!({ "guid": p.guid.as_deref().unwrap_or(guid), "fileId": p.path_id }),
        None => Json::Null,
    };
    let probes = data.probes.as_ref().map(|p| {
        json!({
            "positions": p.positions.iter().flatten().collect::<Vec<_>>(),
            "coefficients": p.coefficients.iter().flatten().collect::<Vec<_>>(),
            "tetrahedra": p.tetrahedra.iter().map(|t| json!({
                "indices": t.indices,
                "neighbors": t.neighbors,
                "matrix": t.matrix,
            })).collect::<Vec<_>>(),
            "hullRays": p.hull_rays.iter().flatten().collect::<Vec<_>>(),
        })
    });
    Ok(json!({
        "name": data.name,
        "lightmapsMode": data.lightmaps_mode,
        "ambientProbe": data.ambient_probe,
        "defaultReflection": reference(&data.default_reflection),
        "lightmaps": data.lightmaps.iter().map(|[color, dir, mask]| json!({
            "color": reference(color),
            "dir": reference(dir),
            "shadowMask": reference(mask),
        })).collect::<Vec<_>>(),
        "renderers": data.renderers.iter().map(|r| json!({
            "object": r.id.object,
            "prefab": r.id.prefab,
            "index": r.lightmap_index,
            "st": r.lightmap_st,
            "indexDynamic": r.lightmap_index_dynamic,
            "stDynamic": r.lightmap_st_dynamic,
        })).collect::<Vec<_>>(),
        "lights": data.lights.iter().map(|l| json!({
            "object": l.id.object,
            "prefab": l.id.prefab,
            "probeOcclusionLightIndex": l.probe_occlusion_light_index,
            "occlusionMaskChannel": l.occlusion_mask_channel,
            "lightmapBakeType": l.lightmap_bake_type,
            "mixedLightingMode": l.mixed_lighting_mode,
            "isBaked": l.is_baked,
        })).collect::<Vec<_>>(),
        "probes": probes,
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
        .get_or_import(
            &SerializedTextureImporter { path_id: file_id },
            &file,
            &meta_of(&file),
        )
        .with_context(|| format!("reading texture {file_id} of {}", file.display()))?;
    if let Err(e) = cache.save() {
        tracing::debug!("saving the built-in cache index: {e:#}");
    }
    // RGBA32 as stored; RGB24 (the built-in checker) gets an opaque alpha, mip by mip.
    let data = match t.format {
        4 => t.data.clone(),
        3 => t
            .data
            .chunks_exact(3)
            .flat_map(|c| [c[0], c[1], c[2], 255])
            .collect(),
        _ => anyhow::bail!(
            "texture `{}` has TextureFormat {}, which Unbound does not read from serialized files yet",
            t.name,
            t.format
        ),
    };
    upload(
        handle,
        crate::render_protocol::owned::Texture {
            width: t.width,
            height: t.height,
            // Linear data samples as stored; gamma data may be read through an sRGB view.
            // m_ColorSpace 1 is colour data, sampled through an sRGB view (the built-in default
            // reflection's RGBM texels and the checker: measured against the Editor's mirror
            // sphere, tests/projects/ambient); 0 is linear data.
            format: i32::from(t.color_space != 1),
            mip_count: t.mip_count,
            data,
            filter_mode: t.filter_mode,
            wrap_u: t.wrap_u,
            wrap_v: t.wrap_v,
            aniso_level: t.aniso,
            render_target: false,
            random_write: false,
            samples: 1,
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
        "linear": t.color_space != 1,
    }))
}

/// A mesh of a serialized file (the install's built-in meshes): its vertex channels (flat
/// arrays), indices and submeshes `[firstIndex, indexCount, baseVertex, topology]`.
pub fn mesh_object(db: &AssetDb, unity_data: &Path, guid: &str, file_id: i64) -> Result<Json> {
    let file = serialized_file(db, unity_data, guid)?;
    let cache = AssetCache::new(unbound_project::cache_root().join("builtin"));
    let m = cache
        .get_or_import(
            &SerializedMeshImporter { path_id: file_id },
            &file,
            &meta_of(&file),
        )
        .with_context(|| format!("reading mesh {file_id} of {}", file.display()))?;
    if let Err(e) = cache.save() {
        tracing::debug!("saving the built-in cache index: {e:#}");
    }
    let flat = |v: Vec<f32>| Json::from(v);
    Ok(json!({
        "name": m.name,
        "positions": flat(m.positions.iter().flatten().copied().collect()),
        "normals": flat(m.normals.iter().flatten().copied().collect()),
        "tangents": flat(m.tangents.iter().flatten().copied().collect()),
        "colors": flat(m.colors.iter().flatten().copied().collect()),
        "uvs": m.uvs.iter().map(|uv| flat(uv.iter().flatten().copied().collect())).collect::<Vec<_>>(),
        "indices": m.indices,
        "submeshes": m.submeshes,
    }))
}
