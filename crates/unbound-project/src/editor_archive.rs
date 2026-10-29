//! The files Unbound reads from a Unity Editor release (reference assemblies, the class
//! libraries, `CGIncludes`, built-in resources and packages, platform modules' editor
//! assemblies), taken from the release's published Editor archive when no installation of it is
//! present: the archive is streamed once, only those files are unpacked, into
//! `<cache>/editors/v<N>/<release>/Editor/Data`. Nothing of the Editor is run.
use crate::releases;
use anyhow::{Context, Result, bail};
use md5::{Digest, Md5};
use std::{
    io::{self, BufReader, Read},
    path::{Path, PathBuf},
    time::Duration,
};
use unbound_compat::UnityVersion;

/// Bumped when the set of unpacked files or their layout changes.
const FORMAT_VERSION: u32 = 1;

/// Directories of `Editor/Data` that are unpacked whole.
const DIRS: &[&str] = &[
    "Managed/",
    "UnityReferenceAssemblies/",
    "NetStandard/",
    "CGIncludes/",
    "Tools/Unity.SourceGenerators/",
    "MonoBleedingEdge/lib/mono/4.5/",
    "Resources/PackageManager/BuiltInPackages/",
];

/// Single files of `Editor/Data`.
const FILES: &[&str] = &[
    "Resources/unity default resources",
    "Resources/unity_builtin_extra",
    "Resources/unity editor resources",
];

/// Whether `rel` (a path below `Editor/Data/`) is unpacked: [`DIRS`], [`FILES`], the platform
/// modules' editor assemblies (`PlaybackEngines/<platform>/UnityEditor.*.dll`,
/// `Unity.Android.*.dll`) and the players' built-in resources.
fn wanted(rel: &str) -> bool {
    if DIRS.iter().any(|dir| rel.starts_with(dir)) || FILES.contains(&rel) {
        return true;
    }
    let Some(rest) = rel.strip_prefix("PlaybackEngines/") else {
        return false;
    };
    if rest.ends_with("/Data/Resources/unity default resources") {
        return true;
    }
    match rest.split_once('/') {
        Some((_, file)) if !file.contains('/') => {
            file.ends_with(".dll")
                && (file.starts_with("UnityEditor.") || file.starts_with("Unity.Android."))
        }
        _ => false,
    }
}

/// The release's unpacked files (holding `Editor/Data`), whether or not they are there yet.
pub fn root(release: UnityVersion) -> PathBuf {
    crate::cache_root()
        .join("editors")
        .join(format!("v{FORMAT_VERSION}"))
        .join(release.to_string())
}

/// `Editor/Data` of the release's unpacked files, when they are complete.
pub fn cached(release: UnityVersion) -> Option<PathBuf> {
    let root = root(release);
    root.join(".complete")
        .is_file()
        .then(|| root.join("Editor/Data"))
}

/// `Editor/Data` of the release's files, downloaded and unpacked first when they are not cached.
/// `UNBOUND_OFFLINE` forbids the download.
pub fn fetch(release: UnityVersion) -> Result<PathBuf> {
    if let Some(data) = cached(release) {
        return Ok(data);
    }
    if std::env::var_os("UNBOUND_OFFLINE").is_some() {
        bail!(
            "Unity {release}'s files are not cached and UNBOUND_OFFLINE forbids downloading them"
        );
    }
    let index = releases::lookup(release)?;
    let download = index.linux_editor().with_context(|| {
        format!("Unity {release} has no Linux Editor archive to take its files from")
    })?;
    let size = download.download_size.as_ref().map(|s| s.value);
    tracing::warn!(
        "Unity {release} is not installed: downloading its Editor archive{} once, for its reference \
         assemblies, built-in resources and packages ({})",
        size.map(|s| format!(" ({:.1} GB)", s as f64 / 1e9))
            .unwrap_or_default(),
        download.url
    );
    let root = root(release);
    let parent = root.parent().context("cache path")?;
    std::fs::create_dir_all(parent)?;
    let temp = parent.join(format!(".unpacking-{release}-{}", std::process::id()));
    if temp.exists() {
        std::fs::remove_dir_all(&temp)?;
    }
    std::fs::create_dir_all(&temp)?;
    let result = unpack(&download.url, size, &download.integrity, release, &temp);
    if let Err(e) = result {
        let _ = std::fs::remove_dir_all(&temp);
        return Err(e).with_context(|| format!("unpacking {}", download.url));
    }
    std::fs::write(temp.join(".complete"), b"")?;
    if root.exists() {
        if root.join(".complete").is_file() {
            // Another process finished first.
            let _ = std::fs::remove_dir_all(&temp);
            return Ok(root.join("Editor/Data"));
        }
        std::fs::remove_dir_all(&root)?;
    }
    std::fs::rename(&temp, &root)?;
    tracing::info!("Unity {release}'s files are in {}", root.display());
    Ok(root.join("Editor/Data"))
}

fn unpack(
    url: &str,
    size: Option<u64>,
    integrity: &str,
    release: UnityVersion,
    into: &Path,
) -> Result<()> {
    let mut stream = Stream {
        url: url.to_string(),
        release,
        position: 0,
        total: size,
        body: None,
        failures: 0,
        hasher: Md5::new(),
        next_report: REPORT_EVERY,
    };
    let mut unpacked = 0usize;
    {
        let xz = liblzma::read::XzDecoder::new_multi_decoder(BufReader::with_capacity(
            1 << 20,
            &mut stream,
        ));
        let mut archive = tar::Archive::new(xz);
        archive.set_preserve_permissions(false);
        archive.set_preserve_mtime(false);
        let mut entries = archive.entries()?;
        for entry in &mut entries {
            let mut entry = entry?;
            let path = entry.path()?.into_owned();
            let Some(rel) = path.to_str().and_then(|p| p.strip_prefix("Editor/Data/")) else {
                continue;
            };
            if !wanted(rel) {
                continue;
            }
            if !entry.unpack_in(into)? {
                bail!(
                    "the archive's entry {} leaves the target directory",
                    path.display()
                );
            }
            unpacked += 1;
        }
        drop(entries);
        // The tar reader stops at the end-of-archive marker; the rest still counts for the hash.
        io::copy(&mut archive.into_inner(), &mut io::sink())?;
    }
    if let Some(expected) = integrity.strip_prefix("md5-") {
        use base64::Engine as _;
        let expected = base64::engine::general_purpose::STANDARD.decode(expected)?;
        let actual = stream.hasher.finalize();
        if actual.as_slice() != expected.as_slice() {
            bail!("the archive's MD5 does not match the release index's");
        }
    }
    if !into.join("Editor/Data/Managed/UnityEngine").is_dir() {
        bail!("the archive has no Editor/Data/Managed/UnityEngine");
    }
    tracing::debug!("unpacked {unpacked} entries of Unity {release}'s Editor archive");
    Ok(())
}

const REPORT_EVERY: u64 = 512 << 20;
const MAX_FAILURES: u32 = 8;

/// The archive's bytes over HTTP, resumed with a range request where a connection breaks, hashed
/// as they pass.
struct Stream {
    url: String,
    release: UnityVersion,
    position: u64,
    total: Option<u64>,
    body: Option<Box<dyn Read + Send>>,
    failures: u32,
    hasher: Md5,
    next_report: u64,
}

impl Stream {
    fn open(&mut self) -> Result<()> {
        let mut request = crate::net::agent().get(&self.url);
        if self.position > 0 {
            request = request.header("Range", &format!("bytes={}-", self.position));
        }
        let response = request.call()?;
        if self.position > 0 && response.status().as_u16() != 206 {
            bail!(
                "the server does not resume the download (HTTP {})",
                response.status()
            );
        }
        self.body = Some(Box::new(response.into_body().into_reader()));
        Ok(())
    }

    fn fail(&mut self, error: impl std::fmt::Display) -> io::Result<()> {
        self.body = None;
        self.failures += 1;
        if self.failures > MAX_FAILURES {
            return Err(io::Error::other(format!(
                "downloading {}: {error}",
                self.url
            )));
        }
        tracing::warn!(
            "downloading Unity {}'s archive: {error}; resuming at {} MB",
            self.release,
            self.position >> 20
        );
        std::thread::sleep(Duration::from_secs(2 * u64::from(self.failures)));
        Ok(())
    }
}

impl Read for Stream {
    fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
        loop {
            if self.body.is_none()
                && let Err(e) = self.open()
            {
                self.fail(format!("{e:#}"))?;
                continue;
            }
            let body = self.body.as_mut().expect("opened above");
            match body.read(buf) {
                Ok(0) if self.total.is_some_and(|total| self.position < total) => {
                    self.fail("the connection closed early")?;
                }
                Ok(n) => {
                    self.hasher.update(&buf[..n]);
                    self.position += n as u64;
                    if self.position >= self.next_report {
                        self.next_report += REPORT_EVERY;
                        match self.total {
                            Some(total) => tracing::info!(
                                "Unity {}'s archive: {} of {} MB",
                                self.release,
                                self.position >> 20,
                                total >> 20
                            ),
                            None => tracing::info!(
                                "Unity {}'s archive: {} MB",
                                self.release,
                                self.position >> 20
                            ),
                        }
                    }
                    return Ok(n);
                }
                Err(e) if e.kind() == io::ErrorKind::Interrupted => {}
                Err(e) => self.fail(e)?,
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn unpacks_only_what_unbound_reads() {
        for rel in [
            "Managed/UnityEngine/UnityEngine.CoreModule.dll",
            "UnityReferenceAssemblies/unity-4.8-api/mscorlib.dll",
            "CGIncludes/UnityCG.cginc",
            "Resources/unity_builtin_extra",
            "Resources/PackageManager/BuiltInPackages/com.unity.ugui/package.json",
            "PlaybackEngines/LinuxStandaloneSupport/UnityEditor.LinuxStandalone.Extensions.dll",
            "PlaybackEngines/AndroidPlayer/Unity.Android.Gradle.dll",
            "PlaybackEngines/AndroidPlayer/Variations/il2cpp/Release/Data/Resources/unity default resources",
        ] {
            assert!(wanted(rel), "{rel}");
        }
        for rel in [
            "Unity",
            "MonoBleedingEdge/bin-linux64/mono",
            "Resources/PackageManager/Editor/com.unity.timeline-1.8.7.tgz",
            "Resources/Licensing/Client/Unity.Licensing.Client",
            "PlaybackEngines/LinuxStandaloneSupport/Variations/linux64_player_development_mono/LinuxPlayer",
            "PlaybackEngines/AndroidPlayer/Tools/UnityEditor.Android.Tool.dll",
            "il2cpp/build/deploy/il2cpp.dll",
        ] {
            assert!(!wanted(rel), "{rel}");
        }
    }
}
