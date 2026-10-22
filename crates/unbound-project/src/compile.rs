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
    pub bcl_references: Vec<PathBuf>,
    pub engine_references: Vec<PathBuf>,
    pub language_version: String,
    pub compiler_options: Vec<String>,
    pub analyzers: Vec<PathBuf>,
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

fn glob_dlls(dir: &std::path::Path, prefix: &str) -> Vec<PathBuf> {
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

impl Project {
    /// The Unity reference assemblies game code compiles against, as the Editor passes them.
    pub fn engine_references(&self, mode: CompileMode) -> Vec<PathBuf> {
        let managed = self.install.managed_engine_dir();
        match mode {
            CompileMode::Player => glob_dlls(&managed, "UnityEngine"),
            CompileMode::EditorPlayMode => {
                let mut out = glob_dlls(&managed, "UnityEngine");
                out.extend(glob_dlls(&managed, "UnityEditor"));
                let graphs = self.install.data.join("Managed/UnityEditor.Graphs.dll");
                if graphs.exists() {
                    out.push(graphs);
                }
                let engines = self.install.data.join("PlaybackEngines");
                let mut platforms: Vec<PathBuf> = std::fs::read_dir(&engines)
                    .into_iter()
                    .flatten()
                    .flatten()
                    .map(|e| e.path())
                    .collect();
                platforms.sort();
                for platform in platforms {
                    out.extend(glob_dlls(&platform, "UnityEditor."));
                    out.extend(glob_dlls(&platform, "Unity.Android."));
                }
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
        target_group: &str,
        include_editor_assemblies: bool,
    ) -> CompileRequest {
        let assemblies = graph
            .assemblies
            .iter()
            .filter(|a| include_editor_assemblies || !a.editor_only)
            .map(|a| AssemblySpec {
                name: a.name.clone(),
                sources: a.sources.iter().map(|s| s.file.clone()).collect(),
                defines: a.defines.clone(),
                references: a.references.clone(),
                plugins: a.plugins.clone(),
                analyzers: a.analyzers.clone(),
                allow_unsafe: a.allow_unsafe,
                no_engine_references: a.no_engine_references,
            })
            .collect();
        CompileRequest {
            cache_dir: self.script_cache_dir(),
            assemblies,
            bcl_references: self.bcl_references(target_group),
            engine_references: self.engine_references(mode),
            language_version: self.profile.data.language_version.clone(),
            compiler_options: self.profile.data.compiler_options.clone(),
            analyzers: Vec::new(),
        }
    }
}
