//! What the native side knows while a game runs: the project, its asset database and loaded
//! documents. The managed engine reaches it through the native API.
use crate::render_protocol::{
    ImportedTextureInfo, NullBackend, RenderBackend, handle, owned, texture_format,
};
use anyhow::{Context, Result, bail};
use serde::Deserialize;
use serde_json::json;
use std::{
    collections::HashMap,
    path::{Path, PathBuf},
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
    /// The asset database; editor APIs that create, move or delete assets update it.
    db: RwLock<Arc<AssetDb>>,
    loader: DocumentLoader<Locator>,
    /// Lowercased path below a `Resources` folder, without extension → assets.
    resources: RwLock<HashMap<String, Vec<Guid>>>,
    /// The asset database's roots (`Assets` and each package), as the project scanned them.
    asset_roots: Vec<(PathBuf, String)>,
    /// The asset folders the project holds itself (`Project::editable_roots`).
    editable_roots: Vec<PathBuf>,
    /// What new `.meta` files depend on (the installation's platform modules).
    new_meta: unbound_assets::new_meta::MetaContext,
    /// The project's release family's new `.meta` files are measured (6000.0).
    new_meta_measured: bool,
    /// TextCore's COLR layers select native hinting in the measured release family.
    pub(crate) font_colr_native_hinting: bool,
    backend: RwLock<Arc<dyn RenderBackend>>,
    imports: AssetCache,
    /// Imports textures with the active build target's overrides.
    texture_importer: RwLock<TextureImporter>,
    /// Imports models as the project's release does.
    model_importer: unbound_assets::ModelImporter,
    /// Imported textures' sprites by GUID (asked for after the import). Only the name and the
    /// sprites stay: the pixels live on the GPU once uploaded.
    textures: RwLock<HashMap<Guid, TextureSprites>>,
    texture_mip_biases: RwLock<HashMap<u64, f32>>,
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
    /// The atlases the project's Sprite Packer mode packs for Play mode.
    pub play_mode_atlases: Option<unbound_project::PlayModeAtlases>,
    /// Those atlases packed (on first use in Play mode, or updated via SpriteAtlasUtility.PackAtlases).
    atlases: RwLock<Option<crate::sprite_atlases::AtlasSet>>,
    /// The texels of the imported texture traced last (Sprite.Create often cuts many sprites
    /// from one texture).
    traced_texels: std::sync::Mutex<Option<(String, Arc<Texels>)>>,
    /// Textures scripts generated (TextureGenerator.GenerateTexture) by their `generated:` key:
    /// they have no file to import again, so their texels stay until the texture is destroyed.
    generated: RwLock<HashMap<String, Arc<unbound_assets::texture::ImportedTexture>>>,
}

/// The key prefix of a texture a script generated: `generated:` and a GUID's 32 hex digits.
pub const GENERATED_KEY_PREFIX: &str = "generated:";

/// The GUID a texture key stands for: an asset's GUID, or a generated texture's own.
fn texture_guid(key: &str) -> Result<Guid> {
    Guid::parse(key.strip_prefix(GENERATED_KEY_PREFIX).unwrap_or(key)).context("bad guid")
}

/// What stays of an imported texture after its upload.
struct TextureSprites {
    name: String,
    sprites: Vec<SpriteDef>,
    /// Polygon sprite mode: each sprite's outline is its mesh's boundary.
    polygon: bool,
}

static STATE: OnceLock<RuntimeState> = OnceLock::new();

pub fn state() -> &'static RuntimeState {
    STATE.get().expect("the runtime state is not initialized")
}

/// The `Resources` keys of every asset of `db`.
fn resources_of(db: &AssetDb) -> HashMap<String, Vec<Guid>> {
    let mut resources: HashMap<String, Vec<Guid>> = HashMap::new();
    for asset in db.iter() {
        if asset.is_folder {
            continue;
        }
        for key in resources_keys(&asset.path) {
            resources.entry(key).or_default().push(asset.guid);
        }
    }
    resources
}

impl RuntimeState {
    /// The asset database as it is now.
    pub fn db(&self) -> Arc<AssetDb> {
        self.db.read().unwrap().clone()
    }

    /// Changes the asset database and what follows from it (the `Resources` index).
    /// Changes the asset database; the `Resources` index follows when `resources` (a change of an
    /// asset below a `Resources` folder, or a new scan).
    fn update_db(&self, resources: bool, change: impl FnOnce(&mut AssetDb)) {
        let mut db = self.db.write().unwrap();
        change(Arc::make_mut(&mut db));
        if resources {
            *self.resources.write().unwrap() = resources_of(&db);
        }
    }

    /// The asset path (`Assets/...`, `Packages/<name>/...`) of a file or folder of the project's
    /// asset roots (not a root itself: `Assets` and a package's root folder, which have no `.meta`
    /// and are never imported or written).
    fn file_of(&self, asset_path: &str) -> Option<PathBuf> {
        self.asset_roots.iter().find_map(|(dir, root)| {
            let head = asset_path.get(..root.len())?;
            let rest = asset_path[root.len()..].strip_prefix('/')?;
            (head.eq_ignore_ascii_case(root) && !rest.is_empty()).then(|| dir.join(rest))
        })
    }

    /// Brings the database up to date for the assets at `paths` as the Editor imports them: a
    /// file or folder without a `.meta` gets one (`main` the main object's file id of a native
    /// asset an API created), one that is gone leaves the database, and one with a `.meta` is
    /// listed as its `.meta` says; a `.meta` whose GUID another asset has (a copied file) gets a
    /// new one, as the Editor gives it. Hidden names and what is inside a folder plug-in are no
    /// assets. Each path's result says what it is now (an empty GUID: not an asset) or why it
    /// was not imported; one path's failure does not stop the others.
    fn import_paths(&self, paths: &[ImportPath]) -> Vec<serde_json::Value> {
        let mut found = Vec::with_capacity(paths.len());
        let mut entries: Vec<(String, Option<unbound_project::assetdb::Asset>)> =
            Vec::with_capacity(paths.len());
        let mut taken: HashMap<Guid, String> = HashMap::new();
        let db = self.db();
        for request in paths {
            match self.import_path(request, &db, &taken) {
                Ok(Some((asset, conflict))) => {
                    let mut result = json!({
                        "guid": asset.guid.to_string(),
                        "kind": listed_kind(&asset),
                        "importer": asset_importer(&asset),
                        "labels": asset.labels,
                    });
                    if let Some((guid, owner)) = conflict {
                        result["conflict"] = json!({ "guid": guid.to_string(), "owner": owner });
                    }
                    found.push(result);
                    taken.insert(asset.guid, request.path.to_ascii_lowercase());
                    entries.push((request.path.clone(), Some(asset)));
                }
                Ok(None) => {
                    found.push(json!({ "guid": "" }));
                    entries.push((request.path.clone(), None));
                }
                Err(e) => found.push(json!({ "guid": "", "error": format!("{e:#}") })),
            }
        }
        let resources = entries
            .iter()
            .any(|(path, _)| !resources_keys(path).is_empty());
        self.update_db(resources, |db| {
            for (path, asset) in entries {
                db.remove_path(&path);
                if let Some(asset) = asset {
                    db.insert(asset);
                }
            }
        });
        found
    }

    /// One path of [`Self::import_paths`]: the asset it is now (with the GUID it had and the
    /// asset that holds it when that GUID was another's), None when nothing is there.
    #[allow(clippy::type_complexity)]
    fn import_path(
        &self,
        request: &ImportPath,
        db: &AssetDb,
        taken: &HashMap<Guid, String>,
    ) -> Result<Option<(unbound_project::assetdb::Asset, Option<(Guid, String)>)>> {
        let file = self
            .file_of(&request.path)
            .with_context(|| format!("{} is no asset of the project", request.path))?;
        let mut segments = request.path.split('/').skip(1).peekable();
        while let Some(segment) = segments.next() {
            anyhow::ensure!(
                !unbound_project::assetdb::is_hidden(segment),
                "{}: a hidden name is no asset",
                request.path
            );
            anyhow::ensure!(
                segments.peek().is_none()
                    || !unbound_assets::new_meta::is_folder_plugin(Path::new(segment)),
                "{}: what is inside a folder plug-in is no asset of its own",
                request.path
            );
        }
        if !file.exists() {
            return Ok(None);
        }
        let meta = unbound_assets::new_meta::meta_path(&file);
        if !meta.exists() {
            anyhow::ensure!(
                self.editable_roots
                    .iter()
                    .any(|root| file.starts_with(root)),
                "{} is in a read-only package: the Editor imports nothing there",
                request.path
            );
            anyhow::ensure!(
                self.new_meta_measured,
                "Unbound has not measured the .meta files this release's Editor writes: {} is left without one",
                request.path
            );
            let importer = match request.main {
                Some(main) => unbound_assets::new_meta::NewImporter::NativeFormat { main },
                None => unbound_assets::new_meta::importer_for(&file, file.is_dir()).with_context(
                    || {
                        format!(
                            "Unbound does not know the .meta file the Editor writes for {} yet",
                            request.path
                        )
                    },
                )?,
            };
            let guid = unbound_assets::new_meta::new_guid();
            std::fs::write(
                &meta,
                unbound_assets::new_meta::text(importer, guid, &file, &self.new_meta),
            )?;
        }
        let (guid, labels, importer) = unbound_project::assetdb::read_meta_header(&meta);
        let mut guid = guid.with_context(|| format!("{}.meta has no guid", request.path))?;
        let path = request.path.to_ascii_lowercase();
        let owner = db
            .by_guid(guid)
            .filter(|other| other.path.to_ascii_lowercase() != path && other.file.exists())
            .map(|other| other.path.clone())
            .or_else(|| taken.get(&guid).filter(|other| **other != path).cloned());
        let mut conflict = None;
        if let Some(owner) = owner {
            let fresh = unbound_assets::new_meta::new_guid();
            // Written again with LF, as the Editor writes a .meta (tests/projects/projectwrites 06.crlf).
            let text = std::fs::read_to_string(&meta)?.replace("\r\n", "\n");
            let (line, rest) = text
                .match_indices("guid: ")
                .map(|(at, _)| at)
                .find(|&at| at == 0 || text.as_bytes()[at - 1] == b'\n')
                .map(|at| text.split_at(at))
                .context("no guid line")?;
            let after = rest.find(['\r', '\n']).unwrap_or(rest.len());
            std::fs::write(&meta, format!("{line}guid: {fresh}{}", &rest[after..]))?;
            conflict = Some((guid, owner));
            guid = fresh;
        }
        Ok(Some((
            unbound_project::assetdb::Asset {
                guid,
                path: request.path.clone(),
                is_folder: file.is_dir(),
                file,
                labels,
                importer,
            },
            conflict,
        )))
    }

    /// AssetDatabase.Refresh as the Editor refreshes: every new file and folder of the project's
    /// own assets imported (a `.meta` written), and what changed on disk without the
    /// AssetDatabase imported again (assets deleted, copied with their `.meta`, `.meta` files
    /// edited: tests/projects/projectwrites 07.outside), a copy whose `.meta` repeats another
    /// asset's GUID getting a new one. Read-only packages do not change in a session and are not
    /// looked at. Returns each path that changed: what it is now, or for a gone asset its former
    /// GUID as `removed` (none when the GUID is listed again elsewhere: the asset moved).
    fn refresh_assets(&self) -> Vec<serde_json::Value> {
        if self.new_meta_measured {
            unbound_assets::new_meta::import_roots(
                &self.editable_roots,
                &self.new_meta,
                unbound_project::assetdb::is_hidden,
            );
        }
        let roots: Vec<_> = self
            .asset_roots
            .iter()
            .filter(|(dir, _)| self.editable_roots.contains(dir))
            .map(|(dir, path)| unbound_project::assetdb::Root {
                dir,
                virtual_path: path.clone(),
            })
            .collect();
        let (changed, gone) = unbound_project::assetdb::changes(
            &self.db(),
            &roots,
            unbound_assets::new_meta::is_folder_plugin,
        );
        let requests: Vec<ImportPath> = changed
            .into_iter()
            .chain(gone.iter().map(|asset| asset.path.clone()))
            .map(|path| ImportPath { path, main: None })
            .collect();
        let results = self.import_paths(&requests);
        let gone: HashMap<&str, Guid> = gone
            .iter()
            .map(|asset| (asset.path.as_str(), asset.guid))
            .collect();
        let listed: std::collections::HashSet<String> = results
            .iter()
            .filter_map(|r| {
                r["guid"]
                    .as_str()
                    .filter(|g| !g.is_empty())
                    .map(str::to_string)
            })
            .collect();
        requests
            .iter()
            .zip(results)
            .map(|(request, mut result)| {
                result["path"] = json!(request.path);
                if let Some(guid) = gone
                    .get(request.path.as_str())
                    .filter(|_| result.get("error").is_none())
                {
                    let guid = guid.to_string();
                    if !listed.contains(&guid) {
                        result["removed"] = json!(guid);
                    }
                }
                result
            })
            .collect()
    }
}

#[derive(Deserialize)]
struct ImportPath {
    path: String,
    #[serde(default)]
    main: Option<i64>,
}

pub fn set_texture_mip_bias(handle: u64, bias: f32) {
    state()
        .texture_mip_biases
        .write()
        .unwrap()
        .insert(handle, bias);
    backend().texture_mip_bias(handle, bias);
}

pub fn release_texture_sampler(handle: u64) {
    state().texture_mip_biases.write().unwrap().remove(&handle);
}

/// Sets up the state for a project; once per process.
pub fn initialize(project: &Project) -> Result<&'static RuntimeState> {
    let db = Arc::new(project.asset_db());
    let locator: Locator = Box::new(move |guid| state().db().by_guid(guid).map(|a| a.file.clone()));
    let resources = resources_of(&db);
    let play_mode_atlases = project.settings.play_mode_atlases(
        db.iter()
            .filter(|asset| !asset.is_folder)
            .map(|asset| asset.path.as_str()),
    );
    // The textures SpeedTree 9 trees use import with the settings the trees give them.
    let tree_textures =
        std::sync::Arc::new(unbound_assets::speedtree9_textures::TreeTextures::read(
            db.iter()
                .filter(|asset| !asset.is_folder)
                .map(|asset| (asset.path.as_str(), asset.file.as_path())),
        ));
    let state = RuntimeState {
        project_root: project.root.clone(),
        unity_data: project.install.data.clone(),
        asset_roots: project
            .asset_root_dirs()
            .into_iter()
            .map(|(dir, path)| (dir.to_path_buf(), path))
            .collect(),
        editable_roots: project
            .editable_roots()
            .into_iter()
            .map(Path::to_path_buf)
            .collect(),
        new_meta_measured: unbound_compat::MeasuredFamily::of(project.version).is_some(),
        font_colr_native_hinting: project.version.measured_family()
            == Some(unbound_compat::MeasuredFamily::Unity6000_0),
        new_meta: unbound_assets::new_meta::MetaContext::for_install(
            &unbound_project::install::playback_engines(&project.install.data),
        ),
        db: RwLock::new(db),
        loader: DocumentLoader::new(locator)
            .with_synthesizer(Box::new(|guid, file| {
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
                    // A ScriptedImporter's prefab (a SpeedTree 9 tree): the managed engine runs the
                    // importer and writes the documents of its hierarchy.
                    return unbound_assets::meta::scripted_importer_of(file)
                        .map(|_| crate::imported_prefabs::documents(guid));
                }
                let data = std::fs::read(file).ok()?;
                unbound_assets::serialized::is_binary(&data)
                    .then(|| unbound_assets::serialized::documents(&data))
            }))
            .with_postprocessor(Box::new(crate::imported_prefabs::postprocess)),
        resources: RwLock::new(resources),
        texture_mip_biases: Default::default(),
        backend: RwLock::new(Arc::new(NullBackend)),
        imports: AssetCache::for_project(&project.root),
        texture_importer: RwLock::new(TextureImporter {
            gamma: project_in_gamma_space(project),
            project: unbound_assets::texture::ProjectCompression::read(&project.root),
            tree_textures,
            ..TextureImporter::default()
        }),
        model_importer: unbound_assets::ModelImporter::for_release(&project.version),
        textures: RwLock::new(HashMap::new()),
        generated: RwLock::new(HashMap::new()),
        shaders: RwLock::new(None),
        builtin_shader_roots: builtin_shader_roots(
            project.builtin_shaders().map(|b| b.shader_roots()),
        ),
        builtin_shaders: RwLock::new(None),
        audio_importer: RwLock::new(AudioImporter::default()),
        models: RwLock::new(HashMap::new()),
        audio: crate::audio::Engine::new_with_output_rules(
            crate::audio::ProjectAudioSettings::read(&project.root),
            true,
            unbound_compat::AudioOutputRules::for_version(project.version),
        ),
        play_mode_atlases,
        atlases: RwLock::new(None),
        traced_texels: std::sync::Mutex::new(None),
    };
    if STATE.set(state).is_err() {
        bail!("the runtime state is already initialized");
    }
    Ok(self::state())
}

/// The folders of the release's built-in shader sources, none when they are not available: that
/// is reported once, with its cause (an `UNBOUND_OFFLINE` cache miss names how to provide them),
/// since built-in shaders found by name then keep default properties.
fn builtin_shader_roots(found: Result<Vec<PathBuf>>) -> Vec<PathBuf> {
    found.unwrap_or_else(|e| {
        tracing::warn!("{}", builtin_shaders_unavailable(&e));
        Vec::new()
    })
}

fn builtin_shaders_unavailable(e: &anyhow::Error) -> String {
    format!(
        "Unity's built-in shader sources are not available, so built-in shaders found by name \
         keep default properties: {e:#}"
    )
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

/// Top-level `Assets/` trees whose files the Editor imports with `DefaultImporter` as
/// `DefaultAsset` instead of their extension's importer, the root's name matched in any case
/// (matches the Unity 6000.0.71f1 Editor, `tests/projects/defaultasset`: `Assets/StreamingAssets/`,
/// `Assets/WebGLTemplates/`, `Assets/webplayertemplates/`; a nested `Assets/Data/StreamingAssets/`
/// is not special).
fn is_special_default_imported_folder(path: &str) -> bool {
    const ROOTS: [&str; 3] = [
        "assets/streamingassets/",
        "assets/webgltemplates/",
        "assets/webplayertemplates/",
    ];
    ROOTS.iter().any(|root| {
        path.len() > root.len()
            && path
                .as_bytes()
                .get(..root.len())
                .is_some_and(|prefix| prefix.eq_ignore_ascii_case(root.as_bytes()))
    })
}

/// What the managed side asks about an asset: how it loads.
fn asset_kind(path: &str) -> &'static str {
    if is_special_default_imported_folder(path) {
        return "unknown";
    }
    match default_importer(path) {
        "PluginImporter" => return "plugin",
        "NativeFormatImporter" => return "yaml",
        _ => {}
    }
    match path_extension(path).as_str() {
        "prefab" => "prefab",
        "unity" => "scene",
        // A V2 atlas's SpriteAtlasAsset document loads like a native-format file.
        "spriteatlasv2" => "yaml",
        "png" | "jpg" | "jpeg" | "tga" | "psd" | "bmp" | "gif" | "tif" | "tiff" | "exr" | "hdr"
        | "iff" | "pict" => "texture",
        "wav" | "mp3" | "ogg" | "aif" | "aiff" | "flac" | "mod" | "it" | "s3m" | "xm" => "audio",
        "fbx" | "obj" | "dae" | "3ds" | "dxf" | "blend" | "max" | "ma" | "mb" => "model",
        "txt" | "json" | "bytes" | "xml" | "csv" | "html" | "htm" | "yaml" | "md" | "fnt" => "text",
        "ttf" | "otf" | "ttc" | "dfont" => "font",
        "cs" => "script",
        "shader" | "cginc" | "hlsl" | "shadergraph" => "shader",
        "compute" => "compute",
        "vfx" | "vfxoperator" | "vfxblock" => "vfx",
        "raytrace" => "raytrace",
        "uxml" => "uxml",
        "uss" => "uss",
        "tss" => "tss",
        "mp4" | "webm" | "mov" | "m4v" => "video",
        _ => "unknown",
    }
}

/// The extension of the last name of an asset path, lowercase; empty for a name without one.
fn path_extension(path: &str) -> String {
    let name = path.rsplit('/').next().unwrap_or(path);
    name.rsplit_once('.')
        .map(|(_, e)| e.to_ascii_lowercase())
        .unwrap_or_default()
}

/// The file id of an asset file's first object: its main object.
fn first_file_id_in(text: &str) -> Option<i64> {
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
        "texture-sampling-properties" => {
            #[derive(Deserialize)]
            #[serde(rename_all = "camelCase")]
            struct Request {
                handle: u64,
                filter_mode: i32,
                wrap_u: i32,
                wrap_v: i32,
                wrap_w: i32,
                aniso_level: i32,
            }
            let r: Request = serde_json::from_str(request)?;
            handle::plain(r.handle, method).map_err(anyhow::Error::msg)?;
            anyhow::ensure!(r.handle != 0, "sampler properties require a texture handle");
            backend().texture_sampler(
                r.handle,
                [r.filter_mode, r.wrap_u, r.wrap_v, r.wrap_w, r.aniso_level],
            );
            Ok("true".to_owned())
        }
        "texture-sampler-state" => {
            #[derive(Deserialize)]
            #[serde(rename_all = "camelCase")]
            struct Request {
                handle: u64,
                mip_bias: Option<f32>,
            }
            let r: Request = serde_json::from_str(request)?;
            handle::plain(r.handle, method).map_err(anyhow::Error::msg)?;
            if let Some(bias) = r.mip_bias {
                set_texture_mip_bias(r.handle, bias);
            }
            Ok(json!(
                s.texture_mip_biases
                    .read()
                    .unwrap()
                    .get(&r.handle)
                    .copied()
                    .unwrap_or(0.0)
            )
            .to_string())
        }
        "lighting-data" => {
            #[derive(serde::Deserialize)]
            #[serde(rename_all = "camelCase")]
            struct Request {
                guid: String,
                file_id: i64,
            }
            let r: Request = serde_json::from_str(request)?;
            Ok(
                crate::lighting::lighting_data(&s.db(), &s.unity_data, &r.guid, r.file_id)?
                    .to_string(),
            )
        }
        "realtime-gi-load" => {
            #[derive(serde::Deserialize)]
            #[serde(rename_all = "camelCase")]
            struct Request {
                guid: String,
                file_id: i64,
            }
            let r: Request = serde_json::from_str(request)?;
            Ok(crate::realtime_gi::load(&s.db(), &s.unity_data, &r.guid, r.file_id)?.to_string())
        }
        "realtime-gi-bake" => {
            let r: crate::realtime_gi::BakeRequest = serde_json::from_str(request)?;
            Ok(crate::realtime_gi::bake_request(r)?.to_string())
        }
        "baked-gi-reflection-image" => {
            let r: crate::baked_gi::DefaultReflectionRequest = serde_json::from_str(request)?;
            Ok(crate::baked_gi::reflection_image(r)?.to_string())
        }
        "baked-gi-lighting-data" => {
            let r: crate::baked_gi::LightingDataRequest = serde_json::from_str(request)?;
            Ok(crate::baked_gi::lighting_data_file(r)?.to_string())
        }
        "baked-gi-default-reflection" => {
            let r: crate::baked_gi::DefaultReflectionRequest = serde_json::from_str(request)?;
            Ok(crate::baked_gi::default_reflection(r)?.to_string())
        }
        "baked-gi-bake" => {
            let r: crate::baked_gi::BakeRequest = serde_json::from_str(request)?;
            Ok(crate::baked_gi::bake_request(r)?.to_string())
        }
        "light-transport-populate" => {
            let r: crate::light_transport::PopulateRequest = serde_json::from_str(request)?;
            Ok(crate::light_transport::populate(r)?.to_string())
        }
        "light-transport-release" => {
            #[derive(serde::Deserialize)]
            #[serde(rename_all = "camelCase")]
            struct Request {
                world_id: u64,
            }
            let r: Request = serde_json::from_str(request)?;
            crate::light_transport::release(r.world_id);
            Ok("null".into())
        }
        "light-transport-integrate" => {
            let r: crate::light_transport::IntegrateRequest = serde_json::from_str(request)?;
            Ok(crate::light_transport::integrate(r)?.to_string())
        }
        "light-transport-dering" => {
            let r: crate::light_transport::DeringRequest = serde_json::from_str(request)?;
            Ok(crate::light_transport::dering(r)?.to_string())
        }
        "light-transport-assign-channels" => {
            let r: crate::light_transport::AssignChannelsRequest = serde_json::from_str(request)?;
            Ok(crate::light_transport::assign_channels(r)?.to_string())
        }
        "realtime-gi-release" => {
            #[derive(serde::Deserialize)]
            struct Request {
                handle: u64,
            }
            let r: Request = serde_json::from_str(request)?;
            crate::realtime_gi::release(r.handle);
            Ok("null".into())
        }
        "mesh-object" => {
            #[derive(serde::Deserialize)]
            #[serde(rename_all = "camelCase")]
            struct Request {
                guid: String,
                file_id: i64,
            }
            let r: Request = serde_json::from_str(request)?;
            Ok(
                crate::lighting::mesh_object(&s.db(), &s.unity_data, &r.guid, r.file_id)?
                    .to_string(),
            )
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
                &s.db(),
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
        "vsync" => crate::presentation::set(request),
        "display-info" => crate::display::query(request),
        "keyboard-configuration" => Ok(crate::keyboard_config::snapshot()),
        "gamepads-poll" => {
            #[derive(serde::Deserialize)]
            struct Request {
                family: String,
            }
            let r: Request = serde_json::from_str(request)?;
            Ok(crate::gamepads::poll(&r.family).to_string())
        }
        "ime-state" => crate::ime::state(request),
        "ime-request" => crate::ime::request(request),
        "bundle-open" => open_bundle(request),
        "bundle-cache-form" => bundle_cache_form(request),
        "bundle-close" => {
            #[derive(serde::Deserialize)]
            struct Request {
                id: u64,
            }
            let r: Request = serde_json::from_str(request)?;
            crate::bundles::close(r.id);
            Ok("{}".into())
        }
        // What shaders declare, from the host's provider (with or without a renderer).
        "shader-declarations" => crate::shader_declarations::shader_request(request),
        "compute-declarations" => crate::shader_declarations::compute_request(request),
        "graphics-fence" => backend()
            .graphics_fence(request == "insert")
            .map(|sequence| sequence.to_string())
            .map_err(|e| anyhow::anyhow!("{e}")),
        "shader-passes" => backend()
            .shader_passes(request)
            .map_err(|e| anyhow::anyhow!("{e}")),
        "shader-warmup" => backend()
            .shader_warmup(serde_json::from_str(request)?)
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
            let db = s.db();
            let asset = match (&r.guid, &r.path) {
                (Some(guid), _) => db.by_guid(Guid::parse(guid).context("bad guid")?),
                (None, Some(path)) => db.by_path(path),
                _ => None,
            }
            .with_context(|| {
                format!(
                    "no asset {}",
                    r.guid.as_deref().or(r.path.as_deref()).unwrap_or("")
                )
            })?;
            let importer = (!asset.is_folder)
                .then(|| unbound_assets::meta::scripted_importer_of(&asset.file))
                .flatten();
            let kind = if importer.is_some() {
                "scripted"
            } else {
                listed_kind(asset)
            };
            let main = if kind == "yaml" {
                unbound_assets::meta::main_object_file_id(&asset.file)
                    .or_else(|| {
                        let text = std::fs::read_to_string(&asset.file).ok()?;
                        unbound_assets::meta::conventional_main_file_id(&text)
                            .or_else(|| first_file_id_in(&text))
                    })
                    .unwrap_or(0)
            } else {
                0
            };
            Ok(json!({
                "guid": asset.guid.to_string(),
                "path": asset.path,
                "file": asset.file,
                "kind": kind,
                "mainFileId": main,
                "importer": importer.as_ref().map(|(guid, _)| guid),
                "importerFileId": importer.as_ref().map_or(0, |(_, id)| *id),
                "managed": kind == "plugin" && unbound_project::plugins::is_managed_assembly(&asset.file),
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
                for (k, guids) in s.resources.read().unwrap().iter() {
                    if k.starts_with(&prefix) || *k == key {
                        found.extend(guids.iter().copied());
                    }
                }
            } else if let Some(guids) = s.resources.read().unwrap().get(&key) {
                found.extend(guids.iter().copied());
            }
            let db = s.db();
            let list: Vec<_> = found
                .into_iter()
                .filter_map(|g| db.by_guid(g))
                .map(|a| json!({ "guid": a.guid.to_string(), "path": a.path }))
                .collect();
            Ok(serde_json::Value::Array(list).to_string())
        }
        "asset-import" => {
            // Editor APIs that made, moved or deleted asset files: the database follows.
            #[derive(Deserialize)]
            struct Request {
                paths: Vec<ImportPath>,
            }
            let r: Request = serde_json::from_str(request)?;
            Ok(json!({ "assets": s.import_paths(&r.paths) }).to_string())
        }
        "asset-refresh" => Ok(json!({ "assets": s.refresh_assets() }).to_string()),
        "texture-platforms" => {
            // The installation's platform modules a texture's settings list, with each entry at
            // its defaults (what the Editor adds to a texture's .meta when it saves its settings).
            // Measured for the 6000.0 family only: nothing is added for another.
            let platforms: &[String] = if s.new_meta_measured { &s.new_meta.platforms } else { &[] };
            let entries: Vec<_> = platforms
                .iter()
                .map(|name| json!({ "name": name, "entry": unbound_assets::new_meta::texture_platform_entry(name) }))
                .collect();
            Ok(serde_json::Value::Array(entries).to_string())
        }
        "script-meta-section" => Ok(json!({
            "section": if s.new_meta_measured { unbound_assets::new_meta::MONO_IMPORTER_SECTION } else { "" }
        })
        .to_string()),
        "asset-roots" => {
            // The asset database's roots, and whether the project holds each (where editor APIs write).
            let roots: Vec<_> = s
                .asset_roots
                .iter()
                .map(|(dir, path)| {
                    json!({
                        "path": path,
                        "editable": s.editable_roots.iter().any(|root| root == dir),
                    })
                })
                .collect();
            Ok(serde_json::Value::Array(roots).to_string())
        }
        "asset-list" => {
            // Every asset: what AssetDatabase.GetAllAssetPaths and FindAssets search.
            let list: Vec<_> = s
                .db()
                .iter()
                .map(|a| {
                    json!({
                        "guid": a.guid.to_string(),
                        "path": a.path,
                        "kind": listed_kind(a),
                        "importer": asset_importer(a),
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
            let db = s.db();
            let asset = db
                .by_guid(Guid::parse(&r.guid).context("bad guid")?)
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
            Ok(model_mesh_json(&model, r.index)?.to_string())
        }
        "upload-model-mesh" => {
            #[derive(Deserialize)]
            struct Request {
                handle: u64,
                guid: String,
                index: usize,
            }
            let r: Request = serde_json::from_str(request)?;
            let model = imported_model(Guid::parse(&r.guid).context("bad guid")?)?;
            let mesh = model.meshes.get(r.index).context("no such mesh")?;
            s.backend
                .read()
                .unwrap()
                .upload_mesh(r.handle, crate::models::model_mesh_owned(mesh));
            Ok("{}".to_string())
        }
        "editor-resource" => {
            #[derive(Deserialize)]
            #[serde(rename_all = "camelCase")]
            struct Request {
                path: Option<String>,
                path_id: Option<i64>,
                // An object of a built-in resource file by `{guid, fileId}`.
                guid: Option<String>,
                file_id: Option<i64>,
            }
            let r: Request = serde_json::from_str(request)?;
            if let Some(guid) = r.guid {
                anyhow::ensure!(
                    r.path.is_none() && r.path_id.is_none(),
                    "an editor resource request by guid takes a fileId, not a path or a path id"
                );
                let file_id = r
                    .file_id
                    .context("an editor resource request by guid needs a fileId")?;
                Ok(
                    crate::editor_resources::builtin_object(
                        &s.db(),
                        &s.unity_data,
                        &guid,
                        file_id,
                    )?
                    .to_string(),
                )
            } else if let Some(path) = r.path {
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
            // An imported texture's requested mip of one image (an array slice, a cube face; a
            // volume's level holds all its depth slices), rows bottom first, retaining float
            // precision.
            #[derive(Deserialize)]
            #[serde(rename_all = "camelCase")]
            struct Request {
                guid: String,
                #[serde(default)]
                mip: usize,
                #[serde(default)]
                layer: Option<usize>,
                /// The pixels before any compression (what OnPostprocessTexture is given).
                #[serde(default)]
                before_compression: bool,
            }
            let r: Request = serde_json::from_str(request)?;
            let texture = if r.before_compression {
                let guid = Guid::parse(&r.guid).context("bad guid")?;
                let db = s.db();
                let asset = db.by_guid(guid).context("no such asset")?;
                let meta = PathBuf::from(format!("{}.meta", asset.file.display()));
                let importer = TextureImporter {
                    before_compression: true,
                    ..texture_importer(s)
                };
                s.imports.get_or_import(&importer, &asset.file, &meta)?
            } else {
                imported_texture(s, &r.guid)?
            };
            let mip = match r.layer {
                // Without a layer: the n-th stored level, as 2D textures always asked.
                None => texture.mips.get(r.mip),
                Some(layer) => texture.level(layer, r.mip),
            }
            .context("the texture has no such mip")?;
            let format = match texture.format {
                unbound_assets::texture::TextureFormat::Rgba8 => 0,
                unbound_assets::texture::TextureFormat::Rgba32F => 2,
            };
            Ok(json!({ "width": mip.width, "height": mip.height, "format": format, "hasAlpha": texture.has_alpha,
                "rgba": crate::models::base64(&mip.data) }).to_string())
        }
        "texture-source" => {
            // What an imported texture's source image is: its size before scaling and whether
            // some pixel is not opaque (TextureImporter.GetSourceTextureWidthAndHeight and
            // DoesSourceTextureHaveAlpha), and the imported size.
            #[derive(Deserialize)]
            struct Request {
                guid: String,
            }
            let r: Request = serde_json::from_str(request)?;
            let texture = imported_texture(s, &r.guid)?;
            Ok(json!({
                "width": texture.original_width,
                "height": texture.original_height,
                "alpha": texture.has_alpha,
                "importedWidth": texture.width,
                "importedHeight": texture.height,
                "compressedFormat": texture.blocks.as_ref().map(|b| b.format.unity_format()),
            })
            .to_string())
        }
        "texture-raw" => {
            // An imported block-compressed texture's mip chain as stored (none for others).
            #[derive(Deserialize)]
            struct Request {
                guid: String,
            }
            let r: Request = serde_json::from_str(request)?;
            let texture = imported_texture(s, &r.guid)?;
            let blocks = texture
                .blocks
                .as_ref()
                .context("the texture is not block-compressed")?;
            let raw = if let Some(crunched) = &texture.crunched {
                crate::models::base64(&crunched.data)
            } else {
                crate::models::base64(&concat(&blocks.mips))
            };
            Ok(json!({ "format": blocks.format.unity_format(), "raw": raw }).to_string())
        }
        "texture-crunch-blocks" => {
            #[derive(Deserialize)]
            struct Request {
                data: String,
            }
            let r: Request = serde_json::from_str(request)?;
            let data = crate::fonts::decode_base64(&r.data).context("data is not base64")?;
            let image = unbound_assets::texture::crunch::transcode(&data)?;
            Ok(
                json!({ "format": image.format.unity_format(), "width": image.width,
                "height": image.height, "mips": image.blocks.mips.len(),
                "blocks": crate::models::base64(&concat(&image.blocks.mips)) })
                .to_string(),
            )
        }
        "texture-decode-blocks" => {
            // One level of BC blocks (Unity's TextureFormat `format`) as RGBA8, as GetPixels32
            // reads it; with `floats`, BC6H and ASTC HDR as RGBA floats.
            #[derive(Deserialize)]
            struct Request {
                format: i32,
                width: u32,
                height: u32,
                data: String,
                #[serde(default)]
                floats: bool,
                #[serde(default)]
                eac16: bool,
            }
            let r: Request = serde_json::from_str(request)?;
            // ETC2 RGBA1, signed EAC and PVRTC, which only layered textures keep as blocks:
            // RGBA8, or signed EAC's snorm channels (with `eac16`) for the renderer.
            if unbound_assets::texture::layered::is_layered_block_format(r.format) {
                use unbound_assets::texture::layered;
                let blocks = crate::fonts::decode_base64(&r.data).context("data is not base64")?;
                let rgba = if r.eac16 {
                    layered::decode_signed_eac_gpu(r.format, &blocks, r.width, r.height)?
                } else {
                    layered::decode_rgba(r.format, &blocks, r.width, r.height)?
                };
                return Ok(json!({ "rgba": crate::models::base64(&rgba) }).to_string());
            }
            let format = unbound_assets::texture::BlockFormat::from_unity_format(r.format)
                .with_context(|| format!("texture format {} is not a block format", r.format))?;
            let blocks = crate::fonts::decode_base64(&r.data).context("data is not base64")?;
            let rgba = if r.eac16 {
                anyhow::ensure!(
                    format.is_eac(),
                    "normalized channel decoding requires unsigned EAC blocks"
                );
                let measured = s
                    .texture_importer
                    .read()
                    .unwrap()
                    .project
                    .encoders
                    .measured_runtime_encoder;
                let packed = unbound_assets::texture::etc::decode_eac_gpu(
                    format, &blocks, r.width, r.height, measured,
                );
                let channels = if format == unbound_assets::texture::BlockFormat::EacR {
                    1
                } else {
                    2
                };
                let mut rgba = Vec::with_capacity(r.width as usize * r.height as usize * 16);
                for pixel in packed.chunks_exact(channels * 2) {
                    let red = f32::from(u16::from_le_bytes([pixel[0], pixel[1]])) / 65535.0;
                    let green = if channels == 2 {
                        f32::from(u16::from_le_bytes([pixel[2], pixel[3]])) / 65535.0
                    } else {
                        0.0
                    };
                    rgba.extend(
                        [red, green, 0.0, 1.0]
                            .into_iter()
                            .flat_map(f32::to_le_bytes),
                    );
                }
                rgba
            } else if r.floats {
                let floats = if format == unbound_assets::texture::BlockFormat::Bc6h {
                    unbound_assets::texture::decode_bc6h_f32(&blocks, r.width, r.height)
                } else {
                    anyhow::ensure!(
                        format.is_astc_hdr(),
                        "float decoding requires BC6H or ASTC HDR blocks"
                    );
                    unbound_assets::texture::decode_blocks_f32(&blocks, r.width, r.height, format)
                };
                floats.into_iter().flat_map(f32::to_le_bytes).collect()
            } else {
                unbound_assets::texture::decode_blocks(format, &blocks, r.width, r.height)
            };
            Ok(json!({ "rgba": crate::models::base64(&rgba) }).to_string())
        }
        "texture-compress-blocks" => {
            // One RGBA8 level (rows as stored) block-compressed to Unity's TextureFormat
            // `format` (Texture2D.Compress).
            #[derive(Deserialize)]
            struct Request {
                format: i32,
                width: u32,
                height: u32,
                data: String,
                #[serde(default, rename = "highQuality")]
                high_quality: bool,
                /// With the import's encoder (pixels a texture's postprocessor changed).
                #[serde(default)]
                import: bool,
            }
            let r: Request = serde_json::from_str(request)?;
            let format = unbound_assets::texture::BlockFormat::from_unity_format(r.format)
                .filter(|f| *f != unbound_assets::texture::BlockFormat::Bc6h && !f.is_astc_hdr())
                .with_context(|| {
                    format!("texture format {} is not an 8-bit block format", r.format)
                })?;
            let rgba = crate::fonts::decode_base64(&r.data).context("data is not base64")?;
            anyhow::ensure!(
                rgba.len() >= r.width as usize * r.height as usize * 4,
                "texture-compress-blocks: {} bytes for a {}x{} level",
                rgba.len(),
                r.width,
                r.height
            );
            let measured = s
                .texture_importer
                .read()
                .unwrap()
                .project
                .encoders
                .measured_runtime_encoder;
            let blocks = if measured && !r.import {
                unbound_assets::texture::compress_runtime_level(
                    format,
                    &rgba,
                    r.width,
                    r.height,
                    r.high_quality,
                )
            } else {
                unbound_assets::texture::compress_level(format, &rgba, r.width, r.height)
            };
            Ok(json!({ "blocks": crate::models::base64(&blocks) }).to_string())
        }
        "texture-layers" => {
            // An imported texture's images: slices of an array, depth of a volume, cube faces.
            #[derive(Deserialize)]
            struct Request {
                guid: String,
            }
            let r: Request = serde_json::from_str(request)?;
            let guid = Guid::parse(&r.guid).context("bad guid")?;
            let db = s.db();
            let asset = db.by_guid(guid).context("no such asset")?;
            let meta = PathBuf::from(format!("{}.meta", asset.file.display()));
            let texture = s
                .imports
                .get_or_import(&texture_importer(s), &asset.file, &meta)?;
            Ok(json!({
                "layers": texture.layers,
                "mipCount": texture.mip_count(),
                "dimension": texture.dimension(),
                "mipBias": texture.mip_bias,
            })
            .to_string())
        }
        "sprite-atlases" => Ok(with_page_set(s, crate::sprite_atlases::to_json).to_string()),
        "sprite-atlases-pack" => {
            #[derive(Deserialize)]
            struct Request {
                paths: Vec<String>,
            }
            let r: Request = serde_json::from_str(request)?;
            if r.paths.iter().any(|p| s.db().by_path(p).is_none()) {
                let _ = s.refresh_assets();
            }
            let packed = crate::sprite_atlases::pack_all(
                &r.paths,
                &s.db(),
                &texture_importer(s),
                &s.imports,
            );
            let json = crate::sprite_atlases::to_json(&packed).to_string();
            // Ensure Play-mode atlases are initialized before merging newly packed atlases.
            let _ = with_page_set(s, |_| ());
            let mut guard = s.atlases.write().unwrap_or_else(|e| e.into_inner());
            if let Some(set) = guard.as_mut() {
                set.merge(&packed);
            }
            Ok(json)
        }
        "sprite-geometry" => {
            // A sprite made at runtime (Sprite.Create), or an imported sprite's fallback outline:
            // its Tight mesh, texture rect and fallback physics shape, traced as the importer
            // traces them.
            #[derive(Deserialize)]
            #[serde(rename_all = "camelCase")]
            struct Request {
                #[serde(flatten)]
                texels: TexelSource,
                rect: [f32; 4],
                pivot: [f32; 2],
                pixels_per_unit: f32,
                extrude: u32,
                tight: bool,
                physics: bool,
            }
            let r: Request = serde_json::from_str(request)?;
            let texels = texel_source(s, &r.texels)?;
            let texture = texels.texture();
            let geometry = unbound_assets::sprite_mesh::sprite_geometry(
                &texture,
                &unbound_assets::sprite_mesh::SpriteSource {
                    rect: r.rect,
                    pivot: r.pivot,
                    pixels_per_unit: r.pixels_per_unit,
                    extrude: r.extrude,
                    tight: r.tight,
                    detail: -1.0,
                    outline: &[],
                    physics_shape: &[],
                    generate_physics_shape: r.physics,
                },
                false,
            );
            Ok(json!({
                "mesh": geometry.mesh,
                "textureRect": geometry.texture_rect,
                "physics": geometry.physics,
            })
            .to_string())
        }
        "sprite-outline" => {
            // SpriteUtility.GenerateOutline; with a sprite's pivot and pixels per unit,
            // SpriteUtility.GenerateOutlineFromSprite.
            #[derive(Deserialize)]
            #[serde(rename_all = "camelCase")]
            struct Request {
                #[serde(flatten)]
                texels: TexelSource,
                rect: [f32; 4],
                detail: f32,
                tolerance: u8,
                holes: bool,
                sprite: Option<SpriteFrame>,
            }
            #[derive(Deserialize)]
            #[serde(rename_all = "camelCase")]
            struct SpriteFrame {
                pivot: [f32; 2],
                pixels_per_unit: f32,
            }
            let r: Request = serde_json::from_str(request)?;
            let texels = texel_source(s, &r.texels)?;
            let texture = texels.texture();
            let paths = match r.sprite {
                Some(sprite) => unbound_assets::sprite_mesh::sprite_outline(
                    &texture,
                    r.rect,
                    sprite.pivot,
                    sprite.pixels_per_unit,
                    r.detail,
                    r.tolerance,
                    r.holes,
                ),
                None => unbound_assets::sprite_mesh::rect_outline(
                    &texture,
                    r.rect,
                    r.detail,
                    r.tolerance,
                    r.holes,
                ),
            };
            Ok(json!({ "paths": paths }).to_string())
        }
        "sprite-mesh-boundary" => {
            // A Polygon-mode sprite's SpriteUtility.GenerateOutlineFromSprite: its mesh's boundary.
            #[derive(Deserialize)]
            struct Request {
                vertices: Vec<[f32; 2]>,
                triangles: Vec<u16>,
            }
            let r: Request = serde_json::from_str(request)?;
            let path = unbound_assets::sprite_mesh::mesh_boundary(&r.vertices, &r.triangles);
            let paths: Vec<_> = Some(path).filter(|p| !p.is_empty()).into_iter().collect();
            Ok(json!({ "paths": paths }).to_string())
        }
        "request-script-compilation" => {
            // CompilationPipeline.RequestScriptCompilation: compiled and reloaded between frames.
            crate::game::request_compilation();
            Ok("{}".to_string())
        }
        "import-settings" => {
            // An AssetPostprocessor's preprocessor changed an asset's import settings: the asset
            // imports with them for the rest of the session (the .meta is not written).
            #[derive(Deserialize)]
            struct Request {
                guid: String,
                importer: String,
                settings: serde_json::Value,
            }
            let r: Request = serde_json::from_str(request)?;
            let guid = Guid::parse(&r.guid).context("bad guid")?;
            let db = s.db();
            let asset = db.by_guid(guid).context("no such asset")?;
            let meta_path = PathBuf::from(format!("{}.meta", asset.file.display()));
            let mut meta = match std::fs::read_to_string(&meta_path) {
                Ok(text) => unbound_yaml::parse_value(&text)?,
                Err(_) => unbound_yaml::Value::Map(Default::default()),
            };
            if let unbound_yaml::Value::Map(map) = &mut meta {
                map.insert(&r.importer, json_to_yaml(&r.settings));
            }
            s.imports.set_settings(&meta_path, meta)?;
            // A model imported or expanded before keeps no stale import.
            s.models.write().unwrap().remove(&guid);
            s.loader.invalidate(Some(guid));
            Ok("{}".to_string())
        }
        "prefab-written" => {
            // Editor APIs wrote a prefab file: the prefabs and scenes nesting it expand it again.
            #[derive(Deserialize)]
            struct Request {
                guid: String,
            }
            let r: Request = serde_json::from_str(request)?;
            s.loader
                .invalidate(Some(Guid::parse(&r.guid).context("bad guid")?));
            Ok("{}".to_string())
        }
        "postprocessors" => {
            // Which imports the editor domain's AssetPostprocessors change (models: the model
            // callbacks; prefabs: OnPostprocessPrefab); a change forgets the expanded prefabs.
            #[derive(Deserialize)]
            struct Request {
                models: bool,
                prefabs: bool,
            }
            let r: Request = serde_json::from_str(request)?;
            if crate::imported_prefabs::set_postprocessing(r.models, r.prefabs) {
                s.loader.invalidate(None);
            }
            Ok("{}".to_string())
        }
        "model-postprocess" => {
            // What a model's postprocessors are given beyond its prefab: the FBX user properties
            // of its nodes, by the nodes' GameObjects, the name the root had in the file when it
            // was its single top-level node, the file's order of the nodes and whether the
            // importer sorts the hierarchy by name.
            #[derive(Deserialize)]
            struct Request {
                guid: String,
            }
            let r: Request = serde_json::from_str(request)?;
            let model = imported_model(Guid::parse(&r.guid).context("bad guid")?)?;
            let nodes: Vec<serde_json::Value> = model
                .user_properties
                .iter()
                .filter_map(|(node, properties)| {
                    let game_object = model.nodes.get(*node as usize)?.game_object.id;
                    let properties: Vec<serde_json::Value> = properties
                        .iter()
                        .map(|p| {
                            use unbound_assets::model::UserValue;
                            let (kind, value) = match &p.value {
                                UserValue::Bool(b) => ("bool", json!(b)),
                                UserValue::Int(i) => ("int", json!(i)),
                                UserValue::Float(f) => ("float", json!(f)),
                                UserValue::String(t) => ("string", json!(t)),
                                UserValue::Vector(v) => ("vector", json!(v)),
                                UserValue::Color(c) => ("color", json!(c)),
                            };
                            json!({ "name": p.name, "type": kind, "value": value })
                        })
                        .collect();
                    Some(json!({ "gameObject": game_object.to_string(), "properties": properties }))
                })
                .collect();
            // The GameObjects in the file's depth-first order, which the postprocessors see.
            let file_order: Vec<String> = model
                .file_order
                .iter()
                .filter_map(|i| model.nodes.get(*i as usize))
                .map(|n| n.game_object.id.to_string())
                .collect();
            Ok(json!({
                "nodes": nodes,
                "rootNodeName": model.root_node_name,
                "fileOrder": file_order,
                "sorted": model.settings.sort_hierarchy_by_name,
            })
            .to_string())
        }
        "model-child-order" => {
            // The order the importer sorts children with these names into.
            #[derive(Deserialize)]
            struct Request {
                names: Vec<String>,
            }
            let r: Request = serde_json::from_str(request)?;
            let names: Vec<&str> = r.names.iter().map(String::as_str).collect();
            Ok(json!({ "order": unbound_assets::model::hierarchy_name_sort(&names) }).to_string())
        }
        "model-file-ids" => {
            // The fileIDs of a model's hierarchy objects as its postprocessors left them.
            #[derive(Deserialize)]
            #[serde(rename_all = "camelCase")]
            struct Object {
                class_id: i32,
                class_name: String,
                path: String,
            }
            #[derive(Deserialize)]
            struct Request {
                guid: String,
                objects: Vec<Object>,
            }
            let r: Request = serde_json::from_str(request)?;
            let model = imported_model(Guid::parse(&r.guid).context("bad guid")?)?;
            let objects: Vec<unbound_assets::model::HierarchyObject> = r
                .objects
                .iter()
                .map(|o| unbound_assets::model::HierarchyObject {
                    class_id: o.class_id,
                    class_name: &o.class_name,
                    below_root: &o.path,
                })
                .collect();
            let ids: Vec<String> = model
                .hierarchy_file_ids(&objects)
                .iter()
                .map(|id| id.to_string())
                .collect();
            Ok(json!({ "ids": ids }).to_string())
        }
        "script-reload" => {
            // The reload a locked session deferred (EditorApplication.UnlockReloadAssemblies).
            #[derive(Deserialize)]
            struct Request {
                #[serde(default)]
                optimized: bool,
            }
            let r: Request = serde_json::from_str(request)?;
            crate::game::request_restart(r.optimized);
            Ok("{}".to_string())
        }
        "scripting-defines" => {
            // PlayerSettings.SetScriptingDefineSymbols: the group's defines for the run's
            // compilations (the caller asks for the compilation when the active group changed).
            #[derive(Deserialize)]
            struct Request {
                group: String,
                defines: String,
            }
            let r: Request = serde_json::from_str(request)?;
            crate::game::set_scripting_defines(&r.group, &r.defines);
            Ok("{}".to_string())
        }
        "generated-texture-release" => {
            #[derive(Deserialize)]
            struct Request {
                key: String,
            }
            let r: Request = serde_json::from_str(request)?;
            release_generated_texture(&r.key);
            Ok("{}".to_string())
        }
        "texture-sprites" => {
            #[derive(Deserialize)]
            struct Request {
                guid: String,
            }
            let r: Request = serde_json::from_str(request)?;
            let guid = texture_guid(&r.guid)?;
            let textures = s.textures.read().unwrap();
            let texture = textures.get(&guid).context("the texture is not imported")?;
            let polygon = texture.polygon;
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
                        "polygon": polygon,
                        "mesh": sp.mesh,
                        "textureRectBounds": sp.texture_rect_bounds,
                        "physics": sp.physics,
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
                        "spriteId": sp.sprite_id,
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
        "font-strike" => {
            #[derive(Deserialize)]
            struct Request {
                handle: u64,
                pixels: i32,
            }
            let r: Request = serde_json::from_str(request)?;
            Ok(crate::fonts::strike(r.handle, r.pixels)?.to_string())
        }
        "font-single-adjustments" => {
            let r: crate::fonts::SingleAdjustmentRequest = serde_json::from_str(request)?;
            Ok(crate::fonts::single_adjustments(&r)?.to_string())
        }
        "font-marks" => {
            let r: crate::fonts::MarkRequest = serde_json::from_str(request)?;
            Ok(crate::fonts::marks(&r)?.to_string())
        }
        "font-ligatures" => {
            #[derive(Deserialize)]
            struct Request {
                handle: u64,
                glyphs: Option<Vec<u32>>,
                lookup: Option<i32>,
            }
            let r: Request = serde_json::from_str(request)?;
            Ok(crate::fonts::ligatures(r.handle, r.glyphs.as_deref(), r.lookup)?.to_string())
        }
        "font-pair-adjustments" => {
            #[derive(Deserialize)]
            struct Request {
                handle: u64,
                glyphs: Option<Vec<u32>>,
                lookup: Option<i32>,
                second: Option<u32>,
            }
            let r: Request = serde_json::from_str(request)?;
            Ok(
                crate::fonts::pair_adjustments(r.handle, r.glyphs.as_deref(), r.lookup, r.second)?
                    .to_string(),
            )
        }
        "font-legacy-kerning" => {
            #[derive(Deserialize)]
            struct Request {
                handle: u64,
                pixels: i32,
                #[serde(default)]
                unscaled: bool,
            }
            let r: Request = serde_json::from_str(request)?;
            Ok(crate::fonts::legacy_kerning(r.handle, r.pixels, r.unscaled)?.to_string())
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
                // `driverCapabilities` and the layout the mixer runs, which a device narrower
                // than the request lowers while the configuration keeps the request.
                "driverCapabilities": crate::audio::driver_capabilities(s.audio.device_channels()),
                "activeSpeakerMode": s.audio.mixer().config.speaker_mode,
            })
            .to_string())
        }
        "audio-mixer-group" => {
            let group: crate::audio::effects::GroupSpec = serde_json::from_str(request)?;
            s.audio.mixer().configure_group(group)?;
            Ok("{}".to_owned())
        }
        "audio-reset" => {
            #[derive(Deserialize)]
            #[serde(rename_all = "camelCase")]
            struct Request {
                #[serde(flatten)]
                configuration: crate::audio::AudioConfiguration,
                #[serde(default)]
                finish_capture_buffer: bool,
            }
            let r: Request = serde_json::from_str(request)?;
            Ok(json!({ "ok": s.audio.reset_with_capture_tail(r.configuration, r.finish_capture_buffer) }).to_string())
        }
        "audio-renderer-start" => Ok(json!(s.audio.capture_start()).to_string()),
        "audio-renderer-stop" => Ok(json!(s.audio.capture_stop()).to_string()),
        "audio-renderer-count" => {
            let delta_time: f32 = serde_json::from_str(request)?;
            Ok(json!(s.audio.capture_sample_count(delta_time)).to_string())
        }
        "audio-renderer-render" => {
            #[derive(Deserialize)]
            struct Request {
                length: usize,
                sinks: Vec<crate::audio::capture::SinkRequest>,
                #[serde(rename = "deltaTime")]
                delta_time: f32,
            }
            let r: Request = serde_json::from_str(request)?;
            Ok(serde_json::to_string(&s.audio.capture_render(
                r.length,
                r.sinks,
                r.delta_time,
            )?)?)
        }
        // The system clipboard's text (`GUIUtility.systemCopyBuffer`).
        "clipboard-get" => crate::clipboard::get(request),
        "clipboard-set" => crate::clipboard::set(request),
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
    // A model or prefab as its postprocessors get it: expanded, without their changes.
    if let Some(guid) = key.strip_prefix("raw:") {
        let guid = Guid::parse(guid).context("bad guid")?;
        let docs = s.loader.prefab_unprocessed(guid)?;
        return Ok(unbound_scene::encode_documents(&docs));
    }
    let (file, guid) = if let Some(guid) = Guid::parse(key) {
        s.db().by_guid(guid).map(|a| (a.file.clone(), Some(a.guid)))
    } else if key.starts_with('/') {
        Some((PathBuf::from(key), None))
    } else {
        s.db().by_path(key).map(|a| (a.file.clone(), Some(a.guid)))
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
    let db = s.db();
    let asset = db.by_guid(guid).context("no such asset")?;
    let meta = PathBuf::from(format!("{}.meta", asset.file.display()));
    let model = s
        .imports
        .get_or_import(&s.model_importer, &asset.file, &meta)?;
    for warning in &model.warnings {
        tracing::warn!("{}: {warning}", asset.path);
    }
    s.models.write().unwrap().insert(guid, model.clone());
    Ok(model)
}

/// The `model-mesh` answer: a model mesh's geometry ([`crate::models::mesh_json`], unchanged) plus
/// `readable`, the model's Read/Write Enabled import setting. Unity 6000.0 `Mesh.isReadable`: "You
/// can set this value using the Read/Write Enabled checkbox when importing a model to Unity."
/// The flag comes from the imported model's settings, which the import cache keys by the meta's
/// content hash; the managed Mesh adopts it after it has filled the geometry.
fn model_mesh_json(
    model: &unbound_assets::ImportedModel,
    index: usize,
) -> Result<serde_json::Value> {
    let mesh = model.meshes.get(index).context("no such mesh")?;
    let mut answer = crate::models::mesh_json(mesh);
    answer["readable"] = serde_json::Value::Bool(model.settings.is_readable);
    Ok(answer)
}

/// The documents of a model's prefab.
fn model_documents(guid: Guid) -> Result<Vec<unbound_yaml::Document>> {
    let model = imported_model(guid)?;
    let s = state();
    let db = s.db();
    let path = db.by_guid(guid).map_or("", |a| a.path.as_str());
    Ok(crate::models::documents(guid, path, &model, &s.db()))
}

/// An asset's importer settings: the importer section of its `.meta`, as one document whose type
/// name is the importer's (`ScriptedImporter`, `TextureImporter`, ...).
fn load_importer_settings(guid: &str) -> Result<Vec<u8>> {
    let s = state();
    let db = s.db();
    let asset = db
        .by_guid(Guid::parse(guid).context("bad guid")?)
        .with_context(|| format!("no asset {guid}"))?;
    // The Assets folder and package roots have no .meta: their importer has the default settings.
    let mut meta = if asset.is_root() {
        unbound_yaml::Value::Map(Default::default())
    } else {
        let text = std::fs::read_to_string(format!("{}.meta", asset.file.display()))?;
        unbound_yaml::parse_value(&text)?
    };
    // The maps of SpeedTree 9 trees: the settings the trees give them, which the Editor saves into
    // their .meta files (the texture import reads the same).
    let tree_textures = s.texture_importer.read().unwrap().tree_textures.clone();
    if let Some(applied) = tree_textures.apply(&asset.file, &meta) {
        meta = applied;
    }
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
                asset_importer(asset).to_string(),
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

/// The importer the Editor uses for a file or folder by its path and extension, what a .meta that
/// names none gets. Every plug-in kind and native-format kind of the list was measured
/// (Unity 6000.0.71f1, tests/projects/defaultasset `asset.plugin.*` and `native.*`) except the
/// native-format `.fontsettings`, `.guiskin`, `.preset`, `.brush`, `.signal` and `.playable`.
fn default_importer(path: &str) -> &'static str {
    importer_of_path(path).unwrap_or("DefaultImporter")
}

/// The importer the Editor picks for a path by its extension (and the special root folders); None
/// for an extension the list does not know.
fn importer_of_path(path: &str) -> Option<&'static str> {
    if is_special_default_imported_folder(path) {
        return Some("DefaultImporter");
    }
    let name = path.rsplit('/').next().unwrap_or(path);
    if name == "package.json" {
        return Some("PackageManifestImporter");
    }
    Some(match path_extension(path).as_str() {
        "txt" | "html" | "htm" | "xml" | "json" | "csv" | "yaml" | "bytes" | "fnt" | "md" => {
            "TextScriptImporter"
        }
        "cs" => "MonoImporter",
        "asmdef" => "AssemblyDefinitionImporter",
        "asmref" => "AssemblyDefinitionReferenceImporter",
        "shader" => "ShaderImporter",
        "compute" => "ComputeShaderImporter",
        "vfx" | "vfxoperator" | "vfxblock" => "VisualEffectImporter",
        "raytrace" => "RayTracingShaderImporter",
        "png" | "jpg" | "jpeg" | "tga" | "psd" | "tif" | "tiff" | "gif" | "bmp" | "exr" | "hdr" => {
            "TextureImporter"
        }
        "fbx" | "obj" | "dae" | "3ds" | "dxf" | "blend" => "ModelImporter",
        "wav" | "mp3" | "ogg" | "aif" | "aiff" | "mod" | "it" | "s3m" | "xm" => "AudioImporter",
        // Native and managed libraries, platform sources and archives, and the folders that are
        // one plug-in each.
        "dll" | "so" | "a" | "dylib" | "jar" | "aar" | "jslib" | "jspre" | "c" | "cc" | "cpp"
        | "h" | "m" | "mm" | "java" | "kt" | "swift" | "bundle" | "framework" | "xcframework"
        | "plugin" | "androidlib" => "PluginImporter",
        "ttf" | "otf" => "TrueTypeFontImporter",
        "spriteatlasv2" => "SpriteAtlasImporter",
        "prefab" => "PrefabImporter",
        "mat" | "asset" | "anim" | "controller" | "overridecontroller" | "physicmaterial"
        | "physicsmaterial2d" | "mask" | "fontsettings" | "guiskin" | "mixer" | "rendertexture"
        | "flare" | "spriteatlas" | "lighting" | "preset" | "brush" | "terrainlayer" | "signal"
        | "playable" | "shadervariants" | "giparams" | "cubemap" => "NativeFormatImporter",
        "" | "unity" | "rsp" | "modulecompilationtrigger" => "DefaultImporter",
        _ => return None,
    })
}

/// Whether a listed asset is a folder of the database: a folder that is not one plug-in.
fn is_plain_folder(asset: &unbound_project::assetdb::Asset) -> bool {
    asset.is_folder && !unbound_assets::new_meta::is_folder_plugin(&asset.file)
}

/// The importer of a listed asset: a plain folder's is the DefaultImporter; a file's is the one the
/// Editor picks by its extension, or for an extension the list does not know, the one its `.meta`
/// names (a native-format kind of a package, for one).
fn asset_importer(asset: &unbound_project::assetdb::Asset) -> &str {
    if is_plain_folder(asset) {
        return "DefaultImporter";
    }
    importer_of_path(&asset.path)
        .or(asset.importer.as_deref())
        .unwrap_or("DefaultImporter")
}

/// How a listed asset loads: a plain folder; a file of an extension the importer list does not
/// know by the importer its `.meta` names; else by its path (a folder plug-in as a plug-in).
fn listed_kind(asset: &unbound_project::assetdb::Asset) -> &'static str {
    if is_plain_folder(asset) {
        return "folder";
    }
    if importer_of_path(&asset.path).is_none() {
        match asset.importer.as_deref() {
            Some("NativeFormatImporter") => return "yaml",
            Some("PluginImporter") => return "plugin",
            _ => {}
        }
    }
    asset_kind(&asset.path)
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

    /// The project shader `Shader.Find` answers for a name: of several files declaring it, the
    /// first by asset path (matches the Unity 6000.0.71f1 Editor, tests/projects/shadernames:
    /// `Assets/Shaders/A` of A, B, C and Z, whose GUIDs sort otherwise; Assets against Packages
    /// is not measured).
    fn shader_guid(&self, name: &str) -> Option<Guid> {
        let mut index = self.shaders.write().unwrap();
        if index.is_none() {
            let mut first: HashMap<String, (String, Guid)> = HashMap::new();
            for asset in self.db().iter() {
                if !asset.path.ends_with(".shader") {
                    continue;
                }
                let Ok(text) = std::fs::read_to_string(&asset.file) else {
                    continue;
                };
                if let Some(declared) = declared_shader_name(&text) {
                    match first.get(declared) {
                        Some((path, _)) if path.as_str() <= asset.path.as_str() => {}
                        _ => {
                            first.insert(declared.to_string(), (asset.path.clone(), asset.guid));
                        }
                    }
                }
            }
            *index = Some(
                first
                    .into_iter()
                    .map(|(name, (_, guid))| (name, guid))
                    .collect(),
            );
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

/// Whether the GPU samples BC1-BC7 textures (set once the render device exists): imported
/// textures then upload their blocks, else what the blocks decode to.
static GPU_BC: std::sync::atomic::AtomicBool = std::sync::atomic::AtomicBool::new(false);

/// The render device can sample BC-compressed textures (`UNBOUND_NO_TEXTURE_COMPRESSION` turns
/// that off): imports upload their blocks, and textures whose target format Unbound does not
/// produce are given BC7 blocks for the GPU.
pub fn set_gpu_bc7(supported: bool) {
    let compress = supported && std::env::var_os("UNBOUND_NO_TEXTURE_COMPRESSION").is_none();
    GPU_BC.store(compress, std::sync::atomic::Ordering::Relaxed);
}

/// The texture importer for the active build target; BC7 for the GPU when it samples BC.
fn texture_importer(s: &RuntimeState) -> TextureImporter {
    TextureImporter {
        compress: GPU_BC.load(std::sync::atomic::Ordering::Relaxed),
        ..s.texture_importer.read().unwrap().clone()
    }
}

/// The active build target; `normal_maps_xyz` when its shaders read XYZ normal maps
/// (`UNITY_NO_DXT5nm`), else textures import normal maps DXT5nm-encoded; `lighting` how it
/// stores lightmaps and HDR cubemaps.
pub fn set_build_target(
    target: &str,
    normal_maps_xyz: bool,
    lighting: unbound_compat::LightingEncodings,
) {
    state().audio_importer.write().unwrap().build_target = target.to_string();
    let mut textures = state().texture_importer.write().unwrap();
    textures.build_target = target.to_string();
    textures.normal_maps_xyz = normal_maps_xyz;
    textures.lighting = lighting;
}

/// A downloaded audio file of the Unity `AudioType` `audio_type` decoded into a mixer clip handle
/// (`header_only`: the format and length a partial WAV announces, no clip made); None when the
/// type is one Unbound does not decode or, header only, the header is not all there.
pub fn decode_download(
    handle: u64,
    data: &[u8],
    audio_type: i32,
    header_only: bool,
) -> Result<Option<crate::audio::protocol::AudioClipInfo>> {
    use unbound_assets::audio_download::{Codec, Container, decode, header};
    let Some(container) = Container::of_audio_type(audio_type) else {
        return Ok(None);
    };
    let info = |channels: u16, rate: u32, frames: u64| crate::audio::protocol::AudioClipInfo {
        frames: frames.min(i32::MAX as u64) as i32,
        channels: channels as i32,
        frequency: rate as i32,
        ..Default::default()
    };
    if header_only {
        return Ok(
            header(data, container).map(|(channels, rate, frames)| info(channels, rate, frames))
        );
    }
    let decoded = decode(data, container)?;
    let frames = decoded.samples.len() / decoded.channels.max(1) as usize;
    let result = crate::audio::protocol::AudioClipInfo {
        // Unity's AudioCompressionFormat of the file's coding.
        compression_format: match decoded.codec {
            Codec::Pcm => 0,
            Codec::Vorbis => 1,
            Codec::ImaAdpcm => 2,
            Codec::Mpeg => 3,
        },
        ..info(decoded.channels, decoded.rate, frames as u64)
    };
    state().audio.mixer().add_clip(
        handle,
        crate::audio::Clip::new(decoded.channels as usize, decoded.rate, decoded.samples),
    );
    Ok(Some(result))
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
    let db = s.db();
    let asset = db.by_guid(guid).context("no such asset")?;
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
    let clip = crate::audio::Clip::new(info.channels as usize, info.frequency as u32, samples);
    s.audio.mixer().add_clip(handle, clip);
    Ok(info)
}

/// The GPU texels of an imported RGBA8 texture that keeps one channel
/// ([`ImportedTexture::gpu_channel`](unbound_assets::texture::ImportedTexture::gpu_channel)):
/// alpha alone (the renderer's Alpha8, sampled (0, 0, 0, a)), opaque RGB as (r, g, b, 1), red
/// and red-green ones in one and two channels ([`texture_format::R8_UNORM`],
/// [`texture_format::RG8_UNORM`]: (r, 0, 0, 1), (r, g, 0, 1)) where they report R8 or RG16 and
/// are not `srgb_sampled` (sRGB in a linear project; in a gamma project they are R8_UNorm and
/// R8G8_UNorm), as the managed side uploads such textures (copies between them need one
/// storage). Others keep four channels, as R8_SRGB and R8G8_SRGB sample: (r, 0, 0, 1) and
/// (r, g, 1, 1) (the Unity 6000.0.71f1 Editor, tests/projects/texturecompression channels.*);
/// the device has no one- or two-channel sRGB formats. Other textures keep `format` and their
/// data.
fn single_channel_texels(
    channel: Option<unbound_assets::texture::GpuChannel>,
    reported: Option<i32>,
    format: i32,
    srgb_sampled: bool,
    mut data: Vec<u8>,
) -> (i32, Vec<u8>) {
    use unbound_assets::texture::GpuChannel;
    use unbound_assets::texture::unity_format::{R8, RG16};
    let keep = |data: &[u8], channels: usize| -> Vec<u8> {
        data.chunks_exact(4)
            .flat_map(|p| p[..channels].iter().copied())
            .collect()
    };
    match channel {
        Some(GpuChannel::Alpha) => (
            texture_format::ALPHA8,
            data.chunks_exact(4).map(|p| p[3]).collect(),
        ),
        Some(GpuChannel::Red) if !srgb_sampled && reported == Some(R8) => {
            (texture_format::R8_UNORM, keep(&data, 1))
        }
        Some(GpuChannel::RedGreen) if !srgb_sampled && reported == Some(RG16) => {
            (texture_format::RG8_UNORM, keep(&data, 2))
        }
        Some(GpuChannel::Red) => {
            for p in data.chunks_exact_mut(4) {
                p[1] = 0;
                p[2] = 0;
                p[3] = 255;
            }
            (format, data)
        }
        Some(GpuChannel::RedGreen) => {
            for p in data.chunks_exact_mut(4) {
                p[2] = if srgb_sampled { 255 } else { 0 };
                p[3] = 255;
            }
            (format, data)
        }
        Some(GpuChannel::Opaque) => {
            for p in data.chunks_exact_mut(4) {
                p[3] = 255;
            }
            (format, data)
        }
        None => (format, data),
    }
}

/// Mip levels' blocks one after another.
fn concat<B: AsRef<[u8]>>(levels: &[B]) -> Vec<u8> {
    let mut data = Vec::with_capacity(levels.iter().map(|b| b.as_ref().len()).sum());
    for level in levels {
        data.extend_from_slice(level.as_ref());
    }
    data
}

/// The renderer's code for an import's block format.
fn block_code(format: unbound_assets::texture::BlockFormat, srgb: bool) -> i32 {
    use unbound_assets::texture::BlockFormat as B;
    let colour = |color: i32, data: i32| if srgb { color } else { data };
    match format {
        B::Bc1 => colour(texture_format::BC1_COLOR, texture_format::BC1_DATA),
        B::Bc3 => colour(texture_format::BC3_COLOR, texture_format::BC3_DATA),
        B::Bc7 => colour(texture_format::BC7_COLOR, texture_format::BC7_DATA),
        B::Bc4 => texture_format::BC4,
        B::Bc5 => texture_format::BC5,
        B::Bc6h => texture_format::BC6H,
        b if b.is_astc() => {
            let unity = b.unity_format();
            if b.is_astc_hdr() {
                texture_format::ASTC_HDR_FIRST + unity - 66
            } else {
                colour(
                    texture_format::ASTC_COLOR_FIRST,
                    texture_format::ASTC_DATA_FIRST,
                ) + unity
                    - 48
            }
        }
        B::Etc1 | B::Etc2Rgb => colour(texture_format::ETC_RGB_COLOR, texture_format::ETC_RGB_DATA),
        B::Etc2Rgba8 => colour(
            texture_format::ETC_RGBA_COLOR,
            texture_format::ETC_RGBA_DATA,
        ),
        _ => unreachable!("EAC blocks upload as normalized channel pixels"),
    }
}

/// Imports an asset texture (through the import cache) into a renderer handle.
/// Imports an asset texture, or a sprite atlas page (`atlas:<guid>:<page>`), into a renderer
/// handle.
pub fn import_texture(handle: u64, guid: &str) -> Result<ImportedTextureInfo> {
    let s = state();
    if let Some(page) = guid.strip_prefix(crate::sprite_atlases::PAGE_KEY_PREFIX) {
        let texture = with_page_set(s, |set| crate::sprite_atlases::page(set, page))?;
        return upload_imported(s, handle, texture, None, 0);
    }
    if guid.starts_with(GENERATED_KEY_PREFIX) {
        let texture = s.generated.read().unwrap().get(guid).cloned();
        let texture = texture.context("the generated texture was destroyed")?;
        let sprites = s
            .textures
            .read()
            .unwrap()
            .get(&texture_guid(guid)?)
            .map_or(0, |t| t.sprites.len());
        return upload_imported(s, handle, (*texture).clone(), None, sprites);
    }
    // A (re)import replaces the texels a trace may have kept.
    {
        let mut recent = s.traced_texels.lock().unwrap_or_else(|e| e.into_inner());
        if recent
            .as_ref()
            .is_some_and(|(key, _)| key.eq_ignore_ascii_case(guid))
        {
            *recent = None;
        }
    }
    let guid = Guid::parse(guid).context("bad guid")?;
    let db = s.db();
    let asset = db.by_guid(guid).context("no such asset")?;
    let meta = PathBuf::from(format!("{}.meta", asset.file.display()));
    let importer = texture_importer(s);
    let texture = s.imports.get_or_import(&importer, &asset.file, &meta)?;
    // The import is ours alone (the cache is on disk): take its data instead of copying it.
    let mut texture = Arc::try_unwrap(texture).unwrap_or_else(|shared| (*shared).clone());
    s.textures.write().unwrap().insert(
        guid,
        TextureSprites {
            name: texture.name.clone(),
            sprites: std::mem::take(&mut texture.sprites),
            polygon: texture.settings.sprite.mode == unbound_assets::sprite::SpriteMode::Polygon,
        },
    );
    let sprite_count = s.textures.read().unwrap()[&guid].sprites.len();
    upload_imported(s, handle, texture, Some((&asset.file, &meta)), sprite_count)
}

/// Imports a script's texels (rows bottom-up: RGBA8, or linear RGBA float colour with
/// `linearColor`) with a `.meta`-shaped settings document under a new `generated:` key and uploads
/// them to `handle` (TextureGenerator.GenerateTexture).
pub fn generate_texture(handle: u64, request: &str, texels: &[u8]) -> Result<ImportedTextureInfo> {
    #[derive(Deserialize)]
    #[serde(rename_all = "camelCase")]
    struct Request {
        key: String,
        name: String,
        width: u32,
        height: u32,
        meta: String,
        #[serde(default)]
        linear_color: bool,
    }
    let s = state();
    let r: Request = serde_json::from_str(request)?;
    let guid = texture_guid(&r.key)?;
    anyhow::ensure!(
        r.key.starts_with(GENERATED_KEY_PREFIX),
        "not a generated texture key: {}",
        r.key
    );
    let meta = unbound_yaml::parse_value(&r.meta).context("the generation settings")?;
    let importer = texture_importer(s);
    let settings = importer.settings(&meta);
    let polygon = settings.sprite.mode == unbound_assets::sprite::SpriteMode::Polygon;
    let texels = if r.linear_color {
        anyhow::ensure!(
            texels.len() % 16 == 0,
            "{} bytes of float colour",
            texels.len()
        );
        unbound_assets::texture::GeneratedTexels::LinearColor(
            texels
                .as_chunks::<16>()
                .0
                .iter()
                .map(|c| {
                    std::array::from_fn(|i| {
                        f32::from_le_bytes(c[i * 4..i * 4 + 4].try_into().unwrap())
                    })
                })
                .collect(),
        )
    } else {
        unbound_assets::texture::GeneratedTexels::Rgba8(texels.to_vec())
    };
    let mut texture = unbound_assets::texture::import_generated(
        &r.name, r.width, r.height, texels, &meta, settings,
    )?;
    importer.store(&mut texture)?;
    let sprites = std::mem::take(&mut texture.sprites);
    let sprite_count = sprites.len();
    s.textures.write().unwrap().insert(
        guid,
        TextureSprites {
            name: texture.name.clone(),
            sprites,
            polygon,
        },
    );
    s.generated
        .write()
        .unwrap()
        .insert(r.key, Arc::new(texture.clone()));
    upload_imported(s, handle, texture, None, sprite_count)
}

/// Forgets a generated texture's texels and sprites (its texture was destroyed).
pub fn release_generated_texture(key: &str) {
    let s = state();
    if s.generated.write().unwrap().remove(key).is_some()
        && let Ok(guid) = texture_guid(key)
    {
        s.textures.write().unwrap().remove(&guid);
    }
    // Nor does the last traced texture keep them.
    let mut recent = s.traced_texels.lock().unwrap_or_else(|e| e.into_inner());
    if recent.as_ref().is_some_and(|(traced, _)| traced == key) {
        *recent = None;
    }
}

/// Where a request's texels come from: an imported texture (its GUID), or the caller's RGBA8
/// texels (base64, rows bottom-up) of a `width` x `height` texture, all of them or only those of
/// `window` (`[x, y, width, height]`).
#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct TexelSource {
    guid: Option<String>,
    rgba: Option<String>,
    width: Option<u32>,
    height: Option<u32>,
    window: Option<[u32; 4]>,
}

enum Texels {
    Imported(Arc<unbound_assets::texture::ImportedTexture>),
    Given {
        width: u32,
        height: u32,
        window: [u32; 4],
        rgba: Vec<u8>,
    },
}

impl Texels {
    fn texture(&self) -> unbound_assets::sprite_mesh::Texture<'_> {
        use unbound_assets::sprite_mesh::Texture;
        match self {
            Texels::Imported(texture) => match texture.mips.first() {
                // A floating-point texture has no 8-bit alpha to trace.
                Some(mip) => Texture::whole(
                    mip.width as i32,
                    mip.height as i32,
                    (texture.format == unbound_assets::texture::TextureFormat::Rgba8)
                        .then_some(&mip.data[..]),
                ),
                None => Texture::whole(0, 0, None),
            },
            Texels::Given {
                width,
                height,
                window,
                rgba,
            } => Texture {
                window: window.map(|v| v as i32),
                ..Texture::whole(*width as i32, *height as i32, Some(rgba))
            },
        }
    }
}

fn texel_source(s: &RuntimeState, source: &TexelSource) -> Result<Arc<Texels>> {
    if let Some(guid) = &source.guid {
        let guid = guid.to_ascii_lowercase();
        let mut recent = s.traced_texels.lock().unwrap_or_else(|e| e.into_inner());
        if let Some((key, texels)) = recent.as_ref()
            && *key == guid
        {
            return Ok(texels.clone());
        }
        let texels = Arc::new(Texels::Imported(imported_texture(s, &guid)?));
        *recent = Some((guid, texels.clone()));
        return Ok(texels);
    }
    let (Some(rgba), Some(width), Some(height)) = (&source.rgba, source.width, source.height)
    else {
        bail!("no texels: a texture GUID or RGBA texels with their size are needed");
    };
    let window = source.window.unwrap_or([0, 0, width, height]);
    let [x, y, w, h] = window;
    anyhow::ensure!(
        x.checked_add(w).is_some_and(|r| r <= width)
            && y.checked_add(h).is_some_and(|t| t <= height),
        "the texel window {window:?} is outside the {width}x{height} texture"
    );
    let rgba = crate::fonts::decode_base64(rgba).context("texels are not base64")?;
    anyhow::ensure!(
        rgba.len() == w as usize * h as usize * 4,
        "the texels do not fill a {w}x{h} window"
    );
    Ok(Arc::new(Texels::Given {
        width,
        height,
        window,
        rgba,
    }))
}

/// An imported texture by its asset GUID, or a sprite atlas page by its key.
fn imported_texture(
    s: &RuntimeState,
    key: &str,
) -> Result<Arc<unbound_assets::texture::ImportedTexture>> {
    if let Some(page) = key.strip_prefix(crate::sprite_atlases::PAGE_KEY_PREFIX) {
        return Ok(Arc::new(with_page_set(s, |set| {
            crate::sprite_atlases::page(set, page)
        })?));
    }
    if key.starts_with(GENERATED_KEY_PREFIX) {
        let texture = s.generated.read().unwrap().get(key).cloned();
        return texture.context("the generated texture was destroyed");
    }
    let guid = Guid::parse(key).context("bad guid")?;
    let db = s.db();
    let asset = db.by_guid(guid).context("no such asset")?;
    let meta = PathBuf::from(format!("{}.meta", asset.file.display()));
    s.imports
        .get_or_import(&texture_importer(s), &asset.file, &meta)
}

/// Runs `f` on the Play-mode sprite atlases, packing them on first use.
fn with_page_set<R>(s: &RuntimeState, f: impl FnOnce(&crate::sprite_atlases::AtlasSet) -> R) -> R {
    {
        let guard = s.atlases.read().unwrap_or_else(|e| e.into_inner());
        if let Some(set) = guard.as_ref() {
            return f(set);
        }
    }
    let initial = crate::sprite_atlases::pack_all(
        s.play_mode_atlases
            .as_ref()
            .map(|a| a.paths.as_slice())
            .unwrap_or_default(),
        &s.db(),
        &texture_importer(s),
        &s.imports,
    );
    let mut guard = s.atlases.write().unwrap_or_else(|e| e.into_inner());
    let set = guard.get_or_insert(initial);
    f(set)
}

/// Uploads an imported texture (`source`: its file and `.meta`, for an asset) to a handle.
fn upload_imported(
    s: &RuntimeState,
    handle: u64,
    mut texture: unbound_assets::texture::ImportedTexture,
    source: Option<(&std::path::Path, &std::path::Path)>,
    sprite_count: usize,
) -> Result<ImportedTextureInfo> {
    let importer = texture_importer(s);
    if let Some(why) = &texture.approximation {
        static WARNED: std::sync::Mutex<std::collections::BTreeSet<String>> =
            std::sync::Mutex::new(std::collections::BTreeSet::new());
        if WARNED
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .insert(why.clone())
        {
            let first =
                source.map_or_else(|| texture.name.clone(), |(f, _)| f.display().to_string());
            tracing::warn!("Unbound: {why} (first: {first})");
        }
    }
    let texture_format = texture.reported_format().unwrap_or(0);
    let format = match texture.format {
        unbound_assets::texture::TextureFormat::Rgba8 => i32::from(!texture.srgb),
        unbound_assets::texture::TextureFormat::Rgba32F => {
            // The importer retains float32 working pixels for both HDR storage formats.
            // Honour the target's explicit RGBAFloat override instead of narrowing it to Half.
            let meta = source.context("a floating-point atlas page")?.1;
            let document = unbound_yaml::parse_value(&std::fs::read_to_string(meta)?)?;
            let settings = unbound_assets::meta::importer(&document, "TextureImporter");
            match unbound_assets::texture::platform_format(settings, &importer.build_target) {
                Some(20) => 9, // TextureImporterFormat.RGBAFloat -> TexturePacket RGBAFloat.
                _ => 2,
            }
        }
    };
    let mip_count = texture.mip_count();
    let dimension = texture.dimension();
    // The import's blocks, else BC7 blocks made for the GPU when it samples them, else the
    // pixels.
    let gpu_bc = GPU_BC.load(std::sync::atomic::Ordering::Relaxed);
    // Keep compressed blocks until the renderer chooses a device representation. ETC can
    // carry its cached BC7 fallback too, preserving imports that precede device initialization.
    let gpu_channel = texture.gpu_channel();
    let blocks = texture.blocks.take().filter(|b| {
        b.format.is_bc() || b.format.is_astc() || (b.format.is_etc() && !b.format.is_eac())
    });
    let (gpu_format, data) = match (blocks, texture.bc7.take()) {
        (Some(blocks), fallback) => {
            let mut code = block_code(blocks.format, texture.srgb);
            let mut data = concat(&blocks.mips);
            if blocks.format.is_etc()
                && let Some(fallback) = fallback
            {
                code += texture_format::ETC_BC7_OFFSET;
                data.reserve(fallback.iter().map(|m| m.len()).sum());
                for mip in fallback {
                    data.extend_from_slice(&mip);
                }
            }
            (code, data)
        }
        (None, Some(blocks)) if gpu_bc => (format + texture_format::BC7_COLOR, concat(&blocks)),
        _ => {
            if dimension == 4 {
                // The import cache keeps bottom-up rows for CPU reads. Cube sampling addresses
                // each GPU face top-down, so reverse each face's mip rows only for this upload
                // (a compressed cubemap's blocks are top-down already).
                for mip in &mut texture.mips {
                    let row = mip.data.len() / mip.height.max(1) as usize;
                    unbound_assets::texture::flip_rows(&mut mip.data, row);
                }
            }
            // Mip 0 keeps its buffer; the smaller levels are appended to it.
            let total: usize = texture.mips.iter().map(|m| m.data.len()).sum();
            let mut mips = std::mem::take(&mut texture.mips).into_iter();
            let mut data = mips.next().map(|m| m.data).unwrap_or_default();
            data.reserve_exact(total - data.len());
            for mip in mips {
                data.extend_from_slice(&mip.data);
            }
            let srgb_sampled = format == texture_format::SRGB_RGBA8 && !importer.gamma;
            single_channel_texels(
                gpu_channel,
                Some(texture_format),
                format,
                srgb_sampled,
                data,
            )
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
            wrap_w: wrap(texture.wrap_w),
            aniso_level: texture.aniso,
            render_target: false,
            random_write: false,
            auto_generate_mips: false,
            defer_mips: false,
            retain_resolve: false,
            bind_ms: false,
            depth_compare: None,
            samples: 1,
            depth_bits: 0,
            layers: texture.layers as i32,
            dimension,
            logical: None,
            screen_order: false,
            blue_first: false,
            packed: 0,
        },
    );
    set_texture_mip_bias(handle, texture.mip_bias);
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
        dimension,
        wrap_w: wrap(texture.wrap_w),
        texture_format,
        usage_mode: texture.usage_mode,
    })
}

/// `bundle-open`: an AssetBundle from a file (at `offset`) or from base64 `data`; a nonzero `crc`
/// is checked against its uncompressed content before it is registered (0, or none, skips it).
/// With `reportCrc` a mismatch answers `{"crcMismatch": {"expected", "actual"}}` instead of failing.
fn open_bundle(request: &str) -> Result<String> {
    #[derive(serde::Deserialize)]
    #[serde(rename_all = "camelCase")]
    struct Request {
        #[serde(default)]
        path: Option<String>,
        #[serde(default)]
        offset: u64,
        #[serde(default)]
        data: Option<String>,
        #[serde(default)]
        crc: u32,
        #[serde(default)]
        report_crc: bool,
    }
    let r: Request = serde_json::from_str(request)?;
    let opened = match (r.path, r.data) {
        (Some(path), _) => crate::bundles::open_file(&path, r.offset, r.crc),
        (None, Some(data)) => crate::bundles::open(
            &crate::fonts::decode_base64(&data).context("bundle data is not base64")?,
            r.crc,
        ),
        _ => anyhow::bail!("bundle-open needs a path or data"),
    };
    match opened {
        Err(error) if r.report_crc => {
            match error.downcast_ref::<unbound_assets::bundle::CrcMismatch>() {
                Some(m) => Ok(serde_json::json!({
                    "crcMismatch": { "expected": m.expected, "actual": m.actual }
                })
                .to_string()),
                None => Err(error),
            }
        }
        other => other,
    }
}

/// `bundle-cache-form`: writes the archive at `source` to `dest` in the form the AssetBundle cache
/// keeps it (LZ4 when `compress`, else uncompressed;
/// [`unbound_assets::bundle_cache::cache_form`]) and answers the room it needs (`required`) and
/// whether it was rewritten.
fn bundle_cache_form(request: &str) -> Result<String> {
    #[derive(serde::Deserialize)]
    struct Request {
        source: String,
        dest: String,
        compress: bool,
    }
    let r: Request = serde_json::from_str(request)?;
    let data =
        std::fs::read(&r.source).with_context(|| format!("reading the download {}", r.source))?;
    let form = unbound_assets::bundle_cache::cache_form(&data, r.compress)?;
    std::fs::write(&r.dest, &form.data).with_context(|| format!("writing {}", r.dest))?;
    Ok(
        serde_json::json!({ "required": form.required_space, "rewritten": form.rewritten })
            .to_string(),
    )
}

/// A .meta tree as the managed side sends it (maps as objects, sequences as arrays, scalars as
/// text).
fn json_to_yaml(value: &serde_json::Value) -> unbound_yaml::Value {
    use serde_json::Value as Json;
    use unbound_yaml::Value;
    match value {
        Json::Null => Value::Null,
        Json::String(text) => Value::Scalar(text.clone()),
        Json::Bool(b) => Value::Scalar(if *b { "1" } else { "0" }.to_string()),
        Json::Number(n) => Value::Scalar(n.to_string()),
        Json::Array(items) => Value::Seq(items.iter().map(json_to_yaml).collect()),
        Json::Object(map) => Value::Map(
            map.iter()
                .map(|(k, v)| (k.clone(), json_to_yaml(v)))
                .collect::<Vec<_>>()
                .into(),
        ),
    }
}

#[cfg(test)]
mod tests {
    #[test]
    fn etc_block_codes_keep_colour_and_data_distinct() {
        use unbound_assets::texture::BlockFormat as B;
        for format in [B::Etc1, B::Etc2Rgb, B::Etc2Rgba8] {
            let colour = if format == B::Etc2Rgba8 {
                super::texture_format::ETC_RGBA_COLOR
            } else {
                super::texture_format::ETC_RGB_COLOR
            };
            assert_eq!(super::block_code(format, true), colour);
            assert_eq!(super::block_code(format, false), colour + 1);
        }
        assert_eq!(
            super::block_code(B::Bc1, true),
            super::texture_format::BC1_COLOR
        );
    }

    use super::*;

    /// Alpha channel imports go up alpha-only, red and red-green ones in one and two channels
    /// unless sampled through an sRGB view, where they keep four; other textures are untouched.
    #[test]
    fn one_channel_imports_sample_their_channel() {
        use texture_format::{ALPHA8, LINEAR_RGBA8, R8_UNORM, RG8_UNORM, SRGB_RGBA8};
        use unbound_assets::texture::GpuChannel;
        use unbound_assets::texture::unity_format::{R8, RG16, RGB24};
        let texels = vec![10, 20, 30, 40, 50, 60, 70, 80];
        let up = |channel, reported, format, srgb| {
            single_channel_texels(channel, reported, format, srgb, texels.clone())
        };
        let (red, red_green) = (Some(GpuChannel::Red), Some(GpuChannel::RedGreen));
        assert_eq!(
            up(Some(GpuChannel::Alpha), None, LINEAR_RGBA8, false),
            (ALPHA8, vec![40, 80])
        );
        assert_eq!(
            up(red, Some(R8), LINEAR_RGBA8, false),
            (R8_UNORM, vec![10, 50])
        );
        // An sRGB texture in a gamma project samples its bytes as they are.
        assert_eq!(
            up(red, Some(R8), SRGB_RGBA8, false),
            (R8_UNORM, vec![10, 50])
        );
        assert_eq!(
            up(red, Some(R8), SRGB_RGBA8, true),
            (SRGB_RGBA8, vec![10, 0, 0, 255, 50, 0, 0, 255])
        );
        // Red kept by a texture reporting another format (scripts upload it as that) stays RGBA.
        assert_eq!(
            up(red, None, LINEAR_RGBA8, false),
            (LINEAR_RGBA8, vec![10, 0, 0, 255, 50, 0, 0, 255])
        );
        assert_eq!(
            up(red_green, Some(RG16), LINEAR_RGBA8, false),
            (RG8_UNORM, vec![10, 20, 50, 60])
        );
        assert_eq!(
            up(red_green, Some(RG16), SRGB_RGBA8, true),
            (SRGB_RGBA8, vec![10, 20, 255, 255, 50, 60, 255, 255])
        );
        assert_eq!(
            up(Some(GpuChannel::Opaque), Some(RGB24), SRGB_RGBA8, true),
            (SRGB_RGBA8, vec![10, 20, 30, 255, 50, 60, 70, 255])
        );
        assert_eq!(
            up(None, None, SRGB_RGBA8, true),
            (SRGB_RGBA8, texels.clone())
        );
    }

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
        assert_eq!(
            default_importer("Assets/StreamingAssets/note.txt"),
            "DefaultImporter"
        );
        assert_eq!(
            default_importer("Assets/StreamingAssets/SubDir/img.png"),
            "DefaultImporter"
        );
        assert_eq!(
            default_importer("Assets/WebGLTemplates/Custom/index.html"),
            "DefaultImporter"
        );
        assert_eq!(
            default_importer("Assets/WebPlayerTemplates/Legacy/readme.txt"),
            "DefaultImporter"
        );
        assert_eq!(
            default_importer("Assets/Data/StreamingAssets/nested_not_root.txt"),
            "TextScriptImporter"
        );
    }

    #[test]
    fn asset_kind_respects_special_default_folders_and_extensionless_files() {
        assert_eq!(asset_kind("Assets/Data/Blob.xyz"), "unknown");
        assert_eq!(asset_kind("Assets/Data/csc.rsp"), "unknown");
        assert_eq!(asset_kind("Assets/Data/NoExtension"), "unknown");
        assert_eq!(asset_kind("Assets/Data/txt"), "unknown");
        assert_eq!(asset_kind("Assets/Folder.WithDot/NoExtension"), "unknown");
        assert_eq!(asset_kind("Assets/StreamingAssets/note.txt"), "unknown");
        assert_eq!(asset_kind("Assets/StreamingAssets/img.png"), "unknown");
        assert_eq!(
            asset_kind("Assets/StreamingAssets/SubDir/nested.txt"),
            "unknown"
        );
        assert_eq!(
            asset_kind("Assets/WebGLTemplates/Custom/index.html"),
            "unknown"
        );
        assert_eq!(
            asset_kind("Assets/WebPlayerTemplates/Legacy/readme.txt"),
            "unknown"
        );
        assert_eq!(
            asset_kind("Assets/Data/StreamingAssets/nested_not_root.txt"),
            "text"
        );
        assert_eq!(asset_kind("Assets/Data/Plain.txt"), "text");
        assert_eq!(asset_kind("Assets/Sprites/Single.png"), "texture");
        assert_eq!(asset_kind("Assets/Scenes/Probe.unity"), "scene");
        assert_eq!(asset_kind("Assets/Data/ProbeSame.dll"), "plugin");
        // The special roots match in any case.
        assert_eq!(
            asset_kind("Assets/webplayertemplates/Legacy/readme.txt"),
            "unknown"
        );
        assert_eq!(asset_kind("Assets/streamingassets/raw.so"), "unknown");
    }

    #[test]
    fn a_meta_names_the_importer_only_of_an_extension_the_list_does_not_know() {
        let asset = |path: &str, importer: Option<&str>| unbound_project::assetdb::Asset {
            guid: Guid(1),
            path: path.to_string(),
            file: PathBuf::from(path),
            is_folder: false,
            labels: Vec::new(),
            importer: importer.map(str::to_string),
        };
        // A native-format kind the list does not know (a package's preset library).
        let colors = asset(
            "Assets/Editor/Swatches.colors",
            Some("NativeFormatImporter"),
        );
        assert_eq!(asset_importer(&colors), "NativeFormatImporter");
        assert_eq!(listed_kind(&colors), "yaml");
        // A .meta from before the extension got its own importer does not change it.
        let prefab = asset("Assets/Old.prefab", Some("NativeFormatImporter"));
        assert_eq!(asset_importer(&prefab), "PrefabImporter");
        assert_eq!(listed_kind(&prefab), "prefab");
        let dll = asset("Assets/Plugins/Lib.dll", Some("DefaultImporter"));
        assert_eq!(asset_importer(&dll), "PluginImporter");
        assert_eq!(listed_kind(&dll), "plugin");
        // Without a .meta importer: the extension list, else the DefaultImporter.
        assert_eq!(
            asset_importer(&asset("Assets/A.anim", None)),
            "NativeFormatImporter"
        );
        assert_eq!(
            asset_importer(&asset("Assets/A.xyz", None)),
            "DefaultImporter"
        );
        assert_eq!(listed_kind(&asset("Assets/A.xyz", None)), "unknown");
        assert_eq!(
            listed_kind(&asset("Assets/UI/A.uxml", Some("ScriptedImporter"))),
            "uxml"
        );
    }

    #[test]
    fn kinds_follow_the_importer_of_the_extension() {
        // Every plug-in kind loads as a plug-in, every native-format kind as YAML.
        for path in [
            "Assets/P/libnative.so",
            "Assets/P/libmac.dylib",
            "Assets/P/library.aar",
            "Assets/P/bridge.jslib",
            "Assets/P/Source.cpp",
            "Assets/P/Header.h",
            "Assets/P/Native.kt",
            "Assets/P/Native.swift",
            "Assets/P/native.DLL",
        ] {
            assert_eq!(default_importer(path), "PluginImporter", "{path}");
            assert_eq!(asset_kind(path), "plugin", "{path}");
        }
        for path in [
            "Assets/N/Mixer.mixer",
            "Assets/N/Sky.cubemap",
            "Assets/N/Glow.flare",
            "Assets/N/Settings.lighting",
            "Assets/N/Atlas.spriteatlas",
            "Assets/N/Target.renderTexture",
            "Assets/N/Data.asset",
        ] {
            assert_eq!(default_importer(path), "NativeFormatImporter", "{path}");
            assert_eq!(asset_kind(path), "yaml", "{path}");
        }
        // A prefab and a V2 atlas have importers of their own; the atlas still loads as YAML.
        assert_eq!(default_importer("Assets/N/Thing.prefab"), "PrefabImporter");
        assert_eq!(asset_kind("Assets/N/Thing.prefab"), "prefab");
        assert_eq!(
            default_importer("Assets/N/A.spriteatlasv2"),
            "SpriteAtlasImporter"
        );
        assert_eq!(asset_kind("Assets/N/A.spriteatlasv2"), "yaml");
        // Kinds the importer table does not list keep their own loaders.
        assert_eq!(asset_kind("Assets/S/Lib.hlsl"), "shader");
        assert_eq!(asset_kind("Assets/U/Doc.uxml"), "uxml");
        assert_eq!(asset_kind("Assets/F/Font.ttc"), "font");
        assert_eq!(asset_kind("Assets/Scenes/Main.unity"), "scene");
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

    /// One triangle, as the importer gets it from a file.
    const TRIANGLE_OBJ: &str = "o Tri\nv 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n";

    fn imported_triangle(meta: &str) -> unbound_assets::ImportedModel {
        let meta = if meta.is_empty() {
            unbound_yaml::Value::Null
        } else {
            unbound_yaml::parse_value(meta).unwrap()
        };
        let source = std::path::Path::new("Tri.obj");
        let mtl = unbound_assets::ModelImporter::default().mtl;
        unbound_assets::model::import_model(source, TRIANGLE_OBJ.as_bytes(), &meta, mtl).unwrap()
    }

    /// The Read/Write Enabled setting where the committed 6000.0.71f1 probe metas write it (under
    /// `animations:`) and under `meshes:`, on and off. A meta without the setting, or no meta, takes
    /// the importer's fallback, off: the 6000.0 Manual's FBX Importer Model tab says "By default,
    /// this option is disabled". That an omitted key reads as that default is not measured.
    #[test]
    fn model_mesh_answers_carry_the_read_write_setting() {
        let on = "ModelImporter:\n  serializedVersion: 22200\n  animations:\n    isReadable: 1\n  meshes:\n    globalScale: 1\n";
        let off = "ModelImporter:\n  serializedVersion: 22200\n  animations:\n    isReadable: 0\n  meshes:\n    globalScale: 1\n";
        let meshes_on = "ModelImporter:\n  meshes:\n    globalScale: 1\n    isReadable: 1\n";
        let unset = "ModelImporter:\n  serializedVersion: 22200\n  meshes:\n    globalScale: 1\n";
        let cases = [
            (on, true),
            (off, false),
            (meshes_on, true),
            (unset, false),
            ("", false),
        ];
        for (meta, readable) in cases {
            let model = imported_triangle(meta);
            assert_eq!(model.settings.is_readable, readable, "{meta:?}");
            let answer = model_mesh_json(&model, 0).unwrap();
            assert_eq!(answer["readable"], json!(readable), "{meta:?}");
        }
    }

    /// The flag is the only addition: the geometry is the same bytes whatever the setting.
    #[test]
    fn model_mesh_answers_keep_the_geometry_unchanged() {
        let on = imported_triangle("ModelImporter:\n  animations:\n    isReadable: 1\n");
        let off = imported_triangle("ModelImporter:\n  animations:\n    isReadable: 0\n");
        let mut with_flag = model_mesh_json(&off, 0).unwrap();
        let flag = with_flag.as_object_mut().unwrap().remove("readable");
        assert_eq!(flag, Some(json!(false)));
        assert_eq!(with_flag, crate::models::mesh_json(&off.meshes[0]));
        let mut readable = model_mesh_json(&on, 0).unwrap();
        readable.as_object_mut().unwrap().remove("readable");
        assert_eq!(readable, with_flag);
        assert!(model_mesh_json(&off, 1).is_err());
    }

    /// The Editor's LZ4 manifest bundle of `tests/projects/assetbundles` and its `.manifest` CRC.
    fn editor_bundle() -> (Vec<u8>, u32) {
        let path = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../tests/projects/assetbundles/Reference/bundles/lz4/lz4");
        let manifest = std::fs::read_to_string(path.with_extension("manifest")).unwrap();
        let crc = manifest
            .lines()
            .find_map(|l| l.strip_prefix("CRC: "))
            .expect("a CRC line")
            .trim()
            .parse()
            .unwrap();
        (std::fs::read(path).unwrap(), crc)
    }

    fn base64(data: &[u8]) -> String {
        const ALPHABET: &[u8; 64] =
            b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        let mut out = String::new();
        for chunk in data.chunks(3) {
            let byte = |i: usize| u32::from(chunk.get(i).copied().unwrap_or(0));
            let n = (byte(0) << 16) | (byte(1) << 8) | byte(2);
            for i in 0..4 {
                if i <= chunk.len() {
                    out.push(ALPHABET[((n >> (18 - 6 * i)) & 63) as usize] as char);
                } else {
                    out.push('=');
                }
            }
        }
        out
    }

    /// Opened: the bundle closes again.
    fn close_opened(response: Result<String>) {
        let response = response.unwrap_or_else(|e| panic!("{e:#}"));
        let info: serde_json::Value = serde_json::from_str(&response).unwrap();
        crate::bundles::close(info["id"].as_u64().expect("an id"));
    }

    fn crc_mismatch(response: Result<String>) {
        let error = format!("{:#}", response.expect_err("a CRC mismatch"));
        assert!(error.contains("CRC mismatch"), "{error}");
    }

    /// The requests of LoadFromFile (with an offset) and LoadFromMemory / LoadFromStream (their
    /// bytes): a missing or zero `crc` loads without a check, the right one loads, another fails.
    #[test]
    fn bundle_open_checks_a_nonzero_crc_on_the_file_and_memory_routes() {
        let (data, crc) = editor_bundle();
        let file = std::env::temp_dir().join(format!("unbound-bundle-crc-{}", std::process::id()));
        let mut prefixed = vec![0xa5u8; 37];
        prefixed.extend(&data);
        std::fs::write(&file, &prefixed).unwrap();
        let path = file.to_str().unwrap().to_string();
        let memory = base64(&data);

        close_opened(open_bundle(
            &json!({ "path": path, "offset": 37 }).to_string(),
        ));
        close_opened(open_bundle(&json!({ "data": memory }).to_string()));
        for good in [0, crc] {
            close_opened(open_bundle(
                &json!({ "path": path, "offset": 37, "crc": good }).to_string(),
            ));
            close_opened(open_bundle(
                &json!({ "data": memory, "crc": good }).to_string(),
            ));
        }
        for wrong in [crc ^ 1, u32::MAX] {
            crc_mismatch(open_bundle(
                &json!({ "path": path, "offset": 37, "crc": wrong }).to_string(),
            ));
            crc_mismatch(open_bundle(
                &json!({ "data": memory, "crc": wrong }).to_string(),
            ));
        }
        // The archive starts at the offset; truncated bytes fail with or without a check.
        assert!(
            open_bundle(&json!({ "path": path, "offset": 0, "crc": crc }).to_string()).is_err()
        );
        let truncated = base64(&data[..data.len() - 1]);
        assert!(open_bundle(&json!({ "data": truncated, "crc": crc }).to_string()).is_err());
        assert!(open_bundle(&json!({ "data": truncated }).to_string()).is_err());
        std::fs::remove_file(&file).ok();
    }

    #[test]
    fn unavailable_builtin_shader_sources_are_reported_with_their_cause() {
        let roots = vec![PathBuf::from("/cache/DefaultResources")];
        assert_eq!(builtin_shader_roots(Ok(roots.clone())), roots);
        let offline =
            anyhow::anyhow!("UNBOUND_OFFLINE forbids downloading; set UNBOUND_BUILTIN_SHADERS")
                .context("finding built-in shaders");
        let message = builtin_shaders_unavailable(&offline);
        assert!(message.contains("built-in shader sources are not available"));
        assert!(message.contains("finding built-in shaders: UNBOUND_OFFLINE forbids downloading"));
        assert!(message.contains("UNBOUND_BUILTIN_SHADERS"));
        assert!(builtin_shader_roots(Err(offline)).is_empty());
    }
}
