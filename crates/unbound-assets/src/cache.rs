//! The import cache: importer outputs stored by a key derived from everything they depend on.
//!
//! Key = blake3 of: [`IMPORTER_FORMAT_VERSION`], the importer's [`Importer::KIND`],
//! [`Importer::VERSION`] and [`Importer::options_key`], the blake3 of the source file's bytes and
//! the blake3 of its `.meta` bytes. An entry can therefore never be stale: any change to an input
//! is a different key. Old entries are simply left behind (`unbound cache gc` territory).
//!
//! Entries live at `<dir>/<kind>/<key[..2]>/<key>.bin` (for a project, `dir` is
//! `<project>/Library/Unbound/Imports`): a small header, then the postcard encoding of the output,
//! LZ4-compressed. Files are written to a temporary name and renamed, so a reader never sees a
//! partial entry and concurrent importers of the same asset are harmless.
//!
//! Fast path: hashing every source on each start would read the whole project. An index maps each
//! file path to its `(mtime, size)` and content hash; files whose `mtime` and size match are not
//! read. The index is one file, `<dir>/index.bin`, replaced atomically by [`AssetCache::save`]
//! (also on drop).
//!
//! All methods take `&self` and are safe to call from many threads (rayon).
use crate::{IMPORTER_FORMAT_VERSION, Importer};
use anyhow::{Context, Result, bail};
use serde::{Deserialize, Serialize};
use std::{
    collections::HashMap,
    fs,
    path::{Path, PathBuf},
    sync::{
        Arc, Mutex,
        atomic::{AtomicBool, AtomicU64, Ordering},
    },
    time::UNIX_EPOCH,
};
use unbound_yaml::Value;

/// Magic and layout version of an entry file.
const ENTRY_MAGIC: &[u8; 4] = b"UAI1";
/// Layout version of the index file; a mismatch discards the index.
const INDEX_VERSION: u32 = 1;

/// Whether a request was served from the cache.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Outcome {
    Hit,
    Imported,
}

#[derive(Debug, Clone, Copy, Serialize, Deserialize)]
struct FileStamp {
    mtime_ns: u128,
    size: u64,
    hash: [u8; 32],
}

#[derive(Default, Serialize, Deserialize)]
struct Index {
    version: u32,
    files: HashMap<String, FileStamp>,
}

pub struct AssetCache {
    dir: PathBuf,
    index: Mutex<Index>,
    dirty: AtomicBool,
    temp_counter: AtomicU64,
}

impl AssetCache {
    /// A cache rooted at `dir` (created on first write). Loads the file index if there is one.
    pub fn new(dir: impl Into<PathBuf>) -> Self {
        let dir = dir.into();
        let index = fs::read(dir.join("index.bin"))
            .ok()
            .and_then(|bytes| postcard::from_bytes::<Index>(&bytes).ok())
            .filter(|index| index.version == INDEX_VERSION)
            .unwrap_or_else(|| Index {
                version: INDEX_VERSION,
                files: HashMap::new(),
            });
        Self {
            dir,
            index: Mutex::new(index),
            dirty: AtomicBool::new(false),
            temp_counter: AtomicU64::new(0),
        }
    }

    /// The cache of a project: `<project>/Library/Unbound/Imports`.
    pub fn for_project(project_root: &Path) -> Self {
        Self::new(project_root.join("Library/Unbound/Imports"))
    }

    pub fn dir(&self) -> &Path {
        &self.dir
    }

    /// The imported `source`, from the cache or by running `importer` (and storing the result).
    /// `meta` is the asset's `.meta` file; a missing `.meta` imports with default settings.
    pub fn get_or_import<I: Importer>(
        &self,
        importer: &I,
        source: &Path,
        meta: &Path,
    ) -> Result<Arc<I::Output>> {
        self.get_or_import_traced(importer, source, meta)
            .map(|(output, _)| output)
    }

    /// [`AssetCache::get_or_import`], also telling whether the cache had it.
    pub fn get_or_import_traced<I: Importer>(
        &self,
        importer: &I,
        source: &Path,
        meta: &Path,
    ) -> Result<(Arc<I::Output>, Outcome)> {
        let (source_hash, mut source_bytes) = self
            .content_hash(source)
            .with_context(|| format!("reading {}", source.display()))?;
        let (meta_hash, meta_bytes) = match self.content_hash(meta) {
            Ok(found) => found,
            Err(_) if !meta.exists() => (*blake3::hash(b"").as_bytes(), Some(Vec::new())),
            Err(e) => return Err(e.context(format!("reading {}", meta.display()))),
        };
        let key = entry_key::<I>(importer, &source_hash, &meta_hash);
        let path = self.entry_path(I::KIND, &key);

        if let Ok(bytes) = fs::read(&path) {
            match decode_entry::<I::Output>(&bytes) {
                Ok(output) => return Ok((Arc::new(output), Outcome::Hit)),
                Err(e) => tracing::warn!("discarding cache entry {}: {e:#}", path.display()),
            }
        }

        let source_bytes = match source_bytes.take() {
            Some(bytes) => bytes,
            None => fs::read(source).with_context(|| format!("reading {}", source.display()))?,
        };
        let meta_bytes = match meta_bytes {
            Some(bytes) => bytes,
            None => fs::read(meta).with_context(|| format!("reading {}", meta.display()))?,
        };
        let meta_value =
            parse_meta(&meta_bytes).with_context(|| format!("parsing {}", meta.display()))?;
        let output = importer.import(source, &source_bytes, &meta_value)?;
        if let Err(e) = self.write_entry(&path, &output) {
            tracing::warn!("cannot write cache entry {}: {e:#}", path.display());
        }
        Ok((Arc::new(output), Outcome::Imported))
    }

    /// The content hash of `path`, from the index when its mtime and size are unchanged; else the
    /// file is read (and its bytes returned, so the caller need not read it again).
    fn content_hash(&self, path: &Path) -> Result<([u8; 32], Option<Vec<u8>>)> {
        let stat = fs::metadata(path)?;
        let mtime_ns = stat
            .modified()
            .ok()
            .and_then(|t| t.duration_since(UNIX_EPOCH).ok())
            .map(|d| d.as_nanos())
            .unwrap_or(0);
        let size = stat.len();
        let name = path.to_string_lossy().into_owned();
        if let Some(stamp) = self.index.lock().unwrap().files.get(&name)
            && stamp.mtime_ns == mtime_ns
            && stamp.size == size
            && mtime_ns != 0
        {
            return Ok((stamp.hash, None));
        }
        let bytes = fs::read(path)?;
        let hash = *blake3::hash(&bytes).as_bytes();
        // A file rewritten between the stat and the read gets a fresh stat next time.
        if bytes.len() as u64 == size {
            self.index.lock().unwrap().files.insert(
                name,
                FileStamp {
                    mtime_ns,
                    size,
                    hash,
                },
            );
            self.dirty.store(true, Ordering::Relaxed);
        }
        Ok((hash, Some(bytes)))
    }

    fn entry_path(&self, kind: &str, key: &str) -> PathBuf {
        self.dir
            .join(kind)
            .join(&key[..2])
            .join(format!("{key}.bin"))
    }

    fn write_entry<T: Serialize>(&self, path: &Path, output: &T) -> Result<()> {
        let payload = postcard::to_allocvec(output)?;
        let mut bytes = Vec::with_capacity(payload.len() / 2 + 16);
        bytes.extend_from_slice(ENTRY_MAGIC);
        bytes.extend_from_slice(&lz4_flex::compress_prepend_size(&payload));
        self.write_atomic(path, &bytes)
    }

    /// Writes `bytes` to a temporary file next to `path` and renames it into place.
    fn write_atomic(&self, path: &Path, bytes: &[u8]) -> Result<()> {
        let dir = path.parent().context("cache path has no parent")?;
        fs::create_dir_all(dir)?;
        let temp = dir.join(format!(
            ".{}.{}.{}.tmp",
            path.file_name().unwrap().to_string_lossy(),
            std::process::id(),
            self.temp_counter.fetch_add(1, Ordering::Relaxed)
        ));
        fs::write(&temp, bytes)?;
        fs::rename(&temp, path).inspect_err(|_| {
            let _ = fs::remove_file(&temp);
        })?;
        Ok(())
    }

    /// Writes the file index if it changed.
    pub fn save(&self) -> Result<()> {
        if !self.dirty.swap(false, Ordering::Relaxed) {
            return Ok(());
        }
        let bytes = {
            let index = self.index.lock().unwrap();
            postcard::to_allocvec(&*index)?
        };
        self.write_atomic(&self.dir.join("index.bin"), &bytes)
            .inspect_err(|_| self.dirty.store(true, Ordering::Relaxed))
    }
}

impl Drop for AssetCache {
    fn drop(&mut self) {
        if let Err(e) = self.save() {
            tracing::warn!("cannot save the import cache index: {e:#}");
        }
    }
}

fn entry_key<I: Importer>(importer: &I, source_hash: &[u8; 32], meta_hash: &[u8; 32]) -> String {
    let mut hasher = blake3::Hasher::new();
    hasher.update(b"unbound-assets\0");
    hasher.update(&IMPORTER_FORMAT_VERSION.to_le_bytes());
    hasher.update(I::KIND.as_bytes());
    hasher.update(b"\0");
    hasher.update(&I::VERSION.to_le_bytes());
    hasher.update(importer.options_key().as_bytes());
    hasher.update(b"\0");
    hasher.update(source_hash);
    hasher.update(meta_hash);
    hasher.finalize().to_hex().to_string()
}

fn decode_entry<T: for<'de> Deserialize<'de>>(bytes: &[u8]) -> Result<T> {
    let Some(compressed) = bytes.strip_prefix(ENTRY_MAGIC.as_slice()) else {
        bail!("not a cache entry");
    };
    let payload = lz4_flex::decompress_size_prepended(compressed)?;
    Ok(postcard::from_bytes(&payload)?)
}

fn parse_meta(bytes: &[u8]) -> Result<Value> {
    if bytes.is_empty() {
        return Ok(Value::Null);
    }
    let text = std::str::from_utf8(bytes).context("not UTF-8")?;
    unbound_yaml::parse_value(text)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::AtomicUsize;

    /// Counts its imports; the output is the source length and the meta's `n`.
    struct Counting(AtomicUsize);

    impl Importer for Counting {
        type Output = (usize, i64);
        const KIND: &'static str = "test";
        const VERSION: u32 = 1;

        fn import(&self, _: &Path, bytes: &[u8], meta: &Value) -> Result<(usize, i64)> {
            self.0.fetch_add(1, Ordering::SeqCst);
            Ok((
                bytes.len(),
                meta.get("n").and_then(Value::as_i64).unwrap_or(-1),
            ))
        }
    }

    fn temp_dir(name: &str) -> PathBuf {
        let dir =
            std::env::temp_dir().join(format!("unbound-assets-cache-{name}-{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        dir
    }

    #[test]
    fn imports_once_and_tracks_inputs() {
        let dir = temp_dir("inputs");
        let (source, meta) = (dir.join("a.png"), dir.join("a.png.meta"));
        fs::write(&source, b"12345").unwrap();
        fs::write(&meta, "n: 7\n").unwrap();
        let importer = Counting(AtomicUsize::new(0));
        let cache_dir = dir.join("cache");

        let cache = AssetCache::new(&cache_dir);
        let (out, outcome) = cache
            .get_or_import_traced(&importer, &source, &meta)
            .unwrap();
        assert_eq!((*out, outcome), ((5, 7), Outcome::Imported));
        let (out, outcome) = cache
            .get_or_import_traced(&importer, &source, &meta)
            .unwrap();
        assert_eq!((*out, outcome), ((5, 7), Outcome::Hit));
        drop(cache);

        // A new process: the index and the entry are reused.
        let cache = AssetCache::new(&cache_dir);
        assert_eq!(cache.index.lock().unwrap().files.len(), 2);
        let (_, outcome) = cache
            .get_or_import_traced(&importer, &source, &meta)
            .unwrap();
        assert_eq!(outcome, Outcome::Hit);
        assert_eq!(importer.0.load(Ordering::SeqCst), 1);

        // Changing the meta (size changes too) is a new key.
        fs::write(&meta, "n: 42\n").unwrap();
        let (out, outcome) = cache
            .get_or_import_traced(&importer, &source, &meta)
            .unwrap();
        assert_eq!((*out, outcome), ((5, 42), Outcome::Imported));

        // A missing meta imports with defaults.
        fs::remove_file(&meta).unwrap();
        let out = cache.get_or_import(&importer, &source, &meta).unwrap();
        assert_eq!(*out, (5, -1));
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn corrupt_entries_are_reimported() {
        let dir = temp_dir("corrupt");
        let (source, meta) = (dir.join("a.wav"), dir.join("a.wav.meta"));
        fs::write(&source, b"abc").unwrap();
        let importer = Counting(AtomicUsize::new(0));
        let cache = AssetCache::new(dir.join("cache"));
        cache.get_or_import(&importer, &source, &meta).unwrap();
        for entry in walkdir::WalkDir::new(dir.join("cache/test")) {
            let entry = entry.unwrap();
            if entry.file_type().is_file() {
                fs::write(entry.path(), b"UAI1garbage").unwrap();
            }
        }
        let (out, outcome) = cache
            .get_or_import_traced(&importer, &source, &meta)
            .unwrap();
        assert_eq!((*out, outcome), ((3, -1), Outcome::Imported));
        let _ = fs::remove_dir_all(&dir);
    }
}
