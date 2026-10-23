//! The model of a Unity project: release and compatibility profile, settings, packages, the asset
//! database and the script assembly graph.
pub mod assemblies;
pub mod assetdb;
pub mod compile;
pub mod install;
pub mod packages;
pub mod plugins;
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
                "no compatibility profile for Unity {}; using {}",
                version.family().0,
                profile.data.family
            );
        }
        let install = UnityInstall::find(version)?;
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

    /// The class library references: .NET Framework 4.8 or .NET Standard 2.1, per the target's API
    /// compatibility level.
    pub fn bcl_references(&self, target_group: &str) -> Vec<PathBuf> {
        if self.settings.uses_net_standard(target_group) {
            let mut out = dll_files(&self.install.data.join("NetStandard/ref/2.1.0"));
            out.extend(dll_files(
                &self.install.data.join("NetStandard/compat/2.1.0/shims/netfx"),
            ));
            return out;
        }
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

    /// The native libraries (`.so`) the Editor would load on this machine, by file.
    pub fn native_plugins(&self) -> Vec<PathBuf> {
        let mut out = Vec::new();
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
                    && path.extension().is_some_and(|e| e == "so")
                    && plugins::native_editor_plugin(path)
                {
                    out.push(path.to_path_buf());
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
        let bcl_names = self
            .bcl_references(target_group)
            .iter()
            .filter_map(|p| Some(p.file_name()?.to_str()?.to_string()))
            .collect();
        assemblies::build(&assemblies::GraphInput {
            bcl_names: &bcl_names,
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
