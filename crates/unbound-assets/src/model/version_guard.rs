//! Keeps `ModelImporter::VERSION` in step with the importer's output.
//!
//! The model cache serves an entry only while `ModelImporter::VERSION` is unchanged, so a commit
//! that changes import output without bumping it would let caches serve stale meshes. This test
//! imports every committed probe model with its `.meta` and compares a fingerprint of each
//! output with `import_fingerprints.txt`, recorded under the VERSION named on its first line.
//!
//! - Output of a recorded model changed: bump `ModelImporter::VERSION`, then rewrite the file
//!   with `UNBOUND_BLESS=1 cargo test -p unbound-assets version_guard`.
//! - A probe model was added or removed: bless as well; that needs no bump. Blessing refuses
//!   to record a changed output under the VERSION it was recorded with.
use super::*;
use serde::ser::{self, Serialize};
use std::collections::BTreeMap;

/// The recorded fingerprints: `version <N>`, then `<blake3> <path under tests/projects>` lines.
const MANIFEST: &str = "src/model/import_fingerprints.txt";

/// Floats are hashed on a 2^-10 grid: output changes are visible far above it, while one-ulp
/// differences between CPUs (reciprocal estimates, fused operations) cannot move a value across
/// a grid line except in the rarest cases.
const FLOAT_GRID: f64 = 1024.0;

/// Probe projects whose `Assets/` models feed the fingerprints. `lightmapuvs` covers unwrapped
/// UV2 and imported/calculated normals in every weighting and smoothing mode, with imperfect
/// content (welded, zero-area, crossing and bridged faces, unenclosed hard edges).
const PROJECTS: &[&str] = &[
    "animculling",
    "blendshapes",
    "humanoid",
    "lightmapuvs",
    "modelaxis",
    "modelclips",
    "modelimport",
    "modelroot",
];

fn models(dir: &Path, out: &mut Vec<std::path::PathBuf>) {
    let Ok(entries) = std::fs::read_dir(dir) else {
        return;
    };
    let mut entries: Vec<_> = entries.filter_map(|e| e.ok().map(|e| e.path())).collect();
    entries.sort();
    for path in entries {
        if path.is_dir() {
            models(&path, out);
        } else if path
            .extension()
            .and_then(|e| e.to_str())
            .is_some_and(|e| e.eq_ignore_ascii_case("fbx") || e.eq_ignore_ascii_case("obj"))
        {
            out.push(path);
        }
    }
}

/// A serde serializer that feeds a value's structure into a hash, with floats on the grid.
struct Fingerprint<'a>(&'a mut blake3::Hasher);

#[derive(Debug)]
struct Unhashable(String);

impl std::fmt::Display for Unhashable {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(&self.0)
    }
}

impl std::error::Error for Unhashable {}

impl ser::Error for Unhashable {
    fn custom<T: std::fmt::Display>(msg: T) -> Self {
        Unhashable(msg.to_string())
    }
}

impl Fingerprint<'_> {
    fn tag(&mut self, tag: u8, bytes: &[u8]) {
        self.0.update(&[tag]);
        self.0.update(bytes);
    }
    fn len(&mut self, tag: u8, len: usize) {
        self.tag(tag, &(len as u64).to_le_bytes());
    }
    fn float(&mut self, v: f64) {
        if v.is_nan() {
            self.tag(b'N', &[]);
        } else {
            // Saturating, so infinities and huge values stay distinct from each other.
            self.tag(b'f', &((v * FLOAT_GRID).round() as i64).to_le_bytes());
        }
    }
}

macro_rules! integers {
    ($($method:ident: $ty:ty),*) => {$(
        fn $method(self, v: $ty) -> Result<(), Unhashable> {
            self.tag(b'i', &(v as i128).to_le_bytes());
            Ok(())
        }
    )*};
}

impl<'a, 'b> ser::Serializer for &'a mut Fingerprint<'b> {
    type Ok = ();
    type Error = Unhashable;
    type SerializeSeq = Self;
    type SerializeTuple = Self;
    type SerializeTupleStruct = Self;
    type SerializeTupleVariant = Self;
    type SerializeMap = Self;
    type SerializeStruct = Self;
    type SerializeStructVariant = Self;

    integers!(serialize_i8: i8, serialize_i16: i16, serialize_i32: i32, serialize_i64: i64,
        serialize_u8: u8, serialize_u16: u16, serialize_u32: u32, serialize_u64: u64,
        serialize_i128: i128);
    fn serialize_u128(self, v: u128) -> Result<(), Unhashable> {
        self.tag(b'U', &v.to_le_bytes());
        Ok(())
    }
    fn serialize_bool(self, v: bool) -> Result<(), Unhashable> {
        self.tag(b'b', &[v as u8]);
        Ok(())
    }
    fn serialize_f32(self, v: f32) -> Result<(), Unhashable> {
        self.float(v.into());
        Ok(())
    }
    fn serialize_f64(self, v: f64) -> Result<(), Unhashable> {
        self.float(v);
        Ok(())
    }
    fn serialize_char(self, v: char) -> Result<(), Unhashable> {
        self.tag(b'c', &(v as u32).to_le_bytes());
        Ok(())
    }
    fn serialize_str(self, v: &str) -> Result<(), Unhashable> {
        self.serialize_bytes(v.as_bytes())
    }
    fn serialize_bytes(self, v: &[u8]) -> Result<(), Unhashable> {
        self.len(b's', v.len());
        self.0.update(v);
        Ok(())
    }
    fn serialize_none(self) -> Result<(), Unhashable> {
        self.tag(b'n', &[]);
        Ok(())
    }
    fn serialize_some<T: Serialize + ?Sized>(self, v: &T) -> Result<(), Unhashable> {
        self.tag(b'o', &[]);
        v.serialize(self)
    }
    fn serialize_unit(self) -> Result<(), Unhashable> {
        self.tag(b'u', &[]);
        Ok(())
    }
    fn serialize_unit_struct(self, _: &'static str) -> Result<(), Unhashable> {
        self.serialize_unit()
    }
    fn serialize_unit_variant(
        self,
        _: &'static str,
        i: u32,
        _: &'static str,
    ) -> Result<(), Unhashable> {
        self.tag(b'v', &i.to_le_bytes());
        Ok(())
    }
    fn serialize_newtype_struct<T: Serialize + ?Sized>(
        self,
        _: &'static str,
        v: &T,
    ) -> Result<(), Unhashable> {
        v.serialize(self)
    }
    fn serialize_newtype_variant<T: Serialize + ?Sized>(
        self,
        _: &'static str,
        i: u32,
        _: &'static str,
        v: &T,
    ) -> Result<(), Unhashable> {
        self.tag(b'v', &i.to_le_bytes());
        v.serialize(self)
    }
    fn serialize_seq(self, len: Option<usize>) -> Result<Self, Unhashable> {
        let len = len.ok_or_else(|| Unhashable("sequence without a length".into()))?;
        self.len(b'[', len);
        Ok(self)
    }
    fn serialize_tuple(self, len: usize) -> Result<Self, Unhashable> {
        self.len(b'(', len);
        Ok(self)
    }
    fn serialize_tuple_struct(self, _: &'static str, len: usize) -> Result<Self, Unhashable> {
        self.serialize_tuple(len)
    }
    fn serialize_tuple_variant(
        self,
        _: &'static str,
        i: u32,
        _: &'static str,
        len: usize,
    ) -> Result<Self, Unhashable> {
        self.tag(b'v', &i.to_le_bytes());
        self.serialize_tuple(len)
    }
    fn serialize_map(self, len: Option<usize>) -> Result<Self, Unhashable> {
        let len = len.ok_or_else(|| Unhashable("map without a length".into()))?;
        self.len(b'{', len);
        Ok(self)
    }
    fn serialize_struct(self, _: &'static str, len: usize) -> Result<Self, Unhashable> {
        self.serialize_tuple(len)
    }
    fn serialize_struct_variant(
        self,
        name: &'static str,
        i: u32,
        variant: &'static str,
        len: usize,
    ) -> Result<Self, Unhashable> {
        self.serialize_tuple_variant(name, i, variant, len)
    }
}

/// Compound values hash their elements in order; struct fields by position, as the cache's
/// postcard encoding stores them.
macro_rules! compound {
    ($($trait:ident :: $method:ident $(, $key:ident)?);*) => {$(
        impl ser::$trait for &mut Fingerprint<'_> {
            type Ok = ();
            type Error = Unhashable;
            fn $method<T: Serialize + ?Sized>(&mut self, $($key: &'static str,)? v: &T) -> Result<(), Unhashable> {
                $(let _ = $key;)?
                v.serialize(&mut **self)
            }
            fn end(self) -> Result<(), Unhashable> {
                Ok(())
            }
        }
    )*};
}

compound!(SerializeSeq::serialize_element; SerializeTuple::serialize_element;
    SerializeTupleStruct::serialize_field; SerializeTupleVariant::serialize_field;
    SerializeStruct::serialize_field, key; SerializeStructVariant::serialize_field, key);

impl ser::SerializeMap for &mut Fingerprint<'_> {
    type Ok = ();
    type Error = Unhashable;
    fn serialize_key<T: Serialize + ?Sized>(&mut self, k: &T) -> Result<(), Unhashable> {
        k.serialize(&mut **self)
    }
    fn serialize_value<T: Serialize + ?Sized>(&mut self, v: &T) -> Result<(), Unhashable> {
        v.serialize(&mut **self)
    }
    fn end(self) -> Result<(), Unhashable> {
        Ok(())
    }
}

/// Each probe model's path under `tests/projects` with the fingerprint of its import output.
fn fingerprints(root: &Path) -> BTreeMap<String, String> {
    let mut sources = Vec::new();
    for project in PROJECTS {
        models(&root.join(project).join("Assets"), &mut sources);
    }
    assert!(
        sources.len() > 100,
        "probe models are missing under {}",
        root.display()
    );
    sources
        .iter()
        .map(|source| {
            let name = source
                .strip_prefix(root)
                .unwrap()
                .to_string_lossy()
                .replace('\\', "/");
            let mut meta_path = source.clone().into_os_string();
            meta_path.push(".meta");
            let meta = std::fs::read_to_string(&meta_path)
                .map(|text| unbound_yaml::parse_value(&text).unwrap())
                .unwrap_or(Value::Null);
            let bytes = std::fs::read(source).unwrap();
            let mut hasher = blake3::Hasher::new();
            match ModelImporter.import(source, &bytes, &meta) {
                // A failure is output too: the cache keeps no entry for it.
                Err(_) => {
                    hasher.update(b"error");
                }
                Ok(model) => model
                    .serialize(&mut Fingerprint(&mut hasher))
                    .unwrap_or_else(|e| panic!("{name}: {e}")),
            }
            (name, hasher.finalize().to_hex().to_string())
        })
        .collect()
}

fn parse_manifest(text: &str) -> (u32, BTreeMap<String, String>) {
    let mut lines = text.lines();
    let version = lines
        .next()
        .and_then(|l| l.strip_prefix("version "))
        .and_then(|v| v.trim().parse().ok())
        .expect("the manifest starts with `version <N>`");
    let rows = lines
        .filter(|l| !l.trim().is_empty())
        .map(|l| {
            let (hash, path) = l.split_once(' ').expect("`<hash> <path>` rows");
            (path.to_owned(), hash.to_owned())
        })
        .collect();
    (version, rows)
}

#[test]
fn import_output_changes_bump_the_model_cache_version() {
    let crate_dir = Path::new(env!("CARGO_MANIFEST_DIR"));
    let manifest_path = crate_dir.join(MANIFEST);
    let (recorded_version, recorded) =
        parse_manifest(&std::fs::read_to_string(&manifest_path).unwrap_or_default());
    let version = ModelImporter::VERSION;
    assert!(
        recorded_version <= version,
        "{MANIFEST} names VERSION {recorded_version}, newer than ModelImporter::VERSION {version}"
    );
    let actual = fingerprints(&crate_dir.join("../../tests/projects"));
    let changed: Vec<&str> = if recorded_version == version {
        actual
            .iter()
            .filter(|(path, hash)| recorded.get(*path).is_some_and(|r| r != *hash))
            .map(|(path, _)| path.as_str())
            .collect()
    } else {
        Vec::new()
    };
    assert!(
        changed.is_empty(),
        "the model import output changed under VERSION {version}, so caches would serve stale \
         output: bump ModelImporter::VERSION, then run `UNBOUND_BLESS=1 cargo test -p \
         unbound-assets version_guard`. Changed: {changed:?}"
    );
    if recorded_version == version && recorded == actual {
        return;
    }
    if std::env::var_os("UNBOUND_BLESS").is_some() {
        let mut text = format!("version {version}\n");
        for (path, hash) in &actual {
            text += &format!("{hash} {path}\n");
        }
        std::fs::write(&manifest_path, text).unwrap();
        return;
    }
    let added: Vec<_> = actual
        .keys()
        .filter(|p| !recorded.contains_key(*p))
        .collect();
    let removed: Vec<_> = recorded
        .keys()
        .filter(|p| !actual.contains_key(*p))
        .collect();
    panic!(
        "{MANIFEST} is out of date (recorded under VERSION {recorded_version}, now {version}; \
         added {added:?}, removed {removed:?}): run `UNBOUND_BLESS=1 cargo test -p unbound-assets \
         version_guard`"
    );
}

#[test]
fn floats_hash_on_the_grid_and_guids_hash_whole() {
    fn hash<T: Serialize>(v: &T) -> blake3::Hash {
        let mut hasher = blake3::Hasher::new();
        v.serialize(&mut Fingerprint(&mut hasher)).unwrap();
        hasher.finalize()
    }
    assert_eq!(hash(&1.0f32), hash(&f32::from_bits(1.0f32.to_bits() + 1)));
    assert_ne!(hash(&1.0f32), hash(&1.01f32));
    assert_ne!(hash(&f32::INFINITY), hash(&f32::NEG_INFINITY));
    let guid = unbound_yaml::Guid;
    assert_ne!(hash(&guid(1 << 100)), hash(&guid(2 << 100)));
    assert_ne!(hash(&Some(0u8)), hash(&None::<u8>));
}
