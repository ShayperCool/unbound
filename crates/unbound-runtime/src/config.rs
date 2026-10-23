//! Preparing a game to start: compile its code, make the module facades, and describe the project
//! to the managed engine (`GameConfig` in managed/Unbound.Host/Game.cs).
use anyhow::{Context, Result, bail};
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
    let request = project.compile_request(&graph, options.mode, &options.target, false);
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
            .iter()
            .filter_map(|s| {
                let stem = s.file.file_stem()?.to_str()?.to_string();
                Some(json!({ "fileName": stem, "guid": meta_guid(&s.file)? }))
            })
            .collect();
        assemblies.push(json!({
            "name": result.name,
            "dll": result.dll,
            "pdb": result.pdb,
            "scripts": scripts,
        }));
    }
    for plugin in &plugins {
        let Some(name) = plugin.file_stem().and_then(|s| s.to_str()) else {
            continue;
        };
        assemblies.push(json!({
            "name": name,
            "dll": plugin,
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
        "projectPath": project.root,
        "unityVersion": project.version.to_string(),
        "productName": product,
        "companyName": company,
        "identifier": format!("com.{}.{}", company.replace(' ', ""), product.replace(' ', "")),
        "bundleVersion": "1.0",
        "persistentDataPath": persistent,
        "facadeDir": facades.output_dir,
        "firstScene": first_scene,
        "buildScenes": build_scenes,
        "assemblies": assemblies,
        "tags": project_tags(project),
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
        "unityDataPath": project.install.data,
        "frameworkDir": project.install.data.join("MonoBleedingEdge/lib/mono/4.5"),
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
