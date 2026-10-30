//! The files Unbound reads from a Unity Editor release (reference assemblies, the class
//! libraries, `CGIncludes`, built-in resources and packages, platform modules' editor
//! assemblies), taken from the release's published downloads when no installation of it is
//! present: the Editor archive and the platform modules games' code compiles against (Android,
//! iOS by default) are streamed once each, and only those files are unpacked, into
//! `<cache>/editors/v<N>/<release>/Editor/Data`. Nothing of the Editor is run.
use crate::releases::{self, Module};
use anyhow::{Context, Result, bail};
use md5::{Digest, Md5};
use std::{
    io::{self, BufReader, Read},
    path::{Component, Path, PathBuf},
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

/// The platform modules taken with the Editor unless `UNBOUND_EDITOR_MODULES` (comma-separated
/// module ids of the release index: `android`, `ios`, `webgl`, ...) names others: game code
/// compiled for the Editor references their editor assemblies (`UnityEditor.Android`,
/// `UnityEditor.iOS.Xcode`, ...).
const DEFAULT_MODULES: &[&str] = &["android", "ios"];

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

/// `Editor/Data` of the release's unpacked Editor files, when they are complete.
pub fn cached(release: UnityVersion) -> Option<PathBuf> {
    let root = root(release);
    root.join(".complete")
        .is_file()
        .then(|| root.join("Editor/Data"))
}

fn modules_wanted() -> Vec<String> {
    match std::env::var("UNBOUND_EDITOR_MODULES") {
        Ok(list) => list
            .split(',')
            .map(|m| m.trim().to_string())
            .filter(|m| !m.is_empty())
            .collect(),
        Err(_) => DEFAULT_MODULES.iter().map(|m| m.to_string()).collect(),
    }
}

fn module_marker(root: &Path, id: &str) -> PathBuf {
    root.join(format!(".module-{id}"))
}

/// `Editor/Data` of the release's files, its Editor archive and wanted modules downloaded and
/// unpacked first when they are not cached. `UNBOUND_OFFLINE` forbids downloading; a module that
/// cannot be had is reported and left out.
pub fn fetch(release: UnityVersion) -> Result<PathBuf> {
    let root = root(release);
    let modules = modules_wanted();
    let missing: Vec<&String> = modules
        .iter()
        .filter(|id| !module_marker(&root, id).is_file())
        .collect();
    if let Some(data) = cached(release)
        && missing.is_empty()
    {
        return Ok(data);
    }
    if std::env::var_os("UNBOUND_OFFLINE").is_some() {
        if let Some(data) = cached(release) {
            tracing::warn!(
                "Unity {release}'s modules {missing:?} are not cached and UNBOUND_OFFLINE forbids \
                 downloading them"
            );
            return Ok(data);
        }
        bail!(
            "Unity {release}'s files are not cached and UNBOUND_OFFLINE forbids downloading them"
        );
    }
    let index = releases::lookup(release)?;
    let editor = index.linux_editor().with_context(|| {
        format!("Unity {release} has no Linux Editor archive to take its files from")
    })?;
    if cached(release).is_none() {
        fetch_editor(release, &root, editor)?;
    }
    for id in missing {
        let Some(module) = editor.modules.iter().find(|m| m.id == *id) else {
            tracing::warn!("Unity {release} has no module `{id}`");
            continue;
        };
        if let Err(e) = fetch_module(release, &root, module) {
            tracing::warn!(
                "Unity {release}'s {id} module is not available, so code using its editor \
                 assemblies does not compile: {e:#}"
            );
        }
    }
    Ok(root.join("Editor/Data"))
}

/// Unpacks the Editor archive into `root` (atomically: in a sibling folder, then renamed).
fn fetch_editor(release: UnityVersion, root: &Path, download: &releases::Download) -> Result<()> {
    let size = download.download_size.as_ref().map(|s| s.value);
    tracing::warn!(
        "Unity {release} is not installed: downloading its Editor archive{} once, for its reference \
         assemblies, built-in resources and packages ({})",
        size.map(|s| format!(" ({:.1} GB)", s as f64 / 1e9))
            .unwrap_or_default(),
        download.url
    );
    let parent = root.parent().context("cache path")?;
    std::fs::create_dir_all(parent)?;
    let temp = parent.join(format!(".unpacking-{release}-{}", std::process::id()));
    if temp.exists() {
        std::fs::remove_dir_all(&temp)?;
    }
    let data = temp.join("Editor/Data");
    std::fs::create_dir_all(&data)?;
    let mut stream = Stream::new(&download.url, format!("Unity {release}'s Editor"), size);
    let result = unpack_tar_xz(&mut stream, &data, |path: &str| {
        path.strip_prefix("Editor/Data/").map(str::to_string)
    })
    .and_then(|count| {
        stream.verify(&download.integrity)?;
        if !data.join("Managed/UnityEngine").is_dir() {
            bail!("the archive has no Editor/Data/Managed/UnityEngine");
        }
        tracing::debug!("unpacked {count} files of Unity {release}'s Editor archive");
        Ok(())
    });
    if let Err(e) = result {
        let _ = std::fs::remove_dir_all(&temp);
        return Err(e).with_context(|| format!("unpacking {}", download.url));
    }
    std::fs::write(temp.join(".complete"), b"")?;
    if root.exists() {
        if root.join(".complete").is_file() {
            // Another process finished first.
            let _ = std::fs::remove_dir_all(&temp);
            return Ok(());
        }
        std::fs::remove_dir_all(root)?;
    }
    std::fs::rename(&temp, root)?;
    tracing::info!("Unity {release}'s files are in {}", root.display());
    Ok(())
}

/// Unpacks a module into `root`'s `Editor/Data` (through a sibling folder whose
/// `PlaybackEngines/<platform>` folders are then moved in).
fn fetch_module(release: UnityVersion, root: &Path, module: &Module) -> Result<()> {
    let id = &module.id;
    // `{UNITY_PATH}/Editor/Data/PlaybackEngines/AndroidPlayer` → `PlaybackEngines/AndroidPlayer`.
    let destination = module
        .destination
        .as_deref()
        .and_then(|d| d.strip_prefix("{UNITY_PATH}"))
        .map(|d| d.trim_start_matches('/'))
        .and_then(|d| d.strip_prefix("Editor/Data"))
        .map(|d| d.trim_start_matches('/').to_string())
        .with_context(|| {
            format!(
                "the module installs outside Editor/Data ({:?})",
                module.destination
            )
        })?;
    let size = module.download_size.as_ref().map(|s| s.value);
    tracing::warn!(
        "downloading Unity {release}'s {id} module{} once, for its editor assemblies ({})",
        size.map(|s| format!(" ({:.0} MB)", s as f64 / 1e6))
            .unwrap_or_default(),
        module.url
    );
    let temp = root.join(format!(".unpacking-{id}-{}", std::process::id()));
    if temp.exists() {
        std::fs::remove_dir_all(&temp)?;
    }
    std::fs::create_dir_all(&temp)?;
    let mut stream = Stream::new(&module.url, format!("Unity {release}'s {id} module"), size);
    let map = |path: &str| {
        let path = path.trim_start_matches("./");
        if path.is_empty() {
            return None;
        }
        // Some modules' archives hold paths from the installation's root whatever their
        // destination says (iOS: `Editor/Data/PlaybackEngines/iOSSupport/...`).
        if let Some(rel) = path.strip_prefix("Editor/Data/") {
            return Some(rel.to_string());
        }
        Some(if destination.is_empty() {
            path.to_string()
        } else {
            format!("{destination}/{path}")
        })
    };
    let result = match module.kind.as_str() {
        "TAR_XZ" => unpack_tar_xz(&mut stream, &temp, map),
        "PKG" => unpack_pkg(&mut stream, &temp, map),
        other => Err(anyhow::anyhow!("its archive type {other} is not supported")),
    }
    .and_then(|count| {
        stream.verify(&module.integrity)?;
        Ok(count)
    });
    let count = match result {
        Ok(count) => count,
        Err(e) => {
            let _ = std::fs::remove_dir_all(&temp);
            return Err(e).with_context(|| format!("unpacking {}", module.url));
        }
    };
    let engines = temp.join("PlaybackEngines");
    for platform in std::fs::read_dir(&engines).into_iter().flatten().flatten() {
        let target = root
            .join("Editor/Data/PlaybackEngines")
            .join(platform.file_name());
        if target.exists() {
            std::fs::remove_dir_all(&target)?;
        }
        std::fs::create_dir_all(target.parent().context("module path")?)?;
        std::fs::rename(platform.path(), &target)?;
    }
    let _ = std::fs::remove_dir_all(&temp);
    std::fs::write(module_marker(root, id), b"")?;
    tracing::info!("unpacked {count} files of Unity {release}'s {id} module");
    Ok(())
}

/// Writes one archive member at `rel` below `data` when [`wanted`]; true when written.
fn write_member(data: &Path, rel: &str, contents: &mut impl Read) -> Result<bool> {
    if !wanted(rel) || rel.ends_with('/') {
        return Ok(false);
    }
    let relative = Path::new(rel);
    if !relative
        .components()
        .all(|c| matches!(c, Component::Normal(_)))
    {
        bail!("the archive's member {rel} leaves the target directory");
    }
    let target = data.join(relative);
    if let Some(parent) = target.parent() {
        std::fs::create_dir_all(parent)?;
    }
    let mut file =
        std::fs::File::create(&target).with_context(|| format!("creating {}", target.display()))?;
    io::copy(contents, &mut file)?;
    Ok(true)
}

/// The regular files of a `.tar.xz` stream whose paths `map` takes to a path below `Editor/Data`,
/// unpacked into `data` when [`wanted`]. Returns how many.
fn unpack_tar_xz(
    stream: &mut Stream,
    data: &Path,
    map: impl Fn(&str) -> Option<String>,
) -> Result<usize> {
    let xz = liblzma::read::XzDecoder::new_multi_decoder(BufReader::with_capacity(1 << 20, stream));
    let mut archive = tar::Archive::new(xz);
    let mut count = 0;
    {
        let mut entries = archive.entries()?;
        for entry in &mut entries {
            let mut entry = entry?;
            if !entry.header().entry_type().is_file() {
                continue;
            }
            let path = entry.path()?.into_owned();
            let Some(rel) = path.to_str().and_then(&map) else {
                continue;
            };
            if write_member(data, &rel, &mut entry)? {
                count += 1;
            }
        }
    }
    // The tar reader stops at the end-of-archive marker; the rest still counts for the hash.
    io::copy(&mut archive.into_inner(), &mut io::sink())?;
    Ok(count)
}

/// A macOS installer package (a XAR archive) whose `Payload` is a gzipped cpio (odc) archive:
/// its regular files unpacked like [`unpack_tar_xz`]'s.
fn unpack_pkg(
    stream: &mut Stream,
    data: &Path,
    map: impl Fn(&str) -> Option<String>,
) -> Result<usize> {
    let mut reader = BufReader::with_capacity(1 << 20, stream);
    // The XAR header: magic, header size, version, compressed and uncompressed TOC sizes.
    let mut header = [0u8; 28];
    reader.read_exact(&mut header)?;
    if &header[..4] != b"xar!" {
        bail!("not a XAR archive");
    }
    let header_size = u16::from_be_bytes([header[4], header[5]]) as usize;
    let toc_size = u64::from_be_bytes(header[8..16].try_into().unwrap());
    io::copy(
        &mut (&mut reader).take(header_size.saturating_sub(28) as u64),
        &mut io::sink(),
    )?;
    let mut compressed = vec![0u8; usize::try_from(toc_size)?];
    reader.read_exact(&mut compressed)?;
    let mut toc = String::new();
    flate2::read::ZlibDecoder::new(compressed.as_slice()).read_to_string(&mut toc)?;
    let (offset, length) = xar_file(&toc, "Payload").context("the package has no Payload")?;
    // The heap starts right after the TOC; its files are read in order of their offsets.
    io::copy(&mut (&mut reader).take(offset), &mut io::sink())?;
    let mut payload = (&mut reader).take(length);
    let mut magic = [0u8; 2];
    payload.read_exact(&mut magic)?;
    if magic != [0x1f, 0x8b] {
        bail!("the package's Payload is not gzipped (pbzx payloads are not supported)");
    }
    let gz = flate2::read::GzDecoder::new(io::Cursor::new(magic).chain(&mut payload));
    let count = unpack_cpio(BufReader::with_capacity(1 << 20, gz), data, &map)?;
    io::copy(&mut payload, &mut io::sink())?;
    io::copy(&mut reader, &mut io::sink())?;
    Ok(count)
}

/// The heap offset and length of the XAR TOC's file `name` (each file's `<data>` comes before its
/// `<name>`).
fn xar_file(toc: &str, name: &str) -> Option<(u64, u64)> {
    let mut rest = toc;
    while let Some(start) = rest.find("<data>") {
        let end = start + rest[start..].find("</data>")?;
        let data = &rest[start..end];
        let after = &rest[end..];
        let name_start = after.find("<name>")? + "<name>".len();
        let name_end = name_start + after[name_start..].find("</name>")?;
        if &after[name_start..name_end] == name {
            let tag = |t: &str| -> Option<u64> {
                let open = format!("<{t}>");
                let s = data.find(&open)? + open.len();
                let e = s + data[s..].find('<')?;
                data[s..e].trim().parse().ok()
            };
            return Some((tag("offset")?, tag("length")?));
        }
        rest = &rest[end..];
    }
    None
}

/// The regular files of a cpio archive in the portable ASCII (odc, `070707`) format.
fn unpack_cpio(
    mut reader: impl Read,
    data: &Path,
    map: &impl Fn(&str) -> Option<String>,
) -> Result<usize> {
    fn octal(field: &[u8]) -> Result<u64> {
        let text = std::str::from_utf8(field)?;
        u64::from_str_radix(text, 8).with_context(|| format!("bad cpio number `{text}`"))
    }
    let mut count = 0;
    loop {
        let mut header = [0u8; 76];
        reader.read_exact(&mut header)?;
        if &header[..6] != b"070707" {
            bail!("not a portable cpio archive");
        }
        let mode = octal(&header[18..24])?;
        let name_size = octal(&header[59..65])? as usize;
        let file_size = octal(&header[65..76])?;
        let mut name = vec![0u8; name_size];
        reader.read_exact(&mut name)?;
        let name = String::from_utf8_lossy(&name[..name_size.saturating_sub(1)]).into_owned();
        if name == "TRAILER!!!" {
            return Ok(count);
        }
        let mut contents = (&mut reader).take(file_size);
        // Regular files only (S_IFREG); directories and links are skipped with their data.
        if mode & 0o170000 == 0o100000
            && let Some(rel) = map(&name)
            && write_member(data, &rel, &mut contents)?
        {
            count += 1;
        }
        io::copy(&mut contents, &mut io::sink())?;
    }
}

const REPORT_EVERY: u64 = 512 << 20;
const MAX_FAILURES: u32 = 8;

/// A download's bytes over HTTP, resumed with a range request where a connection breaks, hashed
/// as they pass.
struct Stream {
    url: String,
    label: String,
    position: u64,
    total: Option<u64>,
    body: Option<Box<dyn Read + Send>>,
    failures: u32,
    hasher: Md5,
    next_report: u64,
}

impl Stream {
    fn new(url: &str, label: String, total: Option<u64>) -> Self {
        Self {
            url: url.to_string(),
            label,
            position: 0,
            total,
            body: None,
            failures: 0,
            hasher: Md5::new(),
            next_report: REPORT_EVERY,
        }
    }

    /// Checks the bytes read against the release index's `md5-<base64>`.
    fn verify(&mut self, integrity: &str) -> Result<()> {
        let Some(expected) = integrity.strip_prefix("md5-") else {
            return Ok(());
        };
        use base64::Engine as _;
        let expected = base64::engine::general_purpose::STANDARD.decode(expected)?;
        let actual = std::mem::take(&mut self.hasher).finalize();
        if actual.as_slice() != expected.as_slice() {
            bail!("the download's MD5 does not match the release index's");
        }
        Ok(())
    }

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
            "downloading {}: {error}; resuming at {} MB",
            self.label,
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
                                "{}: {} of {} MB",
                                self.label,
                                self.position >> 20,
                                total >> 20
                            ),
                            None => tracing::info!("{}: {} MB", self.label, self.position >> 20),
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
            "PlaybackEngines/AndroidPlayer/Data/Resources/unity default resources",
            "PlaybackEngines/iOSSupport/UnityEditor.iOS.Extensions.Xcode.dll",
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

    #[test]
    fn finds_a_xar_file_by_name() {
        let toc = "<toc><file><name>Resources</name><file><data><length>5</length>\
                   <offset>10</offset></data><name>Welcome.rtf</name></file></file>\
                   <file><name>T.pkg</name><file><data><length>700</length><offset>60813</offset>\
                   <size>700</size></data><name>Payload</name></file></file></toc>";
        assert_eq!(xar_file(toc, "Payload"), Some((60813, 700)));
        assert_eq!(xar_file(toc, "Welcome.rtf"), Some((10, 5)));
        assert_eq!(xar_file(toc, "Bom"), None);
    }

    #[test]
    fn reads_portable_cpio() {
        fn member(name: &str, mode: u32, body: &[u8]) -> Vec<u8> {
            let mut out = format!(
                "070707{:06o}{:06o}{:06o}{:06o}{:06o}{:06o}{:06o}{:011o}{:06o}{:011o}",
                0,
                0,
                mode,
                0,
                0,
                1,
                0,
                0,
                name.len() + 1,
                body.len()
            )
            .into_bytes();
            out.extend_from_slice(name.as_bytes());
            out.push(0);
            out.extend_from_slice(body);
            out
        }
        let mut archive = member(".", 0o40755, b"");
        archive.extend(member(
            "./UnityEditor.Android.Extensions.dll",
            0o100644,
            b"dll",
        ));
        archive.extend(member("./Tools/sdktools.jar", 0o100644, b"jar"));
        archive.extend(member("TRAILER!!!", 0, b""));
        let dir = std::env::temp_dir().join(format!("unbound-cpio-{}", std::process::id()));
        let map = |p: &str| {
            Some(format!(
                "PlaybackEngines/AndroidPlayer/{}",
                p.trim_start_matches("./")
            ))
        };
        let count = unpack_cpio(io::Cursor::new(archive), &dir, &map).unwrap();
        assert_eq!(count, 1);
        let dll = dir.join("PlaybackEngines/AndroidPlayer/UnityEditor.Android.Extensions.dll");
        assert_eq!(std::fs::read(&dll).unwrap(), b"dll");
        let _ = std::fs::remove_dir_all(&dir);
    }
}
