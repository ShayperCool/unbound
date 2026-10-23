//! What the native side knows while a game runs: the project, its asset database and loaded
//! documents. The managed engine reaches it through the native API.
use crate::render_protocol::{ImportedTextureInfo, NullBackend, RenderBackend, owned};
use anyhow::{Context, Result, bail};
use serde::Deserialize;
use serde_json::json;
use std::{
    collections::HashMap,
    path::PathBuf,
    sync::{Arc, OnceLock, RwLock},
};
use unbound_assets::{
    AssetCache,
    audio::AudioImporter,
    texture::{ImportedTexture, TextureImporter},
};
use unbound_project::{AssetDb, Project};
use unbound_scene::DocumentLoader;
use unbound_yaml::Guid;

type Locator = Box<dyn Fn(Guid) -> Option<PathBuf> + Send + Sync>;

pub struct RuntimeState {
    pub project_root: PathBuf,
    pub db: Arc<AssetDb>,
    loader: DocumentLoader<Locator>,
    /// Lowercased path below a `Resources` folder, without extension → assets.
    resources: HashMap<String, Vec<Guid>>,
    backend: RwLock<Arc<dyn RenderBackend>>,
    imports: AssetCache,
    texture_importer: TextureImporter,
    /// Imported textures by GUID: their sprites are asked for after the import.
    textures: RwLock<HashMap<Guid, Arc<ImportedTexture>>>,
    /// Project shaders by declared name, built on first use.
    shaders: RwLock<Option<HashMap<String, Guid>>>,
    /// Imports clips with the active build target's overrides.
    audio_importer: RwLock<AudioImporter>,
    pub audio: crate::audio::Engine,
}

static STATE: OnceLock<RuntimeState> = OnceLock::new();

pub fn state() -> &'static RuntimeState {
    STATE.get().expect("the runtime state is not initialized")
}

/// Sets up the state for a project; once per process.
pub fn initialize(project: &Project) -> Result<&'static RuntimeState> {
    let db = Arc::new(project.asset_db());
    let locate_db = db.clone();
    let locator: Locator = Box::new(move |guid| locate_db.by_guid(guid).map(|a| a.file.clone()));
    let mut resources: HashMap<String, Vec<Guid>> = HashMap::new();
    for asset in db.iter() {
        if asset.is_folder {
            continue;
        }
        for key in resources_keys(&asset.path) {
            resources.entry(key).or_default().push(asset.guid);
        }
    }
    let state = RuntimeState {
        project_root: project.root.clone(),
        db,
        loader: DocumentLoader::new(locator),
        resources,
        backend: RwLock::new(Arc::new(NullBackend)),
        imports: AssetCache::for_project(&project.root),
        texture_importer: TextureImporter::default(),
        textures: RwLock::new(HashMap::new()),
        shaders: RwLock::new(None),
        audio_importer: RwLock::new(AudioImporter::default()),
        audio: crate::audio::Engine::new(
            crate::audio::ProjectAudioSettings::read(&project.root),
            true,
        ),
    };
    if STATE.set(state).is_err() {
        bail!("the runtime state is already initialized");
    }
    Ok(self::state())
}

/// `Assets/A/Resources/B/c.png` → `b/c`; every `Resources` folder on the path gives a key.
fn resources_keys(path: &str) -> Vec<String> {
    let mut keys = Vec::new();
    let parts: Vec<&str> = path.split('/').collect();
    for (i, part) in parts.iter().enumerate() {
        if *part == "Resources" && i + 1 < parts.len() {
            let rest = parts[i + 1..].join("/");
            let without_extension = match rest.rfind('.') {
                Some(dot) if !rest[dot..].contains('/') => rest[..dot].to_string(),
                _ => rest,
            };
            keys.push(without_extension.to_lowercase());
        }
    }
    keys
}

/// What the managed side asks about an asset.
fn asset_kind(path: &str) -> &'static str {
    let ext = path.rsplit('.').next().unwrap_or("").to_ascii_lowercase();
    match ext.as_str() {
        "prefab" => "prefab",
        "unity" => "scene",
        "asset" | "mat" | "anim" | "controller" | "overridecontroller" | "physicmaterial"
        | "physicsmaterial2d" | "mask" | "fontsettings" | "guiskin" | "mixer" | "rendertexture"
        | "flare" | "spriteatlas" | "spriteatlasv2" | "lighting" | "preset" | "brush"
        | "terrainlayer" | "signal" | "playable" | "shadervariants" | "giparams" | "cubemap" => {
            "yaml"
        }
        "png" | "jpg" | "jpeg" | "tga" | "psd" | "bmp" | "gif" | "tif" | "tiff" | "exr" | "hdr"
        | "iff" | "pict" => "texture",
        "wav" | "mp3" | "ogg" | "aif" | "aiff" | "flac" | "mod" | "it" | "s3m" | "xm" => "audio",
        "fbx" | "obj" | "dae" | "3ds" | "dxf" | "blend" | "max" | "ma" | "mb" => "model",
        "txt" | "json" | "bytes" | "xml" | "csv" | "html" | "htm" | "yaml" | "md" | "fnt" => "text",
        "ttf" | "otf" | "ttc" | "dfont" => "font",
        "cs" => "script",
        "shader" | "cginc" | "hlsl" | "compute" | "shadergraph" => "shader",
        "mp4" | "webm" | "mov" | "m4v" => "video",
        "dll" => "plugin",
        _ => "unknown",
    }
}

/// The script GUID of the ScriptedImporter that imports a file, from its `.meta`.
fn scripted_importer(file: &std::path::Path) -> Option<String> {
    let meta = std::fs::read_to_string(format!("{}.meta", file.display())).ok()?;
    if !meta.lines().any(|l| l == "ScriptedImporter:") {
        return None;
    }
    meta.lines()
        .find_map(|l| l.strip_prefix("  script: {fileID: 11500000, guid: "))
        .map(|rest| rest.split([',', '}']).next().unwrap_or("").trim().to_string())
}

/// The file id of an asset file's first object: its main object.
fn first_file_id(file: &std::path::Path) -> Option<i64> {
    let text = std::fs::read_to_string(file).ok()?;
    let header = text.lines().find(|l| l.starts_with("--- "))?;
    header
        .split_whitespace()
        .find_map(|p| p.strip_prefix('&'))
        .and_then(|id| id.parse().ok())
}

#[derive(Deserialize)]
struct AssetInfoRequest {
    guid: Option<String>,
    path: Option<String>,
}

#[derive(Deserialize)]
struct ResourcesRequest {
    path: String,
    folder: bool,
}

pub fn dispatch(method: &str, request: &str) -> Result<String> {
    let s = state();
    match method {
        "asset-info" => {
            let r: AssetInfoRequest = serde_json::from_str(request)?;
            let asset = match (&r.guid, &r.path) {
                (Some(guid), _) => s.db.by_guid(Guid::parse(guid).context("bad guid")?),
                (None, Some(path)) => s.db.by_path(path),
                _ => None,
            }
            .with_context(|| {
                format!(
                    "no asset {}",
                    r.guid.as_deref().or(r.path.as_deref()).unwrap_or("")
                )
            })?;
            let importer = (!asset.is_folder)
                .then(|| scripted_importer(&asset.file))
                .flatten();
            let kind = if asset.is_folder {
                "folder"
            } else if importer.is_some() {
                "scripted"
            } else {
                asset_kind(&asset.path)
            };
            let main = if kind == "yaml" {
                first_file_id(&asset.file).unwrap_or(0)
            } else {
                0
            };
            Ok(json!({
                "guid": asset.guid.to_string(),
                "path": asset.path,
                "file": asset.file,
                "kind": kind,
                "mainFileId": main,
                "importer": importer,
            })
            .to_string())
        }
        "resources-find" => {
            let r: ResourcesRequest = serde_json::from_str(request)?;
            let key = r.path.to_lowercase();
            let mut found = Vec::new();
            if r.folder {
                let prefix = if key.is_empty() {
                    String::new()
                } else {
                    format!("{key}/")
                };
                for (k, guids) in &s.resources {
                    if k.starts_with(&prefix) || *k == key {
                        found.extend(guids.iter().copied());
                    }
                }
            } else if let Some(guids) = s.resources.get(&key) {
                found.extend(guids.iter().copied());
            }
            let list: Vec<_> = found
                .into_iter()
                .filter_map(|g| s.db.by_guid(g))
                .map(|a| json!({ "guid": a.guid.to_string(), "path": a.path }))
                .collect();
            Ok(serde_json::Value::Array(list).to_string())
        }
        "asset-list" => {
            // Every asset: what AssetDatabase.GetAllAssetPaths and FindAssets search.
            let list: Vec<_> = s
                .db
                .iter()
                .map(|a| {
                    json!({
                        "guid": a.guid.to_string(),
                        "path": a.path,
                        "kind": if a.is_folder { "folder" } else { asset_kind(&a.path) },
                    })
                })
                .collect();
            Ok(serde_json::Value::Array(list).to_string())
        }
        "asset-script" => {
            // The script GUID of a YAML asset's first MonoBehaviour (a ScriptableObject's class).
            #[derive(Deserialize)]
            struct Request {
                guid: String,
            }
            let r: Request = serde_json::from_str(request)?;
            let asset = s.db.by_guid(Guid::parse(&r.guid).context("bad guid")?).context("no such asset")?;
            let text = std::fs::read_to_string(&asset.file).unwrap_or_default();
            let script = text
                .lines()
                .find_map(|l| l.trim().strip_prefix("m_Script:"))
                .and_then(|r| r.split("guid:").nth(1))
                .map(|g| g.split([',', '}']).next().unwrap_or("").trim().to_string())
                .unwrap_or_default();
            Ok(json!({ "script": script }).to_string())
        }
        "shader-find" => {
            #[derive(Deserialize)]
            struct Request {
                name: String,
            }
            let r: Request = serde_json::from_str(request)?;
            Ok(json!(s.shader_guid(&r.name).map(|g| g.to_string()).unwrap_or_default()).to_string())
        }
        "texture-sprites" => {
            #[derive(Deserialize)]
            struct Request {
                guid: String,
            }
            let r: Request = serde_json::from_str(request)?;
            let guid = Guid::parse(&r.guid).context("bad guid")?;
            let textures = s.textures.read().unwrap();
            let texture = textures.get(&guid).context("the texture is not imported")?;
            let sprites: Vec<_> = texture
                .sprites
                .iter()
                .map(|sp| {
                    json!({
                        "fileId": sp.file_id,
                        "name": sp.name,
                        "rect": [sp.texture_rect.x, sp.texture_rect.y, sp.texture_rect.width, sp.texture_rect.height],
                        "pivot": sp.pivot,
                        "border": sp.border,
                        "pixelsPerUnit": sp.texture_pixels_per_unit,
                        "meshType": sp.mesh_type as i32,
                        "outline": sp.outline,
                        "physicsShape": sp.physics_shape,
                    })
                })
                .collect();
            Ok(json!({ "name": texture.name, "sprites": sprites }).to_string())
        }
        "audio-settings" => {
            // The project's audio settings and the configuration in use.
            Ok(json!({
                "project": s.audio.settings,
                "configuration": s.audio.configuration(),
                "device": s.audio.has_device(),
            })
            .to_string())
        }
        "audio-reset" => {
            let configuration: crate::audio::AudioConfiguration = serde_json::from_str(request)?;
            Ok(json!({ "ok": s.audio.reset(configuration) }).to_string())
        }
        other => bail!("unknown native method `{other}`"),
    }
}

/// The expanded documents of a file named by an `Assets/...` path, a 32-hex GUID or an
/// absolute path, encoded for the managed side.
pub fn load_documents(key: &str) -> Result<Vec<u8>> {
    let s = state();
    if let Some(guid) = key.strip_prefix("meta:") {
        return load_importer_settings(guid);
    }
    let file = if let Some(guid) = Guid::parse(key) {
        s.db.by_guid(guid).map(|a| a.file.clone())
    } else if key.starts_with('/') {
        Some(PathBuf::from(key))
    } else {
        s.db.by_path(key).map(|a| a.file.clone())
    }
    .with_context(|| format!("no asset {key}"))?;
    let docs = s.loader.load(&file)?;
    Ok(unbound_scene::encode_documents(&docs))
}

/// An asset's importer settings: the importer section of its `.meta`, as one document whose type
/// name is the importer's (`ScriptedImporter`, `TextureImporter`, ...).
fn load_importer_settings(guid: &str) -> Result<Vec<u8>> {
    let s = state();
    let asset = s
        .db
        .by_guid(Guid::parse(guid).context("bad guid")?)
        .with_context(|| format!("no asset {guid}"))?;
    let text = std::fs::read_to_string(format!("{}.meta", asset.file.display()))?;
    let meta = unbound_yaml::parse_value(&text)?;
    let (type_name, body) = meta
        .as_map()
        .and_then(|m| {
            m.iter()
                .find(|(k, _)| k.ends_with("Importer"))
                .map(|(k, v)| (k.to_string(), v.clone()))
        })
        .with_context(|| format!("{}.meta names no importer", asset.path))?;
    let doc = unbound_yaml::Document {
        class_id: 0,
        file_id: 0,
        stripped: false,
        type_name,
        body,
    };
    Ok(unbound_scene::encode_documents(&[doc]))
}

/// Installs the renderer that executes the managed side's render protocol.
pub fn set_backend(backend: Arc<dyn RenderBackend>) {
    *state().backend.write().unwrap() = backend;
}

pub fn backend() -> Arc<dyn RenderBackend> {
    match STATE.get() {
        Some(s) => s.backend.read().unwrap().clone(),
        None => Arc::new(NullBackend),
    }
}

impl RuntimeState {
    /// The project shader declaring `Shader "name"`, found by scanning `.shader` files once.
    fn shader_guid(&self, name: &str) -> Option<Guid> {
        let mut index = self.shaders.write().unwrap();
        if index.is_none() {
            let mut map = HashMap::new();
            for asset in self.db.iter() {
                if !asset.path.ends_with(".shader") {
                    continue;
                }
                let Ok(text) = std::fs::read_to_string(&asset.file) else { continue };
                if let Some(declared) = text
                    .lines()
                    .find_map(|l| l.trim_start().strip_prefix("Shader"))
                    .and_then(|rest| rest.split('"').nth(1))
                {
                    map.insert(declared.to_string(), asset.guid);
                }
            }
            *index = Some(map);
        }
        index.as_ref().and_then(|m| m.get(name).copied())
    }
}

/// The active build target group (`Android`, `Standalone`, ...): the import overrides that apply.
pub fn set_build_target(target: &str) {
    state().audio_importer.write().unwrap().build_target = target.to_string();
}

/// Imports an asset audio clip (through the import cache) into a mixer clip handle.
pub fn import_audio(handle: u64, guid: &str) -> Result<crate::audio::protocol::AudioClipInfo> {
    use unbound_assets::audio::LoadType;
    let s = state();
    let guid = Guid::parse(guid).context("bad guid")?;
    let asset = s.db.by_guid(guid).context("no such asset")?;
    let meta = PathBuf::from(format!("{}.meta", asset.file.display()));
    let importer = s.audio_importer.read().unwrap().clone();
    let imported = s.imports.get_or_import(&importer, &asset.file, &meta)?;
    let settings = &imported.settings;
    let info = crate::audio::protocol::AudioClipInfo {
        frames: imported.frames() as i32,
        channels: imported.channels as i32,
        frequency: imported.sample_rate as i32,
        load_type: match settings.sample.load_type {
            LoadType::DecompressOnLoad => 0,
            LoadType::CompressedInMemory => 1,
            LoadType::Streaming => 2,
        },
        preload: settings.sample.preload_audio_data as i32,
        load_in_background: settings.load_in_background as i32,
        ambisonic: settings.ambisonic as i32,
        compression_format: settings.sample.compression_format,
    };
    let samples = match Arc::try_unwrap(imported) {
        Ok(owned) => owned.samples,
        Err(shared) => shared.samples.clone(),
    };
    s.audio.mixer().add_clip(
        handle,
        crate::audio::Clip::new(info.channels as usize, info.frequency as u32, samples),
    );
    Ok(info)
}

/// Imports an asset texture (through the import cache) into a renderer handle.
pub fn import_texture(handle: u64, guid: &str) -> Result<ImportedTextureInfo> {
    let s = state();
    let guid = Guid::parse(guid).context("bad guid")?;
    let asset = s.db.by_guid(guid).context("no such asset")?;
    let meta = PathBuf::from(format!("{}.meta", asset.file.display()));
    let texture = s
        .imports
        .get_or_import(&s.texture_importer, &asset.file, &meta)?;
    s.textures.write().unwrap().insert(guid, texture.clone());
    let format = match texture.format {
        unbound_assets::texture::TextureFormat::Rgba8 => i32::from(!texture.srgb),
        unbound_assets::texture::TextureFormat::Rgba32F => 2,
    };
    let mut data = Vec::new();
    for mip in &texture.mips {
        data.extend_from_slice(&mip.data);
    }
    let wrap = |w: unbound_assets::texture::WrapMode| w as i32;
    let filter = texture.filter as i32;
    s.backend.read().unwrap().upload_texture(
        handle,
        owned::Texture {
            width: texture.width as i32,
            height: texture.height as i32,
            format,
            mip_count: texture.mips.len() as i32,
            data,
            filter_mode: filter,
            wrap_u: wrap(texture.wrap_u),
            wrap_v: wrap(texture.wrap_v),
            aniso_level: texture.aniso,
            render_target: false,
            depth_bits: 0,
        },
    );
    Ok(ImportedTextureInfo {
        width: texture.width as i32,
        height: texture.height as i32,
        mip_count: texture.mips.len() as i32,
        srgb: texture.srgb as i32,
        filter_mode: filter,
        wrap_u: wrap(texture.wrap_u),
        wrap_v: wrap(texture.wrap_v),
        aniso_level: texture.aniso,
        readable: texture.is_readable as i32,
        sprite_count: texture.sprites.len() as i32,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn resources_keys_strip_folders_and_extensions() {
        assert_eq!(resources_keys("Assets/Resources/UI/Icon.png"), ["ui/icon"]);
        assert_eq!(
            resources_keys("Assets/A/Resources/B/Resources/c.prefab"),
            ["b/resources/c", "c"]
        );
        assert!(resources_keys("Assets/Textures/a.png").is_empty());
    }
}
