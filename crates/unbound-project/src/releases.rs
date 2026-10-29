//! Unity's public release index (`services.api.unity.com`): a release's build revision and the
//! downloads of its Editor.
use anyhow::{Context, Result, bail};
use serde::Deserialize;
use unbound_compat::UnityVersion;

const INDEX: &str = "https://services.api.unity.com/unity/editor/release/v1/releases";

#[derive(Debug, Clone)]
pub struct Release {
    pub version: UnityVersion,
    /// The build revision (`907bc2d768b5`), part of every download URL of the release.
    pub revision: String,
    pub downloads: Vec<Download>,
}

#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Download {
    pub url: String,
    /// `TAR_XZ`, `PKG`, `EXE`.
    #[serde(rename = "type")]
    pub kind: String,
    /// `LINUX`, `MAC_OS`, `WINDOWS`.
    pub platform: String,
    /// `X86_64`, `ARM64`.
    pub architecture: String,
    /// `md5-<base64>` of the file.
    #[serde(default)]
    pub integrity: String,
    #[serde(default)]
    pub download_size: Option<Size>,
    /// The optional parts installed beside this Editor (platform support, documentation).
    #[serde(default)]
    pub modules: Vec<Module>,
}

/// A module of an Editor download: `android`, `ios`, `webgl`, ...
#[derive(Debug, Clone, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Module {
    pub id: String,
    pub url: String,
    /// `TAR_XZ`, `PKG`, `ZIP`, ...
    #[serde(rename = "type")]
    pub kind: String,
    /// Where its contents go: `{UNITY_PATH}/Editor/Data/PlaybackEngines/AndroidPlayer`, ...
    #[serde(default)]
    pub destination: Option<String>,
    #[serde(default)]
    pub integrity: String,
    #[serde(default)]
    pub download_size: Option<Size>,
}

#[derive(Debug, Clone, Deserialize)]
pub struct Size {
    pub value: u64,
}

impl Release {
    /// The Linux Editor's `.tar.xz`: the one archive that unpacks with plain readers on every
    /// platform, and its `Editor/Data` holds the same platform-neutral files (managed assemblies,
    /// resources, built-in packages) as the other platforms' installers.
    pub fn linux_editor(&self) -> Option<&Download> {
        self.downloads
            .iter()
            .find(|d| d.platform == "LINUX" && d.kind == "TAR_XZ")
    }
}

/// The index's entry for exactly `version`.
pub fn lookup(version: UnityVersion) -> Result<Release> {
    #[derive(Deserialize)]
    struct Page {
        results: Vec<Entry>,
    }
    #[derive(Deserialize)]
    #[serde(rename_all = "camelCase")]
    struct Entry {
        version: String,
        short_revision: String,
        downloads: Vec<Download>,
    }
    let url = format!("{INDEX}?version={version}&limit=10");
    let text = crate::net::get(&url, |mut r| Ok(r.body_mut().read_to_string()?))
        .with_context(|| format!("querying Unity's release index ({url})"))?;
    let page: Page = serde_json::from_str(&text).context("reading Unity's release index")?;
    let Some(entry) = page.results.into_iter().find(|e| {
        e.version
            .parse::<UnityVersion>()
            .is_ok_and(|v| v == version)
    }) else {
        bail!("Unity's release index does not list {version}");
    };
    Ok(Release {
        version,
        revision: entry.short_revision,
        downloads: entry.downloads,
    })
}
