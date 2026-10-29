//! The request the managed compiler service takes, built from the script graph, and its response.
use crate::{Graph, PackageSource, Project};
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
    /// Compiled with optimizations (`/optimize+`).
    pub optimize: bool,
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

/// The Editor's Code Optimization mode: how script assemblies are compiled. Release is
/// `/optimize+`, Debug `/optimize-`; the defines are the same in both.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CodeOptimization {
    Debug,
    Release,
}

impl CodeOptimization {
    /// The mode the Editor starts in. `UNBOUND_CODE_OPTIMIZATION` (`debug` or `release`) first,
    /// then the Editor's `-debugCodeOptimization` and `-releaseCodeOptimization` options, then its
    /// "Code Optimization On Startup" preference (`ScriptDebugInfoEnabled`, Release when unset).
    pub fn at_startup(command_line: &[String]) -> Self {
        if let Some(mode) = std::env::var("UNBOUND_CODE_OPTIMIZATION")
            .ok()
            .and_then(|v| Self::parse(&v))
        {
            return mode;
        }
        for option in command_line.iter().rev() {
            match option.to_ascii_lowercase().as_str() {
                "-debugcodeoptimization" => return Self::Debug,
                "-releasecodeoptimization" => return Self::Release,
                _ => {}
            }
        }
        match editor_pref_int("ScriptDebugInfoEnabled") {
            Some(v) if v != 0 => Self::Debug,
            _ => Self::Release,
        }
    }

    pub fn parse(text: &str) -> Option<Self> {
        match text.trim().to_ascii_lowercase().as_str() {
            "debug" => Some(Self::Debug),
            "release" => Some(Self::Release),
            _ => None,
        }
    }

    pub fn name(self) -> &'static str {
        match self {
            Self::Debug => "debug",
            Self::Release => "release",
        }
    }
}

/// An int EditorPref as the managed `EditorPrefs` reads it: Unbound's own file (what games
/// changed) over what the installed Editor saved (its prefs file on Linux, the
/// `com.unity3d.UnityEditor5.x` preferences domain on macOS).
fn editor_pref_int(name: &str) -> Option<i64> {
    let own = match std::env::var_os("XDG_CACHE_HOME") {
        Some(dir) => PathBuf::from(dir),
        None => crate::install::home().join(".cache"),
    }
    .join("unbound/EditorPrefs");
    let read = |file: &std::path::Path| pref_int(&std::fs::read_to_string(file).ok()?, name);
    read(&own).or_else(|| {
        if cfg!(target_os = "macos") {
            let output = std::process::Command::new("defaults")
                .args(["read", "com.unity3d.UnityEditor5.x", name])
                .output()
                .ok()?;
            output.status.success().then_some(())?;
            String::from_utf8_lossy(&output.stdout).trim().parse().ok()
        } else {
            read(&crate::install::home().join(".local/share/unity3d/prefs"))
        }
    })
}

/// `<pref name="NAME" type="int">VALUE</pref>` in a Unity prefs file.
fn pref_int(text: &str, name: &str) -> Option<i64> {
    let opening = format!("<pref name=\"{name}\" type=\"int\">");
    let start = text.find(&opening)? + opening.len();
    let end = start + text[start..].find('<')?;
    text[start..end].trim().parse().ok()
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
        optimization: CodeOptimization,
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
        // Packages the Editor does not let you edit (registry, git and built-in ones, kept in a
        // package cache) are never hot-reloaded: they are always optimized.
        let immutable: Vec<&std::path::Path> = self
            .packages
            .iter()
            .filter(|p| {
                matches!(
                    p.source,
                    PackageSource::Registry | PackageSource::Git | PackageSource::BuiltIn
                )
            })
            .map(|p| p.root.as_path())
            .collect();
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
                optimize: optimization == CodeOptimization::Release
                    || (!a.sources.is_empty()
                        && a.sources
                            .iter()
                            .all(|s| immutable.iter().any(|root| s.file.starts_with(root)))),
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

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_int_prefs() {
        let text = "<unity_prefs version_major=\"1\" version_minor=\"1\">\n\
            \t<pref name=\"Other\" type=\"int\">7</pref>\n\
            \t<pref name=\"ScriptDebugInfoEnabled\" type=\"int\">1</pref>\n</unity_prefs>\n";
        assert_eq!(pref_int(text, "ScriptDebugInfoEnabled"), Some(1));
        assert_eq!(pref_int(text, "Other"), Some(7));
        assert_eq!(pref_int(text, "Missing"), None);
    }

    #[test]
    fn parses_modes() {
        assert_eq!(
            CodeOptimization::parse("Debug"),
            Some(CodeOptimization::Debug)
        );
        assert_eq!(
            CodeOptimization::parse("release"),
            Some(CodeOptimization::Release)
        );
        assert_eq!(CodeOptimization::parse("fast"), None);
    }
}
