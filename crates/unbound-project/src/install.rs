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
    /// The files of `release`, the Editor release a project's compatibility profile was captured
    /// from: `UNBOUND_UNITY` (an installation, its `Editor`, `Editor/Data`, `Unity.app` or
    /// `Unity.app/Contents` directory, or the `Unity` binary), else the Hub's installation of the
    /// release, else its files unpacked from its Editor archive (downloaded once).
    pub fn find(release: UnityVersion) -> Result<Self> {
        if let Ok(explicit) = std::env::var("UNBOUND_UNITY") {
            return Self::at(Path::new(&explicit), release);
        }
        if let Some(data) = installed_data(&release.to_string()) {
            return Self::at(&data, release);
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

    pub fn builtin_packages_dir(&self) -> PathBuf {
        self.data.join("Resources/PackageManager/BuiltInPackages")
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
