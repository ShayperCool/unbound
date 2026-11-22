//! Reads established plugin GUID assignments for the compilation graph.
use crate::plugin_import::Entry;
use anyhow::Result;
use std::{collections::HashSet, path::Path};
use unbound_yaml::Guid;

#[cfg(target_os = "linux")]
pub(super) use linux::EditorAssetGuids;

#[cfg(not(target_os = "linux"))]
pub(super) struct EditorAssetGuids;

#[cfg(not(target_os = "linux"))]
impl EditorAssetGuids {
    pub(super) fn open(_: &Path, _: &Path) -> Result<Self> {
        anyhow::bail!("the established Unity asset database is unmeasured on this platform")
    }

    pub(super) fn resolve(
        &self,
        _: Guid,
        _: &[Entry<'_>],
        _: &mut HashSet<Guid>,
    ) -> Result<Vec<(usize, Guid)>> {
        anyhow::bail!("the established Unity asset database is unmeasured on this platform")
    }
}

#[cfg(target_os = "linux")]
mod linux {
    use super::*;
    use crate::plugin_import::{Source, StagedFile};
    use anyhow::Context as _;
    use std::{
        ffi::{CStr, CString, c_char, c_int, c_uint, c_void},
        fs::{self, File},
        io::{self, Seek as _, SeekFrom},
        mem::MaybeUninit,
        ptr::{self, NonNull},
        time::SystemTime,
    };
    use unbound_project::{PackageSource, assetdb::read_meta_guid, plugins::guid_order_bytes};

    #[repr(C)]
    struct Value {
        len: usize,
        data: *const u8,
    }

    #[repr(C)]
    struct EnvInfo {
        map_base: *mut c_void,
        map_size: usize,
        last_page: usize,
        last_transaction: usize,
        max_readers: c_uint,
        readers: c_uint,
    }

    #[repr(C)]
    struct EnvStat {
        page_size: c_uint,
        depth: c_uint,
        branch_pages: usize,
        leaf_pages: usize,
        overflow_pages: usize,
        entries: usize,
    }

    type EnvClose = unsafe extern "C" fn(*mut c_void);
    type TxnAbort = unsafe extern "C" fn(*mut c_void);
    type Get = unsafe extern "C" fn(*mut c_void, c_uint, *mut Value, *mut Value) -> c_int;

    pub(crate) struct EditorAssetGuids {
        env: NonNull<c_void>,
        txn: Option<NonNull<c_void>>,
        env_close: EnvClose,
        txn_abort: TxnAbort,
        get: Get,
        path_to_guid: c_uint,
        guid_to_path: c_uint,
        stamp: SystemTime,
        _library: libloading::Library,
    }

    const READ_ONLY: c_uint = 0x20000;
    const SINGLE_FILE: c_uint = 0x4000;
    const NO_LOCK: c_uint = 0x400000;

    impl EditorAssetGuids {
        pub(crate) fn open(database: &Path, library: &Path) -> Result<Self> {
            let stamp = fs::metadata(database)?.modified()?;
            let source = snapshot(database, library)
                .context("reading a private copy of the Unity asset database")?;
            let snapshot_len = fs::metadata(&source.0)?.len();
            let path = CString::new(source.0.as_os_str().as_encoded_bytes())?;
            // LMDB maps only the private copy. The source database and its lock stay untouched.
            unsafe {
                let lib = libloading::Library::new("liblmdb.so.0")
                    .context("opening liblmdb.so.0 for established plugin GUIDs")?;
                let env_create =
                    *lib.get::<unsafe extern "C" fn(*mut *mut c_void) -> c_int>(b"mdb_env_create")?;
                let set_maxdbs = *lib.get::<unsafe extern "C" fn(*mut c_void, c_uint) -> c_int>(
                    b"mdb_env_set_maxdbs",
                )?;
                let env_open = *lib.get::<unsafe extern "C" fn(
                    *mut c_void,
                    *const c_char,
                    c_uint,
                    c_uint,
                ) -> c_int>(b"mdb_env_open")?;
                let env_info = *lib
                    .get::<unsafe extern "C" fn(*mut c_void, *mut EnvInfo) -> c_int>(
                        b"mdb_env_info",
                    )?;
                let env_stat = *lib
                    .get::<unsafe extern "C" fn(*mut c_void, *mut EnvStat) -> c_int>(
                        b"mdb_env_stat",
                    )?;
                let txn_begin = *lib.get::<unsafe extern "C" fn(
                    *mut c_void,
                    *mut c_void,
                    c_uint,
                    *mut *mut c_void,
                ) -> c_int>(b"mdb_txn_begin")?;
                let dbi_open = *lib.get::<unsafe extern "C" fn(
                    *mut c_void,
                    *const c_char,
                    c_uint,
                    *mut c_uint,
                ) -> c_int>(b"mdb_dbi_open")?;
                let env_close = *lib.get::<EnvClose>(b"mdb_env_close")?;
                let txn_abort = *lib.get::<TxnAbort>(b"mdb_txn_abort")?;
                let get = *lib.get::<Get>(b"mdb_get")?;

                let mut env = ptr::null_mut();
                check(env_create(&mut env), "creating the database handle")?;
                let env = NonNull::new(env).context("missing database handle")?;
                let mut reader = Self {
                    env,
                    txn: None,
                    env_close,
                    txn_abort,
                    get,
                    path_to_guid: 0,
                    guid_to_path: 0,
                    stamp,
                    _library: lib,
                };
                check(
                    set_maxdbs(env.as_ptr(), 64),
                    "configuring the database handle",
                )?;
                check(
                    env_open(
                        env.as_ptr(),
                        path.as_ptr(),
                        READ_ONLY | SINGLE_FILE | NO_LOCK,
                        0,
                    ),
                    "opening the database",
                )?;
                // The private mapping stays live without a directory entry.
                fs::remove_file(&source.0).context("unlinking the private database copy")?;

                // The environment reports its extent from its metadata pages without reading
                // any B-tree pages. Check the file before opening a transaction.
                let mut info = MaybeUninit::<EnvInfo>::uninit();
                let mut stat = MaybeUninit::<EnvStat>::uninit();
                check(
                    env_info(env.as_ptr(), info.as_mut_ptr()),
                    "reading the database extent",
                )?;
                check(
                    env_stat(env.as_ptr(), stat.as_mut_ptr()),
                    "reading the database page size",
                )?;
                let info = info.assume_init();
                let stat = stat.assume_init();
                let required = info
                    .last_page
                    .checked_add(1)
                    .and_then(|pages| pages.checked_mul(stat.page_size as usize))
                    .filter(|&size| size > 0)
                    .context("invalid Unity asset database size")?;
                anyhow::ensure!(
                    snapshot_len >= required as u64,
                    "truncated Unity asset database ({snapshot_len} bytes, expected at least {required})",
                );

                let mut txn = ptr::null_mut();
                check(
                    txn_begin(env.as_ptr(), ptr::null_mut(), READ_ONLY, &mut txn),
                    "starting a read transaction",
                )?;
                reader.txn = Some(NonNull::new(txn).context("missing database transaction")?);
                check(
                    dbi_open(txn, c"PathToGuid".as_ptr(), 0, &mut reader.path_to_guid),
                    "opening the path table",
                )?;
                check(
                    dbi_open(txn, c"GuidToPath".as_ptr(), 0, &mut reader.guid_to_path),
                    "opening the GUID table",
                )?;
                Ok(reader)
            }
        }

        pub(crate) fn resolve(
            &self,
            guid: Guid,
            entries: &[Entry<'_>],
            reserved: &mut HashSet<Guid>,
        ) -> Result<Vec<(usize, Guid)>> {
            let detail = "this established-database source combination is unmeasured";
            let asset = entries
                .iter()
                .find(|entry| entry.source == Source::Assets)
                .context(detail)?;
            let package = entries
                .iter()
                .find(|entry| entry.source == Source::Package(PackageSource::Registry))
                .context(detail)?;
            anyhow::ensure!(entries.len() == 2, "{detail}");
            for entry in entries {
                let meta = entry.metadata()?;
                let file = meta.with_extension("");
                anyhow::ensure!(
                    fs::metadata(meta.as_path())?.modified()? <= self.stamp
                        && fs::metadata(file)?.modified()? <= self.stamp,
                    "{} changed after registration",
                    entry.path
                );
                anyhow::ensure!(
                    read_meta_guid(&meta) == Some(guid),
                    "{} needs an unmeasured metadata rewrite",
                    entry.path
                );
            }
            let registry_guid = self.guid_at_path(package.path)?;
            anyhow::ensure!(
                registry_guid == guid_order_bytes(guid),
                "the shared GUID does not belong to the registry copy in the Unity database"
            );
            let registered = self.guid_at_path(asset.path)?;
            let replacement = Guid(u128::from_be_bytes(
                registered.map(|byte| byte.rotate_left(4)),
            ));
            anyhow::ensure!(
                !replacement.is_zero() && reserved.insert(replacement),
                "the registered GUID of {} is in use elsewhere",
                asset.path
            );
            Ok(vec![(asset.index, replacement)])
        }

        fn guid_at_path(&self, path: &str) -> Result<[u8; 16]> {
            let key = CString::new(path.to_ascii_lowercase())?;
            let record = self
                .find(self.path_to_guid, key.as_bytes_with_nul())
                .with_context(|| format!("looking up {path} in the Unity asset database"))?;
            // The path record starts with the GUID, followed by the source record's fields.
            anyhow::ensure!(record.len() == 48, "unrecognized path record");
            let guid: [u8; 16] = record[..16].try_into()?;
            let reverse = self.find(self.guid_to_path, &guid)?;
            let reverse = CStr::from_bytes_with_nul(reverse)?.to_str()?;
            anyhow::ensure!(
                reverse.eq_ignore_ascii_case(path),
                "inconsistent GUID record for {path}"
            );
            Ok(guid)
        }

        fn find(&self, db: c_uint, key: &[u8]) -> Result<&[u8]> {
            let txn = self.txn.context("missing database transaction")?;
            let mut key = Value {
                len: key.len(),
                data: key.as_ptr(),
            };
            let mut value = Value {
                len: 0,
                data: ptr::null(),
            };
            // The read transaction owns the returned mapped bytes and lasts for this borrow.
            unsafe {
                check(
                    (self.get)(txn.as_ptr(), db, &mut key, &mut value),
                    "looking up a registered GUID",
                )?;
                anyhow::ensure!(
                    !value.data.is_null() && value.len <= isize::MAX as usize,
                    "invalid database record"
                );
                Ok(std::slice::from_raw_parts(value.data, value.len))
            }
        }
    }

    impl Drop for EditorAssetGuids {
        fn drop(&mut self) {
            unsafe {
                if let Some(txn) = self.txn {
                    (self.txn_abort)(txn.as_ptr());
                }
                (self.env_close)(self.env.as_ptr());
            }
        }
    }

    fn check(code: c_int, operation: &str) -> Result<()> {
        anyhow::ensure!(code == 0, "{operation}: LMDB error {code}");
        Ok(())
    }

    fn snapshot(database: &Path, library: &Path) -> Result<StagedFile> {
        anyhow::ensure!(
            fs::metadata(database)?.is_file(),
            "invalid asset database file"
        );
        let mut original = File::open(database)?;
        let metadata = original.metadata()?;
        anyhow::ensure!(
            metadata.is_file() && metadata.len() >= 8192,
            "invalid asset database file"
        );
        fs::create_dir_all(library)?;
        let (copy, mut output) = StagedFile::create(library)?;
        let copied = io::copy(&mut original, &mut output)?;
        drop(output);
        original.seek(SeekFrom::Start(0))?;
        let mut expected = blake3::Hasher::new();
        let mut actual = blake3::Hasher::new();
        let checked = expected.update_reader(&mut original)?.count();
        actual.update_reader(File::open(&copy.0)?)?;
        anyhow::ensure!(
            copied == metadata.len()
                && checked == copied
                && expected.finalize() == actual.finalize(),
            "the Unity asset database changed while being read"
        );
        Ok(copy)
    }
}
