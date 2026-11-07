//! Unity's built-in shader sources (`UI/Default`, `Sprites/Default`, `Standard`, ...): the
//! release's `builtin_shaders-<version>.zip` that Unity publishes (MIT licensed), downloaded once
//! and unpacked in the global cache. The renderer compiles built-in shaders from them like any
//! project shader.
use crate::install::UnityInstall;
use anyhow::{Context, Result, bail};
use std::{
    collections::HashMap,
    fs::{self, File},
    io::Read,
    path::{Path, PathBuf},
    sync::{
        Arc, Mutex, OnceLock,
        atomic::{AtomicU64, Ordering},
    },
};
use unbound_compat::UnityVersion;

/// Immutable archive-content entries, selected by an atomically published release marker.
const FORMAT_VERSION: u32 = 2;

/// The unpacked sources: `CGIncludes`, `DefaultResources`, `DefaultResourcesExtra`, ...
#[derive(Debug, Clone)]
pub struct BuiltinShaders {
    pub root: PathBuf,
}

impl BuiltinShaders {
    /// Directories holding built-in `.shader` files.
    pub fn shader_roots(&self) -> Vec<PathBuf> {
        ["DefaultResources", "DefaultResourcesExtra"]
            .iter()
            .map(|d| self.root.join(d))
            .filter(|d| d.is_dir())
            .collect()
    }

    pub fn cg_includes(&self) -> PathBuf {
        self.root.join("CGIncludes")
    }

    /// `UNBOUND_BUILTIN_SHADERS` (an unpacked archive), else the cached copy for the install's
    /// release, downloaded when missing (its URL needs the release's build revision: from the
    /// installation, the project, else Unity's release index). `UNBOUND_OFFLINE` forbids
    /// downloading: a missing copy is then an error, raised before any request. The first
    /// resolution is shared for this process, including failures; restart to retry a failure.
    pub fn find(install: &UnityInstall, project_version_txt: Option<&str>) -> Result<Self> {
        if let Some(dir) = std::env::var_os("UNBOUND_BUILTIN_SHADERS") {
            let root = PathBuf::from(dir);
            return session_sources(&root, || {
                validate_sources(&root)
                    .with_context(|| format!("UNBOUND_BUILTIN_SHADERS={}", root.display()))?;
                Ok(Self { root: root.clone() })
            });
        }
        let version = install.version;
        let root = crate::cache_root()
            .join("builtin-shaders")
            .join(format!("v{FORMAT_VERSION}"))
            .join(version.to_string());
        session_sources(&root, || {
            acquire(
                &root,
                version,
                std::env::var_os("UNBOUND_OFFLINE").is_some(),
                || {
                    let revision = match revision(install, project_version_txt) {
                        Some(revision) => revision,
                        None => {
                            crate::releases::lookup(version)
                                .with_context(|| {
                                    format!("the build revision of Unity {version} is unknown")
                                })?
                                .revision
                        }
                    };
                    let url = format!(
                        "https://download.unity3d.com/download_unity/{revision}/builtin_shaders-{version}.zip"
                    );
                    tracing::info!(
                        "downloading Unity {version}'s built-in shader sources from {url}"
                    );
                    download(&url).with_context(|| format!("downloading {url}"))
                },
            )
        })
    }
}

type SourceResult = std::result::Result<BuiltinShaders, String>;

/// Managed defaults, declaration queries and the renderer share one resolution, even when the
/// game reopens its Project. A failed dependency is diagnosed consistently for this process;
/// another process can retry it, without changing already initialized materials underneath us.
fn session_sources(
    root: &Path,
    load: impl FnOnce() -> Result<BuiltinShaders>,
) -> Result<BuiltinShaders> {
    static SOURCES: OnceLock<Mutex<HashMap<PathBuf, Arc<OnceLock<SourceResult>>>>> =
        OnceLock::new();
    let entry = SOURCES
        .get_or_init(Default::default)
        .lock()
        .unwrap()
        .entry(root.to_owned())
        .or_default()
        .clone();
    entry
        .get_or_init(|| {
            load().map_err(|e| {
                format!("shader source resolution failed for this process; restart to retry: {e:#}")
            })
        })
        .clone()
        .map_err(anyhow::Error::msg)
}

fn validate_sources(root: &Path) -> Result<()> {
    if !root.join("DefaultResourcesExtra").is_dir()
        || !root.join("CGIncludes/UnityCG.cginc").is_file()
    {
        bail!(
            "{} lacks DefaultResourcesExtra or CGIncludes/UnityCG.cginc",
            root.display()
        );
    }
    Ok(())
}

fn cached(root: &Path) -> Result<Option<BuiltinShaders>> {
    let hash = match fs::read_to_string(root.join(".complete")) {
        Ok(hash) => hash,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Ok(None),
        Err(e) => return Err(e.into()),
    };
    if hash.len() != 64 || !hash.bytes().all(|b| b.is_ascii_hexdigit()) {
        bail!("invalid shader source cache marker at {}", root.display());
    }
    let sources = root.join(hash);
    validate_sources(&sources)?;
    if !sources.join(".complete").is_file() {
        bail!(
            "incomplete shader source cache entry at {}",
            sources.display()
        );
    }
    Ok(Some(BuiltinShaders { root: sources }))
}

/// The lock covers the cache recheck, acquisition and publication. It is never unlinked, so
/// waiters cannot acquire different lock inodes; process termination releases it automatically.
fn acquire(
    root: &Path,
    version: UnityVersion,
    offline: bool,
    load: impl FnOnce() -> Result<Vec<u8>>,
) -> Result<BuiltinShaders> {
    if let Some(found) = cached(root)? {
        return Ok(found);
    }
    fs::create_dir_all(root)?;
    let lock = File::options()
        .create(true)
        .truncate(false)
        .read(true)
        .write(true)
        .open(root.join(".lock"))?;
    lock.lock()
        .with_context(|| format!("locking shader source cache {}", root.display()))?;
    if let Some(found) = cached(root)? {
        return Ok(found);
    }
    check_download_allowed(offline, version, root)?;
    let archive = load()?;
    let hash = blake3::hash(&archive).to_hex().to_string();
    let sources = root.join(&hash);
    if !sources.join(".complete").is_file() {
        static NEXT: AtomicU64 = AtomicU64::new(0);
        let temp = loop {
            let path = root.join(format!(
                ".unpacking-{}-{}",
                std::process::id(),
                NEXT.fetch_add(1, Ordering::Relaxed)
            ));
            match fs::create_dir(&path) {
                Ok(()) => break path,
                Err(e) if e.kind() == std::io::ErrorKind::AlreadyExists => continue,
                Err(e) => return Err(e.into()),
            }
        };
        let extracted = unpack(&archive, &temp).and_then(|()| validate_sources(&temp));
        if let Err(e) = extracted {
            let _ = fs::write(temp.join(".failure.txt"), format!("{e:#}"));
            return Err(e).with_context(|| {
                format!(
                    "unpacking Unity {version} shader sources; diagnostics retained at {}",
                    temp.display()
                )
            });
        }
        fs::write(temp.join(".complete"), b"")?;
        fs::rename(&temp, &sources)
            .with_context(|| format!("publishing shader sources at {}", sources.display()))?;
    }
    validate_sources(&sources)?;
    fs::write(root.join(".complete.pending"), hash)?;
    fs::rename(root.join(".complete.pending"), root.join(".complete"))?;
    Ok(BuiltinShaders { root: sources })
}

/// Fails when `offline` (`UNBOUND_OFFLINE`) forbids downloading the release's missing sources
/// into `root`, naming them and how to provide them.
fn check_download_allowed(offline: bool, version: UnityVersion, root: &Path) -> Result<()> {
    if offline {
        bail!(
            "Unity {version}'s built-in shader sources are not cached ({} has no .complete marker) \
             and UNBOUND_OFFLINE forbids downloading builtin_shaders-{version}.zip; set \
             UNBOUND_BUILTIN_SHADERS to a directory of the unpacked sources (with \
             DefaultResourcesExtra and CGIncludes/UnityCG.cginc), or unset UNBOUND_OFFLINE \
             to download it once (an installed Unity Editor or UNBOUND_UNITY does not provide \
             these sources)",
            root.display()
        );
    }
    Ok(())
}

/// The install's build revision (`907bc2d768b5`): from the Hub's `modules.json` next to the
/// editor, else the project's `m_EditorVersionWithRevision` when the project is on that release.
fn revision(install: &UnityInstall, project_version_txt: Option<&str>) -> Option<String> {
    let hub_dir = install.root()?;
    if let Ok(text) = std::fs::read_to_string(hub_dir.join("modules.json")) {
        // ".../download_unity/<revision>/..."
        if let Some(rest) = text.split("/download_unity/").nth(1)
            && let Some(rev) = rest.split('/').next()
            && is_revision(rev)
        {
            return Some(rev.to_string());
        }
    }
    let text = project_version_txt?;
    let line = text
        .lines()
        .find(|l| l.trim_start().starts_with("m_EditorVersionWithRevision:"))?;
    let (_, value) = line.split_once(':')?;
    let (version, rest) = value.trim().split_once(' ')?;
    if version.parse::<UnityVersion>().ok()? != install.version {
        return None;
    }
    let rev = rest.trim().trim_start_matches('(').trim_end_matches(')');
    is_revision(rev).then(|| rev.to_string())
}

fn is_revision(s: &str) -> bool {
    s.len() >= 8 && s.chars().all(|c| c.is_ascii_hexdigit())
}

fn download(url: &str) -> Result<Vec<u8>> {
    crate::net::get(url, |mut response| {
        Ok(response
            .body_mut()
            .with_config()
            .limit(256 << 20)
            .read_to_vec()?)
    })
}

/// Unpacks the archive (which holds `build/BuiltinShaders/builtin_shaders.zip` in recent
/// releases, the sources themselves in older ones) into a private staging directory.
fn unpack(archive: &[u8], root: &Path) -> Result<()> {
    let mut zip = zip::ZipArchive::new(std::io::Cursor::new(archive))?;
    let inner = (0..zip.len()).find(|&i| {
        zip.by_index(i)
            .is_ok_and(|f| f.name().is_ok_and(|n| n.ends_with("builtin_shaders.zip")))
    });
    if let Some(i) = inner {
        let mut bytes = Vec::new();
        zip.by_index(i)?.read_to_end(&mut bytes)?;
        return unpack(&bytes, root);
    }
    zip.extract(root)?;
    Ok(())
}

#[cfg(test)]
#[path = "builtin_shaders_tests.rs"]
mod publication_tests;

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn the_revision_comes_from_the_project_on_the_same_release() {
        let install = UnityInstall {
            data: PathBuf::from("/nonexistent/6000.0.71f1/Editor/Data"),
            version: "6000.0.71f1".parse().unwrap(),
        };
        let txt = "m_EditorVersion: 6000.0.71f1\nm_EditorVersionWithRevision: 6000.0.71f1 (907bc2d768b5)\n";
        assert_eq!(
            revision(&install, Some(txt)).as_deref(),
            Some("907bc2d768b5")
        );
        let other = "m_EditorVersionWithRevision: 6000.0.70f1 (0123456789ab)\n";
        assert_eq!(revision(&install, Some(other)), None);
    }

    #[test]
    fn offline_forbids_downloading_missing_sources() {
        let version: UnityVersion = "6000.0.71f1".parse().unwrap();
        let root = Path::new("/nonexistent/builtin-shaders/v1/6000.0.71f1");
        assert!(check_download_allowed(false, version, root).is_ok());
        let message = format!(
            "{:#}",
            check_download_allowed(true, version, root).unwrap_err()
        );
        for part in [
            "Unity 6000.0.71f1's built-in shader sources are not cached",
            "/nonexistent/builtin-shaders/v1/6000.0.71f1",
            "UNBOUND_OFFLINE forbids downloading builtin_shaders-6000.0.71f1.zip",
            "UNBOUND_BUILTIN_SHADERS",
            "unset UNBOUND_OFFLINE",
        ] {
            assert!(message.contains(part), "{part:?} missing from {message:?}");
        }
    }
}
