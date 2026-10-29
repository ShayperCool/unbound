//! Unity's built-in shader sources (`UI/Default`, `Sprites/Default`, `Standard`, ...): the
//! release's `builtin_shaders-<version>.zip` that Unity publishes (MIT licensed), downloaded once
//! and unpacked in the global cache. The renderer compiles built-in shaders from them like any
//! project shader.
use crate::install::UnityInstall;
use anyhow::{Context, Result, bail};
use std::{
    io::Read,
    path::{Path, PathBuf},
};
use unbound_compat::UnityVersion;

/// Bumped when the unpacked layout changes.
const FORMAT_VERSION: u32 = 1;

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
    /// release, downloaded when missing.
    pub fn find(install: &UnityInstall, project_version_txt: Option<&str>) -> Result<Self> {
        if let Some(dir) = std::env::var_os("UNBOUND_BUILTIN_SHADERS") {
            let root = PathBuf::from(dir);
            if !root.join("DefaultResourcesExtra").is_dir() {
                bail!(
                    "UNBOUND_BUILTIN_SHADERS={} has no DefaultResourcesExtra",
                    root.display()
                );
            }
            return Ok(Self { root });
        }
        let version = install.version;
        let root = crate::cache_root()
            .join("builtin-shaders")
            .join(format!("v{FORMAT_VERSION}"))
            .join(version.to_string());
        if root.join(".complete").is_file() {
            return Ok(Self { root });
        }
        let revision = revision(install, project_version_txt).with_context(|| {
            format!("which build of Unity {version} is installed (its revision) is unknown")
        })?;
        let url = format!(
            "https://download.unity3d.com/download_unity/{revision}/builtin_shaders-{version}.zip"
        );
        tracing::info!("downloading Unity {version}'s built-in shader sources from {url}");
        let archive = download(&url).with_context(|| format!("downloading {url}"))?;
        unpack(&archive, &root).with_context(|| format!("unpacking {url}"))?;
        Ok(Self { root })
    }
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
    let mut response = ureq::get(url).call()?;
    let mut bytes = Vec::new();
    response
        .body_mut()
        .with_config()
        .limit(256 << 20)
        .reader()
        .read_to_end(&mut bytes)?;
    Ok(bytes)
}

/// Unpacks the archive (which holds `build/BuiltinShaders/builtin_shaders.zip` in recent
/// releases, the sources themselves in older ones) into `root`, atomically.
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
    let parent = root.parent().context("cache path")?;
    std::fs::create_dir_all(parent)?;
    let temp = parent.join(format!(".unpacking-{}", std::process::id()));
    if temp.exists() {
        std::fs::remove_dir_all(&temp)?;
    }
    zip.extract(&temp)?;
    if !temp.join("DefaultResourcesExtra").is_dir() {
        let _ = std::fs::remove_dir_all(&temp);
        bail!("the archive has no DefaultResourcesExtra");
    }
    std::fs::write(temp.join(".complete"), b"")?;
    if root.exists() {
        std::fs::remove_dir_all(root)?;
    }
    std::fs::rename(&temp, root)?;
    Ok(())
}

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
}
