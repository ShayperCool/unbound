//! Intel Open Image Denoise 1.4.2, the denoiser the Unity 6000.0 Editor's Progressive Lightmapper
//! denoises lightmaps with (Apache-2.0). The Linux Editor ships Intel's release libraries
//! unchanged, so Unbound uses that release: its archive, downloaded once into the global cache
//! and checked against its SHA-256, of which the two libraries (and their licences) are kept.
//! `UNBOUND_OIDN` names a directory holding `libOpenImageDenoise.so.1` (and what it needs)
//! instead; `UNBOUND_OFFLINE` forbids the download. Intel publishes no 1.4.2 release for other
//! platforms than Linux, Windows and Intel macOS: elsewhere denoising is unavailable (said).
use anyhow::{Context, Result, bail};
use sha2::{Digest, Sha256};
use std::{
    fs::{self, File},
    io::Read,
    path::{Path, PathBuf},
    sync::{
        OnceLock,
        atomic::{AtomicU64, Ordering},
    },
};

/// The release the Editor's lightmapper uses.
pub const VERSION: &str = "1.4.2";

/// Intel's release archive for Linux on x86-64 and its SHA-256.
const ARCHIVE_URL: &str = "https://github.com/OpenImageDenoise/oidn/releases/download/v1.4.2/oidn-1.4.2.x86_64.linux.tar.gz";
const ARCHIVE_SHA256: &str = "2d0eb873b79935166321df7197659e09ad5fc8a3aca3865793d12bb2b58f608f";
const ARCHIVE_LIMIT: u64 = 64 << 20;
const ARCHIVE_TOP: &str = "oidn-1.4.2.x86_64.linux";

/// The archive's files that are kept, and their names in the cache (the libraries under the
/// names they load each other by).
const KEPT: [(&str, &str); 6] = [
    ("lib/libOpenImageDenoise.so.1.4.2", RELEASE_LIBRARY),
    ("lib/libtbb.so.12.4", "libtbb.so.12"),
    ("doc/LICENSE.txt", "LICENSE.txt"),
    ("doc/third-party-programs.txt", "third-party-programs.txt"),
    (
        "doc/third-party-programs-oneDNN.txt",
        "third-party-programs-oneDNN.txt",
    ),
    (
        "doc/third-party-programs-oneTBB.txt",
        "third-party-programs-oneTBB.txt",
    ),
];

/// The library of the cached Linux release.
const RELEASE_LIBRARY: &str = "libOpenImageDenoise.so.1";

/// The library `UNBOUND_OIDN`'s directory holds, by the platform's name for it.
pub const LIBRARY: &str = if cfg!(target_os = "macos") {
    "libOpenImageDenoise.1.dylib"
} else {
    "libOpenImageDenoise.so.1"
};

/// The path of Open Image Denoise 1.4.2's library: `UNBOUND_OIDN`'s, else the cached release,
/// downloaded when missing. The first resolution is shared by the process, failures included.
pub fn library() -> Result<PathBuf> {
    static FOUND: OnceLock<std::result::Result<PathBuf, String>> = OnceLock::new();
    FOUND
        .get_or_init(|| {
            resolve().map_err(|e| format!("{e:#} (resolved once per process; restart to retry)"))
        })
        .clone()
        .map_err(anyhow::Error::msg)
}

fn resolve() -> Result<PathBuf> {
    if let Some(dir) = std::env::var_os("UNBOUND_OIDN") {
        let path = PathBuf::from(dir).join(LIBRARY);
        if !path.is_file() {
            bail!(
                "UNBOUND_OIDN names no directory holding {LIBRARY}: {}",
                path.display()
            );
        }
        return Ok(path);
    }
    if !(cfg!(target_os = "linux") && cfg!(target_arch = "x86_64")) {
        bail!(
            "Open Image Denoise {VERSION}, which the Editor's lightmapper denoises with, has no \
             release for this platform ({} {}); set UNBOUND_OIDN to a directory holding a build \
             of that release",
            std::env::consts::OS,
            std::env::consts::ARCH
        );
    }
    let root = crate::cache_root()
        .join("dependencies")
        .join(format!("oidn-{VERSION}-x86_64-linux"));
    acquire(&root, std::env::var_os("UNBOUND_OFFLINE").is_some(), || {
        tracing::info!("downloading Open Image Denoise {VERSION} from {ARCHIVE_URL}");
        crate::net::get(ARCHIVE_URL, |mut response| {
            Ok(response
                .body_mut()
                .with_config()
                .limit(ARCHIVE_LIMIT)
                .read_to_vec()?)
        })
        .with_context(|| format!("downloading {ARCHIVE_URL}"))
    })
}

/// The library under `root` once its `.complete` marker is there; else, under a lock that is
/// never removed, the archive `load` gives (when `offline` allows it) checked and unpacked
/// beside `root` and moved into place.
fn acquire(root: &Path, offline: bool, load: impl FnOnce() -> Result<Vec<u8>>) -> Result<PathBuf> {
    let library = root.join(RELEASE_LIBRARY);
    if root.join(".complete").is_file() {
        return Ok(library);
    }
    let parent = root.parent().expect("the cache entry has a parent");
    fs::create_dir_all(parent)?;
    let lock = File::options()
        .create(true)
        .truncate(false)
        .read(true)
        .write(true)
        .open(parent.join(format!("oidn-{VERSION}.lock")))?;
    lock.lock()
        .with_context(|| format!("locking {}", parent.display()))?;
    if root.join(".complete").is_file() {
        return Ok(library);
    }
    if offline {
        bail!(
            "Open Image Denoise {VERSION}, which the Editor's lightmapper denoises with, is not \
             cached ({} is missing) and UNBOUND_OFFLINE forbids downloading {ARCHIVE_URL}; unset \
             UNBOUND_OFFLINE to download it once, or set UNBOUND_OIDN to a directory holding {LIBRARY}",
            root.display()
        );
    }
    let archive = load()?;
    let digest = format!("{:x}", Sha256::digest(&archive));
    if digest != ARCHIVE_SHA256 {
        bail!("{ARCHIVE_URL}: SHA-256 {digest}, not the release's {ARCHIVE_SHA256}");
    }
    static NEXT: AtomicU64 = AtomicU64::new(0);
    let temp = parent.join(format!(
        ".oidn-unpacking-{}-{}",
        std::process::id(),
        NEXT.fetch_add(1, Ordering::Relaxed)
    ));
    fs::create_dir(&temp)?;
    let unpacked = unpack(&archive, &temp);
    if let Err(e) = unpacked {
        let _ = fs::remove_dir_all(&temp);
        return Err(e).context("unpacking Open Image Denoise");
    }
    fs::write(temp.join(".complete"), b"")?;
    if root.exists() {
        // An entry without its marker (an interrupted publication): replaced.
        fs::remove_dir_all(root)?;
    }
    fs::rename(&temp, root).with_context(|| format!("publishing {}", root.display()))?;
    Ok(library)
}

/// The kept files of the gzipped tar `archive` into `dir`: regular files only, each named once.
fn unpack(archive: &[u8], dir: &Path) -> Result<()> {
    let mut tar = tar::Archive::new(flate2::read::GzDecoder::new(archive));
    let mut found = [false; KEPT.len()];
    for entry in tar.entries()? {
        let mut entry = entry?;
        let path = entry.path()?.into_owned();
        let Ok(relative) = path.strip_prefix(ARCHIVE_TOP) else {
            continue;
        };
        let Some(index) = KEPT
            .iter()
            .position(|(from, _)| relative == Path::new(from))
        else {
            continue;
        };
        if !entry.header().entry_type().is_file() || found[index] {
            bail!("{} is not one regular file in the archive", path.display());
        }
        let mut bytes = Vec::new();
        entry.read_to_end(&mut bytes)?;
        fs::write(dir.join(KEPT[index].1), bytes)?;
        found[index] = true;
    }
    if let Some(missing) = found.iter().position(|f| !f) {
        bail!("the archive lacks {}", KEPT[missing].0);
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    fn archive(files: &[(&str, &[u8])]) -> Vec<u8> {
        let mut builder = tar::Builder::new(flate2::write::GzEncoder::new(
            Vec::new(),
            flate2::Compression::fast(),
        ));
        for (name, data) in files {
            let mut header = tar::Header::new_gnu();
            header.set_size(data.len() as u64);
            header.set_mode(0o644);
            header.set_cksum();
            builder
                .append_data(&mut header, format!("{ARCHIVE_TOP}/{name}"), *data)
                .unwrap();
        }
        builder.into_inner().unwrap().finish().unwrap()
    }

    #[test]
    fn the_kept_files_are_unpacked_under_their_load_names() {
        let dir = std::env::temp_dir().join(format!("unbound-oidn-unpack-{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        let mut files: Vec<(&str, &[u8])> =
            KEPT.iter().map(|(from, _)| (*from, &b"x"[..])).collect();
        files.push(("bin/oidnDenoise", b"not kept"));
        unpack(&archive(&files), &dir).unwrap();
        assert!(dir.join(RELEASE_LIBRARY).is_file());
        assert!(dir.join("libtbb.so.12").is_file());
        assert!(!dir.join("oidnDenoise").exists());
        // A kept file missing from the archive is an error.
        let short = archive(&files[1..]);
        let again = dir.join("again");
        fs::create_dir_all(&again).unwrap();
        assert!(unpack(&short, &again).is_err());
        fs::remove_dir_all(&dir).unwrap();
    }

    #[test]
    fn an_archive_of_other_content_or_offline_is_refused_loudly() {
        let root = std::env::temp_dir()
            .join(format!("unbound-oidn-acquire-{}", std::process::id()))
            .join("entry");
        let _ = fs::remove_dir_all(root.parent().unwrap());
        let offline = acquire(&root, true, || unreachable!()).unwrap_err();
        assert!(
            format!("{offline:#}").contains("UNBOUND_OFFLINE"),
            "{offline:#}"
        );
        let other = acquire(&root, false, || Ok(b"other".to_vec())).unwrap_err();
        assert!(format!("{other:#}").contains("SHA-256"), "{other:#}");
        assert!(!root.exists());
        fs::remove_dir_all(root.parent().unwrap()).unwrap();
    }
}
