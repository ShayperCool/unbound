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

/// How shaders compile and where they are: the project, its packages, then Unity's built-in
/// shaders (downloaded once; without them, built-in shaders draw with the error shader). Programs
/// are cached under the global cache.
fn shader_setup(project: &Project) -> (unbound_shaders::Settings, Vec<PathBuf>, Vec<PathBuf>) {
    let v = project.version;
    let mut project_roots = vec![project.assets.clone()];
    project_roots.extend(project.packages.iter().map(|p| p.root.clone()));
    let mut cg_includes = project.install.data.join("CGIncludes");
    let builtin_roots = match project.builtin_shaders() {
        Ok(builtin) => {
            if !cg_includes.join("UnityCG.cginc").is_file() {
                cg_includes = builtin.cg_includes();
            }
            builtin.shader_roots()
        }
        Err(e) => {
            tracing::warn!(
                "Unity's built-in shaders are not available, so materials using them draw with the error shader: {e:#}"
            );
            Vec::new()
        }
    };
    let settings = unbound_shaders::Settings {
        includes: unbound_shaders::Includes {
            project: project.root.clone(),
            packages: project
                .packages
                .iter()
                .map(|p| (p.name.clone(), p.root.clone()))
                .collect(),
            cg_includes: Some(cg_includes),
        },
        // `UNITY_VERSION`: the release's numbers written one after another (6000.0.71: 6000071).
        unity_version: format!("{}{}{}", v.major, v.minor, v.patch)
            .parse()
            .unwrap_or(0),
        cache_dir: Some(unbound_project::cache_root().join("shaders")),
    };
    (settings, project_roots, builtin_roots)
}

pub fn shaders(args: &ProjectArgs, builtin: bool) -> Result<()> {
    let start = Instant::now();
    let project = open(args)?;
    let (settings, project_roots, builtin_roots) = shader_setup(&project);
    let names = if builtin {
        let mut all = unbound_app::shaderlab::ShaderIndex::scan(&project_roots).names();
        all.extend(unbound_app::shaderlab::ShaderIndex::scan(&builtin_roots).names());
        all.sort();
        all.dedup();
        all
    } else {
        unbound_app::shaderlab::ShaderIndex::scan(&project_roots).names()
    };
    let mut roots = project_roots;
    roots.extend(builtin_roots);
    let index = unbound_app::shaderlab::ShaderIndex::scan(&roots);
    let mut resolver = unbound_app::materials::Resolver::new(index);
    let mut compiler = unbound_shaders::ShaderCompiler::new(settings)?;
    let (mut good, mut bad) = (0, 0);
    for name in &names {
        let material = unbound_runtime::render_protocol::owned::Material {
            shader: name.clone(),
            ..Default::default()
        };
        let resolved = resolver.resolve(&material);
        if resolved.shader != *name {
            println!("fallback  {name}: draws as `{}`", resolved.shader);
        }
        if resolved.passes.is_empty() {
            println!("nothing   {name}: no pass draws in forward rendering");
            continue;
        }
        // The variant a material without keywords draws, per pass.
        let mut failures = Vec::new();
        let mut summary = Vec::new();
        for (i, pass) in resolved.passes.iter().enumerate() {
            match compiler.compile(&pass.program, &[], false) {
                Ok(program) => summary.push(format!(
                    "{} uniforms, {} textures",
                    program.uniforms.len(),
                    program.textures.len() + program.main_texture as usize
                )),
                Err(e) => {
                    // glslang's messages follow on their own lines.
                    let reason = format!("{e:#}").replace('\n', "\n            ");
                    failures.push(format!("pass {i}: {reason}"));
                }
            }
        }
        if failures.is_empty() {
            good += 1;
            println!("compiled  {name} ({})", summary.join("; "));
        } else {
            bad += 1;
            println!("FAILED    {name}: {}", failures.join("; "));
        }
    }
    println!(
        "{} shaders: {good} compile, {bad} do not ({:.2?})",
        names.len(),
        start.elapsed()
    );
    Ok(())
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

        // Analyzers: the Editor's own source generators come from the profile; compare the
        // project's (package and Assets) ones.
        let their_analyzers: BTreeSet<String> = unity["Analyzers"]
            .as_array()
            .into_iter()
            .flatten()
            .filter_map(Value::as_str)
            .filter(|p| {
                p.starts_with("Assets/") || p.starts_with("Packages/") || p.starts_with("Library/") || p.starts_with(&root)
            })
            .map(file_name)
            .collect();
        let our_analyzers: BTreeSet<String> = ours
            .analyzers
            .iter()
            .map(|p| file_name(&p.to_string_lossy()))
            .collect();
        report(
            name,
            "analyzer",
            &their_analyzers,
            &our_analyzers,
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
    let request = project.compile_request(&graph, mode(args), editor_assemblies);
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
            "platformModules": project.install.platform_modules(),
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
struct StubReport {
    output_dir: PathBuf,
    modules: Vec<StubModuleReport>,
    new_types: u64,
    extended_types: u64,
    members: u64,
    skipped: Vec<String>,
    conflicts: Vec<String>,
    changed_files: u64,
    milliseconds: u64,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct StubModuleReport {
    name: String,
    new_types: u64,
    extended_types: u64,
    members: u64,
}

/// Regenerates the release's compatibility stubs: builds the hand-written implementation alone
/// (`-p:UnboundNoStubs=true`, into `target/managed-nostubs`), then has the managed generator write
/// what Unity's modules have and it lacks into `managed/UnityEngine/Generated/<family>/`.
/// The hand-written implementation built alone, for `gen-stubs` and `api-check`.
struct NoStubsBuild {
    /// `managed/UnityEngine`
    source: PathBuf,
    /// Its `UnityEngine.CoreModule.dll` built with `-p:UnboundNoStubs=true`.
    dll: PathBuf,
}

/// Builds `managed/UnityEngine` without stubs (`-p:UnboundNoStubs=true`, own intermediate
/// directory so the normal build stays incremental) into `target/managed-nostubs`.
fn build_without_stubs(family: &str) -> Result<NoStubsBuild> {
    let source = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../managed/UnityEngine")
        .canonicalize()
        .context("the managed/UnityEngine sources (this command runs from a Unbound checkout)")?;
    let target = unbound_host::managed_dir()?
        .canonicalize()?
        .parent()
        .context("the managed engine directory has no parent")?
        .to_path_buf();
    let no_stubs = target.join("managed-nostubs");
    let status = std::process::Command::new(unbound_host::dotnet_root()?.join("dotnet"))
        .args([
            "build",
            "-c",
            "Debug",
            "-v",
            "q",
            "-nologo",
            "-clp:ErrorsOnly",
        ])
        .arg("-p:UnboundNoStubs=true")
        .arg(format!("-p:UnboundStubFamily={family}"))
        // Its own intermediate directory, so the normal build stays incremental.
        .arg(format!(
            "-p:IntermediateOutputPath={}/",
            target
                .join("managed-build/obj/UnityEngine-nostubs")
                .display()
        ))
        .arg("-o")
        .arg(&no_stubs)
        .arg(source.join("UnityEngine.csproj"))
        .status()
        .context("running dotnet build")?;
    if !status.success() {
        anyhow::bail!("building the implementation without stubs failed");
    }
    Ok(NoStubsBuild {
        dll: no_stubs.join("UnityEngine.CoreModule.dll"),
        source,
    })
}

pub fn gen_stubs(args: &ProjectArgs) -> Result<()> {
    let start = Instant::now();
    let project = open(args)?;
    let family = project.profile.data.family.clone();
    let NoStubsBuild { source, dll } = build_without_stubs(&family)?;
    let built = start.elapsed();

    let runtime = unbound_host::ManagedRuntime::start()?;
    let report: StubReport = runtime.call(
        "gen-stubs",
        &json!({
            "unityManagedEngineDir": project.install.managed_engine_dir(),
            "platformModules": project.install.platform_modules(),
            "implAssembly": dll,
            "outputDir": source.join("Generated").join(&family),
            "implSourceDir": source,
        }),
    )?;
    println!(
        "{:<48} {:>6} {:>8} {:>8}",
        "module", "types", "extended", "members"
    );
    for module in &report.modules {
        println!(
            "{:<48} {:>6} {:>8} {:>8}",
            module.name, module.new_types, module.extended_types, module.members
        );
    }
    println!(
        "{} new types, {} hand-written types extended, {} members; {} files changed in {}",
        report.new_types,
        report.extended_types,
        report.members,
        report.changed_files,
        report.output_dir.display()
    );
    println!(
        "{} skipped (signatures naming types from assemblies the modules do not ship):",
        report.skipped.len()
    );
    for skipped in report.skipped.iter().take(10) {
        println!("  {skipped}");
    }
    println!(
        "{} conflicts with hand-written code:",
        report.conflicts.len()
    );
    for conflict in &report.conflicts {
        println!("  {conflict}");
    }
    println!(
        "built without stubs in {built:.2?}, generated in {}ms; total {:.2?}.\nRebuild with `cargo build`; run `unbound api-check` to find hand-written signatures that differ from Unity's (they suppress stubs).",
        report.milliseconds,
        start.elapsed()
    );
    Ok(())
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct ApiCheckReport {
    errors: Vec<ApiCheckError>,
    checked_types: u64,
    milliseconds: u64,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct ApiCheckError {
    category: String,
    #[serde(rename = "type")]
    type_name: String,
    message: String,
}

/// Checks the hand-written implementation against the release's Unity modules: members named like
/// Unity's with other signatures, public types Unity does not have, enum values, accessibility and
/// struct layouts. Fails (for CI) when there are errors.
pub fn api_check(args: &ProjectArgs) -> Result<()> {
    let start = Instant::now();
    let project = open(args)?;
    let NoStubsBuild { dll, .. } = build_without_stubs(&project.profile.data.family)?;
    let runtime = unbound_host::ManagedRuntime::start()?;
    let report: ApiCheckReport = runtime.call(
        "api-check",
        &json!({
            "unityManagedEngineDir": project.install.managed_engine_dir(),
            "platformModules": project.install.platform_modules(),
            "implAssembly": dll,
        }),
    )?;
    let mut by_category: BTreeMap<&str, BTreeMap<&str, Vec<&str>>> = BTreeMap::new();
    for error in &report.errors {
        by_category
            .entry(&error.category)
            .or_default()
            .entry(&error.type_name)
            .or_default()
            .push(&error.message);
    }
    for (category, types) in &by_category {
        let count: usize = types.values().map(Vec::len).sum();
        println!("{category} ({count})");
        for (type_name, messages) in types {
            println!("  {type_name}");
            for message in messages {
                println!("    {message}");
            }
        }
    }
    println!(
        "{} errors in {} hand-written types checked against Unity {} ({}ms; total {:.2?})",
        report.errors.len(),
        report.checked_types,
        project.version,
        report.milliseconds,
        start.elapsed()
    );
    if !report.errors.is_empty() {
        anyhow::bail!(
            "{} hand-written API errors: fix them to match Unity's signatures exactly",
            report.errors.len()
        );
    }
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
    /// Missing items that exist as generated stubs.
    #[serde(default)]
    stubbed: u64,
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
            "{:.1}% ({}/{}), {:.1}% of uses ({}/{}), {} of the missing stubbed",
            percent(self.satisfied, self.referenced),
            self.satisfied,
            self.referenced,
            percent(self.satisfied_uses, self.uses),
            self.satisfied_uses,
            self.uses,
            self.stubbed
        )
    }
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct MissingType {
    name: String,
    module: String,
    #[serde(default)]
    stub: bool,
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
    #[serde(default)]
    stub: bool,
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
    let request = project.compile_request(&graph, mode(args), false);
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
        "platformModules": project.install.platform_modules(),
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
            "{:>7}  {:<60} {:<32} {}{}",
            t.uses,
            t.name,
            t.module,
            users(&t.assemblies),
            if t.stub { "  (stub)" } else { "" }
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
        let mut kind = if m.kind == "method" {
            String::new()
        } else {
            format!(" [{}]", m.kind)
        };
        if m.stub {
            kind.push_str(" (stub)");
        }
        println!(
            "{:>7}  {type_name}::{}{kind}  ({})",
            m.uses,
            m.signature,
            users(&m.assemblies)
        );
    }

    // Stubs count as missing: they exist so that code binds, but do nothing yet.
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
    pub size: Option<String>,
    pub simulate_touch: bool,
    pub screenshot: Option<std::path::PathBuf>,
    pub frames: Option<u32>,
    /// Mid-run screenshots, `FRAME:PATH`.
    pub snapshots: Vec<String>,
    /// Fixed game time per frame, as frames per second.
    pub fixed_fps: Option<f64>,
    /// Hot-reload edits made while the game runs.
    pub watch: bool,
    /// Scripted taps, `FRAME:X,Y`.
    pub taps: Vec<String>,
    /// Scripted typing, `FRAME:TEXT`.
    pub typing: Vec<String>,
    /// Scripted pointer moves, `FRAME:X,Y`.
    pub moves: Vec<String>,
    /// Scripted key and button presses and releases, `FRAME:KEY`.
    pub presses: Vec<String>,
    pub releases: Vec<String>,
    /// Scripted wheel turns, `FRAME:DY` or `FRAME:DX,DY`.
    pub scrolls: Vec<String>,
    /// Remote play: the listener's address.
    pub remote: Option<String>,
    /// `webrtc` or `websocket`.
    pub remote_transport: String,
}

fn parse_taps(taps: &[String]) -> Result<Vec<(u64, f32, f32)>> {
    taps.iter()
        .map(|tap| {
            let (frame, at) = tap
                .split_once(':')
                .with_context(|| format!("--tap {tap}: expected FRAME:X,Y"))?;
            let (x, y) = at
                .split_once(',')
                .with_context(|| format!("--tap {tap}: expected FRAME:X,Y"))?;
            Ok((frame.trim().parse()?, x.trim().parse()?, y.trim().parse()?))
        })
        .collect()
}

/// The run's scripted input changes, in frame order (at one frame: moves, presses, releases,
/// scrolls).
fn parse_script(run: &RunArgs) -> Result<Vec<(u64, unbound_runtime::ScriptedInput)>> {
    let mut script = Vec::new();
    for (kind, entries) in [
        ("move", &run.moves),
        ("press", &run.presses),
        ("release", &run.releases),
        ("scroll", &run.scrolls),
    ] {
        for entry in entries {
            script.push(unbound_runtime::ScriptedInput::parse(kind, entry)?);
        }
    }
    script.sort_by_key(|(frame, _)| *frame);
    Ok(script)
}

fn parse_snapshots(snapshots: &[String]) -> Result<Vec<(u32, std::path::PathBuf)>> {
    snapshots
        .iter()
        .map(|entry| {
            let (frame, path) = entry
                .split_once(':')
                .with_context(|| format!("--snapshot {entry}: expected FRAME:PATH"))?;
            Ok((frame.trim().parse()?, path.into()))
        })
        .collect()
}

fn parse_typing(typing: &[String]) -> Result<Vec<(u64, String)>> {
    typing
        .iter()
        .map(|entry| {
            let (frame, text) = entry
                .split_once(':')
                .with_context(|| format!("--type {entry}: expected FRAME:TEXT"))?;
            let text = text.replace("\\n", "\n").replace("\\b", "\u{8}");
            Ok((frame.trim().parse()?, text))
        })
        .collect()
}

pub struct TestArgs {
    pub platform: String,
    pub results: Option<PathBuf>,
    pub filter: Option<String>,
    pub category: Option<String>,
    pub assembly_names: Option<String>,
    pub unity_args: Vec<String>,
}

/// `unbound test`: the Editor starts in Edit mode with the command line `-runTests` gives it, so
/// the Test Framework package runs the tests itself (its `TestStarter` reads the command line),
/// then quits with its exit code.
pub fn test(args: &ProjectArgs, test: TestArgs) -> Result<()> {
    let platform = match test.platform.to_ascii_lowercase().as_str() {
        "editmode" | "editor" => "EditMode",
        "playmode" => "PlayMode",
        other => anyhow::bail!("--platform {other}: expected EditMode or PlayMode"),
    };
    let project = open(args)?;
    let results = match test.results {
        Some(path) if path.is_absolute() => path,
        Some(path) => std::env::current_dir()?.join(path),
        None => project
            .root
            .join("Library/Unbound/TestResults")
            .join(format!("{platform}.xml")),
    };
    if let Some(dir) = results.parent() {
        std::fs::create_dir_all(dir)?;
    }
    let mut command_line: Vec<String> = vec![
        project.install.data.join("../Unity").display().to_string(),
        "-batchmode".into(),
        "-projectPath".into(),
        project.root.display().to_string(),
        "-runTests".into(),
        "-testPlatform".into(),
        platform.into(),
        "-testResults".into(),
        results.display().to_string(),
    ];
    for (option, value) in [
        ("-testFilter", &test.filter),
        ("-testCategory", &test.category),
        ("-assemblyNames", &test.assembly_names),
    ] {
        if let Some(value) = value {
            command_line.push(option.into());
            command_line.push(value.clone());
        }
    }
    command_line.extend(test.unity_args);
    let options = unbound_runtime::GameOptions {
        target: args.target.clone(),
        mode: mode(args),
        scene: None,
        simulate_touch: false,
        edit_mode: true,
        command_line,
    };
    let (width, height) = project.settings.default_screen_for(&args.target);
    let mut game = unbound_runtime::Game::start(&project, &options)?;
    game.run_headless(u32::MAX, width, height);
    let code = game.exit_code().unwrap_or(3);
    drop(game);
    println!("test results: {}", results.display());
    std::process::exit(code);
}

pub fn run(args: &ProjectArgs, run: RunArgs) -> Result<()> {
    run_session(args, run)?;
    if unbound_runtime::restart_requested() {
        return relaunch();
    }
    Ok(())
}

/// Starts this command again in place of the process: an edit stopped the game because it could
/// not be patched in, and the new session compiles and plays the edited code (the compile cache
/// keeps the restart short).
fn relaunch() -> Result<()> {
    let exe = std::env::current_exe()?;
    let args: Vec<_> = std::env::args_os().skip(1).collect();
    tracing::info!("restarting the game with the edited code");
    #[cfg(unix)]
    {
        use std::os::unix::process::CommandExt;
        let error = std::process::Command::new(exe).args(args).exec();
        Err(error.into())
    }
    #[cfg(not(unix))]
    {
        let status = std::process::Command::new(exe).args(args).status()?;
        std::process::exit(status.code().unwrap_or(1));
    }
}

fn run_session(args: &ProjectArgs, run: RunArgs) -> Result<()> {
    if let Some(fps) = run.fixed_fps
        && !(fps.is_finite() && fps > 0.0)
    {
        anyhow::bail!("--fixed-fps {fps}: expected a positive number of frames per second");
    }
    let script = parse_script(&run)?;
    let project = open(args)?;
    let (width, height) = match &run.size {
        Some(size) => parse_size(size)?,
        None => project.settings.default_screen_for(&args.target),
    };
    let options = unbound_runtime::GameOptions {
        target: args.target.clone(),
        mode: mode(args),
        scene: run.scene,
        simulate_touch: run.simulate_touch,
        edit_mode: false,
        command_line: Vec::new(),
    };
    if let Some(frames) = run.headless {
        if !run.snapshots.is_empty() {
            anyhow::bail!("--snapshot needs a window; it cannot be used with --headless");
        }
        let mut game = unbound_runtime::Game::start(&project, &options)?;
        if run.watch {
            game.watch()?;
        }
        game.script_taps(parse_taps(&run.taps)?);
        game.script_typing(parse_typing(&run.typing)?);
        game.script_input(script.clone());
        if let Some(fps) = run.fixed_fps {
            game.fix_frame_rate(fps);
        }
        game.run_headless(frames, width, height);
        return Ok(());
    }
    // Remote play: the session is the audio output (installed before the game opens it).
    let remote = match &run.remote {
        Some(listen) => {
            let options = unbound_remote::Options {
                listen: listen
                    .parse()
                    .with_context(|| format!("--remote {listen}: expected ADDRESS:PORT"))?,
                transport: match run.remote_transport.as_str() {
                    "webrtc" => unbound_remote::Transport::WebRtc,
                    "websocket" => unbound_remote::Transport::WebSocket,
                    other => {
                        anyhow::bail!("--remote-transport {other}: expected webrtc or websocket")
                    }
                },
                ..Default::default()
            };
            let session = std::sync::Arc::new(
                unbound_remote::Session::start(options)
                    .map_err(|e| anyhow::anyhow!("remote play: {e}"))?,
            );
            unbound_runtime::audio::set_device_opener(std::sync::Arc::new(
                unbound_app::remote::RemoteAudio(session.clone()),
            ));
            let url = session.url();
            tracing::info!("remote play: open {url}");
            println!("\nremote play: {url}\n");
            Some(session)
        }
        None => None,
    };
    // The window: the renderer is installed before the game starts so the first scene's
    // uploads reach it.
    let backend = std::sync::Arc::new(unbound_app::backend::GpuBackend::new());
    let mut game =
        unbound_runtime::Game::start_with_backend(&project, &options, Some(backend.clone()))?;
    if run.watch {
        game.watch()?;
    }
    game.script_taps(parse_taps(&run.taps)?);
    game.script_typing(parse_typing(&run.typing)?);
    game.script_input(script);
    if let Some(fps) = run.fixed_fps {
        game.fix_frame_rate(fps);
    }
    let title = if project.settings.product_name.is_empty() {
        "Unbound".to_string()
    } else {
        project.settings.product_name.clone()
    };
    let (shader_compiler, mut shader_roots, builtin_roots) = shader_setup(&project);
    shader_roots.extend(builtin_roots);
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
            snapshots: parse_snapshots(&run.snapshots)?,
            shader_roots,
            shader_compiler: Some(shader_compiler),
            remote,
        },
    )
}
