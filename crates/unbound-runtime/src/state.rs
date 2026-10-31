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
use unbound_assets::{AssetCache, SpriteDef, audio::AudioImporter, texture::TextureImporter};
use unbound_project::{AssetDb, Project};
use unbound_scene::DocumentLoader;
use unbound_yaml::Guid;

type Locator = Box<dyn Fn(Guid) -> Option<PathBuf> + Send + Sync>;

pub struct RuntimeState {
    pub project_root: PathBuf,
    /// The Unity install's `Editor/Data`: built-in resources (fonts) come from there.
    pub unity_data: PathBuf,
    pub db: Arc<AssetDb>,
    loader: DocumentLoader<Locator>,
    /// Lowercased path below a `Resources` folder, without extension → assets.
    resources: HashMap<String, Vec<Guid>>,
    backend: RwLock<Arc<dyn RenderBackend>>,
    imports: AssetCache,
    /// Imports textures with the active build target's overrides.
    texture_importer: RwLock<TextureImporter>,
    /// Imported textures' sprites by GUID (asked for after the import). Only the name and the
    /// sprites stay: the pixels live on the GPU once uploaded.
    textures: RwLock<HashMap<Guid, TextureSprites>>,
    /// Project shaders by declared name, built on first use.
    shaders: RwLock<Option<HashMap<String, Guid>>>,
    /// The folders of the release's built-in shader sources (`DefaultResources`, ...).
    builtin_shader_roots: Vec<PathBuf>,
    /// Built-in shader sources by declared name, built on first use.
    builtin_shaders: RwLock<Option<HashMap<String, PathBuf>>>,
    /// Imports clips with the active build target's overrides.
    audio_importer: RwLock<AudioImporter>,
    models: RwLock<HashMap<Guid, Arc<unbound_assets::ImportedModel>>>,
    pub audio: crate::audio::Engine,
}

/// What stays of an imported texture after its upload.
struct TextureSprites {
    name: String,
    sprites: Vec<SpriteDef>,
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
        unity_data: project.install.data.clone(),
        db,
        loader: DocumentLoader::new(locator).with_synthesizer(Box::new(|guid, file| {
            let is_model = file.extension().and_then(|e| e.to_str()).is_some_and(|e| {
                unbound_assets::model::EXTENSIONS.contains(&e.to_ascii_lowercase().as_str())
            });
            if is_model {
                return Some(model_documents(guid));
            }
            // Binary assets with type trees (NavMeshData the Editor bakes) read by their trees.
            let is_asset = file
                .extension()
                .is_some_and(|e| e.eq_ignore_ascii_case("asset"));
            if !is_asset {
                return None;
            }
            let data = std::fs::read(file).ok()?;
            unbound_assets::serialized::is_binary(&data)
                .then(|| unbound_assets::serialized::documents(&data))
        })),
        resources,
        backend: RwLock::new(Arc::new(NullBackend)),
        imports: AssetCache::for_project(&project.root),
        texture_importer: RwLock::new(TextureImporter {
            gamma: project_in_gamma_space(project),
            ..TextureImporter::default()
        }),
        textures: RwLock::new(HashMap::new()),
        shaders: RwLock::new(None),
        builtin_shader_roots: project
            .builtin_shaders()
            .map(|b| b.shader_roots())
            .unwrap_or_default(),
        builtin_shaders: RwLock::new(None),
        audio_importer: RwLock::new(AudioImporter::default()),
        models: RwLock::new(HashMap::new()),
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
        "shader" | "cginc" | "hlsl" | "shadergraph" => "shader",
        "compute" => "compute",
        "uxml" => "uxml",
        "uss" => "uss",
        "tss" => "tss",
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
        .map(|rest| {
            rest.split([',', '}'])
                .next()
                .unwrap_or("")
                .trim()
                .to_string()
        })
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
        "lighting-data" => {
            #[derive(serde::Deserialize)]
            #[serde(rename_all = "camelCase")]
            struct Request {
                guid: String,
                file_id: i64,
            }
            let r: Request = serde_json::from_str(request)?;
            Ok(
                crate::lighting::lighting_data(&s.db, &s.unity_data, &r.guid, r.file_id)?
                    .to_string(),
            )
        }
        "mesh-object" => {
            #[derive(serde::Deserialize)]
            #[serde(rename_all = "camelCase")]
            struct Request {
                guid: String,
                file_id: i64,
            }
            let r: Request = serde_json::from_str(request)?;
            Ok(crate::lighting::mesh_object(&s.db, &s.unity_data, &r.guid, r.file_id)?.to_string())
        }
        "load-texture-object" => {
            #[derive(serde::Deserialize)]
            #[serde(rename_all = "camelCase")]
            struct Request {
                guid: String,
                file_id: i64,
                handle: u64,
            }
            let r: Request = serde_json::from_str(request)?;
            let backend = s.backend.read().unwrap();
            Ok(crate::lighting::load_texture_object(
                &s.db,
                &s.unity_data,
                &r.guid,
                r.file_id,
                r.handle,
                |handle, texture| backend.upload_texture(handle, texture),
            )?
            .to_string())
        }
        "video-probe" => {
            #[derive(serde::Deserialize)]
            struct Request {
                path: String,
            }
            let r: Request = serde_json::from_str(request)?;
            Ok(crate::video::probe(&r.path)?.to_string())
        }
        "video-open" => {
            #[derive(serde::Deserialize)]
            struct Request {
                path: String,
                width: usize,
                height: usize,
                fps: f64,
            }
            let r: Request = serde_json::from_str(request)?;
            Ok(json!({ "id": crate::video::open(&r.path, r.width, r.height, r.fps) }).to_string())
        }
        "video-close" => {
            #[derive(serde::Deserialize)]
            struct Request {
                id: u64,
            }
            let r: Request = serde_json::from_str(request)?;
            crate::video::close(r.id);
            Ok("{}".into())
        }
        "cursor" => crate::cursor::set(request),
        "gamepads-poll" => {
            #[derive(serde::Deserialize)]
            struct Request {
                family: String,
            }
            let r: Request = serde_json::from_str(request)?;
            Ok(crate::gamepads::poll(&r.family).to_string())
        }
        "bundle-open" => {
            #[derive(serde::Deserialize)]
            struct Request {
                #[serde(default)]
                path: Option<String>,
                #[serde(default)]
                offset: u64,
                #[serde(default)]
                data: Option<String>,
            }
            let r: Request = serde_json::from_str(request)?;
            match (r.path, r.data) {
                (Some(path), _) => crate::bundles::open_file(&path, r.offset),
                (None, Some(data)) => crate::bundles::open(
                    &crate::fonts::decode_base64(&data).context("bundle data is not base64")?,
                ),
                _ => anyhow::bail!("bundle-open needs a path or data"),
            }
        }
        "bundle-close" => {
            #[derive(serde::Deserialize)]
            struct Request {
                id: u64,
            }
            let r: Request = serde_json::from_str(request)?;
            crate::bundles::close(r.id);
            Ok("{}".into())
        }
        "shader-passes" => backend()
            .shader_passes(request)
            .map_err(|e| anyhow::anyhow!("{e}")),
        "compute-kernel-threads" => {
            #[derive(serde::Deserialize)]
            struct Request {
                file: String,
                kernel: String,
                #[serde(default)]
                keywords: Vec<String>,
            }
            let r: Request = serde_json::from_str(request)?;
            let threads = backend()
                .kernel_threads(&r.file, &r.kernel, &r.keywords)
                .map_err(|e| anyhow::anyhow!("{e}"))?;
            Ok(json!({ "x": threads[0], "y": threads[1], "z": threads[2] }).to_string())
        }
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
            let list: Vec<_> =
                s.db.iter()
                    .map(|a| {
                        json!({
                            "guid": a.guid.to_string(),
                            "path": a.path,
                            "kind": if a.is_folder { "folder" } else { asset_kind(&a.path) },
                            "labels": a.labels,
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
            let asset =
                s.db.by_guid(Guid::parse(&r.guid).context("bad guid")?)
                    .context("no such asset")?;
            let text = std::fs::read_to_string(&asset.file).unwrap_or_default();
            let script = text
                .lines()
                .find_map(|l| l.trim().strip_prefix("m_Script:"))
                .and_then(|r| r.split("guid:").nth(1))
                .map(|g| g.split([',', '}']).next().unwrap_or("").trim().to_string())
                .unwrap_or_default();
            Ok(json!({ "script": script }).to_string())
        }
        "model-mesh" => {
            #[derive(Deserialize)]
            struct Request {
                guid: String,
                index: usize,
            }
            let r: Request = serde_json::from_str(request)?;
            let model = imported_model(Guid::parse(&r.guid).context("bad guid")?)?;
            let mesh = model.meshes.get(r.index).context("no such mesh")?;
            Ok(crate::models::mesh_json(mesh).to_string())
        }
        "editor-resource" => {
            #[derive(Deserialize)]
            #[serde(rename_all = "camelCase")]
            struct Request {
                path: Option<String>,
                path_id: Option<i64>,
            }
            let r: Request = serde_json::from_str(request)?;
            if let Some(path) = r.path {
                Ok(crate::editor_resources::find(&s.unity_data, &path)?.to_string())
            } else {
                let id = r
                    .path_id
                    .context("an editor resource request needs a path or a path id")?;
                Ok(crate::editor_resources::object(&s.unity_data, id)?.to_string())
            }
        }
        "builtin-shader-source" => {
            #[derive(Deserialize)]
            struct Request {
                name: String,
            }
            let r: Request = serde_json::from_str(request)?;
            let source = s
                .builtin_shader_file(&r.name)
                .and_then(|f| std::fs::read_to_string(f).ok())
                .unwrap_or_default();
            Ok(json!(source).to_string())
        }
        "shader-find" => {
            #[derive(Deserialize)]
            struct Request {
                name: String,
            }
            let r: Request = serde_json::from_str(request)?;
            Ok(json!(
                s.shader_guid(&r.name)
                    .map(|g| g.to_string())
                    .unwrap_or_default()
            )
            .to_string())
        }
        "memory-stats" => {
            let (clips, clip_bytes) = s.audio.mixer().clip_memory();
            let sprites = s.textures.read().unwrap().len();
            let (prefabs, prefab_bytes) = s.loader.cached_prefabs();
            Ok(json!({
                "audioClips": clips,
                "audioBytes": clip_bytes,
                "spriteTextures": sprites,
                "cachedPrefabs": prefabs,
                "cachedPrefabBytes": prefab_bytes,
            })
            .to_string())
        }
        "texture-pixels" => {
            // An imported texture's requested mip, rows bottom first, retaining float precision.
            #[derive(Deserialize)]
            struct Request {
                guid: String,
                #[serde(default)]
                mip: usize,
            }
            let r: Request = serde_json::from_str(request)?;
            let guid = Guid::parse(&r.guid).context("bad guid")?;
            let asset = s.db.by_guid(guid).context("no such asset")?;
            let meta = PathBuf::from(format!("{}.meta", asset.file.display()));
            let texture = s
                .imports
                .get_or_import(&texture_importer(s), &asset.file, &meta)?;
            let mip = texture
                .mips
                .get(r.mip)
                .context("the texture has no such mip")?;
            let format = match texture.format {
                unbound_assets::texture::TextureFormat::Rgba8 => 0,
                unbound_assets::texture::TextureFormat::Rgba32F => 2,
            };
            Ok(json!({ "width": mip.width, "height": mip.height, "format": format, "rgba": crate::models::base64(&mip.data) }).to_string())
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
                        "vertices": sp.vertices,
                        "indices": sp.indices,
                        "weights": sp.weights.iter().map(|w| json!({ "weights": w.weights, "bones": w.bones })).collect::<Vec<_>>(),
                        "bones": sp.bones.iter().map(|b| json!({
                            "name": b.name,
                            "guid": b.guid,
                            "position": b.position,
                            "rotation": b.rotation,
                            "length": b.length,
                            "parentId": b.parent_id,
                            "color": b.color,
                        })).collect::<Vec<_>>(),
                        "secondaryTextures": sp.secondary_textures.iter().map(|t| json!({ "name": t.name, "guid": t.guid })).collect::<Vec<_>>(),
                    })
                })
                .collect();
            Ok(json!({ "name": texture.name, "sprites": sprites }).to_string())
        }
        "font-open" => {
            let r: crate::fonts::OpenRequest = serde_json::from_str(request)?;
            Ok(crate::fonts::open(&r, &s.unity_data)?.to_string())
        }
        "font-face" => {
            #[derive(Deserialize)]
            struct Request {
                handle: u64,
            }
            let r: Request = serde_json::from_str(request)?;
            Ok(crate::fonts::describe(r.handle)?.to_string())
        }
        "font-kerning" => {
            #[derive(Deserialize)]
            struct Request {
                handle: u64,
                /// The first glyphs of the pairs; absent: every glyph.
                firsts: Option<Vec<u16>>,
                /// The second glyphs; absent: every glyph.
                seconds: Option<Vec<u16>>,
            }
            let r: Request = serde_json::from_str(request)?;
            Ok(
                crate::fonts::kerning(r.handle, r.firsts.as_deref(), r.seconds.as_deref())?
                    .to_string(),
            )
        }
        "font-has-character" => {
            #[derive(Deserialize)]
            struct Request {
                handle: u64,
                codepoint: u32,
            }
            let r: Request = serde_json::from_str(request)?;
            Ok(json!(crate::fonts::has_char(r.handle, r.codepoint)).to_string())
        }
        "font-os-fonts" => {
            // Font.GetOSInstalledFontNames, Font.GetPathsToOSFonts and FontEngine's system fonts.
            let fonts = crate::fonts::os_fonts();
            let mut names: Vec<&str> = fonts.iter().map(|f| f.family.as_str()).collect();
            names.dedup();
            let paths: Vec<_> = fonts.iter().map(|f| &f.path).collect();
            let faces: Vec<_> = fonts
                .iter()
                .map(|f| json!({ "family": f.family, "style": f.style, "path": f.path, "index": f.index }))
                .collect();
            Ok(json!({ "names": names, "paths": paths, "faces": faces }).to_string())
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
    if let Some(bundle) = key.strip_prefix("bundle:") {
        return crate::bundles::documents(bundle);
    }
    let (file, guid) = if let Some(guid) = Guid::parse(key) {
        s.db.by_guid(guid).map(|a| (a.file.clone(), Some(a.guid)))
    } else if key.starts_with('/') {
        Some((PathBuf::from(key), None))
    } else {
        s.db.by_path(key).map(|a| (a.file.clone(), Some(a.guid)))
    }
    .with_context(|| format!("no asset {key}"))?;
    // A model loads as the prefab it imports to.
    if let Some(guid) = guid
        && file.extension().and_then(|e| e.to_str()).is_some_and(|e| {
            unbound_assets::model::EXTENSIONS.contains(&e.to_ascii_lowercase().as_str())
        })
    {
        use unbound_scene::PrefabSource;
        let docs = s.loader.prefab(guid, &mut Vec::new())?;
        return Ok(unbound_scene::encode_documents(&docs));
    }
    // A binary asset (NavMeshData the Editor bakes) reads by its type trees.
    if file
        .extension()
        .is_some_and(|e| e.eq_ignore_ascii_case("asset"))
    {
        let data = std::fs::read(&file).with_context(|| format!("reading {}", file.display()))?;
        if unbound_assets::serialized::is_binary(&data) {
            let docs = unbound_assets::serialized::documents(&data)
                .with_context(|| format!("reading {}", file.display()))?;
            return Ok(unbound_scene::encode_documents(&docs));
        }
    }
    let docs = s.loader.load(&file)?;
    Ok(unbound_scene::encode_documents(&docs))
}

/// A model asset, imported through the import cache (once per process).
fn imported_model(guid: Guid) -> Result<Arc<unbound_assets::ImportedModel>> {
    let s = state();
    if let Some(model) = s.models.read().unwrap().get(&guid) {
        return Ok(model.clone());
    }
    let asset = s.db.by_guid(guid).context("no such asset")?;
    let meta = PathBuf::from(format!("{}.meta", asset.file.display()));
    let model = s
        .imports
        .get_or_import(&unbound_assets::ModelImporter, &asset.file, &meta)?;
    for warning in &model.warnings {
        tracing::warn!("{}: {warning}", asset.path);
    }
    s.models.write().unwrap().insert(guid, model.clone());
    Ok(model)
}

/// The documents of a model's prefab.
fn model_documents(guid: Guid) -> Result<Vec<unbound_yaml::Document>> {
    let model = imported_model(guid)?;
    Ok(crate::models::documents(guid, &model))
}

/// An asset's importer settings: the importer section of its `.meta`, as one document whose type
/// name is the importer's (`ScriptedImporter`, `TextureImporter`, ...).
fn load_importer_settings(guid: &str) -> Result<Vec<u8>> {
    let s = state();
    let asset =
        s.db.by_guid(Guid::parse(guid).context("bad guid")?)
            .with_context(|| format!("no asset {guid}"))?;
    let text = std::fs::read_to_string(format!("{}.meta", asset.file.display()))?;
    let meta = unbound_yaml::parse_value(&text)?;
    // A .meta that names no importer (hand-written ones, such as Firebase's manifests) gets the
    // one the Editor picks for the file's extension, with default settings.
    let (type_name, body) = meta
        .as_map()
        .and_then(|m| {
            m.iter()
                .find(|(k, _)| k.ends_with("Importer"))
                .map(|(k, v)| (k.to_string(), v.clone()))
        })
        .unwrap_or_else(|| {
            (
                default_importer(&asset.path).to_string(),
                unbound_yaml::Value::Map(Default::default()),
            )
        });
    let doc = unbound_yaml::Document {
        class_id: 0,
        file_id: 0,
        stripped: false,
        type_name,
        body,
    };
    Ok(unbound_scene::encode_documents(&[doc]))
}

/// The importer the Editor uses for a file whose .meta names none, by its extension.
fn default_importer(path: &str) -> &'static str {
    let name = path.rsplit('/').next().unwrap_or(path);
    if name == "package.json" {
        return "PackageManifestImporter";
    }
    let ext = name
        .rsplit_once('.')
        .map(|(_, e)| e.to_ascii_lowercase())
        .unwrap_or_default();
    match ext.as_str() {
        "txt" | "html" | "htm" | "xml" | "json" | "csv" | "yaml" | "bytes" | "fnt" | "md" => {
            "TextScriptImporter"
        }
        "cs" => "MonoImporter",
        "asmdef" => "AssemblyDefinitionImporter",
        "asmref" => "AssemblyDefinitionReferenceImporter",
        "shader" => "ShaderImporter",
        "compute" => "ComputeShaderImporter",
        "png" | "jpg" | "jpeg" | "tga" | "psd" | "tif" | "tiff" | "gif" | "bmp" | "exr" | "hdr" => {
            "TextureImporter"
        }
        "fbx" | "obj" | "dae" | "3ds" | "dxf" | "blend" => "ModelImporter",
        "wav" | "mp3" | "ogg" | "aif" | "aiff" | "mod" | "it" | "s3m" | "xm" => "AudioImporter",
        "dll" | "so" | "a" | "aar" | "jar" | "bundle" | "framework" => "PluginImporter",
        "ttf" | "otf" => "TrueTypeFontImporter",
        "mat" | "asset" | "prefab" | "anim" | "controller" | "mask" | "physicmaterial" => {
            "NativeFormatImporter"
        }
        _ => "DefaultImporter",
    }
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
    /// The source of the built-in shader with this declared name.
    fn builtin_shader_file(&self, name: &str) -> Option<PathBuf> {
        let mut index = self.builtin_shaders.write().unwrap();
        let map = index.get_or_insert_with(|| {
            let mut map = HashMap::new();
            for root in &self.builtin_shader_roots {
                for entry in walkdir::WalkDir::new(root).into_iter().flatten() {
                    let path = entry.path();
                    if path.extension().is_none_or(|e| e != "shader") {
                        continue;
                    }
                    let Ok(text) = std::fs::read_to_string(path) else {
                        continue;
                    };
                    if let Some(declared) = declared_shader_name(&text) {
                        map.entry(declared.to_string())
                            .or_insert_with(|| path.to_path_buf());
                    }
                }
            }
            map
        });
        map.get(name).cloned()
    }

    fn shader_guid(&self, name: &str) -> Option<Guid> {
        let mut index = self.shaders.write().unwrap();
        if index.is_none() {
            let mut map = HashMap::new();
            for asset in self.db.iter() {
                if !asset.path.ends_with(".shader") {
                    continue;
                }
                let Ok(text) = std::fs::read_to_string(&asset.file) else {
                    continue;
                };
                if let Some(declared) = declared_shader_name(&text) {
                    map.insert(declared.to_string(), asset.guid);
                }
            }
            *index = Some(map);
        }
        index.as_ref().and_then(|m| m.get(name).copied())
    }
}

/// The name a ShaderLab file declares: `Shader "Group/Name"` (after a byte-order mark, if any).
fn declared_shader_name(text: &str) -> Option<&str> {
    text.trim_start_matches('\u{feff}')
        .lines()
        .find_map(|l| {
            l.trim_start_matches('\u{feff}')
                .trim_start()
                .strip_prefix("Shader")
        })
        .and_then(|rest| rest.split('"').nth(1))
}

/// Whether the project renders in gamma color space (ProjectSettings' m_ActiveColorSpace 0, the
/// default when it is missing).
fn project_in_gamma_space(project: &Project) -> bool {
    let text = std::fs::read_to_string(project.root.join("ProjectSettings/ProjectSettings.asset"))
        .unwrap_or_default();
    let space = unbound_yaml::parse_documents(&text)
        .ok()
        .and_then(|docs| {
            docs.iter().find_map(|d| {
                d.body
                    .get("m_ActiveColorSpace")
                    .and_then(unbound_yaml::Value::as_i64)
            })
        })
        .unwrap_or(0);
    space == 0
}

/// The active build target group (`Android`, `Standalone`, ...): the import overrides that apply.
/// Whether the GPU samples BC7 (set once the render device exists): imported textures then
/// upload compressed.
static GPU_BC7: std::sync::atomic::AtomicBool = std::sync::atomic::AtomicBool::new(false);

/// The render device can sample BC-compressed textures: import textures compressed too.
pub fn set_gpu_bc7(supported: bool) {
    let compress = supported && std::env::var_os("UNBOUND_NO_TEXTURE_COMPRESSION").is_none();
    GPU_BC7.store(compress, std::sync::atomic::Ordering::Relaxed);
}

/// The texture importer for the active build target, compressing when the GPU samples BC7.
fn texture_importer(s: &RuntimeState) -> TextureImporter {
    TextureImporter {
        compress: GPU_BC7.load(std::sync::atomic::Ordering::Relaxed),
        ..s.texture_importer.read().unwrap().clone()
    }
}

pub fn set_build_target(target: &str) {
    state().audio_importer.write().unwrap().build_target = target.to_string();
    state().texture_importer.write().unwrap().build_target = target.to_string();
}

/// Imports an asset audio clip (through the import cache) into a mixer clip handle.
pub fn import_audio(handle: u64, guid: &str) -> Result<crate::audio::protocol::AudioClipInfo> {
    use unbound_assets::audio::LoadType;
    let s = state();
    if guid.starts_with("bundle:") {
        // A player clip of an AssetBundle: its properties are the managed side's to read.
        let decoded = crate::bundles::audio(guid)?;
        let info = crate::audio::protocol::AudioClipInfo {
            frames: (decoded.samples.len() / decoded.channels.max(1)) as i32,
            channels: decoded.channels as i32,
            frequency: decoded.rate as i32,
            ..Default::default()
        };
        s.audio.mixer().add_clip(
            handle,
            crate::audio::Clip::new(decoded.channels, decoded.rate, decoded.samples),
        );
        return Ok(info);
    }
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
        .get_or_import(&texture_importer(s), &asset.file, &meta)?;
    // The import is ours alone (the cache is on disk): take its data instead of copying it.
    let mut texture = Arc::try_unwrap(texture).unwrap_or_else(|shared| (*shared).clone());
    s.textures.write().unwrap().insert(
        guid,
        TextureSprites {
            name: texture.name.clone(),
            sprites: std::mem::take(&mut texture.sprites),
        },
    );
    let sprite_count = s.textures.read().unwrap()[&guid].sprites.len();
    let format = match texture.format {
        unbound_assets::texture::TextureFormat::Rgba8 => i32::from(!texture.srgb),
        unbound_assets::texture::TextureFormat::Rgba32F => 2,
    };
    let mip_count = texture.mips.len() / texture.layers.max(1) as usize;
    // BC7 blocks when the GPU samples them (format 7 sRGB-capable, 8 linear), else RGBA8.
    let (gpu_format, data) = match texture.bc7.take() {
        Some(blocks) if GPU_BC7.load(std::sync::atomic::Ordering::Relaxed) => {
            let mut data = Vec::with_capacity(blocks.iter().map(|b| b.len()).sum());
            for b in &blocks {
                data.extend_from_slice(b);
            }
            (format + 7, data)
        }
        _ => {
            // Mip 0 keeps its buffer; the smaller levels are appended to it.
            let total: usize = texture.mips.iter().map(|m| m.data.len()).sum();
            let mut mips = std::mem::take(&mut texture.mips).into_iter();
            let mut data = mips.next().map(|m| m.data).unwrap_or_default();
            data.reserve_exact(total - data.len());
            for mip in mips {
                data.extend_from_slice(&mip.data);
            }
            (format, data)
        }
    };
    let wrap = |w: unbound_assets::texture::WrapMode| w as i32;
    let filter = texture.filter as i32;
    s.backend.read().unwrap().upload_texture(
        handle,
        owned::Texture {
            width: texture.width as i32,
            height: texture.height as i32,
            format: gpu_format,
            mip_count: mip_count as i32,
            data,
            filter_mode: filter,
            wrap_u: wrap(texture.wrap_u),
            wrap_v: wrap(texture.wrap_v),
            aniso_level: texture.aniso,
            render_target: false,
            random_write: false,
            auto_generate_mips: false,
            samples: 1,
            depth_bits: 0,
            layers: texture.layers as i32,
            dimension: if texture.layers == 6 { 4 } else { 2 },
        },
    );
    Ok(ImportedTextureInfo {
        width: texture.width as i32,
        height: texture.height as i32,
        mip_count: mip_count as i32,
        srgb: texture.srgb as i32,
        filter_mode: filter,
        wrap_u: wrap(texture.wrap_u),
        wrap_v: wrap(texture.wrap_v),
        aniso_level: texture.aniso,
        readable: texture.is_readable as i32,
        sprite_count: sprite_count as i32,
        format,
        dimension: if texture.layers == 6 { 4 } else { 2 },
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn shader_names_are_read_after_a_byte_order_mark() {
        assert_eq!(
            declared_shader_name("\u{feff}Shader \"Gadsme/VisibilityShader\"\n{"),
            Some("Gadsme/VisibilityShader")
        );
        assert_eq!(
            declared_shader_name("// comment\n  Shader \"A/B\" {"),
            Some("A/B")
        );
        assert_eq!(declared_shader_name("Pass {}"), None);
    }

    #[test]
    fn bare_metas_get_the_importer_of_their_extension() {
        assert_eq!(
            default_importer("Assets/Firebase/Editor/X_manifest.txt"),
            "TextScriptImporter"
        );
        assert_eq!(
            default_importer("Packages/com.x/package.json"),
            "PackageManifestImporter"
        );
        assert_eq!(
            default_importer("Packages/com.x/README.md"),
            "TextScriptImporter"
        );
        assert_eq!(default_importer("Assets/Art/Hero.PNG"), "TextureImporter");
        assert_eq!(default_importer("Assets/Folder"), "DefaultImporter");
    }

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
