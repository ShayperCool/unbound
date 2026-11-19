//! Package resolution without the Editor: `Packages/manifest.json` and `packages-lock.json` name the
//! packages; their contents come from the project (embedded, local), the Unity installation
//! (built-in), or a package cache (registry and git packages). A registry package no cache has is
//! unpacked from the tarball the installation ships (`Resources/PackageManager/Editor`) into the
//! Unbound package store, as the Editor does, else downloaded from its registry (with the user's
//! credentials: [`crate::upm_config`]); a local tarball is unpacked into the store, and a git
//! package no cache has is fetched into it ([`crate::git_packages`]). Without a lock file, the
//! packages' own dependencies are followed (the highest version asked for wins, and where the
//! release's rules say so no version below the Editor's own minimum for the package:
//! [`PackageResolutionRules`]).
use crate::install::UnityInstall;
use anyhow::{Context, Result};
use serde::Deserialize;
use std::{
    collections::BTreeMap,
    path::{Path, PathBuf},
};
use unbound_compat::PackageResolutionRules;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PackageSource {
    Embedded,
    Local,
    BuiltIn,
    Registry,
    Git,
    /// A `file:<path>.tgz` dependency, unpacked into the store.
    LocalTarball,
}

#[derive(Debug, Clone)]
pub struct Package {
    pub name: String,
    pub version: String,
    pub source: PackageSource,
    pub root: PathBuf,
    /// Its test assemblies build: listed in the manifest's `testables`, or embedded.
    pub testable: bool,
}

#[derive(Debug, Default)]
pub struct Resolution {
    pub packages: Vec<Package>,
    /// Packages the lock names that no source has: `(name, version, source)`.
    pub missing: Vec<(String, String, String)>,
    /// Registry packages taken at another version than asked for (a cached one, offline or when
    /// the download failed): `(name, asked for)`.
    pub inexact: Vec<(String, String)>,
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
                    || name
                        .strip_prefix(scope.as_str())
                        .is_some_and(|rest| rest.starts_with('.'))
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

/// A JSON object's members in the order its text lists them (serde_json's maps sort them).
struct Ordered(Vec<(String, String)>);

impl<'de> Deserialize<'de> for Ordered {
    fn deserialize<D: serde::Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        struct Members;
        impl<'de> serde::de::Visitor<'de> for Members {
            type Value = Ordered;
            fn expecting(&self, f: &mut std::fmt::Formatter) -> std::fmt::Result {
                f.write_str("an object of strings")
            }
            fn visit_map<A: serde::de::MapAccess<'de>>(
                self,
                mut map: A,
            ) -> Result<Ordered, A::Error> {
                let mut members = Vec::new();
                while let Some(member) = map.next_entry::<String, String>()? {
                    members.push(member);
                }
                Ok(Ordered(members))
            }
        }
        deserializer.deserialize_map(Members)
    }
}

/// What a package's `package.json` asks for, in its own order.
fn package_dependencies(root: &Path) -> Vec<(String, String)> {
    #[derive(Deserialize)]
    struct Dependencies {
        #[serde(default)]
        dependencies: Option<Ordered>,
    }
    std::fs::read(root.join("package.json"))
        .ok()
        .and_then(|bytes| serde_json::from_slice::<Dependencies>(&bytes).ok())
        .and_then(|json| json.dependencies)
        .map_or_else(Vec::new, |ordered| ordered.0)
}

/// The `Packages/packages-lock.json` the Package Manager writes for the project's `packages`:
/// each package by name (the engine's modules last), with its version (an embedded package's folder as `file:<folder>`, a
/// local one's manifest path), its depth from the manifest, its source and its `package.json`'s
/// dependencies in that file's order, and a registry package's registry; two-space JSON with LF
/// endings (matches the Unity 6000.0.71f1 Editor, tests/projects/projectwrites
/// `exit.Packages/packages-lock.json`, and the locks the Editor wrote in tests/projects). A
/// project with a Git or local tarball package gets none: the lock records what was fetched (a
/// commit, a hash), which Unbound does not know when there was no lock.
pub fn lock_text(project: &Path, packages: &[Package]) -> Result<Option<String>> {
    let manifest: Manifest = serde_json::from_slice(
        &std::fs::read(project.join("Packages/manifest.json")).context("Packages/manifest.json")?,
    )?;
    if packages
        .iter()
        .any(|p| matches!(p.source, PackageSource::Git | PackageSource::LocalTarball))
    {
        return Ok(None);
    }
    let by_name: BTreeMap<&str, &Package> = packages.iter().map(|p| (p.name.as_str(), p)).collect();
    // Depths: the manifest's dependencies and the embedded packages (the project's own, named by
    // the manifest or not) at 0, then what each asks for, breadth first. What only an embedded
    // package asks for is at depth 1 (no Editor lock measured with such a package).
    let mut depth: BTreeMap<&str, usize> = BTreeMap::new();
    let mut level: Vec<&str> = manifest
        .dependencies
        .keys()
        .map(String::as_str)
        .filter(|name| by_name.contains_key(name))
        .chain(
            packages
                .iter()
                .filter(|p| p.source == PackageSource::Embedded)
                .map(|p| p.name.as_str()),
        )
        .collect();
    let mut d = 0;
    while !level.is_empty() {
        let mut next = Vec::new();
        for name in level {
            if depth.contains_key(name) {
                continue;
            }
            depth.insert(name, d);
            for (dependency, _) in package_dependencies(&by_name[name].root) {
                if let Some((key, _)) = by_name.get_key_value(dependency.as_str()) {
                    next.push(*key);
                }
            }
        }
        level = next;
        d += 1;
    }
    // A lock without a resolved package would hold the project to less than it uses.
    if let Some(name) = by_name.keys().find(|name| !depth.contains_key(*name)) {
        anyhow::bail!("{name} is not reached from the manifest or an embedded package");
    }
    let quote = |s: &str| serde_json::to_string(s).expect("a string");
    let mut out = String::from("{\n  \"dependencies\": {");
    let mut first = true;
    // By name, the engine's module packages after all others.
    let mut order: Vec<(&&str, &&Package)> = by_name.iter().collect();
    order.sort_by_key(|(name, _)| (name.starts_with("com.unity.modules."), **name));
    for (name, package) in order {
        let depth = depth[name];
        let (version, source) = match package.source {
            PackageSource::Embedded => (
                format!(
                    "file:{}",
                    package
                        .root
                        .file_name()
                        .unwrap_or_default()
                        .to_string_lossy()
                ),
                "embedded",
            ),
            PackageSource::Local => (
                manifest
                    .dependencies
                    .get(*name)
                    .cloned()
                    .unwrap_or_else(|| package.version.clone()),
                "local",
            ),
            PackageSource::BuiltIn => (package.version.clone(), "builtin"),
            _ => (package.version.clone(), "registry"),
        };
        out.push_str(if first { "\n" } else { ",\n" });
        first = false;
        out.push_str(&format!(
            "    {}: {{\n      \"version\": {},\n      \"depth\": {depth},\n      \"source\": {},\n      \"dependencies\": {{",
            quote(name),
            quote(&version),
            quote(source)
        ));
        let dependencies = package_dependencies(&package.root);
        for (i, (dependency, wanted)) in dependencies.iter().enumerate() {
            out.push_str(if i == 0 { "\n" } else { ",\n" });
            out.push_str(&format!("        {}: {}", quote(dependency), quote(wanted)));
        }
        out.push_str(if dependencies.is_empty() {
            "}"
        } else {
            "\n      }"
        });
        if source == "registry" {
            out.push_str(&format!(
                ",\n      \"url\": {}",
                quote(manifest.registry_for(name))
            ));
        }
        out.push_str("\n    }");
    }
    out.push_str("\n  }\n}\n");
    Ok(Some(out))
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

pub fn resolve(
    project: &Path,
    install: &UnityInstall,
    rules: PackageResolutionRules,
) -> Result<Resolution> {
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
                let source = if version.starts_with("file:") && is_tarball(version) {
                    "local-tarball"
                } else if version.starts_with("file:") {
                    "local"
                } else if version.contains(".git") || version.starts_with("git") {
                    "git"
                } else if name.starts_with("com.unity.modules.")
                    || install
                        .builtin_packages_dir()
                        .join(name)
                        .join("package.json")
                        .exists()
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
    // The user's directories and the project's own cache, without the store.
    let mut project_caches = user_package_dirs();
    let user_dirs = project_caches.len();
    project_caches.push(project.join("Library/PackageCache"));
    let builtin = install.builtin_packages_dir();
    let bundled = install.data.join("Resources/PackageManager/Editor");
    let store = crate::cache_root().join("packages");
    let has_lock = std::fs::metadata(packages_dir.join("packages-lock.json")).is_ok();
    let minimums = if !has_lock && rules.editor_minimum_versions {
        editor_minimum_versions(&bundled)
    } else {
        BTreeMap::new()
    };
    let mut resolution = Resolution::default();
    // Each entry: the package, what is asked for, and whether a package (not the project) asks.
    let mut queue: Vec<(String, (String, String, Option<String>), bool)> = wanted
        .into_iter()
        .map(|(name, want)| (name, want, false))
        .collect();
    let mut seen: BTreeMap<String, String> = BTreeMap::new();
    while let Some((name, (mut version, source, hash), indirect)) = queue.pop() {
        // Without a lock, a package asking for a registry package below the Editor's minimum gets
        // the minimum; the project's own dependencies keep the version they name (also when a
        // package asks for them too: unmeasured).
        if indirect
            && source == "registry"
            && !manifest.dependencies.contains_key(&name)
            && let Some(minimum) = minimums.get(&name)
            && newer(minimum, &version)
        {
            version = minimum.clone();
        }
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
                let root = local_path(&packages_dir, &version);
                read_package_json(&root).map(|json| (root, PackageSource::Local, json.version))
            }
            "local-tarball" => {
                let tarball = local_path(&packages_dir, &version);
                match unpack_local_tarball(&tarball, &store, &name) {
                    Ok(root) => read_package_json(&root)
                        .map(|json| (root, PackageSource::LocalTarball, json.version)),
                    Err(e) => {
                        tracing::warn!("package {name} ({version}): {e:#}");
                        None
                    }
                }
            }
            // The user's directories and the project's cache when they hold the lock's commit,
            // else that commit in the store, fetched when it is not there; a cached folder of
            // another commit only when the commit cannot be fetched, with a warning.
            "git" => {
                let (locked, other) = find_git(&project_caches, user_dirs, &name, hash.as_deref());
                locked
                    .or_else(|| {
                        let spec = crate::git_packages::parse_spec(&version);
                        match crate::git_packages::fetch(
                            &store,
                            &name,
                            &spec,
                            hash.as_deref(),
                            std::env::var_os("UNBOUND_OFFLINE").is_some(),
                        ) {
                            Ok(root) => read_package_json(&root).map(|json| (root, json.version)),
                            Err(e) => match other {
                                Some((root, found)) => {
                                    tracing::warn!(
                                        "git package {name}: {e:#}; using {} (another commit than \
                                         the lock's {})",
                                        root.display(),
                                        hash.as_deref().unwrap_or("?")
                                    );
                                    Some((root, found))
                                }
                                None => {
                                    tracing::warn!("git package {name}: {e:#}");
                                    None
                                }
                            },
                        }
                    })
                    .map(|(root, found_version)| (root, PackageSource::Git, found_version))
            }
            // The user's directories and the project's cache (the Editor copies built-in
            // packages there) when they hold this version, else the release's own.
            "builtin" => find_exact(&project_caches, &name, &version)
                .or_else(|| {
                    let root = builtin.join(&name);
                    read_package_json(&root).map(|json| (root, json.version))
                })
                .map(|(root, version)| (root, PackageSource::BuiltIn, version)),
            _ => {
                let kind = PackageSource::Registry;
                let cached = find_in_caches(&caches, &name, Some(&version));
                let exact = cached.as_ref().is_some_and(|(_, v)| *v == version);
                if !exact {
                    let fetched = unpack_bundled(&bundled, &store, &name, &version)
                        .with_context(|| "unpacking the installation's tarball".to_string())
                        .and_then(|found| match found {
                            Some(root) => Ok(Some(root)),
                            None => download_package(
                                manifest.registry_for(&name),
                                &store,
                                &name,
                                &version,
                            ),
                        });
                    let mut fallback = |cached: Option<(PathBuf, String)>| {
                        cached.map(|(root, found_version)| {
                            if found_version != version {
                                resolution.inexact.push((name.clone(), version.clone()));
                            }
                            (root, kind, found_version)
                        })
                    };
                    match fetched {
                        Ok(Some(root)) => Some((root, kind, version.clone())),
                        Ok(None) => fallback(cached),
                        Err(e) => {
                            tracing::warn!("package {name}@{version}: {e:#}");
                            fallback(cached)
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
                let source = if dependency.starts_with("com.unity.modules.")
                    || builtin.join(&dependency).join("package.json").exists()
                {
                    "builtin"
                } else {
                    "registry"
                };
                queue.push((dependency, (wanted_version, source.into(), None), true));
            }
        }
        match found {
            Some((root, source, version)) => resolution.packages.push(Package {
                // Unity 6000.0.71f1 builds an embedded package's test assemblies without a
                // `testables` entry, a local folder's or tarball's not (tests/projects/compilesetup
                // `assemblies`).
                testable: source == PackageSource::Embedded || manifest.testables.contains(&name),
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

/// The names of the project's packages that decide its engine modules: the resolved ones, the
/// manifest's own dependencies (the Editor resolves those even when a stale lock lacks them) and
/// the built-in modules those depend on.
pub fn module_packages(
    project: &Path,
    install: &UnityInstall,
    resolved: &[Package],
) -> std::collections::BTreeSet<String> {
    let mut names: std::collections::BTreeSet<String> =
        resolved.iter().map(|p| p.name.clone()).collect();
    if let Ok(bytes) = std::fs::read(project.join("Packages/manifest.json"))
        && let Ok(manifest) = serde_json::from_slice::<Manifest>(&bytes)
    {
        names.extend(manifest.dependencies.into_keys());
    }
    let builtin = install.builtin_packages_dir();
    let mut queue: Vec<String> = names
        .iter()
        .filter(|n| n.starts_with("com.unity.modules."))
        .cloned()
        .collect();
    while let Some(name) = queue.pop() {
        for dependency in read_package_json(&builtin.join(&name))
            .map(|json| json.dependencies)
            .unwrap_or_default()
            .into_keys()
        {
            if names.insert(dependency.clone()) {
                queue.push(dependency);
            }
        }
    }
    names
}

/// The minimum version of each package the installation's own manifest names
/// (`Resources/PackageManager/Editor/manifest.json`: `packages.<name>.minimumVersion`); empty when
/// the installation has none.
fn editor_minimum_versions(bundled: &Path) -> BTreeMap<String, String> {
    #[derive(Deserialize)]
    struct EditorManifest {
        #[serde(default)]
        packages: BTreeMap<String, EditorPackage>,
    }
    #[derive(Deserialize)]
    struct EditorPackage {
        #[serde(default, rename = "minimumVersion")]
        minimum_version: Option<String>,
    }
    let path = bundled.join("manifest.json");
    let Ok(bytes) = std::fs::read(&path) else {
        return BTreeMap::new();
    };
    match serde_json::from_slice::<EditorManifest>(&bytes) {
        Ok(manifest) => manifest
            .packages
            .into_iter()
            .filter_map(|(name, p)| p.minimum_version.map(|v| (name, v)))
            .collect(),
        Err(e) => {
            tracing::warn!(
                "{}: {e}; packages resolve without the Editor's minimum versions",
                path.display()
            );
            BTreeMap::new()
        }
    }
}

/// Whether version `a` is higher than `b` (dotted numbers, a pre-release lower than its release).
fn newer(a: &str, b: &str) -> bool {
    fn parts(v: &str) -> (Vec<u64>, bool) {
        let (core, pre) = v.split_once('-').map_or((v, false), |(c, _)| (c, true));
        (
            core.split('.').map(|p| p.parse().unwrap_or(0)).collect(),
            pre,
        )
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
fn unpack_bundled(
    bundled: &Path,
    store: &Path,
    name: &str,
    version: &str,
) -> Result<Option<PathBuf>> {
    let tarball = bundled.join(format!("{name}-{version}.tgz"));
    if !tarball.is_file() {
        return Ok(None);
    }
    let root = store.join(format!("{name}@{version}"));
    if read_package_json(&root).is_some_and(|json| json.name == name && json.version == version) {
        return Ok(Some(root));
    }
    let bytes =
        std::fs::read(&tarball).with_context(|| format!("opening {}", tarball.display()))?;
    unpack_tarball(&bytes, store, name, version, "the Unity installation").map(Some)
}

/// `name@version` downloaded from `registry` (an npm registry: the package's document names its
/// tarball) and unpacked into the store; None when the registry does not have that version.
/// `UNBOUND_OFFLINE` forbids the download.
fn download_package(
    registry: &str,
    store: &Path,
    name: &str,
    version: &str,
) -> Result<Option<PathBuf>> {
    let root = store.join(format!("{name}@{version}"));
    if read_package_json(&root).is_some_and(|json| json.name == name && json.version == version) {
        return Ok(Some(root));
    }
    if std::env::var_os("UNBOUND_OFFLINE").is_some() {
        return Ok(None);
    }
    let url = format!("{}/{name}", registry.trim_end_matches('/'));
    let credentials = crate::upm_config::user();
    let text = match crate::net::get_authorized(
        &url,
        credentials.document_authorization(registry),
        |mut response| {
            Ok(response
                .body_mut()
                .with_config()
                .limit(64 << 20)
                .read_to_string()?)
        },
    ) {
        Ok(text) => text,
        Err(e) if crate::net::is_not_found(&e) => return Ok(None),
        Err(e) => {
            let hint = if crate::net::is_unauthorized(&e)
                && credentials.document_authorization(registry).is_none()
            {
                format!(
                    " (the Package Manager's user configuration, {}, has no [npmAuth.\"{registry}\"] section)",
                    crate::upm_config::user_config_path().display()
                )
            } else {
                String::new()
            };
            return Err(e).with_context(|| format!("querying {url}{hint}"));
        }
    };
    let document: serde_json::Value =
        serde_json::from_str(&text).with_context(|| format!("reading {url}"))?;
    let Some(tarball) = document["versions"][version]["dist"]["tarball"].as_str() else {
        return Ok(None);
    };
    tracing::info!("package {name}@{version}: downloading {tarball}");
    let bytes = crate::net::get_authorized(
        tarball,
        credentials.tarball_authorization(registry, tarball),
        |mut response| {
            Ok(response
                .body_mut()
                .with_config()
                .limit(1 << 30)
                .read_to_vec()?)
        },
    )
    .with_context(|| format!("downloading {tarball}"))?;
    unpack_tarball(&bytes, store, name, version, registry).map(Some)
}

/// A package's gzipped npm tarball unpacked into the store as `name@version` (the contents of its
/// `package/` folder).
fn unpack_tarball(
    gzipped: &[u8],
    store: &Path,
    name: &str,
    version: &str,
    origin: &str,
) -> Result<PathBuf> {
    unpack_tarball_at(gzipped, &store.join(format!("{name}@{version}")))?;
    let root = store.join(format!("{name}@{version}"));
    tracing::info!(
        "package {name}@{version}: unpacked from {origin} into {}",
        root.display()
    );
    Ok(root)
}

/// A gzipped npm tarball's `package/` folder unpacked as `root` (staged beside it, then moved).
fn unpack_tarball_at(gzipped: &[u8], root: &Path) -> Result<()> {
    let parent = root.parent().context("a store entry has a parent")?;
    std::fs::create_dir_all(parent)?;
    let file_name = root
        .file_name()
        .context("a store entry has a name")?
        .to_string_lossy();
    let staging = parent.join(format!(".{file_name}.{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&staging);
    let mut data = Vec::new();
    std::io::Read::read_to_end(&mut flate2::read::GzDecoder::new(gzipped), &mut data)
        .context("not a gzipped tarball")?;
    let result = extract_tar(&data, "package", &staging).and_then(|()| {
        let _ = std::fs::remove_dir_all(root);
        Ok(std::fs::rename(&staging, root)?)
    });
    if result.is_err() {
        let _ = std::fs::remove_dir_all(&staging);
    }
    result
}

/// The files and folders of tar archive `data` under its folder `prefix` ("" for all of it)
/// written to `target`; nothing may leave `target`, and links are skipped.
fn extract_tar(data: &[u8], prefix: &str, target: &Path) -> Result<()> {
    std::fs::create_dir_all(target)?;
    for (path, kind, contents) in tar_entries(data)? {
        let Ok(relative) = Path::new(&path).strip_prefix(prefix) else {
            continue;
        };
        if relative.as_os_str().is_empty()
            || relative
                .components()
                .any(|c| !matches!(c, std::path::Component::Normal(_)))
        {
            continue;
        }
        let destination = target.join(relative);
        match kind {
            b'5' => std::fs::create_dir_all(&destination)?,
            b'0' | 0 | b'7' => {
                if let Some(parent) = destination.parent() {
                    std::fs::create_dir_all(parent)?;
                }
                std::fs::write(&destination, contents)?;
            }
            _ => {}
        }
    }
    Ok(())
}

/// The format of the store's `git/` and `tarballs/` entries, part of their keys: bump it when
/// what an entry holds changes, so an older entry is never used.
pub(crate) const STORE_FORMAT: u32 = 1;

/// Whether a `file:` dependency names a tarball rather than a folder (the Editor's
/// `local-tarball`).
fn is_tarball(version: &str) -> bool {
    let path = version.to_ascii_lowercase();
    path.ends_with(".tgz") || path.ends_with(".tar.gz")
}

/// A `file:` dependency's path: absolute, or relative to the `Packages` folder (the Unity 6000.0
/// Manual's "Local folder or local tarball paths").
fn local_path(packages_dir: &Path, version: &str) -> PathBuf {
    let path = version.trim_start_matches("file:");
    if Path::new(path).is_absolute() {
        PathBuf::from(path)
    } else {
        packages_dir.join(path)
    }
}

/// A local tarball unpacked into the store's `tarballs/` folder, keyed by a hash of its contents
/// (and the store format), so a replaced archive is unpacked again whatever its size and time.
fn unpack_local_tarball(tarball: &Path, store: &Path, name: &str) -> Result<PathBuf> {
    let bytes = std::fs::read(tarball).with_context(|| format!("opening {}", tarball.display()))?;
    let mut hasher = blake3::Hasher::new();
    hasher.update(&STORE_FORMAT.to_le_bytes());
    hasher.update(&bytes);
    let key = hasher.finalize().to_hex();
    let root = store
        .join("tarballs")
        .join(format!("{name}@{}", &key[..16]));
    if package_name(&root).as_deref() == Some(name) {
        return Ok(root);
    }
    unpack_tarball_at(&bytes, &root).with_context(|| format!("unpacking {}", tarball.display()))?;
    anyhow::ensure!(
        package_name(&root).as_deref() == Some(name),
        "{} holds no package/package.json naming {name}",
        tarball.display()
    );
    tracing::info!(
        "package {name}: unpacked {} into {}",
        tarball.display(),
        root.display()
    );
    Ok(root)
}

/// The name in `root`'s package.json.
pub(crate) fn package_name(root: &Path) -> Option<String> {
    read_package_json(root).map(|json| json.name)
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
                let prefix = if header[257..262] == *b"ustar" {
                    String::from_utf8_lossy(field(&header[345..500])).into_owned()
                } else {
                    String::new()
                };
                if prefix.is_empty() {
                    name
                } else {
                    format!("{prefix}/{name}")
                }
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

/// A git package's folder in `dirs` (the user's directories, then the project's cache): the
/// first of the lock's commit `hash`, and the first of another or an unknown commit. The Package
/// Manager installs the lock's commit (the Manual's "Locked Git dependencies": it "tracks commit
/// hashes of Git dependencies by using a lock file" so that every user gets the same packages),
/// and the Editor names a git package's folder in `Library/PackageCache`
/// `<name>@<the commit's first 12 hexadecimal digits>`, so a folder's suffix names its commit. A
/// folder without such a suffix is taken as the lock's commit only in the user's directories
/// (`user`, which the user names), or when the lock pins no commit.
fn find_git(
    dirs: &[PathBuf],
    user: usize,
    name: &str,
    hash: Option<&str>,
) -> (Option<(PathBuf, String)>, Option<(PathBuf, String)>) {
    let mut other = None;
    for (index, dir) in dirs.iter().enumerate() {
        let Ok(entries) = std::fs::read_dir(dir) else {
            continue;
        };
        let mut entries: Vec<_> = entries.flatten().collect();
        entries.sort_by_key(|entry| entry.file_name());
        for entry in entries {
            let file_name = entry.file_name();
            let Some(folder) = file_name.to_str() else {
                continue;
            };
            let (base, suffix) = folder.split_once('@').unwrap_or((folder, ""));
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
            let commit = (suffix.len() >= 7 && suffix.bytes().all(|b| b.is_ascii_hexdigit()))
                .then(|| suffix.to_ascii_lowercase());
            let locked = match (hash, commit) {
                (None, _) => true,
                (Some(hash), Some(commit)) => hash.to_ascii_lowercase().starts_with(&commit),
                (Some(_), None) => index < user,
            };
            if locked {
                return (Some((root, json.version)), None);
            }
            other.get_or_insert((root, json.version));
        }
    }
    (None, other)
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

    /// Rewrites each lock the Editor wrote for a probe project from that project's manifest and
    /// packages (each package's `package.json` asking for what its lock entry lists), and expects
    /// the Editor's bytes: entry order, depths, sources, registries and layout.
    #[test]
    fn lock_files_are_written_as_the_editor_writes_them() {
        let projects = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../tests/projects");
        for probe in [
            "gameinterop",
            "avoidance",
            "gamepad",
            "urp",
            "buildplayerhandlers",
        ] {
            let project = projects.join(probe);
            let committed =
                std::fs::read_to_string(project.join("Packages/packages-lock.json")).unwrap();
            let lock: serde_json::Value = serde_json::from_str(&committed).unwrap();
            let scratch =
                std::env::temp_dir().join(format!("unbound-lock-{probe}-{}", std::process::id()));
            let _ = std::fs::remove_dir_all(&scratch);
            std::fs::create_dir_all(scratch.join("Packages")).unwrap();
            std::fs::copy(
                project.join("Packages/manifest.json"),
                scratch.join("Packages/manifest.json"),
            )
            .unwrap();
            let entries = Ordered::deserialize(&lock["dependencies"]).ok();
            assert!(entries.is_none(), "objects of objects are not strings");
            let mut packages = Vec::new();
            for (name, entry) in lock["dependencies"].as_object().unwrap() {
                let source = entry["source"].as_str().unwrap();
                let version = entry["version"].as_str().unwrap();
                let root = match source {
                    "embedded" => scratch
                        .join("Packages")
                        .join(version.strip_prefix("file:").unwrap()),
                    _ => scratch.join("cache").join(name),
                };
                std::fs::create_dir_all(&root).unwrap();
                // The dependencies in the lock's order, which is the package.json's.
                let start = committed.find(&format!("\"{name}\": {{")).unwrap();
                let block = &committed[start..];
                let deps = &block
                    [block.find("\"dependencies\": {").unwrap()..block.find('}').unwrap() + 1];
                std::fs::write(
                    root.join("package.json"),
                    format!("{{\"name\": \"{name}\", \"version\": \"{version}\", {deps}}}"),
                )
                .unwrap();
                packages.push(Package {
                    name: name.clone(),
                    version: version.to_string(),
                    source: match source {
                        "embedded" => PackageSource::Embedded,
                        "builtin" => PackageSource::BuiltIn,
                        "registry" => PackageSource::Registry,
                        other => panic!("{probe}: no {other} packages expected"),
                    },
                    root,
                    testable: false,
                });
            }
            // Resolution order is not the lock's.
            packages.reverse();
            assert_eq!(
                lock_text(&scratch, &packages).unwrap().as_deref(),
                Some(committed.as_str()),
                "{probe}"
            );
            std::fs::remove_dir_all(&scratch).unwrap();
        }
    }

    #[test]
    fn a_git_package_leaves_the_lock_unwritten() {
        let scratch = std::env::temp_dir().join(format!("unbound-lock-git-{}", std::process::id()));
        std::fs::create_dir_all(scratch.join("Packages")).unwrap();
        std::fs::write(
            scratch.join("Packages/manifest.json"),
            "{\"dependencies\": {\"a\": \"https://x/a.git\"}}",
        )
        .unwrap();
        let git = Package {
            name: "a".into(),
            version: "1.0.0".into(),
            source: PackageSource::Git,
            root: scratch.join("a"),
            testable: false,
        };
        assert_eq!(lock_text(&scratch, &[git]).unwrap(), None);
        std::fs::remove_dir_all(&scratch).unwrap();
    }

    /// An embedded package the manifest does not name brings what it asks for into the lock
    /// (tests/projects/packageresolve: Timeline and Settings Manager, which only it asks for).
    #[test]
    fn what_only_an_embedded_package_asks_for_is_locked() {
        let scratch =
            std::env::temp_dir().join(format!("unbound-lock-embedded-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&scratch);
        let embedded = scratch.join("Packages/com.probe.own");
        let timeline = scratch.join("cache/com.unity.timeline");
        std::fs::create_dir_all(&embedded).unwrap();
        std::fs::create_dir_all(&timeline).unwrap();
        std::fs::write(
            scratch.join("Packages/manifest.json"),
            "{\"dependencies\": {}}",
        )
        .unwrap();
        std::fs::write(
            embedded.join("package.json"),
            "{\"name\": \"com.probe.own\", \"version\": \"1.0.0\", \
             \"dependencies\": {\"com.unity.timeline\": \"1.7.0\"}}",
        )
        .unwrap();
        std::fs::write(
            timeline.join("package.json"),
            "{\"name\": \"com.unity.timeline\", \"version\": \"1.8.10\", \"dependencies\": {}}",
        )
        .unwrap();
        let package = |name: &str, version: &str, source, root: &Path| Package {
            name: name.into(),
            version: version.into(),
            source,
            root: root.to_path_buf(),
            testable: false,
        };
        let packages = [
            package(
                "com.unity.timeline",
                "1.8.10",
                PackageSource::Registry,
                &timeline,
            ),
            package("com.probe.own", "1.0.0", PackageSource::Embedded, &embedded),
        ];
        let text = lock_text(&scratch, &packages).unwrap().unwrap();
        let lock: serde_json::Value = serde_json::from_str(&text).unwrap();
        assert_eq!(lock["dependencies"]["com.probe.own"]["depth"], 0);
        assert_eq!(
            lock["dependencies"]["com.unity.timeline"]["version"],
            "1.8.10"
        );
        assert_eq!(lock["dependencies"]["com.unity.timeline"]["depth"], 1);
        std::fs::remove_dir_all(&scratch).unwrap();
    }

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

    fn write(path: &Path, text: &str) {
        std::fs::create_dir_all(path.parent().unwrap()).unwrap();
        std::fs::write(path, text).unwrap();
    }

    fn package(dir: &Path, name: &str, version: &str, dependencies: &str) {
        write(
            &dir.join("package.json"),
            &format!(
                r#"{{"name": "{name}", "version": "{version}", "dependencies": {{{dependencies}}}}}"#
            ),
        );
    }

    /// A lock-less project: an embedded package asks for `com.example.dep`, `com.example.free` and
    /// `com.example.direct` 1.0.0, the project for `com.example.direct` 1.0.0 too; the installation's manifest names 2.0.0 as
    /// each one's minimum but `com.example.free`'s (which has a version only).
    fn minimum_project(root: &Path) -> (PathBuf, UnityInstall) {
        let _ = std::fs::remove_dir_all(root);
        let project = root.join("project");
        write(
            &project.join("Packages/manifest.json"),
            r#"{"dependencies": {"com.example.direct": "1.0.0"}}"#,
        );
        package(
            &project.join("Packages/com.example.embedded"),
            "com.example.embedded",
            "1.0.0",
            r#""com.example.dep": "1.0.0", "com.example.free": "1.0.0", "com.example.direct": "1.0.0""#,
        );
        let cache = project.join("Library/PackageCache");
        for name in ["com.example.dep", "com.example.direct", "com.example.free"] {
            for version in ["1.0.0", "2.0.0"] {
                package(&cache.join(format!("{name}@{version}")), name, version, "");
            }
        }
        let data = root.join("Data");
        write(
            &data.join("Resources/PackageManager/Editor/manifest.json"),
            r#"{"packages": {"com.example.dep": {"minimumVersion": "2.0.0", "version": "2.0.0"},
                "com.example.direct": {"minimumVersion": "2.0.0"}, "com.example.free": {"version": "2.0.0"}}}"#,
        );
        let install = UnityInstall {
            data,
            version: unbound_compat::UnityVersion::new(6000, 0, 71),
        };
        (project, install)
    }

    fn versions(resolution: &Resolution) -> BTreeMap<String, String> {
        resolution
            .packages
            .iter()
            .map(|p| (p.name.clone(), p.version.clone()))
            .collect()
    }

    #[test]
    fn lockless_packages_take_the_editors_minimum_where_the_release_does() {
        let root = std::env::temp_dir().join(format!("unbound-minimums-{}", std::process::id()));
        let (project, install) = minimum_project(&root);
        let measured = PackageResolutionRules {
            editor_minimum_versions: true,
        };
        let raised = versions(&resolve(&project, &install, measured).unwrap());
        assert_eq!(raised["com.example.dep"], "2.0.0");
        // The project's own dependency keeps the version it names, also when a package asks for it.
        assert_eq!(raised["com.example.direct"], "1.0.0");
        // A package with a version but no minimum keeps the version asked for.
        assert_eq!(raised["com.example.free"], "1.0.0");
        // Releases without the rule keep the highest version asked for.
        let kept =
            versions(&resolve(&project, &install, PackageResolutionRules::default()).unwrap());
        assert_eq!(kept["com.example.dep"], "1.0.0");
        assert_eq!(kept["com.example.direct"], "1.0.0");
        // A lock decides on its own.
        write(
            &project.join("Packages/packages-lock.json"),
            r#"{"dependencies": {"com.example.dep": {"version": "1.0.0", "source": "registry"}}}"#,
        );
        let locked = versions(&resolve(&project, &install, measured).unwrap());
        assert_eq!(locked["com.example.dep"], "1.0.0");
        let _ = std::fs::remove_dir_all(&root);
    }

    /// Test assemblies build for an embedded package and those the manifest lists in `testables`.
    #[test]
    fn embedded_and_listed_packages_are_testable() {
        let root = std::env::temp_dir().join(format!("unbound-testables-{}", std::process::id()));
        let (project, install) = minimum_project(&root);
        write(
            &project.join("Packages/manifest.json"),
            r#"{"dependencies": {"com.example.direct": "1.0.0"}, "testables": ["com.example.free"]}"#,
        );
        let resolution = resolve(&project, &install, PackageResolutionRules::default()).unwrap();
        let _ = std::fs::remove_dir_all(&root);
        let testable: BTreeMap<&str, bool> = resolution
            .packages
            .iter()
            .map(|p| (p.name.as_str(), p.testable))
            .collect();
        assert!(testable["com.example.embedded"]);
        assert!(testable["com.example.free"]);
        assert!(!testable["com.example.dep"]);
        assert!(!testable["com.example.direct"]);
    }

    #[test]
    fn versions_compare_by_number_then_release() {
        assert!(newer("1.10.0", "1.9.3"));
        assert!(newer("2.0.0", "2.0.0-pre.1"));
        assert!(!newer("1.2.0", "1.2.0"));
    }

    /// A gzipped npm tarball of `files` under `package/`.
    fn npm_tarball(files: &[(&str, &str)]) -> Vec<u8> {
        let mut tar = Vec::new();
        for (path, text) in files {
            tar.extend(header(
                &format!("package/{path}"),
                text.len(),
                b'0',
                b"ustar\x0000",
                "",
            ));
            tar.extend(padded(text.as_bytes()));
        }
        tar.extend(vec![0u8; 1024]);
        let mut encoder = flate2::write::GzEncoder::new(Vec::new(), flate2::Compression::fast());
        std::io::Write::write_all(&mut encoder, &tar).unwrap();
        encoder.finish().unwrap()
    }

    #[test]
    fn local_tarballs_are_unpacked_by_their_contents() {
        let dir = std::env::temp_dir().join(format!("unbound-tarball-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let store = dir.join("store");
        let tarball = dir.join("com.test.local-1.0.0.tgz");
        let package =
            |version: &str| format!(r#"{{"name": "com.test.local", "version": "{version}"}}"#);
        let write = |version: &str, marker: &str| {
            std::fs::write(
                &tarball,
                npm_tarball(&[("package.json", &package(version)), ("Marker.txt", marker)]),
            )
            .unwrap();
            let file = std::fs::File::options().write(true).open(&tarball).unwrap();
            file.set_modified(std::time::UNIX_EPOCH + std::time::Duration::from_secs(1_000_000))
                .unwrap();
        };
        write("1.0.0", "one");
        let first = unpack_local_tarball(&tarball, &store, "com.test.local").unwrap();
        assert_eq!(
            std::fs::read_to_string(first.join("Marker.txt")).unwrap(),
            "one"
        );
        assert!(first.starts_with(store.join("tarballs")));
        // Replaced by an archive of the same size and time: unpacked again.
        write("1.0.0", "two");
        let second = unpack_local_tarball(&tarball, &store, "com.test.local").unwrap();
        assert_ne!(first, second);
        assert_eq!(
            std::fs::read_to_string(second.join("Marker.txt")).unwrap(),
            "two"
        );
        // The same contents again: the first entry.
        write("1.0.0", "one");
        assert_eq!(
            unpack_local_tarball(&tarball, &store, "com.test.local").unwrap(),
            first
        );
        // An archive of another package, or not an archive, fails.
        assert!(unpack_local_tarball(&tarball, &store, "com.test.other").is_err());
        std::fs::write(&tarball, b"not a tarball").unwrap();
        assert!(unpack_local_tarball(&tarball, &store, "com.test.local").is_err());
        assert!(unpack_local_tarball(&dir.join("absent.tgz"), &store, "com.test.local").is_err());
        let _ = std::fs::remove_dir_all(&dir);
    }

    /// A git package's folder in the project's cache is taken only when its name holds the lock's
    /// commit (the Editor names it `<name>@<the commit's first 12 hexadecimal digits>`); a
    /// folder of another commit is kept as the fallback, one without a commit in the name is taken
    /// from the user's directories only, and without a locked commit any folder is taken.
    #[test]
    fn git_package_folders_are_taken_for_the_locked_commit() {
        let root = std::env::temp_dir().join(format!("unbound-git-cache-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        let user = root.join("user");
        let cache = root.join("Library/PackageCache");
        let name = "com.example.git";
        package(
            &cache.join(format!("{name}@aaaaaaaaaaaa")),
            name,
            "1.0.0",
            "",
        );
        package(
            &cache.join(format!("{name}@bbbbbbbbbbbb")),
            name,
            "2.0.0",
            "",
        );
        let locked_a = "aaaaaaaaaaaa1111111111111111111111111111";
        let locked_c = "cccccccccccc1111111111111111111111111111";
        let dirs = vec![user.clone(), cache.clone()];

        let (locked, other) = find_git(&dirs, 1, name, Some(locked_a));
        assert_eq!(locked.map(|(_, v)| v).as_deref(), Some("1.0.0"));
        assert!(other.is_none());
        // Upper-case hashes name the same commit.
        let upper = locked_a.to_ascii_uppercase();
        let (locked, _) = find_git(&dirs, 1, name, Some(&upper));
        assert_eq!(locked.map(|(_, v)| v).as_deref(), Some("1.0.0"));

        let (locked, other) = find_git(&dirs, 1, name, Some(locked_c));
        assert!(locked.is_none(), "no cached folder holds the lock's commit");
        assert_eq!(other.map(|(_, v)| v).as_deref(), Some("1.0.0"));

        let (locked, _) = find_git(&dirs, 1, name, None);
        assert_eq!(locked.map(|(_, v)| v).as_deref(), Some("1.0.0"));

        // A folder without a commit: the user's is taken, the project cache's is not.
        package(&cache.join(name), name, "3.0.0", "");
        let (locked, other) = find_git(&dirs, 1, name, Some(locked_c));
        assert!(locked.is_none());
        assert!(other.is_some());
        package(&user.join(name), name, "4.0.0", "");
        let (locked, _) = find_git(&dirs, 1, name, Some(locked_c));
        assert_eq!(locked.map(|(_, v)| v).as_deref(), Some("4.0.0"));
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn file_dependencies_name_folders_or_tarballs_relative_to_packages() {
        assert!(is_tarball("file:../LocalPackages/a-1.0.0.tgz"));
        assert!(is_tarball("file:/abs/A.TAR.GZ"));
        assert!(!is_tarball("file:../my_package_folder"));
        let packages = Path::new("/project/Packages");
        assert_eq!(
            local_path(packages, "file:../x.tgz"),
            Path::new("/project/Packages/../x.tgz")
        );
        assert_eq!(
            local_path(packages, "file:/abs/x.tgz"),
            Path::new("/abs/x.tgz")
        );
    }
}
