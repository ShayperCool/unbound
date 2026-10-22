use crate::ProjectArgs;
use anyhow::{Context, Result};
use serde_json::{Value, json};
use std::{
    collections::{BTreeMap, BTreeSet},
    path::Path,
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
