//! The script assembly graph, derived the way the Editor derives it.
//!
//! * An `.asmdef` owns the `.cs` files of its folder and subfolders, up to folders that have an
//!   `.asmdef` or `.asmref` of their own; an `.asmref` adds its folder to the assembly it names.
//! * Scripts of `Assets/` outside every asmdef go to the predefined assemblies:
//!   `Assembly-CSharp-firstpass` (`Plugins`, `Standard Assets`, `Pro Standard Assets`),
//!   `Assembly-CSharp-Editor[-firstpass]` (an `Editor` folder) or `Assembly-CSharp`.
//!   Package scripts outside every asmdef are not compiled.
//! * An assembly builds for the Editor unless its platforms exclude it, and is dropped when its
//!   `defineConstraints` fail. Predefined assemblies reference every auto-referenced assembly;
//!   every assembly references the auto-referenced plugins unless `overrideReferences` is set.
use crate::{assetdb::is_hidden, plugins::Plugin, semver, settings::Settings};
use anyhow::{Context, Result, bail};
use serde::Deserialize;
use std::{
    collections::{BTreeMap, HashMap, HashSet},
    path::{Path, PathBuf},
};
use unbound_compat::{CompileMode, Feature, Profile};

#[derive(Debug, Clone, Deserialize, Default)]
#[serde(default, rename_all = "camelCase")]
pub struct AsmdefJson {
    pub name: String,
    pub root_namespace: String,
    pub references: Vec<String>,
    pub include_platforms: Vec<String>,
    pub exclude_platforms: Vec<String>,
    pub allow_unsafe_code: bool,
    pub override_references: bool,
    pub precompiled_references: Vec<String>,
    #[serde(default = "yes")]
    pub auto_referenced: bool,
    pub define_constraints: Vec<String>,
    pub version_defines: Vec<VersionDefine>,
    pub no_engine_references: bool,
    pub optional_unity_references: Vec<String>,
}

impl AsmdefJson {
    fn is_editor_only(&self) -> bool {
        self.include_platforms == ["Editor"]
    }

    /// A test assembly: Unity compiles those of packages only when the manifest lists the package
    /// in `testables`.
    fn is_test(&self) -> bool {
        self.optional_unity_references
            .iter()
            .any(|r| r == "TestAssemblies")
            || self
                .define_constraints
                .iter()
                .any(|c| c.trim() == "UNITY_INCLUDE_TESTS")
    }
}

fn yes() -> bool {
    true
}

#[derive(Debug, Clone, Deserialize, Default)]
#[serde(default)]
pub struct VersionDefine {
    pub name: String,
    pub expression: String,
    pub define: String,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub enum Predefined {
    FirstPass,
    EditorFirstPass,
    Main,
    Editor,
}

impl Predefined {
    pub fn name(self) -> &'static str {
        match self {
            Predefined::FirstPass => "Assembly-CSharp-firstpass",
            Predefined::EditorFirstPass => "Assembly-CSharp-Editor-firstpass",
            Predefined::Main => "Assembly-CSharp",
            Predefined::Editor => "Assembly-CSharp-Editor",
        }
    }

    fn is_editor(self) -> bool {
        matches!(self, Predefined::EditorFirstPass | Predefined::Editor)
    }
}

/// One assembly to compile.
#[derive(Debug, Clone)]
pub struct ScriptAssembly {
    pub name: String,
    /// The asmdef, or `None` for a predefined assembly.
    pub asmdef: Option<PathBuf>,
    pub predefined: Option<Predefined>,
    /// Source files, sorted by their Unity path.
    pub sources: Vec<Source>,
    pub defines: Vec<String>,
    /// Script assemblies referenced, by name.
    pub references: Vec<String>,
    /// Precompiled plugin DLLs referenced.
    pub plugins: Vec<PathBuf>,
    pub analyzers: Vec<PathBuf>,
    pub allow_unsafe: bool,
    pub no_engine_references: bool,
    /// Builds only for the Editor: Editor tooling, not loaded by the game.
    pub editor_only: bool,
    /// Compiles against the .NET Standard 2.1 class library.
    pub net_standard: bool,
    pub root_namespace: String,
}

#[derive(Debug, Clone)]
pub struct Source {
    pub file: PathBuf,
    /// `Assets/...` or `Packages/<name>/...`
    pub path: String,
}

/// A directory scanned for scripts: `Assets` or a package root.
pub struct ScriptRoot {
    pub dir: PathBuf,
    pub virtual_path: String,
    pub is_package: bool,
    /// Listed in the manifest's `testables`: its test assemblies build.
    pub testable: bool,
}

pub struct GraphInput<'a> {
    pub roots: &'a [ScriptRoot],
    pub profile: &'a Profile,
    pub settings: &'a Settings,
    pub mode: CompileMode,
    /// `Android`, `Standalone`, `iOS`, ...
    pub target_group: &'a str,
    /// Package name → version, for `versionDefines`.
    pub package_versions: &'a BTreeMap<String, String>,
    pub plugins: &'a [Plugin],
    /// GUID of every asmdef, for `GUID:` references.
    pub asmdef_guid: &'a dyn Fn(&Path) -> Option<String>,
    /// File names of the class library references; a plugin of the same name is not referenced.
    pub bcl_names: &'a HashSet<String>,
    /// The target's API compatibility level is .NET Standard 2.1 (runtime assemblies only; the
    /// Editor builds Editor-only assemblies against .NET Framework either way).
    pub net_standard: bool,
}

#[derive(Debug, Default)]
pub struct Graph {
    /// In dependency order: every assembly after the assemblies it references.
    pub assemblies: Vec<ScriptAssembly>,
    pub warnings: Vec<String>,
}

impl Graph {
    pub fn get(&self, name: &str) -> Option<&ScriptAssembly> {
        self.assemblies.iter().find(|a| a.name == name)
    }
}

struct Definition {
    json: AsmdefJson,
    file: PathBuf,
    guid: Option<String>,
    sources: Vec<Source>,
    /// From a package not listed in `testables`.
    untestable_package: bool,
}

/// The assemblies of the uGUI package were engine assemblies once; Unity still references them
/// from every assembly but those of the packages they depend on.
const UGUI_RUNTIME: &str = "UnityEngine.UI";
const UGUI_EDITOR: &str = "UnityEditor.UI";
const TEST_RUNTIME: &str = "UnityEngine.TestRunner";
const TEST_EDITOR: &str = "UnityEditor.TestRunner";
const NUNIT: &str = "nunit.framework.dll";

enum Owner {
    Asmdef(usize),
    /// An asmref, by the reference it names.
    Asmref(String),
}

pub fn build(input: &GraphInput<'_>) -> Result<Graph> {
    let mut graph = Graph::default();
    let mut definitions: Vec<Definition> = Vec::new();
    let mut owners: HashMap<PathBuf, Owner> = HashMap::new();
    let mut scripts: Vec<(Source, bool)> = Vec::new(); // (source, in a package)

    for root in input.roots {
        let walker = walkdir::WalkDir::new(&root.dir)
            .follow_links(true)
            .sort_by_file_name()
            .into_iter()
            .filter_entry(|e| e.depth() == 0 || !is_hidden(&e.file_name().to_string_lossy()));
        for entry in walker {
            let entry = entry?;
            if !entry.file_type().is_file() {
                continue;
            }
            let file = entry.path();
            let Some(ext) = file.extension().and_then(|e| e.to_str()) else {
                continue;
            };
            let dir = file.parent().unwrap().to_path_buf();
            match ext {
                "asmdef" => {
                    let text = std::fs::read_to_string(file)?;
                    let json: AsmdefJson = parse_lenient_json(&text)
                        .with_context(|| format!("reading {}", file.display()))?;
                    if owners.contains_key(&dir) {
                        graph.warnings.push(format!(
                            "{}: a folder can have one asmdef or asmref",
                            file.display()
                        ));
                        continue;
                    }
                    owners.insert(dir, Owner::Asmdef(definitions.len()));
                    definitions.push(Definition {
                        guid: (input.asmdef_guid)(file),
                        json,
                        file: file.to_path_buf(),
                        sources: Vec::new(),
                        untestable_package: root.is_package && !root.testable,
                    });
                }
                "asmref" => {
                    #[derive(Deserialize)]
                    struct Asmref {
                        reference: String,
                    }
                    let text = std::fs::read_to_string(file)?;
                    let asmref: Asmref = parse_lenient_json(&text)
                        .with_context(|| format!("reading {}", file.display()))?;
                    owners.entry(dir).or_insert(Owner::Asmref(asmref.reference));
                }
                "cs" => {
                    let relative = file.strip_prefix(&root.dir).unwrap_or(file);
                    scripts.push((
                        Source {
                            file: file.to_path_buf(),
                            path: format!("{}/{}", root.virtual_path, relative.to_string_lossy()),
                        },
                        root.is_package,
                    ));
                }
                _ => {}
            }
        }
    }

    let by_name: HashMap<String, usize> = definitions
        .iter()
        .enumerate()
        .map(|(i, d)| (d.json.name.clone(), i))
        .collect();
    let by_guid: HashMap<String, usize> = definitions
        .iter()
        .enumerate()
        .filter_map(|(i, d)| Some((d.guid.clone()?, i)))
        .collect();
    let resolve_reference = |reference: &str| -> Option<usize> {
        match reference.strip_prefix("GUID:") {
            Some(guid) => by_guid.get(&guid.to_ascii_lowercase()).copied(),
            None => by_name.get(reference).copied(),
        }
    };

    // Assign scripts to their owners.
    let mut predefined_sources: BTreeMap<Predefined, Vec<Source>> = BTreeMap::new();
    let roots: Vec<&Path> = input.roots.iter().map(|r| r.dir.as_path()).collect();
    for (source, in_package) in scripts {
        let mut dir = source.file.parent();
        let mut owner = None;
        while let Some(d) = dir {
            if let Some(o) = owners.get(d) {
                owner = Some(o);
                break;
            }
            if roots.contains(&d) {
                break;
            }
            dir = d.parent();
        }
        match owner {
            Some(Owner::Asmdef(i)) => definitions[*i].sources.push(source),
            Some(Owner::Asmref(reference)) => match resolve_reference(reference) {
                Some(i) => definitions[i].sources.push(source),
                None => graph.warnings.push(format!(
                    "{}: asmref names a missing assembly `{reference}`",
                    source.path
                )),
            },
            None if in_package => {}
            None => {
                let kind = predefined_for(&source.path);
                predefined_sources.entry(kind).or_default().push(source);
            }
        }
    }

    // Defines shared by every assembly.
    let project_defines = input.settings.defines_for(input.target_group);
    let base_defines = |editor_only: bool| {
        let mut d = input.profile.engine_defines(
            input.mode,
            input.target_group,
            editor_only,
            input.net_standard,
            input.settings.input_handling,
        );
        d.extend(project_defines.iter().cloned());
        d
    };
    let unity_version = input.profile.version.to_string();
    let version_of = |name: &str| -> Option<&str> {
        if name == "Unity" {
            Some(unity_version.as_str())
        } else {
            input.package_versions.get(name).map(String::as_str)
        }
    };

    // Which asmdefs build, and with which defines.
    let platform_ok = |json: &AsmdefJson| -> bool {
        let platform = if input.mode == CompileMode::EditorPlayMode {
            "Editor"
        } else {
            player_platform(input.target_group)
        };
        if !json.include_platforms.is_empty() {
            json.include_platforms.iter().any(|p| p == platform)
        } else {
            !json.exclude_platforms.iter().any(|p| p == platform)
        }
    };
    let mut included = vec![false; definitions.len()];
    let mut defines_of: Vec<Vec<String>> = vec![Vec::new(); definitions.len()];
    for (i, def) in definitions.iter().enumerate() {
        if def.sources.is_empty() || !platform_ok(&def.json) {
            continue;
        }
        if def.untestable_package && def.json.is_test() {
            continue;
        }
        let editor_only = def.json.is_editor_only();
        let mut defines = base_defines(editor_only);
        for vd in &def.json.version_defines {
            if vd.define.is_empty() {
                continue;
            }
            if version_of(&vd.name).is_some_and(|v| semver::matches(&vd.expression, v)) {
                defines.push(vd.define.clone());
            }
        }
        defines.extend(input.profile.language_defines().iter().cloned());
        dedup(&mut defines);
        if !def
            .json
            .define_constraints
            .iter()
            .all(|c| constraint_holds(c, &defines))
        {
            continue;
        }
        included[i] = true;
        defines_of[i] = defines;
    }
    // An assembly whose references are missing still builds (Unity warns); drop only references.

    let plugin_ok = |plugin: &Plugin, defines: &[String]| {
        let platform = if input.mode == CompileMode::EditorPlayMode {
            "Editor"
        } else {
            player_platform(input.target_group)
        };
        plugin.compatible_with(platform)
            && !input.bcl_names.contains(plugin.file_name())
            && plugin
                .define_constraints
                .iter()
                .all(|c| constraint_holds(c, defines))
    };
    let editor_mode = input.mode == CompileMode::EditorPlayMode;
    let exists = |name: &str| by_name.get(name).is_some_and(|&i| included[i]);
    // The references Unity adds without an asmdef naming them.
    let implicit_references = |name: &str, editor_only: bool, test: bool| -> Vec<String> {
        let mut out = Vec::new();
        // IL post-processor assemblies (`*.CodeGen`) build apart, with only what they name.
        if name.ends_with(".CodeGen") {
            return out;
        }
        let is_ugui = name == UGUI_RUNTIME || name == UGUI_EDITOR;
        let is_test_runner = name == TEST_RUNTIME || name == TEST_EDITOR;
        if !is_ugui && !is_test_runner && input.profile.has(Feature::ImplicitUguiReferences) {
            out.push(UGUI_RUNTIME.to_string());
            if editor_mode {
                out.push(UGUI_EDITOR.to_string());
            }
        }
        if name == UGUI_EDITOR {
            out.push(UGUI_RUNTIME.to_string());
        }
        if !is_test_runner
            && (test || (editor_only && input.profile.has(Feature::ImplicitTestRunnerReferences)))
        {
            out.push(TEST_RUNTIME.to_string());
            if editor_mode {
                out.push(TEST_EDITOR.to_string());
            }
        }
        out.retain(|r| r != name && exists(r));
        out
    };
    let nunit = |editor_only: bool, test: bool, defines: &[String]| -> Option<PathBuf> {
        if !(test || (editor_only && input.profile.has(Feature::ImplicitTestRunnerReferences))) {
            return None;
        }
        input
            .plugins
            .iter()
            .find(|p| p.file_name() == NUNIT && plugin_ok(p, defines))
            .map(|p| p.file.clone())
    };

    let mut assemblies: Vec<ScriptAssembly> = Vec::new();
    for (i, def) in definitions.iter().enumerate() {
        if !included[i] {
            continue;
        }
        let defines = &defines_of[i];
        let test = def
            .json
            .optional_unity_references
            .iter()
            .any(|r| r == "TestAssemblies");
        let mut references = implicit_references(&def.json.name, def.json.is_editor_only(), test);
        for reference in &def.json.references {
            match resolve_reference(reference) {
                Some(j) if included[j] => references.push(definitions[j].json.name.clone()),
                Some(_) => {}
                None => graph.warnings.push(format!(
                    "{}: reference `{reference}` matches no assembly",
                    def.json.name
                )),
            }
        }
        let mut plugins: Vec<PathBuf> = if def.json.override_references {
            def.json
                .precompiled_references
                .iter()
                .filter_map(|name| {
                    input
                        .plugins
                        .iter()
                        .find(|p| !p.analyzer && p.file_name() == name && plugin_ok(p, defines))
                        .map(|p| p.file.clone())
                })
                .collect()
        } else {
            input
                .plugins
                .iter()
                .filter(|p| !p.analyzer && !p.explicitly_referenced && plugin_ok(p, defines))
                .map(|p| p.file.clone())
                .collect()
        };
        plugins.extend(nunit(def.json.is_editor_only(), test, defines));
        dedup_paths(&mut plugins);
        dedup(&mut references);
        let mut sources = def.sources.clone();
        sources.sort_by(|a, b| a.path.cmp(&b.path));
        assemblies.push(ScriptAssembly {
            name: def.json.name.clone(),
            asmdef: Some(def.file.clone()),
            predefined: None,
            sources,
            defines: defines.clone(),
            references,
            plugins,
            analyzers: Vec::new(),
            allow_unsafe: def.json.allow_unsafe_code,
            no_engine_references: def.json.no_engine_references,
            editor_only: def.json.is_editor_only(),
            net_standard: input.net_standard && !def.json.is_editor_only(),
            root_namespace: def.json.root_namespace.clone(),
        });
    }

    // Predefined assemblies reference every auto-referenced assembly they may see.
    let auto: Vec<(String, bool)> = definitions
        .iter()
        .enumerate()
        .filter(|(i, d)| included[*i] && d.json.auto_referenced)
        .map(|(_, d)| (d.json.name.clone(), d.json.is_editor_only()))
        .collect();
    for (kind, mut sources) in predefined_sources {
        if kind.is_editor() && input.mode == CompileMode::Player {
            continue;
        }
        let mut defines = base_defines(kind.is_editor());
        defines.extend(input.profile.language_defines().iter().cloned());
        dedup(&mut defines);
        let mut references = implicit_references(kind.name(), kind.is_editor(), false);
        references.extend(
            auto.iter()
                .filter(|(_, editor_only)| kind.is_editor() || !editor_only)
                .map(|(name, _)| name.clone()),
        );
        let earlier: &[Predefined] = match kind {
            Predefined::FirstPass => &[],
            Predefined::EditorFirstPass => &[Predefined::FirstPass],
            Predefined::Main => &[Predefined::FirstPass],
            Predefined::Editor => &[
                Predefined::FirstPass,
                Predefined::EditorFirstPass,
                Predefined::Main,
            ],
        };
        for e in earlier {
            if assemblies.iter().any(|a| a.predefined == Some(*e)) {
                references.push(e.name().to_string());
            }
        }
        let mut plugins: Vec<PathBuf> = input
            .plugins
            .iter()
            .filter(|p| !p.analyzer && !p.explicitly_referenced && plugin_ok(p, &defines))
            .map(|p| p.file.clone())
            .collect();
        plugins.extend(nunit(kind.is_editor(), false, &defines));
        dedup_paths(&mut plugins);
        dedup(&mut references);
        sources.sort_by(|a, b| a.path.cmp(&b.path));
        assemblies.push(ScriptAssembly {
            name: kind.name().to_string(),
            asmdef: None,
            predefined: Some(kind),
            sources,
            defines,
            references,
            plugins,
            analyzers: Vec::new(),
            allow_unsafe: input.settings.allow_unsafe_code,
            no_engine_references: false,
            editor_only: kind.is_editor(),
            net_standard: input.net_standard && !kind.is_editor(),
            root_namespace: String::new(),
        });
    }

    graph.assemblies = topological(assemblies)?;
    Ok(graph)
}

fn predefined_for(path: &str) -> Predefined {
    let relative = path.strip_prefix("Assets/").unwrap_or(path);
    let mut parts = relative.split('/');
    let first = parts.next().unwrap_or_default();
    let folders: Vec<&str> = relative.split('/').collect();
    let folders = &folders[..folders.len().saturating_sub(1)];
    let editor = folders.iter().any(|p| *p == "Editor");
    let firstpass =
        folders.len() > 0 && matches!(first, "Plugins" | "Standard Assets" | "Pro Standard Assets");
    match (firstpass, editor) {
        (true, true) => Predefined::EditorFirstPass,
        (true, false) => Predefined::FirstPass,
        (false, true) => Predefined::Editor,
        (false, false) => Predefined::Main,
    }
}

/// The asmdef platform name of a Player of a build target group.
fn player_platform(group: &str) -> &str {
    match group {
        "Standalone" => "LinuxStandalone64",
        "iPhone" => "iOS",
        other => other,
    }
}

/// `A`, `!A`, `A || B || !C`.
pub fn constraint_holds(constraint: &str, defines: &[String]) -> bool {
    constraint.split("||").any(|term| {
        let term = term.trim();
        match term.strip_prefix('!') {
            Some(symbol) => !defines.iter().any(|d| d == symbol.trim()),
            None => term.is_empty() || defines.iter().any(|d| d == term),
        }
    })
}

fn dedup_paths(list: &mut Vec<PathBuf>) {
    let mut seen = HashSet::new();
    list.retain(|item| seen.insert(item.clone()));
}

fn dedup(list: &mut Vec<String>) {
    let mut seen = HashSet::new();
    list.retain(|item| seen.insert(item.clone()));
}

fn topological(assemblies: Vec<ScriptAssembly>) -> Result<Vec<ScriptAssembly>> {
    let index: HashMap<String, usize> = assemblies
        .iter()
        .enumerate()
        .map(|(i, a)| (a.name.clone(), i))
        .collect();
    let mut state = vec![0u8; assemblies.len()]; // 0 new, 1 visiting, 2 done
    let mut order = Vec::with_capacity(assemblies.len());
    fn visit(
        i: usize,
        assemblies: &[ScriptAssembly],
        index: &HashMap<String, usize>,
        state: &mut [u8],
        order: &mut Vec<usize>,
    ) -> Result<()> {
        match state[i] {
            2 => return Ok(()),
            1 => bail!("assembly reference cycle through {}", assemblies[i].name),
            _ => {}
        }
        state[i] = 1;
        for r in &assemblies[i].references {
            if let Some(&j) = index.get(r) {
                visit(j, assemblies, index, state, order)?;
            }
        }
        state[i] = 2;
        order.push(i);
        Ok(())
    }
    let mut sorted: Vec<usize> = (0..assemblies.len()).collect();
    sorted.sort_by(|&a, &b| assemblies[a].name.cmp(&assemblies[b].name));
    for i in sorted {
        visit(i, &assemblies, &index, &mut state, &mut order)?;
    }
    let mut slots: Vec<Option<ScriptAssembly>> = assemblies.into_iter().map(Some).collect();
    Ok(order
        .into_iter()
        .map(|i| slots[i].take().unwrap())
        .collect())
}

/// asmdef files are JSON that Unity reads leniently: comments and trailing commas occur, and a
/// key may repeat (the first one wins: Input System's TestFixture asmdef names `versionDefines`
/// twice and the Editor applies the first).
fn parse_lenient_json<T: serde::de::DeserializeOwned>(text: &str) -> Result<T> {
    let text = text.trim_start_matches('\u{feff}');
    let value: FirstWins = match serde_json::from_str(text) {
        Ok(v) => v,
        Err(_) => serde_json::from_str(&strip_json_comments(text))?,
    };
    Ok(serde_json::from_value(value.0)?)
}

/// A JSON value whose objects keep the first of repeated keys.
struct FirstWins(serde_json::Value);

impl<'de> serde::Deserialize<'de> for FirstWins {
    fn deserialize<D: serde::Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        use serde_json::Value;
        struct Visitor;
        impl<'de> serde::de::Visitor<'de> for Visitor {
            type Value = FirstWins;
            fn expecting(&self, f: &mut std::fmt::Formatter) -> std::fmt::Result {
                f.write_str("JSON")
            }
            fn visit_bool<E>(self, v: bool) -> Result<FirstWins, E> {
                Ok(FirstWins(Value::Bool(v)))
            }
            fn visit_i64<E>(self, v: i64) -> Result<FirstWins, E> {
                Ok(FirstWins(v.into()))
            }
            fn visit_u64<E>(self, v: u64) -> Result<FirstWins, E> {
                Ok(FirstWins(v.into()))
            }
            fn visit_f64<E>(self, v: f64) -> Result<FirstWins, E> {
                Ok(FirstWins(v.into()))
            }
            fn visit_str<E>(self, v: &str) -> Result<FirstWins, E> {
                Ok(FirstWins(Value::String(v.to_owned())))
            }
            fn visit_string<E>(self, v: String) -> Result<FirstWins, E> {
                Ok(FirstWins(Value::String(v)))
            }
            fn visit_unit<E>(self) -> Result<FirstWins, E> {
                Ok(FirstWins(Value::Null))
            }
            fn visit_seq<A: serde::de::SeqAccess<'de>>(
                self,
                mut seq: A,
            ) -> Result<FirstWins, A::Error> {
                let mut out = Vec::new();
                while let Some(FirstWins(v)) = seq.next_element()? {
                    out.push(v);
                }
                Ok(FirstWins(Value::Array(out)))
            }
            fn visit_map<A: serde::de::MapAccess<'de>>(
                self,
                mut map: A,
            ) -> Result<FirstWins, A::Error> {
                let mut out = serde_json::Map::new();
                while let Some((key, FirstWins(v))) = map.next_entry::<String, FirstWins>()? {
                    out.entry(key).or_insert(v);
                }
                Ok(FirstWins(Value::Object(out)))
            }
        }
        deserializer.deserialize_any(Visitor)
    }
}

fn strip_json_comments(text: &str) -> String {
    let mut out = String::with_capacity(text.len());
    let bytes = text.as_bytes();
    let mut i = 0;
    let mut in_string = false;
    while i < bytes.len() {
        let c = bytes[i];
        if in_string {
            out.push(c as char);
            if c == b'\\' && i + 1 < bytes.len() {
                out.push(bytes[i + 1] as char);
                i += 1;
            } else if c == b'"' {
                in_string = false;
            }
        } else if c == b'"' {
            in_string = true;
            out.push('"');
        } else if c == b'/' && bytes.get(i + 1) == Some(&b'/') {
            while i < bytes.len() && bytes[i] != b'\n' {
                i += 1;
            }
            continue;
        } else if c == b'/' && bytes.get(i + 1) == Some(&b'*') {
            i += 2;
            while i + 1 < bytes.len() && !(bytes[i] == b'*' && bytes[i + 1] == b'/') {
                i += 1;
            }
            i += 2;
            continue;
        } else if c == b',' {
            // Drop a trailing comma before `}` or `]`.
            let rest = text[i + 1..].trim_start();
            if !(rest.starts_with('}') || rest.starts_with(']')) {
                out.push(',');
            }
        } else {
            out.push(c as char);
        }
        i += 1;
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn predefined_assignment() {
        assert_eq!(predefined_for("Assets/Scripts/A.cs"), Predefined::Main);
        assert_eq!(predefined_for("Assets/Plugins/A.cs"), Predefined::FirstPass);
        assert_eq!(
            predefined_for("Assets/Plugins/X/Editor/A.cs"),
            Predefined::EditorFirstPass
        );
        assert_eq!(
            predefined_for("Assets/Scripts/Editor/A.cs"),
            Predefined::Editor
        );
        assert_eq!(predefined_for("Assets/A.cs"), Predefined::Main);
    }

    #[test]
    fn constraints() {
        let defines = vec!["UNITY_EDITOR".to_string(), "A".to_string()];
        assert!(constraint_holds("UNITY_EDITOR", &defines));
        assert!(!constraint_holds("!A", &defines));
        assert!(constraint_holds("B || A", &defines));
        assert!(constraint_holds("!B", &defines));
    }

    #[test]
    fn lenient_json() {
        let v: AsmdefJson = parse_lenient_json(
            "{\n // c\n \"name\": \"X\", /* y */\n \"references\": [\"A\",],\n}",
        )
        .unwrap();
        assert_eq!(v.name, "X");
        assert_eq!(v.references, ["A"]);
        assert!(v.auto_referenced);
    }

    #[test]
    fn repeated_key_keeps_the_first() {
        let v: AsmdefJson =
            parse_lenient_json("{\"name\": \"X\", \"references\": [\"A\"], \"references\": []}")
                .unwrap();
        assert_eq!(v.references, ["A"]);
    }
}
