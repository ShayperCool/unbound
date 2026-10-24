//! Preparing a game to start: compile its code, make the module facades, and describe the project
//! to the managed engine (`GameConfig` in managed/Unbound.Host/Game.cs).
use anyhow::{Context, Result, bail};
use rayon::prelude::*;
use serde::Deserialize;
use serde_json::{Value as Json, json};
use std::{
    collections::{BTreeMap, BTreeSet},
    path::{Path, PathBuf},
    time::Instant,
};
use unbound_compat::CompileMode;
use unbound_host::ManagedRuntime;
use unbound_project::{Project, assetdb::read_meta_guid, compile::CompileResponse};
use unbound_yaml::Value;

#[derive(Debug, Clone)]
pub struct GameOptions {
    /// Active build target group: `Standalone`, `Android`, `iOS`, ...
    pub target: String,
    pub mode: CompileMode,
    /// The scene to start with (`Assets/...`); the first enabled build scene when unset.
    pub scene: Option<String>,
    /// Turn the left mouse button into a touch, for mobile games.
    pub simulate_touch: bool,
}

/// Where a Unity release's module facades are kept: they depend on the release alone, so they
/// live in the global cache, `<cache>/facades/<unity-version>/`.
pub fn facades_dir(project: &Project) -> PathBuf {
    unbound_project::cache_root()
        .join("facades")
        .join(project.version.to_string())
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FacadeReport {
    pub output_dir: PathBuf,
    pub reused: bool,
    pub assemblies: u32,
    pub types: u32,
    pub milliseconds: u64,
}

pub fn ensure_facades(project: &Project, runtime: &ManagedRuntime) -> Result<FacadeReport> {
    runtime.call(
        "facades",
        &json!({
            "unityManagedEngineDir": project.install.managed_engine_dir(),
            "platformModules": project.install.platform_modules(),
            "outputDir": facades_dir(project),
        }),
    )
}

fn meta_guid(file: &Path) -> Option<String> {
    read_meta_guid(Path::new(&format!("{}.meta", file.display()))).map(|g| g.to_string())
}

/// Compiles the game and builds the configuration the managed engine starts from.
pub fn prepare(project: &Project, runtime: &ManagedRuntime, options: &GameOptions) -> Result<Json> {
    let start = Instant::now();
    let graph = project.script_graph(options.mode, &options.target)?;
    // Play mode runs inside the Editor, which has every editor assembly loaded (importers, editor
    // windows, [InitializeOnLoad] code); a player has none.
    let editor = options.mode == CompileMode::EditorPlayMode;
    let request = project.compile_request(&graph, options.mode, editor);
    let compiled: CompileResponse = runtime.call("compile", &request)?;
    if compiled.failed > 0 {
        let mut message = String::new();
        for assembly in compiled.assemblies.iter().filter(|a| a.status == "failed") {
            message.push_str(&format!("\n{}:", assembly.name));
            for error in assembly.errors.iter().take(10) {
                message.push_str(&format!("\n  {error}"));
            }
        }
        bail!("the game's code does not compile (as in the Editor, it cannot be played):{message}");
    }
    tracing::info!(
        "compiled {} assemblies ({} compiled, {} cached) in {}ms",
        compiled.assemblies.len(),
        compiled.compiled,
        compiled.cached,
        compiled.milliseconds
    );
    let facades = ensure_facades(project, runtime)?;

    let mut assemblies = Vec::new();
    let mut plugins: BTreeSet<PathBuf> = BTreeSet::new();
    for result in &compiled.assemblies {
        let script = graph
            .get(&result.name)
            .context("compiled an assembly the graph lacks")?;
        plugins.extend(script.plugins.iter().cloned());
        let scripts: Vec<Json> = script
            .sources
            .par_iter()
            .filter_map(|s| {
                let stem = s.file.file_stem()?.to_str()?.to_string();
                let guid = meta_guid(&s.file)?;
                // The namespace of the class the file declares: the script is that class.
                let namespace = std::fs::read_to_string(&s.file).ok().and_then(|text| {
                    unbound_project::script_class::declared_namespace(&text, &stem)
                });
                Some(json!({ "fileName": stem, "guid": guid, "namespace": namespace }))
            })
            .collect();
        // What CompilationPipeline reports about it, with Unity's project-relative paths.
        let sources: Vec<&str> = script.sources.iter().map(|s| s.path.as_str()).collect();
        let compiled_references: Vec<PathBuf> = script
            .plugins
            .iter()
            .map(|p| crate::view::to_view(project, p))
            .collect();
        assemblies.push(json!({
            "name": result.name,
            "dll": result.dll.as_ref().map(|d| crate::view::to_view(project, Path::new(d))),
            "pdb": result.pdb.as_ref().map(|d| crate::view::to_view(project, Path::new(d))),
            "scripts": scripts,
            "sourcePath": script.sources.first().map(|s| &s.file),
            "editor": script.editor_only,
            "sources": sources,
            "defines": script.defines,
            "references": script.references,
            "compiledReferences": compiled_references,
            "asmdef": script.asmdef.as_ref().map(|a| crate::view::unity_path(project, a)),
            "rootNamespace": script.root_namespace,
        }));
    }
    for plugin in &plugins {
        let Some(name) = plugin.file_stem().and_then(|s| s.to_str()) else {
            continue;
        };
        assemblies.push(json!({
            "name": name,
            "dll": crate::view::to_view(project, plugin),
            "pluginGuid": meta_guid(plugin),
        }));
    }

    let settings = &project.settings;
    let build_scenes: Vec<Json> = settings
        .build_scenes
        .iter()
        .filter(|s| s.enabled)
        .map(|s| json!({ "path": s.path, "guid": s.guid.to_string() }))
        .collect();
    let first_scene = options
        .scene
        .clone()
        .or_else(|| {
            settings
                .build_scenes
                .iter()
                .find(|s| s.enabled)
                .map(|s| s.path.clone())
        })
        .context("no scene to start: the build settings list none; pass --scene")?;

    let time = read_settings(project, "TimeManager.asset");
    let company = if settings.company_name.is_empty() {
        "DefaultCompany"
    } else {
        &settings.company_name
    };
    let product = if settings.product_name.is_empty() {
        "Game"
    } else {
        &settings.product_name
    };
    let persistent = unbound_project::install::home()
        .join(".config/unity3d")
        .join(company)
        .join(product);

    let config = json!({
        // The game sees the project through its view (its working directory, see view.rs).
        "projectPath": crate::view::dir(project),
        "unityVersion": project.version.to_string(),
        // The compatibility profile's behaviour switches the release has.
        "features": unbound_compat::Feature::ALL
            .iter()
            .filter(|f| project.profile.has(**f))
            .map(|f| f.name())
            .collect::<Vec<_>>(),
        "productName": product,
        "companyName": company,
        // As Unity: the active group's application identifier, else com.Company.Product.
        "identifier": settings
            .identifier_for(&options.target)
            .map(String::from)
            .unwrap_or_else(|| format!("com.{}.{}", company.replace(' ', ""), product.replace(' ', ""))),
        "bundleVersion": settings.bundle_version,
        "persistentDataPath": persistent,
        "facadeDir": facades.output_dir,
        "firstScene": first_scene,
        "buildScenes": build_scenes,
        "assemblies": assemblies,
        "tags": project_tags(project),
        "renderingLayers": rendering_layers(project),
        "layers": settings.layers,
        "inputAxes": input_axes(project),
        "fixedDeltaTime": time.as_ref().and_then(|t| t.get("Fixed Timestep")).and_then(Value::as_f64).unwrap_or(0.02),
        "maximumDeltaTime": time.as_ref().and_then(|t| t.get("Maximum Allowed Timestep")).and_then(Value::as_f64).unwrap_or(1.0 / 3.0),
        "timeScale": time.as_ref().and_then(|t| t.get("m_TimeScale")).and_then(Value::as_f64).unwrap_or(1.0),
        "simulateTouch": options.simulate_touch,
        "executionOrders": execution_orders(&graph),
        "builtins": builtins(project),
        "sortingLayers": sorting_layers(project),
        "scriptingDefines": settings.defines_for(&options.target),
        "targetGroup": options.target,
        "preloadedAssets": settings.preloaded_assets.iter().map(|(guid, file_id)| json!({ "guid": guid.to_string(), "fileId": file_id })).collect::<Vec<_>>(),
        "editorScenes": settings.build_scenes.iter().map(|s| json!({ "path": s.path, "guid": s.guid.to_string(), "enabled": s.enabled })).collect::<Vec<_>>(),
        "configObjects": settings.config_objects.iter().map(|(name, (guid, file_id))| (name.clone(), json!({ "guid": guid.to_string(), "fileId": file_id }))).collect::<serde_json::Map<String, Json>>(),
        "nativePlugins": project.native_plugins().iter().map(|p| crate::view::to_view(project, p)).collect::<Vec<_>>(),
        "workingDirectory": crate::view::build(project)?,
        "packages": crate::view::package_infos(project),
        "unityDataPath": project.install.data,
        "frameworkDir": project.install.data.join("MonoBleedingEdge/lib/mono/4.5"),
        "vSyncCount": vsync_count(project),
        "renderPipeline": render_pipeline(project),
        "colorSpace": read_settings(project, "ProjectSettings.asset")
            .and_then(|p| p.get("m_ActiveColorSpace").and_then(Value::as_i64))
            .unwrap_or(0),
    });
    tracing::info!("prepared the game in {:.2?}", start.elapsed());
    Ok(config)
}

fn read_settings(project: &Project, file: &str) -> Option<Value> {
    let text = std::fs::read_to_string(project.root.join("ProjectSettings").join(file)).ok()?;
    let docs = unbound_yaml::parse_documents(&text).ok()?;
    docs.into_iter().next().map(|d| d.body)
}

/// `vSyncCount` of the quality level the Editor has selected (`m_CurrentQuality`); 1 without one.
fn vsync_count(project: &Project) -> i64 {
    let Some(quality) = read_settings(project, "QualitySettings.asset") else {
        return 1;
    };
    let current = quality
        .get("m_CurrentQuality")
        .and_then(Value::as_i64)
        .unwrap_or(0)
        .max(0) as usize;
    quality
        .get("m_QualitySettings")
        .and_then(Value::as_seq)
        .and_then(|levels| levels.get(current))
        .and_then(|level| level.get("vSyncCount"))
        .and_then(Value::as_i64)
        .unwrap_or(1)
}

/// An asset reference (`{fileID, guid}`) as the managed side loads it; null for none.
fn asset_ref(v: Option<&Value>) -> Json {
    let Some(v) = v else { return Json::Null };
    let file_id = v.get("fileID").and_then(Value::as_i64).unwrap_or(0);
    match v.get("guid").and_then(Value::as_guid) {
        Some(guid) if file_id != 0 => json!({ "guid": guid.to_string(), "fileId": file_id }),
        _ => Json::Null,
    }
}

/// The scriptable render pipeline settings: GraphicsSettings' default pipeline, each quality
/// level's override (and the current level) and the pipelines' global settings assets.
fn render_pipeline(project: &Project) -> Json {
    let graphics = read_settings(project, "GraphicsSettings.asset");
    let quality = read_settings(project, "QualitySettings.asset");
    let levels: Vec<Json> = quality
        .as_ref()
        .and_then(|q| q.get("m_QualitySettings"))
        .and_then(Value::as_seq)
        .map(|levels| {
            levels
                .iter()
                .map(|l| {
                    json!({
                        "name": l.get("name").and_then(Value::as_str).unwrap_or(""),
                        "pipeline": asset_ref(l.get("customRenderPipeline")),
                    })
                })
                .collect()
        })
        .unwrap_or_default();
    let global_settings: serde_json::Map<String, Json> = graphics
        .as_ref()
        .and_then(|g| g.get("m_RenderPipelineGlobalSettingsMap"))
        .and_then(Value::as_map)
        .map(|m| {
            m.iter()
                .map(|(k, v)| (k.to_string(), asset_ref(Some(v))))
                .collect()
        })
        .unwrap_or_default();
    json!({
        "default": asset_ref(graphics.as_ref().and_then(|g| g.get("m_CustomRenderPipeline"))),
        "qualityLevels": levels,
        "currentQuality": quality.as_ref().and_then(|q| q.get("m_CurrentQuality")).and_then(Value::as_i64).unwrap_or(0),
        "globalSettings": global_settings,
    })
}

/// The built-in resource tables of the profile, without their description.
fn builtins(project: &Project) -> Json {
    let mut table: Json =
        serde_json::from_str(project.profile.builtin_resources()).unwrap_or(Json::Null);
    if let Some(map) = table.as_object_mut() {
        map.remove("source");
    }
    table
}

/// `m_SortingLayers` of TagManager.asset: names and unique ids, in order.
fn sorting_layers(project: &Project) -> Vec<Json> {
    let Some(tags) = read_settings(project, "TagManager.asset") else {
        return vec![json!({ "name": "Default", "id": 0 })];
    };
    let layers: Vec<Json> = tags
        .get("m_SortingLayers")
        .and_then(Value::as_seq)
        .unwrap_or_default()
        .iter()
        .map(|l| {
            json!({
                "name": l.get("name").and_then(Value::as_str).unwrap_or(""),
                // Written unsigned; Unity reads the 32 bits as a signed id.
                "id": l.get("uniqueID").and_then(Value::as_i64).unwrap_or(0) as u32 as i32,
            })
        })
        .collect();
    if layers.is_empty() {
        vec![json!({ "name": "Default", "id": 0 })]
    } else {
        layers
    }
}

fn project_tags(project: &Project) -> Vec<String> {
    project
        .settings
        .tags
        .iter()
        .filter(|t| !t.is_empty())
        .cloned()
        .collect()
}

/// The rendering layer names (TagManager's `m_RenderingLayers`; undefined layers empty).
fn rendering_layers(project: &Project) -> Vec<String> {
    let layers: Vec<String> = read_settings(project, "TagManager.asset")
        .and_then(|t| {
            t.get("m_RenderingLayers")
                .and_then(Value::as_seq)
                .map(|seq| {
                    seq.iter()
                        .map(|v| v.as_str().unwrap_or("").to_string())
                        .collect()
                })
        })
        .unwrap_or_default();
    if layers.is_empty() {
        vec!["Default".to_string()]
    } else {
        layers
    }
}

fn input_axes(project: &Project) -> Vec<Json> {
    let Some(input) = read_settings(project, "InputManager.asset") else {
        return Vec::new();
    };
    let mut axes = Vec::new();
    for axis in input
        .get("m_Axes")
        .and_then(Value::as_seq)
        .unwrap_or_default()
    {
        let s = |k: &str| {
            axis.get(k)
                .and_then(Value::as_str)
                .unwrap_or("")
                .to_string()
        };
        let f = |k: &str| axis.get(k).and_then(Value::as_f64).unwrap_or(0.0);
        let i = |k: &str| axis.get(k).and_then(Value::as_i64).unwrap_or(0);
        axes.push(json!({
            "name": s("m_Name"),
            "negative": s("negativeButton"),
            "positive": s("positiveButton"),
            "altNegative": s("altNegativeButton"),
            "altPositive": s("altPositiveButton"),
            "gravity": f("gravity"),
            "dead": f("dead"),
            "sensitivity": f("sensitivity"),
            "snap": i("snap") != 0,
            "invert": i("invert") != 0,
            "type": i("type"),
            "axis": i("axis"),
        }));
    }
    axes
}

/// `executionOrder` of every script `.meta` (MonoImporter), by script GUID.
fn execution_orders(graph: &unbound_project::Graph) -> BTreeMap<String, i64> {
    let mut orders = BTreeMap::new();
    for assembly in &graph.assemblies {
        for source in &assembly.sources {
            let meta = format!("{}.meta", source.file.display());
            let Ok(text) = std::fs::read_to_string(&meta) else {
                continue;
            };
            let Some(order) = text
                .lines()
                .find_map(|l| l.trim().strip_prefix("executionOrder:"))
                .and_then(|v| v.trim().parse::<i64>().ok())
            else {
                continue;
            };
            if order != 0
                && let Some(guid) = meta_guid(&source.file)
            {
                orders.insert(guid, order);
            }
        }
    }
    orders
}
