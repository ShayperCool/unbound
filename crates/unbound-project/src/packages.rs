//! Package resolution without the Editor: `Packages/manifest.json` and `packages-lock.json` name the
//! packages; their contents come from the project (embedded, local), the Unity installation
//! (built-in), or a package cache (registry and git packages).
use crate::install::UnityInstall;
use anyhow::{Context, Result};
use serde::Deserialize;
use std::{
    collections::BTreeMap,
    path::{Path, PathBuf},
};

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PackageSource {
    Embedded,
    Local,
    BuiltIn,
    Registry,
    Git,
}

#[derive(Debug, Clone)]
pub struct Package {
    pub name: String,
    pub version: String,
    pub source: PackageSource,
    pub root: PathBuf,
    /// Listed in the manifest's `testables`.
    pub testable: bool,
}

#[derive(Debug, Default)]
pub struct Resolution {
    pub packages: Vec<Package>,
    /// Packages the lock names that no source has: `(name, version, source)`.
    pub missing: Vec<(String, String, String)>,
}

#[derive(Deserialize)]
struct Lock {
    dependencies: BTreeMap<String, LockEntry>,
}

#[derive(Deserialize)]
struct LockEntry {
    version: String,
    source: String,
    #[serde(default)]
    hash: Option<String>,
}

#[derive(Deserialize)]
struct Manifest {
    #[serde(default)]
    dependencies: BTreeMap<String, String>,
    #[serde(default)]
    testables: Vec<String>,
}

#[derive(Deserialize)]
struct PackageJson {
    name: String,
    #[serde(default)]
    version: String,
}

/// The directories searched for registry and git packages, in order: the project's own
/// `Library/PackageCache`, the Unbound package store, and `UNBOUND_PACKAGE_CACHES`
/// (colon-separated `PackageCache`-like directories, for example another checkout's).
pub fn package_caches(project: &Path) -> Vec<PathBuf> {
    let mut caches = vec![project.join("Library/PackageCache")];
    caches.push(crate::cache_root().join("packages"));
    if let Ok(extra) = std::env::var("UNBOUND_PACKAGE_CACHES") {
        caches.extend(
            extra
                .split(':')
                .filter(|s| !s.is_empty())
                .map(PathBuf::from),
        );
    }
    caches
}

pub fn resolve(project: &Path, install: &UnityInstall) -> Result<Resolution> {
    let packages_dir = project.join("Packages");
    let manifest: Manifest = serde_json::from_slice(
        &std::fs::read(packages_dir.join("manifest.json")).context("Packages/manifest.json")?,
    )?;
    let lock: Option<Lock> = std::fs::read(packages_dir.join("packages-lock.json"))
        .ok()
        .map(|bytes| serde_json::from_slice(&bytes))
        .transpose()
        .context("Packages/packages-lock.json")?;

    // Every package the lock knows, else the manifest's direct dependencies.
    let mut wanted: BTreeMap<String, (String, String, Option<String>)> = BTreeMap::new();
    match lock {
        Some(lock) => {
            for (name, entry) in lock.dependencies {
                wanted.insert(name, (entry.version, entry.source, entry.hash));
            }
        }
        None => {
            for (name, version) in &manifest.dependencies {
                let source = if version.starts_with("file:") {
                    "local"
                } else if version.contains(".git") || version.starts_with("git") {
                    "git"
                } else if name.starts_with("com.unity.modules.") {
                    "builtin"
                } else {
                    "registry"
                };
                wanted.insert(name.clone(), (version.clone(), source.into(), None));
            }
        }
    }
    // Embedded packages: any folder of `Packages/` with a package.json wins over the lock.
    for entry in std::fs::read_dir(&packages_dir)?.flatten() {
        let root = entry.path();
        if let Some(json) = read_package_json(&root) {
            wanted.insert(json.name, (json.version, "embedded".into(), None));
        }
    }

    let caches = package_caches(project);
    let builtin = install.builtin_packages_dir();
    let mut resolution = Resolution::default();
    for (name, (version, source, _hash)) in wanted {
        let found = match source.as_str() {
            "embedded" => find_embedded(&packages_dir, &name)
                .map(|root| (root, PackageSource::Embedded, version.clone())),
            "local" => {
                let path = version.trim_start_matches("file:");
                let root = if Path::new(path).is_absolute() {
                    PathBuf::from(path)
                } else {
                    packages_dir.join(path)
                };
                read_package_json(&root).map(|json| (root, PackageSource::Local, json.version))
            }
            "builtin" => {
                let root = builtin.join(&name);
                read_package_json(&root).map(|json| (root, PackageSource::BuiltIn, json.version))
            }
            other => {
                let kind = if other == "git" {
                    PackageSource::Git
                } else {
                    PackageSource::Registry
                };
                find_in_caches(
                    &caches,
                    &name,
                    (kind == PackageSource::Registry).then_some(&*version),
                )
                .map(|(root, found_version)| (root, kind, found_version))
            }
        };
        match found {
            Some((root, source, version)) => resolution.packages.push(Package {
                testable: manifest.testables.contains(&name),
                name,
                version,
                source,
                root,
            }),
            None => resolution.missing.push((name, version, source)),
        }
    }
    Ok(resolution)
}

fn read_package_json(root: &Path) -> Option<PackageJson> {
    let bytes = std::fs::read(root.join("package.json")).ok()?;
    serde_json::from_slice(&bytes).ok()
}

fn find_embedded(packages_dir: &Path, name: &str) -> Option<PathBuf> {
    std::fs::read_dir(packages_dir)
        .ok()?
        .flatten()
        .find_map(|entry| {
            let root = entry.path();
            (read_package_json(&root)?.name == name).then_some(root)
        })
}

/// `name@<anything>` (Unity's cache layout) or `name` directories; for a registry package the
/// version in its package.json has to match.
fn find_in_caches(
    caches: &[PathBuf],
    name: &str,
    version: Option<&str>,
) -> Option<(PathBuf, String)> {
    let mut fallback = None;
    for cache in caches {
        let Ok(entries) = std::fs::read_dir(cache) else {
            continue;
        };
        for entry in entries.flatten() {
            let file_name = entry.file_name();
            let Some(dir) = file_name.to_str() else {
                continue;
            };
            let base = dir.split('@').next().unwrap_or(dir);
            if base != name {
                continue;
            }
            let root = entry.path();
            let Some(json) = read_package_json(&root) else {
                continue;
            };
            if json.name != name {
                continue;
            }
            match version {
                Some(v) if json.version != v => {
                    fallback.get_or_insert((root, json.version));
                }
                _ => return Some((root, json.version)),
            }
        }
    }
    if let Some((_, found)) = &fallback {
        tracing::warn!(
            "package {name}: wanted {}, using cached {found}",
            version.unwrap_or("?")
        );
    }
    fallback
}
