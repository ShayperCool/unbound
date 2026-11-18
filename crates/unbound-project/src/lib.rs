//! The model of a Unity project: release and compatibility profile, settings, packages, the asset
//! database and the script assembly graph.
pub mod assemblies;
pub mod assetdb;
pub mod builtin_shaders;
pub mod compile;
pub mod editor_archive;
pub mod git_packages;
pub mod install;
pub mod modules;
pub mod net;
pub mod packages;
pub mod plugins;
pub mod releases;
pub mod response_files;
pub mod script_class;
pub mod semver;
pub mod settings;
pub mod upm_config;

use anyhow::{Context, Result};
use rayon::prelude::*;
use std::{
    collections::BTreeMap,
    path::{Path, PathBuf},
};
use unbound_compat::{CompileMode, Profile, UnityVersion};

pub use assemblies::{Graph, ScriptAssembly};
pub use assetdb::AssetDb;
pub use compile::CodeOptimization;
pub use install::UnityInstall;
pub use packages::{Package, PackageSource};
pub use settings::{PlayModeAtlases, Settings};

/// The global cache: `UNBOUND_CACHE`, else `$XDG_CACHE_HOME/unbound`, else `~/.cache/unbound`.
pub fn cache_root() -> PathBuf {
    if let Some(dir) = std::env::var_os("UNBOUND_CACHE") {
        return dir.into();
    }
    match std::env::var_os("XDG_CACHE_HOME") {
        Some(dir) => PathBuf::from(dir).join("unbound"),
        None => install::home().join(".cache/unbound"),
    }
}

pub struct Project {
    pub root: PathBuf,
    /// `<root>/Assets`
    pub assets: PathBuf,
    pub version: UnityVersion,
    pub profile: Profile,
    pub install: UnityInstall,
    pub settings: Settings,
    pub packages: Vec<Package>,
    pub missing_packages: Vec<(String, String, String)>,
}

impl Project {
    pub fn open(root: &Path) -> Result<Self> {
        let root = root
            .canonicalize()
            .with_context(|| format!("project {}", root.display()))?;
        let version_txt = std::fs::read_to_string(root.join("ProjectSettings/ProjectVersion.txt"))
            .context("not a Unity project: ProjectSettings/ProjectVersion.txt is missing")?;
        let version = UnityVersion::from_project_version_txt(&version_txt)?;
        let profile = Profile::for_version(version)?;
        if profile.approximate {
            tracing::warn!(
                "no compatibility profile for Unity {}.{}; using {}",
                version.major,
                version.minor,
                profile.data.family
            );
        }
        let install = UnityInstall::find(version, profile.release())?;
        let settings = Settings::read(&root)?;
        let resolution = packages::resolve(
            &root,
            &install,
            unbound_compat::PackageResolutionRules::for_version(version),
        )?;
        Ok(Self {
            assets: root.join("Assets"),
            root,
            version,
            profile,
            install,
            settings,
            packages: resolution.packages,
            missing_packages: resolution.missing,
        })
    }

    /// Unity's built-in shader sources for the install's release (downloaded once).
    pub fn builtin_shaders(&self) -> Result<builtin_shaders::BuiltinShaders> {
        let txt =
            std::fs::read_to_string(self.root.join("ProjectSettings/ProjectVersion.txt")).ok();
        builtin_shaders::BuiltinShaders::find(&self.install, txt.as_deref())
    }

    /// `<project>/Library/Unbound`: everything Unbound keeps per project.
    pub fn library_dir(&self) -> PathBuf {
        self.root.join("Library/Unbound")
    }

    fn asset_roots(&self) -> Vec<assetdb::Root<'_>> {
        let mut roots = vec![assetdb::Root {
            dir: &self.assets,
            virtual_path: "Assets".into(),
        }];
        for package in &self.packages {
            roots.push(assetdb::Root {
                dir: &package.root,
                virtual_path: format!("Packages/{}", package.name),
            });
        }
        roots
    }

    pub fn asset_db(&self) -> AssetDb {
        AssetDb::scan(&self.asset_roots())
    }

    /// The .NET Framework 4.8 class library the Editor compiles against: every Editor-only
    /// assembly, and runtime assemblies at that API compatibility level.
    pub fn framework_references(&self) -> Vec<PathBuf> {
        let bcl = self.install.data.join(&self.profile.data.bcl);
        let mut out = dll_files(&bcl.join("Facades"));
        out.extend(
            self.profile
                .data
                .bcl_assemblies
                .iter()
                .map(|name| bcl.join(name)),
        );
        out
    }

    /// The .NET Standard 2.1 class library of runtime assemblies at that API compatibility level:
    /// `netstandard.dll`, its facades, the .NET Framework shims and the extensions, as the Editor
    /// lists them.
    pub fn net_standard_references(&self) -> Vec<PathBuf> {
        let root = self.install.data.join("NetStandard");
        let mut out = Vec::new();
        for dir in [
            "ref/2.1.0",
            "compat/2.1.0/shims/netstandard",
            "compat/2.1.0/shims/netfx",
            "Extensions/2.0.0",
        ] {
            out.extend(dll_files(&root.join(dir)));
        }
        out
    }

    /// The class library a runtime assembly of the target compiles against.
    pub fn bcl_references(&self, target_group: &str) -> Vec<PathBuf> {
        if self.settings.uses_net_standard(target_group) {
            self.net_standard_references()
        } else {
            self.framework_references()
        }
    }

    /// Every managed plugin DLL of the project and its packages.
    pub fn plugins(&self) -> Vec<plugins::Plugin> {
        let mut dlls = Vec::new();
        for root in self.asset_roots() {
            let walker = walkdir::WalkDir::new(root.dir)
                .follow_links(true)
                .into_iter()
                .filter_entry(|e| {
                    e.depth() == 0 || !assetdb::is_hidden(&e.file_name().to_string_lossy())
                });
            for entry in walker.flatten() {
                let path = entry.path();
                if entry.file_type().is_file()
                    && path
                        .extension()
                        .is_some_and(|e| e.eq_ignore_ascii_case("dll"))
                {
                    let relative = path.strip_prefix(root.dir).unwrap_or(path);
                    dlls.push((
                        path.to_path_buf(),
                        format!("{}/{}", root.virtual_path, relative.to_string_lossy()),
                    ));
                }
            }
        }
        let mut plugins: Vec<_> = dlls
            .into_par_iter()
            .filter_map(|(file, path)| plugins::read_plugin(&file, path))
            .collect();
        plugins.sort_by(|a, b| a.path.cmp(&b.path));
        plugins
    }

    /// The native libraries the Editor would load on this machine (`.so`; `.bundle`, `.dylib` on
    /// macOS), by file: a bundle directory by its executable.
    pub fn native_plugins(&self) -> Vec<PathBuf> {
        self.native_plugin_libraries()
            .into_iter()
            .map(|(library, _)| library)
            .collect()
    }

    /// [`Self::native_plugins`] with whether each one's importer loads it on startup
    /// (`isPreloaded`).
    pub fn native_plugin_libraries(&self) -> Vec<(PathBuf, bool)> {
        let mut out = Vec::new();
        for root in self.asset_roots() {
            let mut walker = walkdir::WalkDir::new(root.dir)
                .follow_links(true)
                .into_iter()
                .filter_entry(|e| {
                    e.depth() == 0 || !assetdb::is_hidden(&e.file_name().to_string_lossy())
                });
            while let Some(entry) = walker.next() {
                let Ok(entry) = entry else { continue };
                let path = entry.path();
                let native = path
                    .extension()
                    .and_then(|e| e.to_str())
                    .is_some_and(|e| plugins::NATIVE_EXTENSIONS.contains(&e));
                if !native {
                    continue;
                }
                if entry.file_type().is_dir() {
                    // A bundle directory is one plugin, never searched inside.
                    walker.skip_current_dir();
                }
                if plugins::native_editor_plugin(path)
                    && let Some(library) = plugins::native_library_file(path)
                {
                    out.push((library, plugins::native_plugin_preloaded(path)));
                }
            }
        }
        out.sort();
        out
    }

    pub fn script_graph(&self, mode: CompileMode, target_group: &str) -> Result<Graph> {
        let mut roots = vec![assemblies::ScriptRoot {
            dir: self.root.join("Assets"),
            virtual_path: "Assets".into(),
            is_package: false,
            testable: true,
        }];
        for package in &self.packages {
            roots.push(assemblies::ScriptRoot {
                dir: package.root.clone(),
                virtual_path: format!("Packages/{}", package.name),
                is_package: true,
                testable: package.testable,
            });
        }
        let package_versions: BTreeMap<String, String> = self
            .packages
            .iter()
            .map(|p| (p.name.clone(), p.version.clone()))
            .collect();
        let plugins = self.plugins();
        let guid_of = |file: &Path| {
            assetdb::read_meta_guid(Path::new(&format!("{}.meta", file.display())))
                .map(|g| g.to_string())
        };
        let net_standard = self.settings.uses_net_standard(target_group);
        let mut bcl = self.framework_references();
        if net_standard {
            bcl.extend(self.net_standard_references());
        }
        let bcl_names = bcl
            .iter()
            .filter_map(|p| Some(p.file_name()?.to_str()?.to_string()))
            .collect();
        let engine_modules = self.install.engine_module_names();
        let mut system_reference_dirs: Vec<PathBuf> = bcl
            .iter()
            .filter_map(|p| p.parent().map(Path::to_path_buf))
            .collect();
        system_reference_dirs.sort();
        system_reference_dirs.dedup();
        let mut graph = assemblies::build(&assemblies::GraphInput {
            system_reference_dirs: &system_reference_dirs,
            has_engine_module: &|name| engine_modules.contains(name),
            bcl_names: &bcl_names,
            net_standard,
            roots: &roots,
            profile: &self.profile,
            settings: &self.settings,
            mode,
            target_group,
            package_versions: &package_versions,
            plugins: &plugins,
            asmdef_guid: &guid_of,
        })?;
        (graph.excluded_modules, graph.disabled_modules) =
            self.excluded_modules(mode, target_group)?;
        Ok(graph)
    }

    /// The engine modules runtime script assemblies leave out for `target_group`: those its
    /// player's module list lacks and those whose built-in package the project does not have.
    /// Without a module list in the installation, none (with a warning).
    fn excluded_modules(
        &self,
        mode: CompileMode,
        target_group: &str,
    ) -> Result<(
        std::collections::BTreeSet<String>,
        Vec<modules::DisabledModule>,
    )> {
        let Some((list, _)) = modules::modules_for(&self.install, target_group)? else {
            tracing::warn!(
                "the Unity installation has no module list (Resources/modules.asset): script assemblies \
                 compile against every engine module, whatever the project's packages"
            );
            return Ok(Default::default());
        };
        let engine: Vec<String> = self
            .engine_references(mode)
            .iter()
            .filter_map(|p| Some(p.file_stem()?.to_str()?.to_string()))
            .collect();
        let packages = packages::module_packages(&self.root, &self.install, &self.packages);
        let names: std::collections::BTreeSet<&str> = packages.iter().map(String::as_str).collect();
        let builtin = self.install.builtin_packages_dir();
        Ok(modules::excluded(&list, &engine, &names, &|package| {
            let json: serde_json::Value = serde_json::from_slice(
                &std::fs::read(builtin.join(package).join("package.json")).ok()?,
            )
            .ok()?;
            Some(json["displayName"].as_str()?.to_string())
        }))
    }
}

/// The DLLs of a directory, sorted.
fn dll_files(dir: &Path) -> Vec<PathBuf> {
    let Ok(entries) = std::fs::read_dir(dir) else {
        return Vec::new();
    };
    let mut files: Vec<PathBuf> = entries
        .flatten()
        .map(|e| e.path())
        .filter(|p| p.extension().is_some_and(|e| e == "dll"))
        .collect();
    files.sort();
    files
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A project on an earlier 6000.0 patch compiles against its own installation, which may
    /// lack engine modules the profile's release has: their defines follow the installation.
    #[test]
    fn module_defines_follow_the_installation_the_project_compiles_against() {
        let scratch = std::env::temp_dir().join(format!(
            "unbound-module-defines-{}-{:?}",
            std::process::id(),
            std::thread::current().id()
        ));
        let _ = std::fs::remove_dir_all(&scratch);
        let root = scratch.join("project");
        std::fs::create_dir_all(root.join("Assets")).unwrap();
        std::fs::write(root.join("Assets/A.cs"), "class A {}\n").unwrap();
        let defines_with = |modules: &[&str]| {
            let data = scratch.join(format!("install-{}/Editor/Data", modules.len()));
            let managed = data.join("Managed/UnityEngine");
            std::fs::create_dir_all(&managed).unwrap();
            for module in modules {
                std::fs::write(managed.join(format!("{module}.dll")), b"").unwrap();
            }
            let version: UnityVersion = "6000.0.67f1".parse().unwrap();
            let project = Project {
                assets: root.join("Assets"),
                root: root.clone(),
                version,
                profile: Profile::for_version(version).unwrap(),
                install: UnityInstall { data, version },
                settings: Settings::default(),
                packages: Vec::new(),
                missing_packages: Vec::new(),
            };
            let graph = project
                .script_graph(CompileMode::EditorPlayMode, "Android")
                .unwrap();
            let main = graph
                .assemblies
                .iter()
                .find(|a| a.name == "Assembly-CSharp")
                .expect("the predefined assembly builds");
            main.defines.clone()
        };
        let base = ["UnityEngine", "UnityEngine.CoreModule"];
        let without = defines_with(&base);
        let with = defines_with(&[
            base[0],
            base[1],
            "UnityEngine.UnityConsentModule",
            "UnityEngine.IdentifiersModule",
        ]);
        let _ = std::fs::remove_dir_all(&scratch);
        for define in ["ENABLE_UNITY_CONSENT", "ENABLE_UNITY_CLOUD_IDENTIFIERS"] {
            assert!(with.iter().any(|d| d == define), "{define}");
            assert!(!without.iter().any(|d| d == define), "{define}");
        }
        let removed: Vec<&String> = with.iter().filter(|d| !without.contains(d)).collect();
        assert_eq!(removed.len(), 2, "{removed:?}");
        assert!(without.iter().any(|d| d == "UNITY_6000_0_67"));
    }

    /// Each assembly takes its asmdef folder's csc.rsp, else the project's: the file's defines
    /// decide define constraints but are not the assembly's defines, its references compile the
    /// assembly but are not listed among its plugins, and its other arguments reach the compiler
    /// (Unity 6000.0.71f1, tests/projects/gameinterop rsp.*).
    #[test]
    fn response_files_apply_per_assembly() {
        let scratch = std::env::temp_dir().join(format!(
            "unbound-response-files-{}-{:?}",
            std::process::id(),
            std::thread::current().id()
        ));
        let _ = std::fs::remove_dir_all(&scratch);
        let root = scratch.join("project");
        let assets = root.join("Assets");
        let write = |path: &str, text: &str| {
            let file = root.join(path);
            std::fs::create_dir_all(file.parent().unwrap()).unwrap();
            std::fs::write(file, text).unwrap();
        };
        write("Assets/A.cs", "class A {}\n");
        write("Assets/Libs/Ref.dll", "");
        write(
            "Assets/csc.rsp",
            "-define:RSP_GLOBAL\n-unsafe\n-r:Assets/Libs/Ref.dll\n-r:Missing.dll\n",
        );
        let asmdef = |dir: &str, name: &str, constraints: &str| {
            write(
                &format!("Assets/{dir}/{name}.asmdef"),
                &format!(r#"{{"name": "{name}", "defineConstraints": [{constraints}]}}"#),
            );
            write(&format!("Assets/{dir}/{name}.cs"), "class B {}\n");
        };
        asmdef("Local", "Local", "");
        write(
            "Assets/Local/csc.rsp",
            "-define:RSP_LOCAL\n-langversion:10\n",
        );
        asmdef("Inherit", "Inherit", "");
        asmdef("Constrained", "Constrained", r#""RSP_GLOBAL""#);
        asmdef("LocalOnly", "LocalOnly", r#""RSP_LOCAL""#);
        let data = scratch.join("install/Editor/Data");
        std::fs::create_dir_all(data.join("Managed/UnityEngine")).unwrap();
        let version: UnityVersion = "6000.0.71f1".parse().unwrap();
        let project = Project {
            assets: assets.clone(),
            root: root.clone(),
            version,
            profile: Profile::for_version(version).unwrap(),
            install: UnityInstall { data, version },
            settings: Settings::default(),
            packages: Vec::new(),
            missing_packages: Vec::new(),
        };
        let graph = project
            .script_graph(CompileMode::EditorPlayMode, "Standalone")
            .unwrap();
        let _ = std::fs::remove_dir_all(&scratch);
        let get = |name: &str| graph.get(name);
        let main = get("Assembly-CSharp").expect("predefined");
        let rsp = main
            .response_file
            .as_ref()
            .expect("the project's response file");
        assert_eq!(rsp.path, assets.join("csc.rsp"));
        assert_eq!(rsp.arguments, ["-define:RSP_GLOBAL", "-unsafe"]);
        assert_eq!(rsp.references, [root.join("Assets/Libs/Ref.dll")]);
        assert!(!main.defines.iter().any(|d| d == "RSP_GLOBAL"));
        assert!(!main.plugins.iter().any(|p| p.ends_with("Ref.dll")));
        assert!(
            graph
                .warnings
                .iter()
                .any(|w| w.contains("Missing.dll could not be found"))
        );
        let local = get("Local").unwrap().response_file.as_ref().unwrap();
        assert_eq!(local.arguments, ["-define:RSP_LOCAL", "-langversion:10"]);
        assert!(local.references.is_empty());
        assert_eq!(
            get("Inherit").unwrap().response_file.as_ref().unwrap().path,
            assets.join("csc.rsp")
        );
        assert!(
            get("Constrained").is_some(),
            "the project's define satisfies the constraint"
        );
        assert!(
            get("LocalOnly").is_none(),
            "another assembly's response file does not"
        );
        let request = project.compile_request(
            &graph,
            CompileMode::EditorPlayMode,
            true,
            crate::compile::CodeOptimization::Debug,
        );
        let spec = request
            .assemblies
            .iter()
            .find(|a| a.name == "Assembly-CSharp")
            .unwrap();
        assert_eq!(spec.response_arguments, ["-define:RSP_GLOBAL", "-unsafe"]);
        assert!(spec.plugins.iter().any(|p| p.ends_with("Ref.dll")));
    }
}
