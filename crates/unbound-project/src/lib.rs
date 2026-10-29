//! The model of a Unity project: release and compatibility profile, settings, packages, the asset
//! database and the script assembly graph.
pub mod assemblies;
pub mod assetdb;
pub mod builtin_shaders;
pub mod compile;
pub mod editor_archive;
pub mod install;
pub mod packages;
pub mod net;
pub mod plugins;
pub mod releases;
pub mod script_class;
pub mod semver;
pub mod settings;

use anyhow::{Context, Result};
use rayon::prelude::*;
use std::{
    collections::BTreeMap,
    path::{Path, PathBuf},
};
use unbound_compat::{CompileMode, Profile, UnityVersion};

pub use assemblies::{Graph, ScriptAssembly};
pub use compile::CodeOptimization;
pub use assetdb::AssetDb;
pub use install::UnityInstall;
pub use packages::{Package, PackageSource};
pub use settings::Settings;

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
        let resolution = packages::resolve(&root, &install)?;
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
                    out.push(library);
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
        assemblies::build(&assemblies::GraphInput {
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
        })
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
