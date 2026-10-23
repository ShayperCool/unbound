use crate::ProjectArgs;
use anyhow::{Context, Result};
use serde::Deserialize;
use serde_json::{Value, json};
use std::{
    collections::{BTreeMap, BTreeSet},
    path::{Path, PathBuf},
    time::Instant,
};
use unbound_compat::CompileMode;
use unbound_project::Project;

fn open(args: &ProjectArgs) -> Result<Project> {
    Project::open(&args.project)
}

fn mode(args: &ProjectArgs) -> CompileMode {
    if args.player {
        CompileMode::Player
    } else {
        CompileMode::EditorPlayMode
    }
}

pub fn info(args: &ProjectArgs) -> Result<()> {
    let start = Instant::now();
    let project = open(args)?;
    println!("project   {}", project.root.display());
    println!("unity     {}", project.version);
    println!(
        "profile   {}{}",
        project.profile.data.family,
        if project.profile.approximate {
            " (nearest)"
        } else {
            ""
        }
    );
    println!("install   {}", project.install.data.display());
    println!(
        "packages  {} resolved, {} missing",
        project.packages.len(),
        project.missing_packages.len()
    );
    for (name, version, source) in &project.missing_packages {
        println!("  missing {name} {version} ({source})");
    }
    let db = project.asset_db();
    println!("assets    {} ({} problems)", db.len(), db.problems.len());
    for problem in db.problems.iter().take(10) {
        println!("  {problem}");
    }
    println!(
        "scenes    {}",
        project
            .settings
            .build_scenes
            .iter()
            .map(|s| s.path.as_str())
            .collect::<Vec<_>>()
            .join(", ")
    );
    println!("took      {:.2?}", start.elapsed());
    Ok(())
}

pub fn graph(args: &ProjectArgs, as_json: bool, compare_bee: Option<&Path>) -> Result<()> {
    let start = Instant::now();
    let project = open(args)?;
    let graph = project.script_graph(mode(args), &args.target)?;
    let took = start.elapsed();
    for warning in &graph.warnings {
        tracing::warn!("{warning}");
    }
    if as_json {
        let list: Vec<Value> = graph
            .assemblies
            .iter()
            .map(|a| {
                json!({
                    "name": a.name,
                    "editorOnly": a.editor_only,
                    "sources": a.sources.iter().map(|s| &s.path).collect::<Vec<_>>(),
                    "defines": a.defines,
                    "references": a.references,
                    "plugins": a.plugins,
                })
            })
            .collect();
        println!("{}", serde_json::to_string_pretty(&list)?);
    } else if compare_bee.is_none() {
        for a in &graph.assemblies {
            println!(
                "{:<60} {:>5} files {:>3} refs {:>3} plugins{}",
                a.name,
                a.sources.len(),
                a.references.len(),
                a.plugins.len(),
                if a.editor_only { "  [editor]" } else { "" }
            );
        }
        let files: usize = graph.assemblies.iter().map(|a| a.sources.len()).sum();
        println!(
            "{} assemblies, {files} files in {took:.2?}",
            graph.assemblies.len()
        );
    }
    if let Some(bee) = compare_bee {
        compare_with_bee(&project, &graph, bee)?;
    }
    Ok(())
}

/// Reports every difference between our graph and the Editor's own record of the same project.
fn compare_with_bee(project: &Project, graph: &unbound_project::Graph, bee: &Path) -> Result<()> {
    let data: Value =
        serde_json::from_slice(&std::fs::read(bee).context("reading the Bee record")?)?;
    let data = &data["ScriptCompilationBuildProgram.Data.ScriptCompilationData"];
    let assemblies = data["Assemblies"].as_array().context("no Assemblies")?;
    let names: Vec<&str> = assemblies
        .iter()
        .map(|a| a["Name"].as_str().unwrap_or(""))
        .collect();
    let theirs: BTreeMap<&str, &Value> = names.iter().copied().zip(assemblies.iter()).collect();
    let ours: BTreeMap<&str, &unbound_project::ScriptAssembly> = graph
        .assemblies
        .iter()
        .map(|a| (a.name.as_str(), a))
        .collect();

    let mut differences = 0;
    for name in theirs.keys() {
        if !ours.contains_key(name) {
            println!("only in Unity: {name}");
            differences += 1;
        }
    }
    for name in ours.keys() {
        if !theirs.contains_key(name) {
            println!("only in Unbound: {name}");
            differences += 1;
        }
    }
    let file_name = |p: &str| p.rsplit('/').next().unwrap_or(p).to_string();
    for (name, unity) in &theirs {
        let Some(ours) = ours.get(name) else { continue };
        let their_sources: BTreeSet<String> = unity["SourceFiles"]
            .as_array()
            .into_iter()
            .flatten()
            .filter_map(Value::as_str)
            .map(String::from)
            .collect();
        let our_sources: BTreeSet<String> = ours.sources.iter().map(|s| s.path.clone()).collect();
        report(
            name,
            "source",
            &their_sources,
            &our_sources,
            &mut differences,
        );

        let their_defines: BTreeSet<String> = unity["Defines"]
            .as_array()
            .into_iter()
            .flatten()
            .filter_map(Value::as_str)
            .map(String::from)
            .collect();
        let our_defines: BTreeSet<String> = ours.defines.iter().cloned().collect();
        report(
            name,
            "define",
            &their_defines,
            &our_defines,
            &mut differences,
        );

        let their_refs: BTreeSet<String> = unity["References"]
            .as_array()
            .into_iter()
            .flatten()
            .filter_map(|i| names.get(i.as_u64()? as usize).map(|s| s.to_string()))
            .collect();
        let our_refs: BTreeSet<String> = ours.references.iter().cloned().collect();
        report(name, "reference", &their_refs, &our_refs, &mut differences);

        // Plugins: Unity lists engine and BCL references too; compare the project's DLLs only.
        let root = project.root.to_string_lossy().to_string();
        let their_plugins: BTreeSet<String> = unity["PrebuiltReferences"]
            .as_array()
            .into_iter()
            .flatten()
            .filter_map(Value::as_str)
            .filter(|p| {
                p.starts_with("Assets/") || p.starts_with("Packages/") || p.starts_with(&root)
            })
            .map(file_name)
            .collect();
        let our_plugins: BTreeSet<String> = ours
            .plugins
            .iter()
            .map(|p| file_name(&p.to_string_lossy()))
            .collect();
        report(
            name,
            "plugin",
            &their_plugins,
            &our_plugins,
            &mut differences,
        );
    }
    println!(
        "{} assemblies in Unity, {} in Unbound, {differences} differences",
        theirs.len(),
        ours.len()
    );
    Ok(())
}

fn report(
    name: &str,
    what: &str,
    theirs: &BTreeSet<String>,
    ours: &BTreeSet<String>,
    count: &mut usize,
) {
    let missing: Vec<_> = theirs.difference(ours).collect();
    let extra: Vec<_> = ours.difference(theirs).collect();
    if !missing.is_empty() {
        *count += 1;
        println!(
            "{name}: {} {what}s only in Unity: {:?}",
            missing.len(),
            missing.iter().take(8).collect::<Vec<_>>()
        );
    }
    if !extra.is_empty() {
        *count += 1;
        println!(
            "{name}: {} {what}s only in Unbound: {:?}",
            extra.len(),
            extra.iter().take(8).collect::<Vec<_>>()
        );
    }
}

pub fn compile(args: &ProjectArgs, editor_assemblies: bool, repeat: u32) -> Result<()> {
    let start = Instant::now();
    let project = open(args)?;
    let graph = project.script_graph(mode(args), &args.target)?;
    let request = project.compile_request(&graph, mode(args), &args.target, editor_assemblies);
    let prepared = start.elapsed();
    let runtime = unbound_host::ManagedRuntime::start()?;
    let started = start.elapsed();
    println!(
        "{} assemblies; graph {prepared:.2?}, runtime start {:.2?}",
        request.assemblies.len(),
        started - prepared
    );
    let mut failed = 0;
    for round in 0..repeat.max(1) {
        let before = Instant::now();
        let response: unbound_project::compile::CompileResponse =
            runtime.call("compile", &request)?;
        failed = response.failed;
        for assembly in &response.assemblies {
            if assembly.status == "failed" {
                println!(
                    "FAILED {} ({} errors)",
                    assembly.name,
                    assembly.errors.len()
                );
                for error in assembly.errors.iter().take(10) {
                    println!("  {error}");
                }
            } else if assembly.status == "skipped" {
                println!(
                    "skipped {}: {}",
                    assembly.name,
                    assembly
                        .errors
                        .first()
                        .map(|e| e.message.as_str())
                        .unwrap_or("")
                );
            }
        }
        let mut slowest: Vec<_> = response
            .assemblies
            .iter()
            .filter(|a| a.status == "compiled")
            .collect();
        slowest.sort_by_key(|a| std::cmp::Reverse(a.milliseconds));
        println!(
            "round {}: {} compiled, {} cached, {} failed in {:.2?} (slowest: {})",
            round + 1,
            response.compiled,
            response.cached,
            response.failed,
            before.elapsed(),
            slowest
                .iter()
                .take(3)
                .map(|a| format!("{} {}ms", a.name, a.milliseconds))
                .collect::<Vec<_>>()
                .join(", ")
        );
    }
    if failed > 0 {
        anyhow::bail!("{failed} assemblies failed to compile");
    }
    Ok(())
}

/// Where a Unity release's module facades are kept: they depend on the release alone, so they live
/// in the global cache, `<cache>/facades/<unity-version>/`.
fn facades_dir(project: &Project) -> PathBuf {
    unbound_project::cache_root()
        .join("facades")
        .join(project.version.to_string())
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct FacadeReport {
    output_dir: PathBuf,
    reused: bool,
    assemblies: u32,
    types: u32,
    milliseconds: u64,
}

pub fn facades(args: &ProjectArgs) -> Result<()> {
    let start = Instant::now();
    let project = open(args)?;
    let runtime = unbound_host::ManagedRuntime::start()?;
    let report: FacadeReport = runtime.call(
        "facades",
        &json!({
            "unityManagedEngineDir": project.install.managed_engine_dir(),
            "outputDir": facades_dir(&project),
        }),
    )?;
    println!("unity     {}", project.version);
    println!("facades   {}", report.output_dir.display());
    println!(
        "{} {} facades forwarding {} types in {}ms (total {:.2?})",
        if report.reused { "reused" } else { "generated" },
        report.assemblies,
        report.types,
        report.milliseconds,
        start.elapsed()
    );
    Ok(())
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct GapReport {
    assemblies: Vec<String>,
    skipped: Vec<String>,
    types: Coverage,
    members: Coverage,
    missing_types: Vec<MissingType>,
    missing_members: Vec<MissingMemberGroup>,
    milliseconds: u64,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Coverage {
    referenced: u64,
    satisfied: u64,
    uses: u64,
    satisfied_uses: u64,
}

impl std::fmt::Display for Coverage {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        let percent = |part: u64, whole: u64| {
            if whole == 0 {
                100.0
            } else {
                100.0 * part as f64 / whole as f64
            }
        };
        write!(
            f,
            "{:.1}% ({}/{}), {:.1}% of uses ({}/{})",
            percent(self.satisfied, self.referenced),
            self.satisfied,
            self.referenced,
            percent(self.satisfied_uses, self.uses),
            self.satisfied_uses,
            self.uses
        )
    }
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct MissingType {
    name: String,
    module: String,
    uses: u64,
    assemblies: Vec<String>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct MissingMemberGroup {
    #[serde(rename = "type")]
    type_name: String,
    members: Vec<MissingMember>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct MissingMember {
    kind: String,
    signature: String,
    uses: u64,
    assemblies: Vec<String>,
}

/// `A, B +3`: the first assemblies using an item.
fn users(assemblies: &[String]) -> String {
    let shown = assemblies
        .iter()
        .take(2)
        .map(String::as_str)
        .collect::<Vec<_>>()
        .join(", ");
    if assemblies.len() > 2 {
        format!("{shown} +{}", assemblies.len() - 2)
    } else {
        shown
    }
}

pub fn api_gaps(args: &ProjectArgs, top: usize, as_json: bool) -> Result<()> {
    let start = Instant::now();
    let project = open(args)?;
    let graph = project.script_graph(mode(args), &args.target)?;
    for warning in &graph.warnings {
        tracing::warn!("{warning}");
    }
    // The runtime assemblies, compiled as `compile` does (cached, so usually instant).
    let request = project.compile_request(&graph, mode(args), &args.target, false);
    let runtime = unbound_host::ManagedRuntime::start()?;
    let compiled: unbound_project::compile::CompileResponse = runtime.call("compile", &request)?;
    for assembly in &compiled.assemblies {
        if assembly.dll.is_none() {
            tracing::warn!(
                "{} did not compile ({}); its code is not analyzed",
                assembly.name,
                assembly.status
            );
        }
    }
    let mut game: BTreeSet<PathBuf> = compiled
        .assemblies
        .iter()
        .filter_map(|a| a.dll.clone())
        .collect();
    // The precompiled plugins those assemblies reference run too, and call the engine as well.
    for assembly in &request.assemblies {
        game.extend(assembly.plugins.iter().cloned());
    }
    let implementation = unbound_host::managed_dir()?.join("UnityEngine.CoreModule.dll");
    let gaps_request = json!({
        "gameAssemblies": game,
        "implAssembly": implementation,
        "unityManagedEngineDir": project.install.managed_engine_dir(),
    });
    let response = runtime.call_raw("api-gaps", &serde_json::to_vec(&gaps_request)?)?;
    if as_json {
        let report: Value = serde_json::from_slice(&response)?;
        println!("{}", serde_json::to_string_pretty(&report)?);
        return Ok(());
    }
    let report: GapReport =
        serde_json::from_slice(&response).context("decoding the api-gaps report")?;

    println!(
        "{} assemblies analyzed in {}ms ({} skipped); total {:.2?}",
        report.assemblies.len(),
        report.milliseconds,
        report.skipped.len(),
        start.elapsed()
    );
    for skipped in &report.skipped {
        println!("  skipped {skipped}");
    }

    println!(
        "\nmissing types: top {} of {}",
        top.min(report.missing_types.len()),
        report.missing_types.len()
    );
    for t in report.missing_types.iter().take(top) {
        println!(
            "{:>7}  {:<60} {:<32} {}",
            t.uses,
            t.name,
            t.module,
            users(&t.assemblies)
        );
    }

    let mut members: Vec<(&str, &MissingMember)> = report
        .missing_members
        .iter()
        .flat_map(|g| g.members.iter().map(move |m| (g.type_name.as_str(), m)))
        .collect();
    members.sort_by_key(|m| std::cmp::Reverse(m.1.uses));
    println!(
        "\nmissing members: top {} of {} (in {} types)",
        top.min(members.len()),
        members.len(),
        report.missing_members.len()
    );
    for (type_name, m) in members.iter().take(top) {
        let kind = if m.kind == "method" {
            String::new()
        } else {
            format!(" [{}]", m.kind)
        };
        println!(
            "{:>7}  {type_name}::{}{kind}  ({})",
            m.uses,
            m.signature,
            users(&m.assemblies)
        );
    }

    println!(
        "\ncoverage: members {}; types {}",
        report.members, report.types
    );
    Ok(())
}

fn parse_size(size: &str) -> Result<(i32, i32)> {
    let (w, h) = size.split_once('x').context("size is WIDTHxHEIGHT")?;
    Ok((w.trim().parse()?, h.trim().parse()?))
}

pub struct RunArgs {
    pub scene: Option<String>,
    pub headless: Option<u32>,
    pub size: String,
    pub simulate_touch: bool,
    pub screenshot: Option<std::path::PathBuf>,
    pub frames: Option<u32>,
}

pub fn run(args: &ProjectArgs, run: RunArgs) -> Result<()> {
    let project = open(args)?;
    let (width, height) = parse_size(&run.size)?;
    let options = unbound_runtime::GameOptions {
        target: args.target.clone(),
        mode: mode(args),
        scene: run.scene,
        simulate_touch: run.simulate_touch,
    };
    if let Some(frames) = run.headless {
        let mut game = unbound_runtime::Game::start(&project, &options)?;
        game.run_headless(frames, width, height);
        return Ok(());
    }
    // The window: the renderer is installed before the game starts so the first scene's
    // uploads reach it.
    let backend = std::sync::Arc::new(unbound_app::backend::QueuedBackend::new());
    let game = unbound_runtime::Game::start_with_backend(&project, &options, Some(backend.clone()))?;
    let title = if project.settings.product_name.is_empty() {
        "Unbound".to_string()
    } else {
        project.settings.product_name.clone()
    };
    let mut shader_roots = vec![project.assets.clone()];
    shader_roots.extend(project.packages.iter().map(|p| p.root.clone()));
    let frames = match (run.frames, &run.screenshot) {
        (None, Some(_)) => Some(5),
        (frames, _) => frames,
    };
    unbound_app::run(
        Box::new(game),
        backend,
        unbound_app::AppOptions {
            title,
            width: width.max(1) as u32,
            height: height.max(1) as u32,
            screenshot: run.screenshot,
            frames,
            shader_roots,
        },
    )
}
