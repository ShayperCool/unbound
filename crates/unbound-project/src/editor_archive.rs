//! The files Unbound reads from a Unity Editor release (reference assemblies, the class
//! libraries, `CGIncludes`, built-in resources and packages, platform modules' editor
//! assemblies), taken from the release's published downloads when no installation of it is
//! present: the Editor archive and the platform modules games' code compiles against (Android,
//! iOS by default) are streamed once each, and only those files are unpacked, into
//! `<cache>/editors/v<N>/<release>/Editor/Data`. Nothing of the Editor is run. When a newer
//! unpack set only adds files, an earlier complete tree is linked into the new one and the added
//! files are fetched when first needed ([`ensure`]).
use crate::releases::{self, Module};
use anyhow::{Context, Result, bail};
use md5::{Digest, Md5};
use std::{
    io::{self, BufReader, Read},
    path::{Component, Path, PathBuf},
    sync::Mutex,
    time::Duration,
};
use unbound_compat::UnityVersion;

/// Bumped when the set of unpacked files or their layout changes.
const FORMAT_VERSION: u32 = 3;

/// The files a format version added to the unpack set, for each version that changed nothing
/// else: a complete tree of the version before becomes this version's by linking its files
/// ([`upgrade`]), and the added files are fetched when first needed ([`ensure`]) instead of
/// downloading the Editor archive again at once.
const ADDED: &[(u32, &[&str])] = &[(3, &["Tools/Unity-BlenderToFBX.py"])];

/// The files (below `Editor/Data`, one per line) a tree linked from an earlier format version
/// still lacks: they are not there, so nothing stale can be read.
const PENDING: &str = ".pending";

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
    "Resources/modules.asset",
    "Tools/Unity-BlenderToFBX.py",
];

/// The platform modules taken with the Editor unless `UNBOUND_EDITOR_MODULES` (comma-separated
/// module ids of the release index: `android`, `ios`, `webgl`, ...) names others: game code
/// compiled for the Editor references their editor assemblies (`UnityEditor.Android`,
/// `UnityEditor.iOS.Xcode`, ...).
const DEFAULT_MODULES: &[&str] = &["android", "ios"];

/// Whether `rel` (a path below `Editor/Data/`) is unpacked: [`DIRS`], [`FILES`], the platform
/// modules' editor assemblies (`PlaybackEngines/<platform>/UnityEditor.*.dll`,
/// `Unity.Android.*.dll`), their module lists (`modules.asset`) and the players' built-in resources.
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
            file == "modules.asset"
                || (file.ends_with(".dll")
                    && (file.starts_with("UnityEditor.") || file.starts_with("Unity.Android.")))
        }
        _ => false,
    }
}

/// The release's unpacked files (holding `Editor/Data`), whether or not they are there yet.
pub fn root(release: UnityVersion) -> PathBuf {
    root_in(
        &crate::cache_root().join("editors"),
        FORMAT_VERSION,
        release,
    )
}

fn root_in(editors: &Path, version: u32, release: UnityVersion) -> PathBuf {
    editors
        .join(format!("v{version}"))
        .join(release.to_string())
}

/// Makes this format version's tree of the release from a complete tree of an earlier version
/// when every version since only added files ([`ADDED`]): its files are linked (copied where
/// links are not possible), the earlier tree stays for the builds that read it, and the added
/// files are listed as [`PENDING`]. True when it made the tree.
fn upgrade(editors: &Path, release: UnityVersion) -> Result<bool> {
    let root = root_in(editors, FORMAT_VERSION, release);
    if root.join(".complete").is_file() {
        return Ok(false);
    }
    let mut pending: Vec<String> = Vec::new();
    for version in (1..FORMAT_VERSION).rev() {
        let Some((_, added)) = ADDED.iter().find(|(v, _)| *v == version + 1) else {
            return Ok(false);
        };
        pending.extend(added.iter().map(|rel| rel.to_string()));
        let old = root_in(editors, version, release);
        if !old.join(".complete").is_file() {
            continue;
        }
        pending.extend(read_pending(&old)?);
        let parent = root.parent().context("cache path")?;
        std::fs::create_dir_all(parent)?;
        let temp = parent.join(format!(".upgrading-{release}-{}", std::process::id()));
        if temp.exists() {
            std::fs::remove_dir_all(&temp)?;
        }
        let result = link_tree(&old, &temp).and_then(|()| {
            let data = temp.join("Editor/Data");
            pending.retain(|rel| !data.join(rel).is_file());
            pending.sort();
            pending.dedup();
            write_pending(&temp, &pending)
        });
        if let Err(e) = result {
            let _ = std::fs::remove_dir_all(&temp);
            return Err(e)
                .with_context(|| format!("linking {} into {}", old.display(), root.display()));
        }
        if !publish_tree(&temp, &root)? {
            return Ok(false);
        }
        tracing::info!(
            "Unity {release}'s files are linked from {} into {}{}",
            old.display(),
            root.display(),
            if pending.is_empty() {
                String::new()
            } else {
                format!("; {} are fetched when first needed", pending.join(", "))
            }
        );
        return Ok(true);
    }
    Ok(false)
}

/// Moves the finished tree `temp` to `root`; false (and `temp` removed) when another process's
/// complete tree is there first, also when it arrives between the check and the move.
fn publish_tree(temp: &Path, root: &Path) -> Result<bool> {
    if root.exists() {
        if root.join(".complete").is_file() {
            let _ = std::fs::remove_dir_all(temp);
            return Ok(false);
        }
        std::fs::remove_dir_all(root)?;
    }
    if let Err(e) = std::fs::rename(temp, root) {
        let _ = std::fs::remove_dir_all(temp);
        if root.join(".complete").is_file() {
            return Ok(false);
        }
        return Err(e).with_context(|| format!("moving {} to {}", temp.display(), root.display()));
    }
    Ok(true)
}

/// The files of `src` (folders, regular files; not unpacking leftovers) at the same places below
/// `dst`, hard-linked, else copied.
fn link_tree(src: &Path, dst: &Path) -> Result<()> {
    std::fs::create_dir_all(dst)?;
    for entry in std::fs::read_dir(src).with_context(|| format!("reading {}", src.display()))? {
        let entry = entry?;
        let name = entry.file_name();
        let text = name.to_string_lossy();
        if [".unpacking-", ".fetching-", ".pending-"]
            .iter()
            .any(|leftover| text.starts_with(leftover))
        {
            continue;
        }
        let kind = entry.file_type()?;
        let (from, to) = (entry.path(), dst.join(&name));
        if kind.is_dir() {
            link_tree(&from, &to)?;
        } else if kind.is_file() && std::fs::hard_link(&from, &to).is_err() {
            std::fs::copy(&from, &to).with_context(|| format!("copying {}", from.display()))?;
        }
    }
    Ok(())
}

fn read_pending(root: &Path) -> Result<Vec<String>> {
    match std::fs::read_to_string(root.join(PENDING)) {
        Ok(text) => Ok(text
            .lines()
            .filter(|l| !l.is_empty())
            .map(str::to_string)
            .collect()),
        Err(e) if e.kind() == io::ErrorKind::NotFound => Ok(Vec::new()),
        Err(e) => Err(e).with_context(|| format!("reading {}", root.join(PENDING).display())),
    }
}

/// Replaces the tree's [`PENDING`] list (atomically; none when empty).
fn write_pending(root: &Path, pending: &[String]) -> Result<()> {
    let path = root.join(PENDING);
    if pending.is_empty() {
        return match std::fs::remove_file(&path) {
            Err(e) if e.kind() != io::ErrorKind::NotFound => Err(e.into()),
            _ => Ok(()),
        };
    }
    let temp = root.join(format!("{PENDING}-{}", std::process::id()));
    // (`link_tree` skips such a leftover.)
    std::fs::write(&temp, pending.join("\n") + "\n")?;
    std::fs::rename(&temp, &path)?;
    Ok(())
}

/// Makes sure `rel` (a path below `data`, a release's `Editor/Data`) is there when it is one of
/// the files a tree linked from an earlier format version still lacks ([`PENDING`]): those files
/// are fetched from the release's Editor archive now, after the same consent as a first download.
/// Anything else is left to the caller, who reports a missing file itself.
/// A fetch that failed or was declined is not tried again in the same process: its error is
/// returned for every later file of that tree.
pub fn ensure(data: &Path, rel: &str) -> Result<()> {
    static REFUSED: Refused = Mutex::new(None);
    ensure_once(&REFUSED, data, rel, fetch_now)
}

type Refused = Mutex<Option<std::collections::HashMap<PathBuf, String>>>;

/// [`ensure_with`], remembering in `refused` the error of a fetch for its tree.
fn ensure_once(
    refused: &Refused,
    data: &Path,
    rel: &str,
    fetch: impl FnOnce(UnityVersion, &Path, &[String]) -> Result<()>,
) -> Result<()> {
    let mut refused = refused.lock().unwrap_or_else(|e| e.into_inner());
    let refused = refused.get_or_insert_with(Default::default);
    if let Some(error) = refused.get(data) {
        if data.join(rel).is_file() {
            return Ok(());
        }
        bail!("{error}");
    }
    let result = ensure_with(data, rel, fetch);
    if let Err(e) = &result {
        refused.insert(data.to_path_buf(), format!("{e:#}"));
    }
    result
}

/// Fetches a tree's [`PENDING`] files: refused offline, or at once without a terminal to ask on
/// (before the release index is asked); else after the user agrees.
fn fetch_now(release: UnityVersion, root: &Path, pending: &[String]) -> Result<()> {
    let parts = [format!("its Editor archive (for {})", pending.join(", "))];
    if std::env::var_os("UNBOUND_OFFLINE").is_some() {
        bail!(
            "Unity {release}'s {} are not cached in {} and UNBOUND_OFFLINE forbids \
                 downloading them",
            pending.join(", "),
            root.display()
        );
    }
    // Refused at once, before the release index is asked, when there is no one to ask.
    let (accepted, interactive) = consent();
    if !accepted && !interactive {
        decide_download(release, &parts, None, root, false, false, |_| {
            unreachable!("not interactive")
        })?;
    }
    let index = releases::lookup(release)?;
    let editor = index.linux_editor().with_context(|| {
        format!("Unity {release} has no Linux Editor archive to take its files from")
    })?;
    confirm_download(
        release,
        &parts,
        editor.download_size.as_ref().map(|s| s.value),
        root,
    )?;
    fetch_pending(release, root, editor, pending)
}

fn ensure_with(
    data: &Path,
    rel: &str,
    fetch: impl FnOnce(UnityVersion, &Path, &[String]) -> Result<()>,
) -> Result<()> {
    if data.join(rel).is_file() {
        return Ok(());
    }
    let Some(root) = data.parent().and_then(Path::parent) else {
        return Ok(());
    };
    let pending = read_pending(root)?;
    if !pending.iter().any(|p| p == rel) {
        return Ok(());
    }
    let Some(release) = root
        .file_name()
        .and_then(|n| n.to_str())
        .and_then(|n| n.parse::<UnityVersion>().ok())
    else {
        bail!(
            "{} lists files to fetch but names no Unity release",
            root.display()
        );
    };
    fetch(release, root, &pending)
}

/// Unpacks the [`PENDING`] files of `root` from the release's Editor archive (verified whole) and
/// takes them off the list.
fn fetch_pending(
    release: UnityVersion,
    root: &Path,
    download: &releases::Download,
    pending: &[String],
) -> Result<()> {
    let size = download.download_size.as_ref().map(|s| s.value);
    tracing::warn!(
        "downloading Unity {release}'s Editor archive{} for {} ({})",
        size.map(|s| format!(" ({:.1} GB)", s as f64 / 1e9))
            .unwrap_or_default(),
        pending.join(", "),
        download.url
    );
    let mut stream = Stream::new(&download.url, format!("Unity {release}'s Editor"), size);
    unpack_pending(&mut stream, root, pending, |stream| {
        stream.verify(&download.integrity)
    })
    .with_context(|| format!("unpacking {}", download.url))
}

/// The [`PENDING`] files of `root` taken from an Editor archive stream (`verify` checks the stream
/// once it is read whole), moved into place and taken off the list.
fn unpack_pending<R: Read>(
    stream: &mut R,
    root: &Path,
    pending: &[String],
    verify: impl FnOnce(&mut R) -> Result<()>,
) -> Result<()> {
    let temp = root.join(format!(".fetching-{}", std::process::id()));
    if temp.exists() {
        std::fs::remove_dir_all(&temp)?;
    }
    let result = unpack_tar_xz(stream, &temp, |path: &str| {
        path.strip_prefix("Editor/Data/")
            .filter(|rel| pending.iter().any(|p| p == rel))
            .map(str::to_string)
    })
    .and_then(|_| verify(stream))
    .and_then(|()| {
        let data = root.join("Editor/Data");
        for rel in pending {
            let from = temp.join(rel);
            if !from.is_file() {
                bail!("the archive has no Editor/Data/{rel}");
            }
            let to = data.join(rel);
            std::fs::create_dir_all(to.parent().context("archive path")?)?;
            std::fs::rename(&from, &to)?;
        }
        let left: Vec<String> = read_pending(root)?
            .into_iter()
            .filter(|rel| !data.join(rel).is_file())
            .collect();
        write_pending(root, &left)
    });
    let _ = std::fs::remove_dir_all(&temp);
    result
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
    fetch_with(release, &modules_wanted(), false)
}

/// `Editor/Data` of the release's Linux Editor files with exactly the default modules
/// ([`DEFAULT_MODULES`], whatever `UNBOUND_EDITOR_MODULES` says), downloaded first when they are not
/// cached: the API reference (`UnityInstall::api_reference`). A module that cannot be had is an
/// error, as the stubs generated without it would lose its API. The modules land in the same cache
/// as [`fetch`]'s, so later runs on these cached files also see them.
pub fn fetch_api_reference(release: UnityVersion) -> Result<PathBuf> {
    let modules: Vec<String> = DEFAULT_MODULES.iter().map(|m| m.to_string()).collect();
    fetch_with(release, &modules, true)
}

fn fetch_with(release: UnityVersion, modules: &[String], required: bool) -> Result<PathBuf> {
    if cached(release).is_none()
        && let Err(e) = upgrade(&crate::cache_root().join("editors"), release)
    {
        tracing::warn!("{e:#}");
    }
    let root = root(release);
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
        if required && let Some(data) = cached(release) {
            bail!(
                "Unity {release}'s modules {missing:?} are not cached in {} and UNBOUND_OFFLINE \
                 forbids downloading them",
                data.display()
            );
        }
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
    // What would be downloaded: the Editor archive when it is not cached, the missing modules
    // the release has. The user agrees to it first.
    let mut parts = Vec::new();
    let mut bytes = Some(0u64);
    let mut add = |name: String, size: Option<&releases::Size>| {
        parts.push(name);
        bytes = bytes.zip(size).map(|(total, size)| total + size.value);
    };
    if cached(release).is_none() {
        add(
            "its Editor archive".to_string(),
            editor.download_size.as_ref(),
        );
    }
    for id in &missing {
        if let Some(module) = editor.modules.iter().find(|m| m.id == **id) {
            add(format!("its {id} module"), module.download_size.as_ref());
        }
    }
    if !parts.is_empty()
        && let Err(e) = confirm_download(release, &parts, bytes, &root)
    {
        match cached(release) {
            Some(data) if !required => {
                tracing::warn!("{e:#}");
                return Ok(data);
            }
            _ => return Err(e),
        }
    }
    if cached(release).is_none() {
        fetch_editor(release, &root, editor)?;
    }
    for id in missing {
        let Some(module) = editor.modules.iter().find(|m| m.id == *id) else {
            if required {
                bail!("Unity {release}'s Linux Editor has no module `{id}`");
            }
            tracing::warn!("Unity {release} has no module `{id}`");
            continue;
        };
        if required {
            fetch_module(release, &root, module)
                .with_context(|| format!("fetching Unity {release}'s {id} module"))?;
        } else if let Err(e) = fetch_module(release, &root, module) {
            tracing::warn!(
                "Unity {release}'s {id} module is not available, so code using its editor \
                 assemblies does not compile: {e:#}"
            );
        }
    }
    Ok(root.join("Editor/Data"))
}

/// Set (to anything but `0`) to download Unity's files without asking: scripts, CI, machines
/// without a terminal.
pub const ACCEPT_DOWNLOAD_VARIABLE: &str = "UNBOUND_ACCEPT_UNITY_DOWNLOAD";

/// Asks the user before Unity's files (`parts` of the release, `bytes` in all when known) are
/// downloaded from Unity's servers into `root`; [`ACCEPT_DOWNLOAD_VARIABLE`] answers yes.
/// Without a terminal to ask on, the download is refused with what to do instead.
fn confirm_download(
    release: UnityVersion,
    parts: &[String],
    bytes: Option<u64>,
    root: &Path,
) -> Result<()> {
    use std::io::Write;
    let (accepted, interactive) = consent();
    decide_download(
        release,
        parts,
        bytes,
        root,
        accepted,
        interactive,
        |question| {
            let mut stderr = io::stderr();
            write!(stderr, "{question} [y/N] ")?;
            stderr.flush()?;
            let mut answer = String::new();
            io::stdin().read_line(&mut answer)?;
            Ok(answer)
        },
    )
}

/// Whether downloading is accepted beforehand ([`ACCEPT_DOWNLOAD_VARIABLE`]) and whether there
/// is a terminal to ask on.
fn consent() -> (bool, bool) {
    use std::io::IsTerminal;
    let accepted =
        std::env::var_os(ACCEPT_DOWNLOAD_VARIABLE).is_some_and(|v| !v.is_empty() && v != "0");
    (
        accepted,
        io::stdin().is_terminal() && io::stderr().is_terminal(),
    )
}

fn decide_download(
    release: UnityVersion,
    parts: &[String],
    bytes: Option<u64>,
    root: &Path,
    accepted: bool,
    interactive: bool,
    ask: impl FnOnce(&str) -> io::Result<String>,
) -> Result<()> {
    if accepted {
        return Ok(());
    }
    let what = parts.join(" and ");
    let size = bytes
        .filter(|&b| b > 0)
        .map(|b| format!(", {:.1} GB", b as f64 / 1e9))
        .unwrap_or_default();
    let instead = format!(
        "install Unity {release} with the Unity Hub, point UNBOUND_UNITY (--unity) at an \
         installation of it, or set {ACCEPT_DOWNLOAD_VARIABLE}=1 to download them"
    );
    if !interactive {
        bail!(
            "Unity {release}'s files are not installed, and downloading {what}{size} from Unity's \
             servers needs your consent, which cannot be asked without a terminal: {instead}"
        );
    }
    let question = format!(
        "Unity {release} is not installed. Download {what}{size} from Unity's servers and unpack \
         the files Unbound reads into {}? (Unity's files are Unity's, under Unity's terms.)",
        root.display()
    );
    let answer = ask(&question).context("reading the answer")?;
    if matches!(answer.trim().to_ascii_lowercase().as_str(), "y" | "yes") {
        Ok(())
    } else {
        bail!("not downloading Unity {release}'s files: {instead}")
    }
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
    if publish_tree(&temp, root)? {
        tracing::info!("Unity {release}'s files are in {}", root.display());
    }
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
    stream: &mut impl Read,
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

    fn decide(accepted: bool, interactive: bool, answer: &str) -> Result<()> {
        decide_download(
            "6000.0.71f1".parse().unwrap(),
            &[
                "its Editor archive".to_string(),
                "its android module".to_string(),
            ],
            Some(2_500_000_000),
            Path::new("/cache/editors/v2/6000.0.71f1"),
            accepted,
            interactive,
            |question| {
                assert!(
                    question.contains("Download its Editor archive and its android module, 2.5 GB")
                );
                assert!(question.contains("/cache/editors/v2/6000.0.71f1"));
                Ok(answer.to_string())
            },
        )
    }

    #[test]
    fn download_needs_consent() {
        assert!(decide(true, false, "").is_ok());
        assert!(decide(false, true, "y\n").is_ok());
        assert!(decide(false, true, " Yes \n").is_ok());
        for answer in ["\n", "n\n", "no\n", ""] {
            let error = format!("{:#}", decide(false, true, answer).unwrap_err());
            assert!(
                error.contains("not downloading Unity 6000.0.71f1's files"),
                "{error}"
            );
            assert!(error.contains(ACCEPT_DOWNLOAD_VARIABLE), "{error}");
        }
        let error = format!("{:#}", decide(false, false, "y\n").unwrap_err());
        assert!(
            error.contains("cannot be asked without a terminal"),
            "{error}"
        );
        assert!(error.contains("UNBOUND_UNITY"), "{error}");
        assert!(error.contains(ACCEPT_DOWNLOAD_VARIABLE), "{error}");
    }

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
            "Resources/modules.asset",
            "PlaybackEngines/AndroidPlayer/modules.asset",
            // The Blender export script .blend files are converted with.
            "Tools/Unity-BlenderToFBX.py",
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
            "Tools/FBXMayaExport.mel",
        ] {
            assert!(!wanted(rel), "{rel}");
        }
    }

    fn temp_editors(name: &str) -> PathBuf {
        let dir =
            std::env::temp_dir().join(format!("unbound-editors-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        dir
    }

    fn complete_tree(root: &Path) -> PathBuf {
        let dll = root.join("Editor/Data/Managed/UnityEngine/UnityEngine.dll");
        std::fs::create_dir_all(dll.parent().unwrap()).unwrap();
        std::fs::write(&dll, b"dll").unwrap();
        std::fs::write(root.join(".complete"), b"").unwrap();
        std::fs::write(root.join(".module-android"), b"").unwrap();
        dll
    }

    #[test]
    fn an_earlier_complete_tree_is_linked_and_its_added_files_wait() {
        let editors = temp_editors("upgrade");
        let release: UnityVersion = "6000.0.71f1".parse().unwrap();
        let old = root_in(&editors, FORMAT_VERSION - 1, release);
        let old_dll = complete_tree(&old);
        assert!(upgrade(&editors, release).unwrap());
        let root = root_in(&editors, FORMAT_VERSION, release);
        assert!(root.join(".complete").is_file());
        assert!(module_marker(&root, "android").is_file());
        let dll = root.join("Editor/Data/Managed/UnityEngine/UnityEngine.dll");
        assert_eq!(std::fs::read(&dll).unwrap(), b"dll");
        #[cfg(unix)]
        {
            use std::os::unix::fs::MetadataExt;
            assert_eq!(
                dll.metadata().unwrap().ino(),
                old_dll.metadata().unwrap().ino()
            );
        }
        assert_eq!(
            read_pending(&root).unwrap(),
            vec!["Tools/Unity-BlenderToFBX.py".to_string()]
        );
        // The earlier tree stays for the builds that read it; a complete tree is not made again.
        assert!(old.join(".complete").is_file() && old_dll.is_file());
        assert!(!upgrade(&editors, release).unwrap());
        let _ = std::fs::remove_dir_all(&editors);
    }

    #[test]
    fn a_tree_is_not_linked_across_a_change_that_added_more_than_files() {
        // Version 2 has no ADDED entry: a version 1 tree cannot become version 2's, nor later.
        assert!(!ADDED.iter().any(|(v, _)| *v == 2));
        let editors = temp_editors("noupgrade");
        let release: UnityVersion = "6000.0.71f1".parse().unwrap();
        complete_tree(&root_in(&editors, 1, release));
        assert!(!upgrade(&editors, release).unwrap());
        assert!(!root_in(&editors, FORMAT_VERSION, release).exists());
        let _ = std::fs::remove_dir_all(&editors);
    }

    #[test]
    fn a_pending_file_is_fetched_when_it_is_needed() {
        let editors = temp_editors("pending");
        let release: UnityVersion = "6000.0.71f1".parse().unwrap();
        let root = root_in(&editors, FORMAT_VERSION, release);
        complete_tree(&root);
        let script = "Tools/Unity-BlenderToFBX.py".to_string();
        write_pending(&root, std::slice::from_ref(&script)).unwrap();
        let data = root.join("Editor/Data");
        // A missing file that is not pending is the caller's to report.
        ensure_with(&data, "Managed/Missing.dll", |_, _, _| {
            panic!("not pending")
        })
        .unwrap();
        let mut asked = None;
        ensure_with(&data, &script, |r, at, pending| {
            asked = Some((r, at.to_path_buf(), pending.to_vec()));
            Ok(())
        })
        .unwrap();
        assert_eq!(asked, Some((release, root.clone(), vec![script.clone()])));
        // A failed fetch reaches the caller.
        let error = ensure_with(&data, &script, |_, _, _| bail!("offline")).unwrap_err();
        assert!(format!("{error:#}").contains("offline"));
        // Once the file is there nothing is fetched; an empty list is no file.
        std::fs::create_dir_all(data.join("Tools")).unwrap();
        std::fs::write(data.join(&script), b"script").unwrap();
        ensure_with(&data, &script, |_, _, _| panic!("present")).unwrap();
        write_pending(&root, &[]).unwrap();
        assert!(!root.join(PENDING).exists());
        let _ = std::fs::remove_dir_all(&editors);
    }

    #[test]
    fn every_added_file_is_unpacked() {
        for (_, added) in ADDED {
            for rel in *added {
                assert!(wanted(rel), "{rel}");
            }
        }
    }

    /// A `.tar.xz` of `members` (path, contents).
    fn tar_xz(members: &[(&str, &[u8])]) -> Vec<u8> {
        let mut tar = tar::Builder::new(Vec::new());
        for (path, body) in members {
            let mut header = tar::Header::new_gnu();
            header.set_size(body.len() as u64);
            header.set_mode(0o644);
            header.set_cksum();
            tar.append_data(&mut header, path, *body).unwrap();
        }
        let tar = tar.into_inner().unwrap();
        let mut xz = liblzma::write::XzEncoder::new(Vec::new(), 1);
        io::Write::write_all(&mut xz, &tar).unwrap();
        xz.finish().unwrap()
    }

    #[test]
    fn pending_files_are_taken_from_the_archive_and_off_the_list() {
        let editors = temp_editors("unpack");
        let release: UnityVersion = "6000.0.71f1".parse().unwrap();
        let root = root_in(&editors, FORMAT_VERSION, release);
        complete_tree(&root);
        let script = "Tools/Unity-BlenderToFBX.py".to_string();
        write_pending(&root, std::slice::from_ref(&script)).unwrap();
        let archive = tar_xz(&[
            ("Editor/Data/Managed/UnityEngine/Other.dll", b"other"),
            ("Editor/Data/Tools/Unity-BlenderToFBX.py", b"script"),
        ]);
        // A failed verification moves nothing in and keeps the list.
        let error = unpack_pending(
            &mut io::Cursor::new(&archive),
            &root,
            std::slice::from_ref(&script),
            |_| bail!("the download's MD5 does not match"),
        )
        .unwrap_err();
        assert!(format!("{error:#}").contains("MD5"));
        let data = root.join("Editor/Data");
        assert!(!data.join(&script).exists());
        assert_eq!(read_pending(&root).unwrap(), vec![script.clone()]);
        unpack_pending(
            &mut io::Cursor::new(&archive),
            &root,
            std::slice::from_ref(&script),
            |_| Ok(()),
        )
        .unwrap();
        assert_eq!(std::fs::read(data.join(&script)).unwrap(), b"script");
        // Only the pending files are taken; the list is gone, and so is the temporary folder.
        assert!(!data.join("Managed/UnityEngine/Other.dll").exists());
        assert!(!root.join(PENDING).exists());
        assert!(
            std::fs::read_dir(&root)
                .unwrap()
                .flatten()
                .all(|e| !e.file_name().to_string_lossy().starts_with(".fetching-"))
        );
        // A pending file the archive lacks is an error.
        let missing = vec!["Resources/unity editor resources".to_string()];
        let error = unpack_pending(&mut io::Cursor::new(&archive), &root, &missing, |_| Ok(()))
            .unwrap_err();
        assert!(
            format!("{error:#}")
                .contains("the archive has no Editor/Data/Resources/unity editor resources")
        );
        let _ = std::fs::remove_dir_all(&editors);
    }

    #[test]
    fn a_refused_fetch_is_not_tried_again() {
        let editors = temp_editors("refused");
        let release: UnityVersion = "6000.0.71f1".parse().unwrap();
        let root = root_in(&editors, FORMAT_VERSION, release);
        complete_tree(&root);
        let script = "Tools/Unity-BlenderToFBX.py";
        write_pending(&root, &[script.to_string()]).unwrap();
        let data = root.join("Editor/Data");
        let refused: Refused = Mutex::new(None);
        let error =
            ensure_once(&refused, &data, script, |_, _, _| bail!("not downloading")).unwrap_err();
        assert!(format!("{error:#}").contains("not downloading"));
        let again =
            ensure_once(&refused, &data, script, |_, _, _| panic!("asked again")).unwrap_err();
        assert!(format!("{again:#}").contains("not downloading"));
        // Present files are fine whatever was refused.
        ensure_once(
            &refused,
            &data,
            "Managed/UnityEngine/UnityEngine.dll",
            |_, _, _| panic!("present"),
        )
        .unwrap();
        let _ = std::fs::remove_dir_all(&editors);
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
