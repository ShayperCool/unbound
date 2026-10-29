//! Package resolution without the Editor: `Packages/manifest.json` and `packages-lock.json` name the
//! packages; their contents come from the project (embedded, local), the Unity installation
//! (built-in), or a package cache (registry and git packages). A registry package no cache has is
//! unpacked from the tarball the installation ships (`Resources/PackageManager/Editor`) into the
//! Unbound package store, as the Editor does; without a lock file, the packages' own dependencies are
//! followed (the highest version asked for wins).
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
    #[serde(default, rename = "scopedRegistries")]
    scoped_registries: Vec<ScopedRegistry>,
}

#[derive(Deserialize)]
struct ScopedRegistry {
    url: String,
    #[serde(default)]
    scopes: Vec<String>,
}

/// Unity's package registry, for packages no scoped registry claims.
const UNITY_REGISTRY: &str = "https://packages.unity.com";

impl Manifest {
    /// The registry `name` comes from: the scoped registry with the longest matching scope, else
    /// Unity's.
    fn registry_for(&self, name: &str) -> &str {
        self.scoped_registries
            .iter()
            .flat_map(|r| r.scopes.iter().map(move |scope| (scope, r.url.as_str())))
            .filter(|(scope, _)| {
                name == scope.as_str()
                    || name.strip_prefix(scope.as_str()).is_some_and(|rest| rest.starts_with('.'))
            })
            .max_by_key(|(scope, _)| scope.len())
            .map_or(UNITY_REGISTRY, |(_, url)| url)
    }
}

#[derive(Deserialize)]
struct PackageJson {
    name: String,
    #[serde(default)]
    version: String,
    #[serde(default)]
    dependencies: BTreeMap<String, String>,
}

/// The package directories the user named (`--packages`, `UNBOUND_PACKAGES`, and the older
/// `UNBOUND_PACKAGE_CACHES`): folders of `<name>` or `<name>@<version>` packages, searched first.
pub fn user_package_dirs() -> Vec<PathBuf> {
    ["UNBOUND_PACKAGES", "UNBOUND_PACKAGE_CACHES"]
        .iter()
        .filter_map(std::env::var_os)
        .flat_map(|list| std::env::split_paths(&list).collect::<Vec<_>>())
        .filter(|dir| !dir.as_os_str().is_empty())
        .collect()
}

/// The directories searched for registry and git packages, in order: the user's package
/// directories, the project's own `Library/PackageCache` and the Unbound package store.
pub fn package_caches(project: &Path) -> Vec<PathBuf> {
    let mut caches = user_package_dirs();
    caches.push(project.join("Library/PackageCache"));
    caches.push(crate::cache_root().join("packages"));
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
                } else if name.starts_with("com.unity.modules.")
                    || install.builtin_packages_dir().join(name).join("package.json").exists()
                {
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
    let mut builtin_dirs = user_package_dirs();
    builtin_dirs.push(project.join("Library/PackageCache"));
    let builtin = install.builtin_packages_dir();
    let bundled = install.data.join("Resources/PackageManager/Editor");
    let store = crate::cache_root().join("packages");
    let has_lock = std::fs::metadata(packages_dir.join("packages-lock.json")).is_ok();
    let mut resolution = Resolution::default();
    let mut queue: Vec<(String, (String, String, Option<String>))> = wanted.into_iter().collect();
    let mut seen: BTreeMap<String, String> = BTreeMap::new();
    while let Some((name, (version, source, _hash))) = queue.pop() {
        // Without a lock, a package asked for again at a higher version replaces the first.
        if let Some(previous) = seen.get(&name) {
            if !has_lock && source == "registry" && newer(&version, previous) {
                resolution.packages.retain(|p| p.name != name);
                resolution.missing.retain(|m| m.0 != name);
            } else {
                continue;
            }
        }
        seen.insert(name.clone(), version.clone());
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
            // The user's directories and the project's cache (the Editor copies built-in
            // packages there) when they hold this version, else the release's own.
            "builtin" => find_exact(&builtin_dirs, &name, &version)
                .or_else(|| {
                    let root = builtin.join(&name);
                    read_package_json(&root).map(|json| (root, json.version))
                })
                .map(|(root, version)| (root, PackageSource::BuiltIn, version)),
            other => {
                let kind = if other == "git" {
                    PackageSource::Git
                } else {
                    PackageSource::Registry
                };
                let cached = find_in_caches(&caches, &name, (kind == PackageSource::Registry).then_some(&*version));
                let exact = cached.as_ref().is_some_and(|(_, v)| *v == version);
                if kind == PackageSource::Registry && !exact {
                    let fetched = unpack_bundled(&bundled, &store, &name, &version)
                        .with_context(|| "unpacking the installation's tarball".to_string())
                        .and_then(|found| match found {
                            Some(root) => Ok(Some(root)),
                            None => download_package(manifest.registry_for(&name), &store, &name, &version),
                        });
                    match fetched {
                        Ok(Some(root)) => Some((root, kind, version.clone())),
                        Ok(None) => cached.map(|(root, found_version)| (root, kind, found_version)),
                        Err(e) => {
                            tracing::warn!("package {name}@{version}: {e:#}");
                            cached.map(|(root, found_version)| (root, kind, found_version))
                        }
                    }
                } else {
                    cached.map(|(root, found_version)| (root, kind, found_version))
                }
            }
        };
        // Without a lock the packages' own dependencies come along.
        if !has_lock
            && let Some((root, _, _)) = &found
            && let Some(json) = read_package_json(root)
        {
            for (dependency, wanted_version) in json.dependencies {
                let source = if dependency.starts_with("com.unity.modules.") || builtin.join(&dependency).join("package.json").exists() {
                    "builtin"
                } else {
                    "registry"
                };
                queue.push((dependency, (wanted_version, source.into(), None)));
            }
        }
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

/// Whether version `a` is higher than `b` (dotted numbers, a pre-release lower than its release).
fn newer(a: &str, b: &str) -> bool {
    fn parts(v: &str) -> (Vec<u64>, bool) {
        let (core, pre) = v.split_once('-').map_or((v, false), |(c, _)| (c, true));
        (core.split('.').map(|p| p.parse().unwrap_or(0)).collect(), pre)
    }
    let (pa, pre_a) = parts(a);
    let (pb, pre_b) = parts(b);
    match pa.cmp(&pb) {
        std::cmp::Ordering::Equal => pre_b && !pre_a,
        order => order == std::cmp::Ordering::Greater,
    }
}

/// The installation's tarball of `name@version` unpacked into the store (`name@version`, its
/// `package/` folder's contents); None when the installation does not ship that version.
fn unpack_bundled(bundled: &Path, store: &Path, name: &str, version: &str) -> Result<Option<PathBuf>> {
    let tarball = bundled.join(format!("{name}-{version}.tgz"));
    if !tarball.is_file() {
        return Ok(None);
    }
    let root = store.join(format!("{name}@{version}"));
    if read_package_json(&root).is_some_and(|json| json.name == name && json.version == version) {
        return Ok(Some(root));
    }
    let bytes = std::fs::read(&tarball).with_context(|| format!("opening {}", tarball.display()))?;
    unpack_tarball(&bytes, store, name, version, "the Unity installation").map(Some)
}

/// `name@version` downloaded from `registry` (an npm registry: the package's document names its
/// tarball) and unpacked into the store; None when the registry does not have that version.
/// `UNBOUND_OFFLINE` forbids the download.
fn download_package(registry: &str, store: &Path, name: &str, version: &str) -> Result<Option<PathBuf>> {
    let root = store.join(format!("{name}@{version}"));
    if read_package_json(&root).is_some_and(|json| json.name == name && json.version == version) {
        return Ok(Some(root));
    }
    if std::env::var_os("UNBOUND_OFFLINE").is_some() {
        return Ok(None);
    }
    let url = format!("{}/{name}", registry.trim_end_matches('/'));
    let text = match ureq::get(&url).call() {
        Ok(mut response) => response.body_mut().with_config().limit(64 << 20).read_to_string()?,
        Err(ureq::Error::StatusCode(404)) => return Ok(None),
        Err(e) => return Err(e).with_context(|| format!("querying {url}")),
    };
    let document: serde_json::Value = serde_json::from_str(&text).with_context(|| format!("reading {url}"))?;
    let Some(tarball) = document["versions"][version]["dist"]["tarball"].as_str() else {
        return Ok(None);
    };
    tracing::info!("package {name}@{version}: downloading {tarball}");
    use std::io::Read as _;
    let mut bytes = Vec::new();
    ureq::get(tarball)
        .call()
        .with_context(|| format!("downloading {tarball}"))?
        .body_mut()
        .with_config()
        .limit(1 << 30)
        .reader()
        .read_to_end(&mut bytes)?;
    unpack_tarball(&bytes, store, name, version, registry).map(Some)
}

/// A package's gzipped npm tarball unpacked into the store as `name@version` (the contents of its
/// `package/` folder).
fn unpack_tarball(gzipped: &[u8], store: &Path, name: &str, version: &str, origin: &str) -> Result<PathBuf> {
    let root = store.join(format!("{name}@{version}"));
    std::fs::create_dir_all(store)?;
    let staging = store.join(format!(".{name}@{version}.{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&staging);
    std::fs::create_dir_all(&staging)?;
    let mut data = Vec::new();
    std::io::Read::read_to_end(&mut flate2::read::GzDecoder::new(gzipped), &mut data)?;
    for (path, kind, contents) in tar_entries(&data)? {
        // Everything sits under `package/`; nothing may leave the staging folder.
        let Ok(relative) = Path::new(&path).strip_prefix("package") else { continue };
        if relative.as_os_str().is_empty() || relative.components().any(|c| !matches!(c, std::path::Component::Normal(_))) {
            continue;
        }
        let target = staging.join(relative);
        match kind {
            b'5' => std::fs::create_dir_all(&target)?,
            b'0' | 0 | b'7' => {
                if let Some(parent) = target.parent() {
                    std::fs::create_dir_all(parent)?;
                }
                std::fs::write(&target, contents)?;
            }
            _ => {}
        }
    }
    let _ = std::fs::remove_dir_all(&root);
    std::fs::rename(&staging, &root)?;
    tracing::info!("package {name}@{version}: unpacked from {origin} into {}", root.display());
    Ok(root)
}

/// A tar archive's entries: path, type flag, contents. Reads ustar names with their prefix, GNU
/// long names (`L` entries, whatever the header's magic: npm's tarballs of Unity packages carry
/// `ustar\0 \0`, which stricter readers take for neither format) and PAX `path` records.
fn tar_entries(data: &[u8]) -> Result<Vec<(String, u8, &[u8])>> {
    fn field(bytes: &[u8]) -> &[u8] {
        let end = bytes.iter().position(|&b| b == 0).unwrap_or(bytes.len());
        &bytes[..end]
    }
    fn octal(bytes: &[u8]) -> Result<usize> {
        let text = std::str::from_utf8(field(bytes))?.trim();
        if text.is_empty() {
            return Ok(0);
        }
        usize::from_str_radix(text, 8).with_context(|| format!("bad tar number `{text}`"))
    }
    let mut entries = Vec::new();
    let mut offset = 0;
    let mut long_name: Option<String> = None;
    while offset + 512 <= data.len() {
        let header = &data[offset..offset + 512];
        if header.iter().all(|&b| b == 0) {
            break;
        }
        let size = octal(&header[124..136])?;
        let kind = header[156];
        let start = offset + 512;
        let end = start + size;
        anyhow::ensure!(end <= data.len(), "a truncated tar entry");
        let contents = &data[start..end];
        offset = start + size.div_ceil(512) * 512;
        match kind {
            b'L' => {
                long_name = Some(String::from_utf8_lossy(field(contents)).into_owned());
                continue;
            }
            b'x' => {
                // PAX records: "<length> <key>=<value>\n".
                let text = String::from_utf8_lossy(contents);
                for record in text.split('\n') {
                    if let Some((_, pair)) = record.split_once(' ')
                        && let Some(path) = pair.strip_prefix("path=")
                    {
                        long_name = Some(path.to_string());
                    }
                }
                continue;
            }
            b'g' => continue,
            _ => {}
        }
        let path = match long_name.take() {
            Some(name) => name,
            None => {
                let name = String::from_utf8_lossy(field(&header[0..100])).into_owned();
                let prefix = if header[257..262] == *b"ustar" { String::from_utf8_lossy(field(&header[345..500])).into_owned() } else { String::new() };
                if prefix.is_empty() { name } else { format!("{prefix}/{name}") }
            }
        };
        entries.push((path, kind, contents));
    }
    Ok(entries)
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
/// `name` at exactly `version` in one of `dirs`.
fn find_exact(dirs: &[PathBuf], name: &str, version: &str) -> Option<(PathBuf, String)> {
    dirs.iter().find_map(|dir| {
        let entries = std::fs::read_dir(dir).ok()?;
        entries.flatten().find_map(|entry| {
            let file_name = entry.file_name();
            let base = file_name.to_str()?.split('@').next()?;
            if base != name {
                return None;
            }
            let root = entry.path();
            let json = read_package_json(&root)?;
            (json.name == name && json.version == version).then_some((root, json.version))
        })
    })
}

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

#[cfg(test)]
mod tests {
    use super::*;

    fn header(name: &str, size: usize, kind: u8, magic: &[u8; 8], prefix: &str) -> Vec<u8> {
        let mut h = vec![0u8; 512];
        h[..name.len()].copy_from_slice(name.as_bytes());
        let size = format!("{size:011o}\0");
        h[124..136].copy_from_slice(size.as_bytes());
        h[156] = kind;
        h[257..265].copy_from_slice(magic);
        h[345..345 + prefix.len()].copy_from_slice(prefix.as_bytes());
        h
    }

    fn padded(data: &[u8]) -> Vec<u8> {
        let mut v = data.to_vec();
        v.resize(data.len().div_ceil(512) * 512, 0);
        v
    }

    /// npm's tarballs of Unity packages: a GNU long name under the magic `ustar\0 \0`, whose
    /// truncated 100-byte name equals another entry's.
    #[test]
    fn long_names_under_any_magic_and_ustar_prefixes() {
        let odd = b"ustar\0 \0";
        let long = "package/".to_string() + &"a".repeat(92) + ".meta";
        let mut tar = Vec::new();
        tar.extend(header(&long[..100], 3, b'0', odd, ""));
        tar.extend(padded(b"one"));
        tar.extend(header("././@LongLink", long.len() + 1, b'L', odd, ""));
        tar.extend(padded(format!("{long}\0").as_bytes()));
        tar.extend(header(&long[..100], 3, b'0', odd, ""));
        tar.extend(padded(b"two"));
        tar.extend(header("file.txt", 5, b'0', b"ustar\x0000", "package/dir"));
        tar.extend(padded(b"three"));
        tar.extend(vec![0u8; 1024]);
        let entries = tar_entries(&tar).unwrap();
        assert_eq!(entries.len(), 3);
        assert_eq!(entries[0].0, long[..100]);
        assert_eq!(entries[1].0, long);
        assert_eq!(entries[1].2, b"two");
        assert_eq!(entries[2].0, "package/dir/file.txt");
    }

    #[test]
    fn versions_compare_by_number_then_release() {
        assert!(newer("1.10.0", "1.9.3"));
        assert!(newer("2.0.0", "2.0.0-pre.1"));
        assert!(!newer("1.2.0", "1.2.0"));
    }
}
