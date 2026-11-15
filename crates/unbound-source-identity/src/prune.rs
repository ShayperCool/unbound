//! Keeping the keyed cache directories ([`crate::keyed_dir`]) from piling up: a build that
//! keeps no use for a directory leaves it behind, and no build may read another's.
//!
//! A process that uses a keyed directory claims it ([`claim`]): it marks the directory used
//! (`.used`, touched) and holds a shared lock on it (`.lock`) until it exits. Its siblings (the
//! directories of other formats and identities under the same parent) are pruned once per
//! parent and process, off the caller's thread: a sibling unused for [`RETENTION`] whose lock
//! can be taken exclusively (no running process holds it) is moved aside and removed. A build
//! that ran within the retention, or one running now however long ago it started, keeps its
//! entries. Entries of the layout before keyed directories (files directly in the parent) are
//! removed once they are as old: a build still reading them compiles what it misses. A prune
//! interrupted while removing a directory (the process exited) leaves it set aside, and a later
//! prune finishes it. A build of before claims (holding no lock) is judged by its directory's
//! time alone; the built-in layouts of before (`builtin-shaders/generated-v*`,
//! `declarations-v*`, a few kilobytes) are not pruned.
use std::collections::HashSet;
use std::fs::{File, OpenOptions};
use std::io;
use std::path::{Path, PathBuf};
use std::sync::{Mutex, OnceLock};
use std::time::{Duration, SystemTime};

/// How long a keyed directory nothing used is kept: a slot's build of a week ago may be run
/// again without recompiling everything.
pub const RETENTION: Duration = Duration::from_secs(7 * 24 * 60 * 60);

/// The marker a use touches.
const USED: &str = ".used";
/// The file whose lock a using process holds.
const LOCK: &str = ".lock";

/// Claims a keyed directory for this process (see the module docs): creates it, marks it used,
/// holds its shared lock until the process exits, and prunes its siblings in the background
/// (once per parent and process).
pub fn claim(dir: &Path) -> io::Result<()> {
    static CLAIMED: OnceLock<Mutex<HashSet<PathBuf>>> = OnceLock::new();
    let claimed = CLAIMED.get_or_init(Default::default);
    if !claimed.lock().unwrap().insert(dir.to_path_buf()) {
        return Ok(());
    }
    std::fs::create_dir_all(dir)?;
    mark_used(dir)?;
    let lock = OpenOptions::new()
        .create(true)
        .truncate(false)
        .write(true)
        .open(dir.join(LOCK))?;
    lock.lock_shared()?;
    // Held until the process exits.
    std::mem::forget(lock);
    if let Some(parent) = dir.parent().map(Path::to_path_buf) {
        let keep = dir.to_path_buf();
        std::thread::spawn(move || {
            if let Err(e) = prune(&parent, &keep, RETENTION, SystemTime::now()) {
                tracing::debug!("pruning {}: {e}", parent.display());
            }
        });
    }
    Ok(())
}

/// Touches a directory's use marker.
fn mark_used(dir: &Path) -> io::Result<()> {
    let marker = OpenOptions::new()
        .create(true)
        .truncate(false)
        .write(true)
        .open(dir.join(USED))?;
    marker.set_modified(SystemTime::now())
}

/// When a directory was last used: its marker's time, else (a directory of a build before
/// markers) its own.
fn last_used(dir: &Path) -> io::Result<SystemTime> {
    match std::fs::metadata(dir.join(USED)) {
        Ok(meta) => meta.modified(),
        Err(_) => std::fs::metadata(dir)?.modified(),
    }
}

/// Removes the siblings of `keep` under `parent` unused since `now - retention` and not held by
/// a running process, the files directly in `parent` (entries of the layout before keyed
/// directories) modified before then, and what an interrupted prune left set aside. Returns what
/// it removed. An entry that cannot be read or removed (another pruner took it first) is passed
/// over.
pub fn prune(
    parent: &Path,
    keep: &Path,
    retention: Duration,
    now: SystemTime,
) -> io::Result<Vec<PathBuf>> {
    let before = now.checked_sub(retention).unwrap_or(SystemTime::UNIX_EPOCH);
    let mut removed = Vec::new();
    for entry in std::fs::read_dir(parent)?.flatten() {
        let path = entry.path();
        match prune_entry(parent, &entry, keep, before) {
            Ok(true) => removed.push(path),
            Ok(false) => {}
            Err(e) => tracing::debug!("not pruning {}: {e}", path.display()),
        }
    }
    Ok(removed)
}

/// Prunes one entry of a cache directory ([`prune`]): whether it removed it.
fn prune_entry(
    parent: &Path,
    entry: &std::fs::DirEntry,
    keep: &Path,
    before: SystemTime,
) -> io::Result<bool> {
    let path = entry.path();
    let name = entry.file_name().to_string_lossy().into_owned();
    let kind = entry.file_type()?;
    if name.starts_with(ASIDE) && kind.is_dir() {
        // Set aside by a prune: finished here unless that prune is still removing it (it holds
        // the directory's lock until its last file).
        return remove_aside(&path);
    }
    // A temporary file being written, a lock or marker, the directory in use.
    if name.starts_with('.') || path == keep {
        return Ok(false);
    }
    if kind.is_file() {
        if entry.metadata()?.modified()? >= before {
            return Ok(false);
        }
        std::fs::remove_file(&path)?;
        return Ok(true);
    }
    if !kind.is_dir() || last_used(&path)? >= before {
        return Ok(false);
    }
    // A running process holding it keeps it. A process claiming it meanwhile (its claim waits
    // for the lock) holds the lock of a directory set aside: its entries are written to a new
    // directory, made again, and compile what they miss.
    let lock = lock_file(&path)?;
    if lock.try_lock().is_err() {
        return Ok(false);
    }
    // Checked again under the lock: a claim may have marked it used since.
    if last_used(&path)? >= before {
        return Ok(false);
    }
    let unique = SystemTime::now()
        .duration_since(SystemTime::UNIX_EPOCH)
        .map_or(0, |d| d.as_nanos());
    let aside = parent.join(format!("{ASIDE}{}-{unique}-{name}", std::process::id()));
    std::fs::rename(&path, &aside)?;
    // The lock moved with the directory: it is held while the files go.
    finish(&aside, lock)?;
    Ok(true)
}

/// The name a directory being removed is set aside under.
const ASIDE: &str = ".pruned-";

/// A directory's lock file, opened.
fn lock_file(dir: &Path) -> io::Result<File> {
    OpenOptions::new()
        .create(true)
        .truncate(false)
        .write(true)
        .open(dir.join(LOCK))
}

/// Removes a directory set aside by a prune, unless a process (that prune) holds its lock.
fn remove_aside(dir: &Path) -> io::Result<bool> {
    let lock = lock_file(dir)?;
    if lock.try_lock().is_err() {
        return Ok(false);
    }
    finish(dir, lock)?;
    Ok(true)
}

/// Removes a directory whose lock `lock` holds: its files, then the lock file, then itself.
fn finish(dir: &Path, lock: File) -> io::Result<()> {
    remove_contents(dir)?;
    drop(lock);
    std::fs::remove_file(dir.join(LOCK))?;
    std::fs::remove_dir(dir)
}

/// Removes everything in a directory but its lock file.
fn remove_contents(dir: &Path) -> io::Result<()> {
    for entry in std::fs::read_dir(dir)? {
        let entry = entry?;
        if entry.file_name() == LOCK {
            continue;
        }
        match entry.file_type()?.is_dir() {
            true => std::fs::remove_dir_all(entry.path())?,
            false => std::fs::remove_file(entry.path())?,
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    struct Scratch(PathBuf);

    impl Scratch {
        fn new(name: &str) -> Scratch {
            let dir =
                std::env::temp_dir().join(format!("unbound-prune-{name}-{}", std::process::id()));
            let _ = std::fs::remove_dir_all(&dir);
            std::fs::create_dir_all(&dir).unwrap();
            Scratch(dir)
        }
    }

    impl Drop for Scratch {
        fn drop(&mut self) {
            let _ = std::fs::remove_dir_all(&self.0);
        }
    }

    fn keyed(parent: &Path, name: &str, used_days_ago: u64) -> PathBuf {
        let dir = parent.join(name);
        std::fs::create_dir_all(&dir).unwrap();
        std::fs::write(dir.join("entry.json"), "{}").unwrap();
        let marker = File::create(dir.join(USED)).unwrap();
        marker
            .set_modified(SystemTime::now() - Duration::from_secs(used_days_ago * 24 * 3600))
            .unwrap();
        dir
    }

    /// Stale siblings go; recent ones, the kept one, ones a running process holds and one
    /// another prune is removing stay; old files of the flat layout go, recent ones stay.
    #[test]
    fn stale_unheld_siblings_are_removed() {
        let scratch = Scratch::new("siblings");
        let parent = &scratch.0;
        let mine = keyed(parent, "mine", 30);
        let stale = keyed(parent, "stale", 8);
        let recent = keyed(parent, "recent", 2);
        let held = keyed(parent, "held", 30);
        // A directory another prune is still removing.
        let pruning = keyed(parent, ".pruned-1-2-other", 30);
        let removing = File::create(pruning.join(LOCK)).unwrap();
        removing.lock().unwrap();
        let lock = File::create(held.join(LOCK)).unwrap();
        lock.lock_shared().unwrap();
        let old_file = parent.join("0abc.json");
        std::fs::write(&old_file, "{}").unwrap();
        File::options()
            .write(true)
            .open(&old_file)
            .unwrap()
            .set_modified(SystemTime::now() - Duration::from_secs(9 * 24 * 3600))
            .unwrap();
        let new_file = parent.join("1abc.json");
        std::fs::write(&new_file, "{}").unwrap();
        let removed = prune(parent, &mine, RETENTION, SystemTime::now()).unwrap();
        assert_eq!(removed.len(), 2, "{removed:?}");
        assert!(!stale.exists() && !old_file.exists());
        for kept in [&mine, &recent, &held, &pruning, &new_file] {
            assert!(kept.exists(), "{}", kept.display());
        }
        // Released, the held one goes too.
        drop(lock);
        prune(parent, &mine, RETENTION, SystemTime::now()).unwrap();
        assert!(!held.exists());
    }

    /// A directory a prune set aside and did not finish is removed by a later prune, unless the
    /// pruning process still holds its lock.
    #[test]
    fn an_interrupted_removal_is_finished_later() {
        let scratch = Scratch::new("aside");
        let parent = &scratch.0;
        let aside = keyed(parent, ".pruned-1-2-old", 30);
        std::fs::create_dir_all(aside.join("nested")).unwrap();
        std::fs::write(aside.join("nested/entry.json"), "{}").unwrap();
        let lock = File::create(aside.join(LOCK)).unwrap();
        lock.lock().unwrap();
        let mine = parent.join("mine");
        assert!(
            prune(parent, &mine, RETENTION, SystemTime::now())
                .unwrap()
                .is_empty()
        );
        assert!(aside.exists());
        drop(lock);
        assert_eq!(
            prune(parent, &mine, RETENTION, SystemTime::now()).unwrap(),
            [aside.clone()]
        );
        assert!(!aside.exists());
    }

    /// A claim makes the directory, marks it used and holds it: a pruner (another process, or a
    /// later time) does not remove it while this process runs.
    #[test]
    fn a_claimed_directory_is_kept_while_the_process_runs() {
        let scratch = Scratch::new("claim");
        let mine = scratch.0.join("mine");
        claim(&mine).unwrap();
        assert!(mine.join(USED).is_file());
        let other = scratch.0.join("other");
        let later = SystemTime::now() + RETENTION * 2;
        assert!(
            prune(&scratch.0, &other, RETENTION, later)
                .unwrap()
                .is_empty()
        );
        assert!(mine.is_dir());
    }
}
