//! A Unity Editor release's files: reference assemblies, class libraries, `CGIncludes`, built-in
//! resources and packages. They come from an installation of the release when there is one, else
//! from the release's Editor archive (see [`crate::editor_archive`]); the Editor is never run.
use anyhow::{Result, bail};
use std::path::{Path, PathBuf};
use unbound_compat::UnityVersion;

#[derive(Debug, Clone)]
pub struct UnityInstall {
    /// `.../Editor/Data`
    pub data: PathBuf,
    pub version: UnityVersion,
}

impl UnityInstall {
    /// The files for a project on `project` whose compatibility profile was captured from
    /// `release`: `UNBOUND_UNITY` (an installation, its `Editor`, `Editor/Data`, `Unity.app` or
    /// `Unity.app/Contents` directory, or the `Unity` binary), else the Hub's installation of the
    /// project's own release, else of the profile's release, else the profile's release unpacked
    /// from its downloads (once).
    pub fn find(project: UnityVersion, release: UnityVersion) -> Result<Self> {
        if let Ok(explicit) = std::env::var("UNBOUND_UNITY") {
            return Self::at(Path::new(&explicit), project);
        }
        for version in [project, release] {
            if let Some(data) = installed_data(&version.to_string()) {
                return Self::at(&data, version);
            }
        }
        let data = crate::editor_archive::fetch(release)?;
        Ok(Self {
            data,
            version: release,
        })
    }

    fn at(path: &Path, version: UnityVersion) -> Result<Self> {
        let mut path = path.to_path_buf();
        if path.file_name().is_some_and(|n| n == "Unity") && path.is_file() {
            path.pop();
        }
        let Some(data) = data_dir(&path) else {
            bail!("{} is not a Unity installation (no Editor/Data or Unity.app/Contents)", path.display());
        };
        Ok(Self { data, version })
    }

    /// The installation's directory (`.../Hub/Editor/<version>`), holding `modules.json`.
    pub fn root(&self) -> Option<&Path> {
        self.data.parent()?.parent()
    }

    /// The `Unity` executable: `Editor/Unity` on Linux, `Unity.app/Contents/MacOS/Unity` on macOS.
    pub fn editor_executable(&self) -> PathBuf {
        let mac = self.data.join("MacOS/Unity");
        if mac.is_file() { mac } else { self.data.join("../Unity") }
    }

    /// The directories of the installed platform modules (`AndroidPlayer`, ...).
    pub fn playback_engines_dirs(&self) -> Vec<PathBuf> {
        playback_engines(&self.data)
    }

    pub fn managed_engine_dir(&self) -> PathBuf {
        self.data.join("Managed/UnityEngine")
    }

    /// The editor's modules outside `Managed/UnityEngine`: `Managed/UnityEditor.Graphs.dll` and
    /// each platform's `PlaybackEngines/*/UnityEditor.*.dll` and `Unity.Android.*.dll`. Editor
    /// play mode compiles game code against them (`Project::engine_references`), and the runtime's
    /// facades and stubs cover them, from this one list so the two cannot drift.
    pub fn platform_modules(&self) -> Vec<PathBuf> {
        let mut out = Vec::new();
        let graphs = self.data.join("Managed/UnityEditor.Graphs.dll");
        if graphs.exists() {
            out.push(graphs);
        }
        let mut platforms: Vec<PathBuf> = self
            .playback_engines_dirs()
            .iter()
            .flat_map(|engines| std::fs::read_dir(engines).into_iter().flatten().flatten())
            .map(|e| e.path())
            .collect();
        platforms.sort();
        for platform in platforms {
            out.extend(crate::compile::glob_dlls(&platform, "UnityEditor."));
            out.extend(crate::compile::glob_dlls(&platform, "Unity.Android."));
        }
        out
    }

    /// The release's API reference: the files `gen-stubs` and `api-check` read Unity's declarations
    /// from. The stubs are committed, so they must not depend on the machine generating them, but
    /// Editor builds for different OSes list the same members and attributes in different metadata
    /// orders and ship different players (`LinuxStandaloneSupport`, `MacStandaloneSupport`), and an
    /// installation has whichever modules its owner chose. The reference is therefore always the
    /// release's Linux Editor with the default modules (android, ios): this installation when it is
    /// one with those modules, else the release's downloads ([`crate::editor_archive`]).
    pub fn api_reference(&self) -> Result<ApiReference> {
        let data = if cfg!(target_os = "linux") && has_api_reference_platforms(&self.data) {
            self.data.clone()
        } else {
            tracing::info!(
                "{} is not a Linux Editor with the {API_REFERENCE_PLATFORMS:?} modules: reading \
                 Unity {}'s API from its Linux Editor downloads",
                self.data.display(),
                self.version
            );
            let data = crate::editor_archive::fetch_api_reference(self.version)?;
            if !has_api_reference_platforms(&data) {
                bail!(
                    "Unity {}'s Linux Editor files in {} lack one of {API_REFERENCE_PLATFORMS:?}",
                    self.version,
                    data.display()
                );
            }
            data
        };
        let install = UnityInstall {
            data,
            version: self.version,
        };
        let platform_modules = install
            .platform_modules()
            .into_iter()
            .filter(|module| is_api_reference_module(&install.data, module))
            .collect();
        Ok(ApiReference {
            managed_engine_dir: install.managed_engine_dir(),
            platform_modules,
        })
    }

    pub fn builtin_packages_dir(&self) -> PathBuf {
        self.data.join("Resources/PackageManager/BuiltInPackages")
    }
}

/// The `PlaybackEngines` folders of the API reference ([`UnityInstall::api_reference`]): the Linux
/// Editor's own player and the folders of the default modules (`editor_archive`'s android, ios).
pub const API_REFERENCE_PLATFORMS: &[&str] =
    &["AndroidPlayer", "LinuxStandaloneSupport", "iOSSupport"];

/// Unity's declarations as `gen-stubs` and `api-check` read them, the same on every machine.
#[derive(Debug, Clone)]
pub struct ApiReference {
    /// `Editor/Data/Managed/UnityEngine`
    pub managed_engine_dir: PathBuf,
    /// [`UnityInstall::platform_modules`] of the [`API_REFERENCE_PLATFORMS`] only.
    pub platform_modules: Vec<PathBuf>,
}

fn has_api_reference_platforms(data: &Path) -> bool {
    data.ends_with("Editor/Data")
        && API_REFERENCE_PLATFORMS
            .iter()
            .all(|platform| data.join("PlaybackEngines").join(platform).is_dir())
}

/// Whether a platform module of the installation in `data` is part of the API reference: outside
/// `PlaybackEngines` (`Managed/UnityEditor.Graphs.dll`) or in one of [`API_REFERENCE_PLATFORMS`].
fn is_api_reference_module(data: &Path, module: &Path) -> bool {
    match module.strip_prefix(data.join("PlaybackEngines")) {
        Ok(rel) => rel.components().next().is_some_and(|platform| {
            API_REFERENCE_PLATFORMS
                .iter()
                .any(|p| platform.as_os_str() == *p)
        }),
        Err(_) => true,
    }
}

/// The directory of an installation that holds `Managed`, `Resources`, `CGIncludes`, ...:
/// `Editor/Data` on Linux and Windows, `Unity.app/Contents` on macOS. `path` is that directory or
/// one above it.
pub fn data_dir(path: &Path) -> Option<PathBuf> {
    ["", "Data", "Editor/Data", "Contents", "Unity.app/Contents"]
        .iter()
        .map(|sub| if sub.is_empty() { path.to_path_buf() } else { path.join(sub) })
        .find(|dir| dir.join("Managed").is_dir() && dir.join("Resources").is_dir())
}

/// The directories of the platform modules of the installation whose data directory is `data`:
/// `Editor/Data/PlaybackEngines` on Linux and Windows; on macOS the Editor's own player in
/// `Unity.app/Contents/PlaybackEngines` and the installed modules beside `Unity.app`.
pub fn playback_engines(data: &Path) -> Vec<PathBuf> {
    let mut dirs = vec![data.join("PlaybackEngines")];
    if data.ends_with("Contents") {
        dirs.push(data.join("../../PlaybackEngines"));
    }
    dirs.retain(|dir| dir.is_dir());
    dirs
}

/// Where Unity Hub installs editors: `~/Unity/Hub/Editor`, on macOS also
/// `/Applications/Unity/Hub/Editor`, and the Hub's own install path setting.
pub fn hub_dirs() -> Vec<PathBuf> {
    let mut dirs = vec![home().join("Unity/Hub/Editor")];
    if cfg!(target_os = "macos") {
        dirs.push(PathBuf::from("/Applications/Unity/Hub/Editor"));
    }
    let config = if cfg!(target_os = "macos") {
        home().join("Library/Application Support/UnityHub")
    } else {
        home().join(".config/UnityHub")
    };
    // The Hub stores its "Installs location" as a JSON string.
    if let Ok(text) = std::fs::read_to_string(config.join("secondaryInstallPath.json"))
        && let Ok(path) = serde_json::from_str::<String>(&text)
        && !path.is_empty()
    {
        dirs.insert(0, PathBuf::from(path));
    }
    dirs.dedup();
    dirs
}

/// The data directory of the Hub installation of exactly `version`, if there is one.
pub fn installed_data(version: &str) -> Option<PathBuf> {
    hub_dirs().iter().find_map(|hub| data_dir(&hub.join(version)))
}

pub fn home() -> PathBuf {
    std::env::var_os("HOME")
        .map(PathBuf::from)
        .unwrap_or_else(|| "/".into())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn touch(path: &Path) {
        std::fs::create_dir_all(path.parent().unwrap()).unwrap();
        std::fs::write(path, b"").unwrap();
    }

    #[test]
    fn api_reference_modules_are_the_reference_platforms_only() {
        let root = std::env::temp_dir().join(format!("unbound-apiref-{}", std::process::id()));
        let data = root.join("Editor/Data");
        for rel in [
            "Managed/UnityEditor.Graphs.dll",
            "PlaybackEngines/AndroidPlayer/UnityEditor.Android.Extensions.dll",
            "PlaybackEngines/AndroidPlayer/Unity.Android.Gradle.dll",
            "PlaybackEngines/LinuxStandaloneSupport/UnityEditor.LinuxStandalone.Extensions.dll",
            "PlaybackEngines/iOSSupport/UnityEditor.iOS.Extensions.dll",
            "PlaybackEngines/WebGLSupport/UnityEditor.WebGL.Extensions.dll",
            "PlaybackEngines/MacStandaloneSupport/UnityEditor.OSXStandalone.Extensions.dll",
        ] {
            touch(&data.join(rel));
        }
        assert!(has_api_reference_platforms(&data));
        let install = UnityInstall {
            data: data.clone(),
            version: "6000.0.71f1".parse().unwrap(),
        };
        let kept: Vec<String> = install
            .platform_modules()
            .iter()
            .filter(|module| is_api_reference_module(&data, module))
            .map(|module| {
                module
                    .strip_prefix(&data)
                    .unwrap()
                    .to_string_lossy()
                    .into_owned()
            })
            .collect();
        assert_eq!(
            kept,
            [
                "Managed/UnityEditor.Graphs.dll",
                "PlaybackEngines/AndroidPlayer/UnityEditor.Android.Extensions.dll",
                "PlaybackEngines/AndroidPlayer/Unity.Android.Gradle.dll",
                "PlaybackEngines/LinuxStandaloneSupport/UnityEditor.LinuxStandalone.Extensions.dll",
                "PlaybackEngines/iOSSupport/UnityEditor.iOS.Extensions.dll",
            ]
        );
        std::fs::remove_dir_all(&root).unwrap();
    }

    #[test]
    fn an_install_without_a_reference_platform_or_of_another_os_is_not_the_reference() {
        let root = std::env::temp_dir().join(format!("unbound-notref-{}", std::process::id()));
        let data = root.join("Editor/Data");
        for platform in ["AndroidPlayer", "LinuxStandaloneSupport"] {
            std::fs::create_dir_all(data.join("PlaybackEngines").join(platform)).unwrap();
        }
        assert!(!has_api_reference_platforms(&data));
        // A macOS installation's data directory, even with every reference platform folder.
        let mac = root.join("Unity.app/Contents");
        for platform in API_REFERENCE_PLATFORMS {
            std::fs::create_dir_all(mac.join("PlaybackEngines").join(platform)).unwrap();
        }
        assert!(!has_api_reference_platforms(&mac));
        std::fs::remove_dir_all(&root).unwrap();
    }
}
