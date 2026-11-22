//! The model of a Unity project: release and compatibility profile, settings, packages, the asset
//! database and the script assembly graph.
pub mod assemblies;
pub mod assembly_identity;
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
    /// Registry packages resolved at another version than asked for (`Resolution::inexact`).
    pub inexact_packages: Vec<(String, String)>,
}

/// The name of Unbound's own directory in a project's root, where nothing else is asked for:
/// everything Unbound keeps per project (caches, the project view, logs, results). `Library/`
/// is Unity's: Unbound keeps nothing there.
pub const UNBOUND_DIR: &str = "LibraryUnbound";

/// The environment variable that names another place for that directory (the command line's
/// `--project-data` sets it): relative to the project's root, or absolute.
pub const PROJECT_DATA_VARIABLE: &str = "UNBOUND_PROJECT_DATA";

/// Where Unbound kept its directory before it left Unity's `Library/`, relative to the project.
pub const FORMER_UNBOUND_DIR: &str = "Library/Unbound";

/// The project folders Unbound's directory may not be, or be in: the authored ones and Unity's.
const NOT_FOR_PROJECT_DATA: &[&str] = &["Assets", "Packages", "ProjectSettings", "Library"];

/// The place asked for, read once: the process works on one project.
fn asked_project_data() -> Option<&'static Path> {
    static ASKED: std::sync::OnceLock<Option<PathBuf>> = std::sync::OnceLock::new();
    ASKED
        .get_or_init(|| {
            std::env::var_os(PROJECT_DATA_VARIABLE)
                .filter(|v| !v.is_empty())
                .map(PathBuf::from)
        })
        .as_deref()
}

/// A path without its `.` and `..` components (no link is read).
fn lexical(path: &Path) -> PathBuf {
    let mut clean = PathBuf::new();
    for part in path.components() {
        match part {
            std::path::Component::CurDir => {}
            std::path::Component::ParentDir => {
                clean.pop();
            }
            other => clean.push(other),
        }
    }
    clean
}

/// Unbound's directory for a project root and the place asked for (`None`: the default,
/// `<project>/LibraryUnbound`): a relative place is in the project, an absolute one anywhere.
pub fn unbound_dir_at(project_root: &Path, asked: Option<&Path>) -> PathBuf {
    match asked {
        None => project_root.join(UNBOUND_DIR),
        Some(asked) => lexical(&project_root.join(asked)),
    }
}

/// Unbound's directory of a project: the one place that names it. `<project>/LibraryUnbound`
/// unless `--project-data` or `UNBOUND_PROJECT_DATA` asks for another.
pub fn unbound_dir(project_root: &Path) -> PathBuf {
    unbound_dir_at(project_root, asked_project_data())
}

/// Why a directory cannot be Unbound's for this project (`None`: it can): the project's root
/// itself, and the authored folders and Unity's `Library/` or anything in them. Decided on the
/// path as spelled and, where it exists, on where its links lead.
pub fn refused_project_data(project_root: &Path, dir: &Path) -> Option<String> {
    let landed_root = project_root.canonicalize().ok();
    let landed = dir.canonicalize().ok();
    let spelled = Some((project_root, dir));
    let followed = landed_root.as_deref().zip(landed.as_deref());
    for (root, candidate) in spelled.into_iter().chain(followed) {
        if candidate == root {
            return Some(format!(
                "{} is the project's folder itself: Unbound's project data needs a directory of its own",
                dir.display()
            ));
        }
        for name in NOT_FOR_PROJECT_DATA {
            if candidate.starts_with(root.join(name)) {
                return Some(format!(
                    "{} is in the project's {name}/ folder: Unbound's project data may not be in Assets/, Packages/, ProjectSettings/ or Library/",
                    dir.display()
                ));
            }
        }
    }
    None
}

/// What is kept of a former directory: the files that are no cache and cannot be made again
/// (the settings the game set, the last sessions' logs, test results).
const KEPT_OF_FORMER: &[&str] = &[
    "EditorUserSettings.asset",
    "Editor.log",
    "Editor-prev.log",
    "TestResults",
];

/// Makes a project's Unbound directory ready: creates it, in the project with a `.gitignore`
/// that ignores all of it (a Unity project ignores `/Library/`, not this directory, and Unbound
/// does not edit the project's own `.gitignore`), and retires a former `Library/Unbound`: the files of
/// [`KEPT_OF_FORMER`] move over where the new directory has none, the rest (caches, which are
/// made again) is removed. Nothing else of `Library/` is touched. Answers what to say when a
/// former directory was retired.
pub fn prepare_unbound_dir(project_root: &Path, dir: &Path) -> std::io::Result<Option<String>> {
    std::fs::create_dir_all(dir)?;
    // A directory outside the project is in no project's version control.
    let ignore = dir.join(".gitignore");
    if dir.starts_with(project_root) && !ignore.exists() {
        std::fs::write(&ignore, "*\n")?;
    }
    let former = project_root.join(FORMER_UNBOUND_DIR);
    // A link is not followed: only a directory of Unbound's own is retired.
    if !std::fs::symlink_metadata(&former).is_ok_and(|m| m.is_dir()) {
        return Ok(None);
    }
    let mut kept = Vec::new();
    for name in KEPT_OF_FORMER {
        let (from, to) = (former.join(name), dir.join(name));
        if std::fs::symlink_metadata(&from).is_ok() && std::fs::symlink_metadata(&to).is_err() {
            std::fs::rename(&from, &to)?;
            kept.push(*name);
        }
    }
    std::fs::remove_dir_all(&former)?;
    Ok(Some(format!(
        "{} removed: Unbound keeps its files in {} now and makes its caches again{}",
        former.display(),
        dir.display(),
        if kept.is_empty() {
            String::new()
        } else {
            format!(" (kept: {})", kept.join(", "))
        }
    )))
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
        let data = unbound_dir(&root);
        if let Some(refused) = refused_project_data(&root, &data) {
            anyhow::bail!("--project-data / {PROJECT_DATA_VARIABLE}: {refused}");
        }
        match prepare_unbound_dir(&root, &data) {
            Ok(Some(retired)) => tracing::warn!("{retired}"),
            Ok(None) => {}
            Err(e) => tracing::warn!("{}: {e}", data.display()),
        }
        settings::write_missing_input_manager(&root, &profile);
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
            inexact_packages: resolution.inexact,
        })
    }

    /// Unity's built-in shader sources for the install's release (downloaded once).
    pub fn builtin_shaders(&self) -> Result<builtin_shaders::BuiltinShaders> {
        let txt =
            std::fs::read_to_string(self.root.join("ProjectSettings/ProjectVersion.txt")).ok();
        builtin_shaders::BuiltinShaders::find(&self.install, txt.as_deref())
    }

    /// `<project>/LibraryUnbound`: everything Unbound keeps per project.
    pub fn unbound_dir(&self) -> PathBuf {
        unbound_dir(&self.root)
    }

    /// The asset folders the project holds itself, where the Editor writes (a new asset's `.meta`
    /// file, edited importer settings): `Assets` and the packages embedded in `Packages/` or kept
    /// in a local folder. Packages from the registry, the installation or Git are read-only.
    pub fn editable_roots(&self) -> Vec<&Path> {
        let mut roots = vec![self.assets.as_path()];
        roots.extend(
            self.packages
                .iter()
                .filter(|p| matches!(p.source, PackageSource::Embedded | PackageSource::Local))
                .map(|p| p.root.as_path()),
        );
        roots
    }

    /// The directories of the asset database's roots with the paths Unity gives them
    /// (`Assets`, `Packages/<name>`).
    pub fn asset_root_dirs(&self) -> Vec<(&Path, String)> {
        self.asset_roots()
            .into_iter()
            .map(|root| (root.dir, root.virtual_path))
            .collect()
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

    /// What the Editor writes into `ProjectSettings/ProjectVersion.txt` when it opens a project of
    /// its own release whose file names the release without its build revision: the release and
    /// the release with its revision (matches the Unity 6000.0.71f1 Editor,
    /// tests/projects/projectwrites `exit.ProjectSettings/ProjectVersion.txt`). Returns whether it
    /// wrote. A project of another release is left as it is (the Editor would upgrade it).
    pub fn complete_version_file(&self) -> Result<bool> {
        let path = self.root.join("ProjectSettings/ProjectVersion.txt");
        let text = std::fs::read_to_string(&path)?;
        if self.version != self.install.version || text.contains("m_EditorVersionWithRevision:") {
            return Ok(false);
        }
        let Some(revision) = builtin_shaders::revision(&self.install, None) else {
            return Ok(false);
        };
        let version = self.version;
        std::fs::write(
            &path,
            format!(
                "m_EditorVersion: {version}\nm_EditorVersionWithRevision: {version} ({revision})\n"
            ),
        )?;
        Ok(true)
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
        // The Editor imports a file without a `.meta` (writing one) where files can change: the
        // project's Assets and embedded and local folder packages; in a package it keeps in its
        // cache such a file is ignored.
        let mut roots = vec![(self.assets.as_path(), "Assets".to_string(), true)];
        roots.extend(self.packages.iter().map(|p| {
            (
                p.root.as_path(),
                format!("Packages/{}", p.name),
                matches!(p.source, PackageSource::Embedded | PackageSource::Local),
            )
        }));
        for (dir, virtual_path, mutable) in roots {
            let walker = walkdir::WalkDir::new(dir)
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
                    let relative = path.strip_prefix(dir).unwrap_or(path);
                    dlls.push((
                        path.to_path_buf(),
                        format!("{virtual_path}/{}", relative.to_string_lossy()),
                        mutable,
                    ));
                }
            }
        }
        let mut plugins: Vec<_> = dlls
            .into_par_iter()
            .filter_map(|(file, path, mutable)| plugins::read_plugin(&file, path, mutable))
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
    fn scratch_project(name: &str) -> std::path::PathBuf {
        let root =
            std::env::temp_dir().join(format!("unbound-project-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir_all(&root).unwrap();
        root
    }

    #[test]
    fn the_unbound_directory_is_made_with_an_ignore_file_and_leaves_library_alone() {
        let root = scratch_project("prepare");
        std::fs::create_dir_all(root.join("Library/PackageCache")).unwrap();
        let dir = super::unbound_dir_at(&root, None);
        assert_eq!(dir, root.join("LibraryUnbound"));
        assert_eq!(super::prepare_unbound_dir(&root, &dir).unwrap(), None);
        assert_eq!(
            std::fs::read_to_string(dir.join(".gitignore")).unwrap(),
            "*\n"
        );
        assert!(root.join("Library/PackageCache").is_dir());
        assert!(!root.join("Library/Unbound").exists());
        // An ignore file the user changed stays as it is.
        std::fs::write(dir.join(".gitignore"), "*\n!keep\n").unwrap();
        assert_eq!(super::prepare_unbound_dir(&root, &dir).unwrap(), None);
        assert_eq!(
            std::fs::read_to_string(dir.join(".gitignore")).unwrap(),
            "*\n!keep\n"
        );
        std::fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn a_former_directory_is_retired_once_keeping_what_is_no_cache() {
        let root = scratch_project("retire");
        let former = root.join("Library/Unbound");
        std::fs::create_dir_all(former.join("Imports")).unwrap();
        std::fs::create_dir_all(former.join("TestResults")).unwrap();
        std::fs::create_dir_all(former.join("ProjectView")).unwrap();
        std::fs::create_dir_all(root.join("Assets")).unwrap();
        std::fs::write(root.join("Assets/kept.txt"), "asset").unwrap();
        // The view's links are removed, not what they name.
        std::os::unix::fs::symlink(root.join("Assets"), former.join("ProjectView/Assets")).unwrap();
        std::fs::write(former.join("Imports/entry.bin"), "cache").unwrap();
        std::fs::write(former.join("TestResults/EditMode.xml"), "<r/>").unwrap();
        std::fs::write(former.join("EditorUserSettings.asset"), "settings").unwrap();
        std::fs::write(former.join("Editor.log"), "old log").unwrap();
        std::fs::write(root.join("Library/Other.txt"), "unity's").unwrap();
        // A log the new directory has already is not replaced.
        let dir = super::unbound_dir_at(&root, None);
        std::fs::create_dir_all(&dir).unwrap();
        std::fs::write(dir.join("Editor.log"), "new log").unwrap();

        let said = super::prepare_unbound_dir(&root, &dir).unwrap().unwrap();
        assert!(
            said.contains("removed") && said.contains("EditorUserSettings.asset, TestResults"),
            "{said}"
        );
        assert!(!former.exists());
        assert_eq!(
            std::fs::read_to_string(root.join("Library/Other.txt")).unwrap(),
            "unity's"
        );
        assert_eq!(
            std::fs::read_to_string(root.join("Assets/kept.txt")).unwrap(),
            "asset"
        );
        assert_eq!(
            std::fs::read_to_string(dir.join("EditorUserSettings.asset")).unwrap(),
            "settings"
        );
        assert_eq!(
            std::fs::read_to_string(dir.join("TestResults/EditMode.xml")).unwrap(),
            "<r/>"
        );
        assert_eq!(
            std::fs::read_to_string(dir.join("Editor.log")).unwrap(),
            "new log"
        );
        assert!(!dir.join("Imports").exists());
        // Once: nothing is left to retire.
        assert_eq!(super::prepare_unbound_dir(&root, &dir).unwrap(), None);
        std::fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn another_place_is_relative_to_the_project_or_absolute() {
        let root = scratch_project("place");
        let outside = scratch_project("place-outside").join("data");
        let relative = super::unbound_dir_at(&root, Some(Path::new("Build/./UnboundData")));
        assert_eq!(relative, root.join("Build/UnboundData"));
        assert_eq!(super::refused_project_data(&root, &relative), None);
        assert_eq!(super::prepare_unbound_dir(&root, &relative).unwrap(), None);
        assert_eq!(
            std::fs::read_to_string(relative.join(".gitignore")).unwrap(),
            "*\n"
        );

        let absolute = super::unbound_dir_at(&root, Some(&outside));
        assert_eq!(absolute, outside);
        assert_eq!(super::refused_project_data(&root, &absolute), None);
        // The former directory is retired whatever the place; outside the project no ignore
        // file is written.
        std::fs::create_dir_all(root.join("Library/Unbound/Imports")).unwrap();
        assert!(
            super::prepare_unbound_dir(&root, &absolute)
                .unwrap()
                .is_some()
        );
        assert!(!root.join("Library/Unbound").exists());
        assert!(absolute.is_dir() && !absolute.join(".gitignore").exists());
        assert!(!root.join("LibraryUnbound").exists());
        std::fs::remove_dir_all(&root).unwrap();
        std::fs::remove_dir_all(outside.parent().unwrap()).unwrap();
    }

    #[test]
    fn the_authored_folders_library_and_the_project_itself_are_refused() {
        let root = scratch_project("refused");
        let elsewhere = scratch_project("refused-elsewhere");
        for asked in [
            "Assets",
            "Assets/Data",
            "Packages/x",
            "ProjectSettings",
            "Library",
            "Library/Unbound",
            ".",
            "Sub/..",
            "Sub/../Assets/Data",
        ] {
            let dir = super::unbound_dir_at(&root, Some(Path::new(asked)));
            assert!(
                super::refused_project_data(&root, &dir).is_some(),
                "{asked}"
            );
        }
        let absolute = super::unbound_dir_at(&root, Some(&root.join("Assets/Deep")));
        assert!(super::refused_project_data(&root, &absolute).is_some());
        // A link that leads into an authored folder is refused by where it lands.
        std::fs::create_dir_all(root.join("Assets/Real")).unwrap();
        std::os::unix::fs::symlink(root.join("Assets/Real"), elsewhere.join("link")).unwrap();
        assert!(super::refused_project_data(&root, &elsewhere.join("link")).is_some());
        for asked in ["LibraryUnbound", "AssetsData", "Build/Data"] {
            let dir = super::unbound_dir_at(&root, Some(Path::new(asked)));
            assert_eq!(super::refused_project_data(&root, &dir), None, "{asked}");
        }
        std::fs::remove_dir_all(&root).unwrap();
        std::fs::remove_dir_all(&elsewhere).unwrap();
    }

    /// No source spells the former place: every path into Unbound's directory goes through
    /// `unbound_dir` (Rust) or `ProjectInfo.UnboundDirectory` (managed), so nothing can make
    /// `Library/Unbound` again. Allowed: this file (the constant that retires it and its tests)
    /// the checks that the former place is refused, and the README's note on its retirement.
    #[test]
    fn no_source_names_the_former_directory() {
        const ALLOWED: &[&str] = &[
            "crates/unbound-project/src/lib.rs",
            "managed/UnityEngine.Tests/ConsoleLogTests.cs",
            "tests/projects/appfacts/test_compare.py",
        ];
        const SPELLINGS: &[&str] = &[
            "Library/Unbound",
            "\"Library\", \"Unbound\"",
            "'Library'/'Unbound'",
            "'Library', 'Unbound'",
            "join(\"Library\").join(\"Unbound\")",
        ];
        fn scan(dir: &Path, repo: &Path, found: &mut Vec<String>) {
            for entry in std::fs::read_dir(dir).unwrap().flatten() {
                let path = entry.path();
                let name = entry.file_name().to_string_lossy().into_owned();
                if entry.file_type().is_ok_and(|t| t.is_symlink()) {
                    continue;
                }
                if path.is_dir() {
                    if !matches!(
                        name.as_str(),
                        "target"
                            | "bin"
                            | "obj"
                            | "Generated"
                            | "Library"
                            | "LibraryUnbound"
                            | "Reference"
                    ) && !name.starts_with('.')
                    {
                        scan(&path, repo, found);
                    }
                    continue;
                }
                let source = matches!(
                    path.extension().and_then(|e| e.to_str()),
                    Some("rs" | "cs" | "py" | "sh" | "md" | "toml" | "json")
                );
                let relative = path
                    .strip_prefix(repo)
                    .unwrap()
                    .to_string_lossy()
                    .replace('\\', "/");
                if !source || ALLOWED.contains(&relative.as_str()) {
                    continue;
                }
                let Ok(text) = std::fs::read_to_string(&path) else {
                    continue;
                };
                if SPELLINGS.iter().any(|s| text.contains(s)) {
                    found.push(relative);
                }
            }
        }
        let repo = Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../..")
            .canonicalize()
            .unwrap();
        let mut found = Vec::new();
        for part in ["crates", "managed", "tools", "tests", "docs"] {
            scan(&repo.join(part), &repo, &mut found);
        }
        // README.md is left out: it says what becomes of a directory an earlier version left.
        for file in ["AGENTS.md"] {
            let text = std::fs::read_to_string(repo.join(file)).unwrap();
            if SPELLINGS.iter().any(|s| text.contains(s)) {
                found.push(file.to_string());
            }
        }
        assert!(
            found.is_empty(),
            "the former Library/Unbound is spelled in: {found:#?}"
        );
    }

    #[test]
    fn a_link_in_the_former_place_is_not_followed() {
        let root = scratch_project("retire-link");
        let elsewhere = scratch_project("retire-link-target");
        std::fs::write(elsewhere.join("file"), "theirs").unwrap();
        std::fs::create_dir_all(root.join("Library")).unwrap();
        std::os::unix::fs::symlink(&elsewhere, root.join("Library/Unbound")).unwrap();
        let dir = super::unbound_dir_at(&root, None);
        assert_eq!(super::prepare_unbound_dir(&root, &dir).unwrap(), None);
        assert_eq!(
            std::fs::read_to_string(elsewhere.join("file")).unwrap(),
            "theirs"
        );
        std::fs::remove_dir_all(&root).unwrap();
        std::fs::remove_dir_all(&elsewhere).unwrap();
    }

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
                inexact_packages: Vec::new(),
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

    /// A plugin DLL without a `.meta` counts in the project's Assets and an embedded package, not
    /// in a package the Editor keeps in its cache.
    #[test]
    fn plugins_without_a_meta_count_where_the_editor_imports_them() {
        let scratch = std::env::temp_dir().join(format!(
            "unbound-plugins-no-meta-{}-{:?}",
            std::process::id(),
            std::thread::current().id()
        ));
        let _ = std::fs::remove_dir_all(&scratch);
        let root = scratch.join("project");
        let dll = Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../tests/projects/compilesetup/Assets/Plugins/Dup/ProbeDup.dll");
        let place = |dir: &Path| {
            std::fs::create_dir_all(dir).unwrap();
            std::fs::copy(&dll, dir.join("ProbeDup.dll")).unwrap();
        };
        place(&root.join("Assets/Libs"));
        let package = |name: &str, source: PackageSource| {
            let dir = scratch.join(name);
            place(&dir);
            Package {
                name: name.into(),
                version: "1.0.0".into(),
                source,
                root: dir,
                testable: false,
            }
        };
        let data = scratch.join("install/Editor/Data");
        let version: UnityVersion = "6000.0.71f1".parse().unwrap();
        let project = Project {
            assets: root.join("Assets"),
            root: root.clone(),
            version,
            profile: Profile::for_version(version).unwrap(),
            install: UnityInstall { data, version },
            settings: Settings::default(),
            packages: vec![
                package("com.example.embedded", PackageSource::Embedded),
                package("com.example.cached", PackageSource::Registry),
            ],
            missing_packages: Vec::new(),
            inexact_packages: Vec::new(),
        };
        let paths: Vec<String> = project.plugins().into_iter().map(|p| p.path).collect();
        let _ = std::fs::remove_dir_all(&scratch);
        assert_eq!(
            paths,
            [
                "Assets/Libs/ProbeDup.dll",
                "Packages/com.example.embedded/ProbeDup.dll"
            ]
        );
    }

    /// In the Editor the predefined assemblies reference the auto-referenced Editor-only
    /// assemblies too (runtime scripts use them under `#if UNITY_EDITOR`); a player build has none
    /// (Unity 6000.0.71f1, tests/projects/compilesetup `assembly.Assembly-CSharp.references`).
    #[test]
    fn predefined_assemblies_reference_editor_only_assemblies_in_the_editor() {
        let scratch = std::env::temp_dir().join(format!(
            "unbound-editor-only-refs-{}-{:?}",
            std::process::id(),
            std::thread::current().id()
        ));
        let _ = std::fs::remove_dir_all(&scratch);
        let root = scratch.join("project");
        let write = |path: &str, text: &str| {
            let file = root.join(path);
            std::fs::create_dir_all(file.parent().unwrap()).unwrap();
            std::fs::write(file, text).unwrap();
        };
        write("Assets/A.cs", "class A {}\n");
        for (name, auto) in [("EditorAuto", true), ("EditorHidden", false)] {
            write(
                &format!("Assets/{name}/{name}.asmdef"),
                &format!(
                    r#"{{"name": "{name}", "includePlatforms": ["Editor"], "autoReferenced": {auto}}}"#
                ),
            );
            write(&format!("Assets/{name}/{name}.cs"), "class B {}\n");
        }
        write("Assets/Runtime/Runtime.asmdef", r#"{"name": "Runtime"}"#);
        write("Assets/Runtime/R.cs", "class R {}\n");
        let data = scratch.join("install/Editor/Data");
        std::fs::create_dir_all(data.join("Managed/UnityEngine")).unwrap();
        let version: UnityVersion = "6000.0.71f1".parse().unwrap();
        let project = Project {
            assets: root.join("Assets"),
            root: root.clone(),
            version,
            profile: Profile::for_version(version).unwrap(),
            install: UnityInstall { data, version },
            settings: Settings::default(),
            packages: Vec::new(),
            missing_packages: Vec::new(),
            inexact_packages: Vec::new(),
        };
        let editor = project
            .script_graph(CompileMode::EditorPlayMode, "Standalone")
            .unwrap();
        let player = project
            .script_graph(CompileMode::Player, "Standalone")
            .unwrap();
        let _ = std::fs::remove_dir_all(&scratch);
        let references = |graph: &Graph| graph.get("Assembly-CSharp").unwrap().references.clone();
        assert_eq!(references(&editor), ["EditorAuto", "Runtime"]);
        assert_eq!(references(&player), ["Runtime"]);
        assert!(player.get("EditorAuto").is_none());
    }

    /// Plugins with one assembly name: of those with one file name an assembly references the
    /// highest version enabled for the platform; a lower version under another file name is still
    /// referenced by that name (Unity 6000.0.71f1, tests/projects/compilesetup `plugins.*`,
    /// `assembly.Probe.Explicit*.plugins`).
    #[test]
    fn plugins_of_one_file_name_bind_the_highest_version() {
        let scratch = std::env::temp_dir().join(format!(
            "unbound-plugin-versions-{}-{:?}",
            std::process::id(),
            std::thread::current().id()
        ));
        let _ = std::fs::remove_dir_all(&scratch);
        let root = scratch.join("project");
        let probe = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../tests/projects/compilesetup");
        let v1 = probe.join("Assets/Plugins/Dup/ProbeDup.dll");
        let v2 = probe.join("Packages/com.probe.setup/Plugins/ProbeDup.dll");
        let any = "PluginImporter:\n  platformData:\n    Any:\n      enabled: 1\n";
        let explicit = "PluginImporter:\n  isExplicitlyReferenced: 1\n  platformData:\n    Any:\n      enabled: 1\n";
        let android = "PluginImporter:\n  platformData:\n    Any:\n      enabled: 0\n    Android:\n      enabled: 1\n";
        let place = |dir: &str, name: &str, from: &Path, importer: &str| {
            let file = root.join("Assets").join(dir).join(name);
            std::fs::create_dir_all(file.parent().unwrap()).unwrap();
            std::fs::copy(from, &file).unwrap();
            std::fs::write(
                format!("{}.meta", file.display()),
                format!("fileFormatVersion: 2\nguid: {:032x}\n{importer}", dir.len()),
            )
            .unwrap();
        };
        // The older version first by path.
        place("A", "ProbeDup.dll", &v1, any);
        place("C", "ProbeDup.dll", &v2, any);
        // The newest for Android only: in the Editor it is no candidate.
        place("D", "ProbeDup.dll", &v2, android);
        // The older version under another file name, referenced only by name.
        place("E", "Renamed.dll", &v1, explicit);
        // The same version as C's under another file name: referenced as well (two copies of one
        // assembly then fail to compile with CS1704, in the Editor too).
        place("F", "Other.dll", &v2, any);
        std::fs::write(root.join("Assets/A.cs"), "class A {}\n").unwrap();
        std::fs::create_dir_all(root.join("Assets/Explicit")).unwrap();
        std::fs::write(
            root.join("Assets/Explicit/Explicit.asmdef"),
            r#"{"name": "Explicit", "overrideReferences": true, "precompiledReferences": ["ProbeDup.dll", "Renamed.dll"]}"#,
        )
        .unwrap();
        std::fs::write(root.join("Assets/Explicit/E.cs"), "class E {}\n").unwrap();
        let data = scratch.join("install/Editor/Data");
        std::fs::create_dir_all(data.join("Managed/UnityEngine")).unwrap();
        let version: UnityVersion = "6000.0.71f1".parse().unwrap();
        let project = Project {
            assets: root.join("Assets"),
            root: root.clone(),
            version,
            profile: Profile::for_version(version).unwrap(),
            install: UnityInstall { data, version },
            settings: Settings::default(),
            packages: Vec::new(),
            missing_packages: Vec::new(),
            inexact_packages: Vec::new(),
        };
        let editor = project
            .script_graph(CompileMode::EditorPlayMode, "Android")
            .unwrap();
        let player = project
            .script_graph(CompileMode::Player, "Android")
            .unwrap();
        let _ = std::fs::remove_dir_all(&scratch);
        let names = |graph: &Graph, assembly: &str| -> Vec<String> {
            let assembly = graph.get(assembly).unwrap();
            assembly
                .plugins
                .iter()
                .map(|p| p.strip_prefix(&root).unwrap().display().to_string())
                .collect()
        };
        assert_eq!(
            names(&editor, "Assembly-CSharp"),
            ["Assets/C/ProbeDup.dll", "Assets/F/Other.dll"]
        );
        assert_eq!(
            names(&editor, "Explicit"),
            ["Assets/C/ProbeDup.dll", "Assets/E/Renamed.dll"]
        );
        // For Android the platform's copy is a candidate too, of the same version as C's and
        // later by path.
        assert_eq!(
            names(&player, "Assembly-CSharp"),
            ["Assets/C/ProbeDup.dll", "Assets/F/Other.dll"]
        );
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
            inexact_packages: Vec::new(),
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
