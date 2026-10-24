//! The request the managed compiler service takes, built from the script graph, and its response.
use crate::{Graph, Project};
use serde::{Deserialize, Serialize};
use std::path::PathBuf;
use unbound_compat::CompileMode;

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct CompileRequest {
    pub cache_dir: PathBuf,
    pub assemblies: Vec<AssemblySpec>,
    /// The .NET Framework 4.8 class library.
    pub bcl_references: Vec<PathBuf>,
    /// The .NET Standard 2.1 class library, for assemblies marked `net_standard`.
    pub net_standard_references: Vec<PathBuf>,
    pub engine_references: Vec<PathBuf>,
    pub language_version: String,
    pub compiler_options: Vec<String>,
    pub analyzers: Vec<PathBuf>,
    /// The project directory, which the Editor hands to source generators.
    pub project_path: PathBuf,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct AssemblySpec {
    pub name: String,
    pub sources: Vec<PathBuf>,
    pub defines: Vec<String>,
    pub references: Vec<String>,
    pub plugins: Vec<PathBuf>,
    pub analyzers: Vec<PathBuf>,
    pub allow_unsafe: bool,
    pub no_engine_references: bool,
    pub net_standard: bool,
    /// Not IL post-processed: on the Editor's skip list.
    pub skip_code_gen: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct CompileResponse {
    pub assemblies: Vec<AssemblyResult>,
    pub milliseconds: u64,
    pub compiled: u32,
    pub cached: u32,
    pub failed: u32,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct AssemblyResult {
    pub name: String,
    pub status: String,
    pub dll: Option<PathBuf>,
    pub pdb: Option<PathBuf>,
    /// `dll` is an IL post-processor's output.
    #[serde(default)]
    pub post_processed: bool,
    pub interface_hash: Option<String>,
    pub errors: Vec<DiagnosticInfo>,
    pub warnings: u32,
    pub milliseconds: u64,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase")]
pub struct DiagnosticInfo {
    pub id: String,
    pub message: String,
    pub file: Option<String>,
    pub line: u32,
    pub column: u32,
}

impl std::fmt::Display for DiagnosticInfo {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match &self.file {
            Some(file) => write!(
                f,
                "{file}({},{}): {}: {}",
                self.line, self.column, self.id, self.message
            ),
            None => write!(f, "{}: {}", self.id, self.message),
        }
    }
}

pub(crate) fn glob_dlls(dir: &std::path::Path, prefix: &str) -> Vec<PathBuf> {
    let mut out: Vec<PathBuf> = std::fs::read_dir(dir)
        .into_iter()
        .flatten()
        .flatten()
        .map(|e| e.path())
        .filter(|p| {
            p.extension().is_some_and(|e| e == "dll")
                && p.file_name()
                    .and_then(|n| n.to_str())
                    .is_some_and(|n| n.starts_with(prefix))
        })
        .collect();
    out.sort();
    out
}

/// The Editor's build program's rule for IL post-processor assemblies: `Unity.*.CodeGen` or
/// `Unity.*.Compiler`.
pub fn is_post_processor(name: &str) -> bool {
    let lower = name.to_ascii_lowercase();
    name.starts_with("Unity.") && (lower.ends_with(".codegen") || lower.ends_with(".compiler"))
}

impl Project {
    /// The Unity reference assemblies game code compiles against, as the Editor passes them.
    pub fn engine_references(&self, mode: CompileMode) -> Vec<PathBuf> {
        let managed = self.install.managed_engine_dir();
        match mode {
            CompileMode::Player => glob_dlls(&managed, "UnityEngine"),
            CompileMode::EditorPlayMode => {
                let mut out = glob_dlls(&managed, "UnityEngine");
                out.extend(glob_dlls(&managed, "UnityEditor"));
                out.extend(self.install.platform_modules());
                out
            }
        }
    }

    /// Where compiled script assemblies are kept, content-addressed.
    pub fn script_cache_dir(&self) -> PathBuf {
        self.library_dir().join("ScriptAssemblies")
    }

    pub fn compile_request(
        &self,
        graph: &Graph,
        mode: CompileMode,
        include_editor_assemblies: bool,
    ) -> CompileRequest {
        // Without the editor assemblies, still the ones a runtime assembly references: in the
        // Editor a runtime assembly may reference an Editor-only one (Input System references
        // `UnityEditor.UI`).
        let mut wanted: std::collections::HashSet<&str> = graph
            .assemblies
            .iter()
            .filter(|a| include_editor_assemblies || !a.editor_only)
            .map(|a| a.name.as_str())
            .collect();
        // The IL post-processor assemblies (Editor-only) build whenever there are any: the
        // Editor post-processes every compile with them.
        wanted.extend(
            graph
                .assemblies
                .iter()
                .filter(|a| is_post_processor(&a.name))
                .map(|a| a.name.as_str()),
        );
        for a in graph.assemblies.iter().rev() {
            if wanted.contains(a.name.as_str()) {
                wanted.extend(a.references.iter().map(String::as_str));
            }
        }
        let assemblies = graph
            .assemblies
            .iter()
            .filter(|a| wanted.contains(a.name.as_str()))
            .map(|a| AssemblySpec {
                name: a.name.clone(),
                sources: a.sources.iter().map(|s| s.file.clone()).collect(),
                defines: a.defines.clone(),
                references: a.references.clone(),
                plugins: {
                    let mut plugins = a.plugins.clone();
                    // IL post-processors build against the Editor's ILPostProcessor API.
                    if is_post_processor(&a.name) {
                        plugins.push(
                            self.install
                                .data
                                .join("Managed/Unity.CompilationPipeline.Common.dll"),
                        );
                    }
                    plugins
                },
                analyzers: a.analyzers.clone(),
                allow_unsafe: a.allow_unsafe,
                no_engine_references: a.no_engine_references,
                net_standard: a.net_standard,
                skip_code_gen: self.profile.data.skip_code_gen.iter().any(|s| *s == a.name),
            })
            .collect();
        CompileRequest {
            cache_dir: self.script_cache_dir(),
            assemblies,
            bcl_references: self.framework_references(),
            net_standard_references: self.net_standard_references(),
            engine_references: self.engine_references(mode),
            language_version: self.profile.data.language_version.clone(),
            compiler_options: self.profile.data.compiler_options.clone(),
            analyzers: self
                .profile
                .data
                .source_generators
                .iter()
                .map(|g| self.install.data.join(g))
                .collect(),
            project_path: self.root.clone(),
        }
    }
}
