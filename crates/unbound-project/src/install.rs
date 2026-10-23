//! A Unity installation, used only as a source of reference assemblies and built-in packages.
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
    /// `UNBOUND_UNITY` (an `Editor` or `Editor/Data` directory or the `Unity` binary) or the Hub
    /// installation of the project's exact release, else the newest installed release of the same
    /// family.
    pub fn find(version: UnityVersion) -> Result<Self> {
        if let Ok(explicit) = std::env::var("UNBOUND_UNITY") {
            return Self::at(Path::new(&explicit), version);
        }
        let hub = home().join("Unity/Hub/Editor");
        let exact = hub.join(version.to_string()).join("Editor");
        if exact.is_dir() {
            return Self::at(&exact, version);
        }
        let mut candidates: Vec<UnityVersion> = std::fs::read_dir(&hub)
            .into_iter()
            .flatten()
            .filter_map(|e| e.ok()?.file_name().to_str()?.parse().ok())
            .filter(|v: &UnityVersion| v.family() == version.family())
            .collect();
        candidates.sort();
        if let Some(nearest) = candidates.last() {
            tracing::warn!(
                "Unity {version} is not installed; using {nearest} for reference assemblies"
            );
            return Self::at(&hub.join(nearest.to_string()).join("Editor"), *nearest);
        }
        bail!(
            "no Unity {version} installation found (looked in {}); set UNBOUND_UNITY",
            hub.display()
        )
    }

    fn at(path: &Path, version: UnityVersion) -> Result<Self> {
        let mut data = path.to_path_buf();
        if data.file_name().is_some_and(|n| n == "Unity") && data.is_file() {
            data.pop();
        }
        if data.join("Data").is_dir() {
            data = data.join("Data");
        }
        if !data.join("Managed").is_dir() {
            bail!("{} is not a Unity Editor/Data directory", data.display());
        }
        Ok(Self { data, version })
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
        let engines = self.data.join("PlaybackEngines");
        let mut platforms: Vec<PathBuf> = std::fs::read_dir(&engines)
            .into_iter()
            .flatten()
            .flatten()
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

pub fn home() -> PathBuf {
    std::env::var_os("HOME")
        .map(PathBuf::from)
        .unwrap_or_else(|| "/".into())
}
