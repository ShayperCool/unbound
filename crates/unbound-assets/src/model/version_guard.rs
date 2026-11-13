//! Keeps `ModelImporter::VERSION` in step with the importer's output.
//!
//! The model cache serves an entry only while `ModelImporter::VERSION` is unchanged, so a commit
//! that changes import output without bumping it would let caches serve stale meshes. This test
//! imports every committed probe model with its `.meta` and compares a fingerprint of each
//! output with `import_fingerprints.txt`, keyed by the source, `.meta` and dependency content
//! under the VERSION named on its first line.
//!
//! - Output changed for the same recorded inputs: bump `ModelImporter::VERSION`, then rewrite the file
//!   with `UNBOUND_BLESS=1 cargo test -p unbound-assets version_guard`.
//! - Probe inputs changed, or a model was added or removed: bless as well; that needs no bump.
//!   Blessing refuses to record changed output for unchanged inputs under the same VERSION.
use super::*;
use serde::ser::{self, Serialize};
use std::collections::BTreeMap;

/// Each line after `version <N>` is JSON: `[probe path, input content, output hash]`.
/// Input content retains dependency provenance as well as its hashes: changing discovery
/// cannot disguise an output change as an edit of an authored file.
#[derive(Debug, Clone, PartialEq, Eq, PartialOrd, Ord, serde::Serialize, serde::Deserialize)]
struct InputContent {
    source: String,
    meta: Option<String>,
    dependencies: Vec<(String, Option<String>)>,
}

type Fingerprints = BTreeMap<(String, InputContent), String>;
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
    "modelfileids",
    "modelimport",
    "modelroot",
    "modeltoproot",
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

/// A serde serializer that feeds a value's structure into a hash, with floats on the grid. A
/// string under the second field's directory (`tests/projects`: an imported texture's resolved
/// file name) is hashed relative to it, so the fingerprint is the same in every checkout.
struct Fingerprint<'a>(&'a mut blake3::Hasher, &'a str);

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
        match v.strip_prefix(self.1).filter(|_| !self.1.is_empty()) {
            Some(relative) => {
                self.tag(b'r', &[]);
                self.serialize_bytes(relative.as_bytes())
            }
            None => self.serialize_bytes(v.as_bytes()),
        }
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

/// Content read by the importer, excluding its implementation and cache version. Paths are
/// relative to the probes' root so a checkout move does not look like an input edit.
fn file_fingerprint(path: &Path) -> Option<String> {
    match std::fs::read(path) {
        Ok(bytes) => Some(blake3::hash(&bytes).to_hex().to_string()),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => None,
        Err(error) => panic!("{}: {error}", path.display()),
    }
}

fn input_fingerprint(
    root: &Path,
    source: &Path,
    bytes: &[u8],
    meta: Option<&[u8]>,
) -> InputContent {
    let dependencies = ModelImporter::default()
        .dependencies(source)
        .unwrap()
        .into_iter()
        .map(|path| {
            let name = path
                .strip_prefix(root)
                .unwrap_or(&path)
                .to_string_lossy()
                .replace('\\', "/");
            (name, file_fingerprint(&path))
        })
        .collect();
    InputContent {
        source: blake3::hash(bytes).to_hex().to_string(),
        meta: meta.map(|bytes| blake3::hash(bytes).to_hex().to_string()),
        dependencies,
    }
}

impl InputContent {
    fn authored_files_unchanged(&self, root: &Path, actual: &Self) -> bool {
        self.source == actual.source
            && self.meta == actual.meta
            && self
                .dependencies
                .iter()
                .all(|(path, hash)| file_fingerprint(&root.join(path)) == *hash)
    }
}

/// Each probe model's (path, input content) with the fingerprint of its import output.
fn fingerprints(root: &Path) -> Fingerprints {
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
            let meta_bytes = match std::fs::read(&meta_path) {
                Ok(bytes) => Some(bytes),
                Err(error) if error.kind() == std::io::ErrorKind::NotFound => None,
                Err(error) => panic!("{name}.meta: {error}"),
            };
            let meta = meta_bytes
                .as_ref()
                .map(|bytes| {
                    unbound_yaml::parse_value(std::str::from_utf8(bytes).unwrap()).unwrap()
                })
                .unwrap_or(Value::Null);
            let bytes = std::fs::read(source).unwrap();
            let input = input_fingerprint(root, source, &bytes, meta_bytes.as_deref());
            let mut hasher = blake3::Hasher::new();
            let root = root.to_string_lossy();
            // One OS's sorts on every host, so the fingerprints do not depend on the OS that
            // records them (the sorts differ only in where equal elements end).
            let importer = ModelImporter {
                editor_os: unbound_compat::HostOs::Linux,
                ..ModelImporter::default()
            };
            match importer.import(source, &bytes, &meta) {
                // A failure is output too: the cache keeps no entry for it.
                Err(_) => {
                    hasher.update(b"error");
                }
                Ok(model) => model
                    .serialize(&mut Fingerprint(&mut hasher, &root))
                    .unwrap_or_else(|e| panic!("{name}: {e}")),
            }
            ((name, input), hasher.finalize().to_hex().to_string())
        })
        .collect()
}

fn parse_manifest(text: &str) -> (u32, Fingerprints) {
    let mut lines = text.lines();
    let version = lines
        .next()
        .and_then(|l| l.strip_prefix("version "))
        .and_then(|v| v.trim().parse().ok())
        .expect("the manifest starts with `version <N>`");
    let rows = lines
        .filter(|l| !l.trim().is_empty())
        .map(|l| {
            let (path, input, output): (String, InputContent, String) = serde_json::from_str(l)
                .expect("JSON [probe path, input content, output hash] rows");
            ((path, input), output)
        })
        .collect();
    (version, rows)
}

fn manifest_text(version: u32, rows: &Fingerprints) -> String {
    let mut text = format!("version {version}\n");
    for ((path, input), output) in rows {
        text += &serde_json::to_string(&(path, input, output)).unwrap();
        text.push('\n');
    }
    text
}

/// Compare authored content using the recorded dependency paths, independently of what
/// today's importer discovers. A new selection rule still requires a bump when the
/// previously recorded files have not changed and the result differs.
fn changed_outputs<'a>(
    recorded_version: u32,
    version: u32,
    recorded: &Fingerprints,
    actual: &'a Fingerprints,
    root: &Path,
) -> Vec<&'a str> {
    if recorded_version != version {
        return Vec::new();
    }
    let prior: BTreeMap<_, _> = recorded
        .iter()
        .map(|((path, input), output)| (path.as_str(), (input, output)))
        .collect();
    actual
        .iter()
        .filter(|((path, input), output)| {
            prior
                .get(path.as_str())
                .is_some_and(|(before, old_output)| {
                    *old_output != *output && before.authored_files_unchanged(root, input)
                })
        })
        .map(|((path, _), _)| path.as_str())
        .collect()
}

#[test]
fn input_edits_allow_recording_but_output_changes_require_a_version_bump() {
    let root = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../tests/projects");
    let source = root.join("modelclips/Assets/Resources/Rig.fbx");
    let bytes = std::fs::read(&source).unwrap();
    let meta = std::fs::read(source.with_extension("fbx.meta")).unwrap();
    let input = input_fingerprint(&root, &source, &bytes, Some(&meta));
    let original = BTreeMap::from([(("Rig.fbx".into(), input.clone()), "output one".into())]);
    let changed = BTreeMap::from([(("Rig.fbx".into(), input.clone()), "output two".into())]);
    assert_eq!(
        changed_outputs(26, 26, &original, &changed, &root),
        ["Rig.fbx"]
    );
    assert!(changed_outputs(26, 27, &original, &changed, &root).is_empty());
    assert!(changed_outputs(26, 26, &original, &original, &root).is_empty());
    let mut edited_meta = meta.clone();
    edited_meta.extend_from_slice(b"\n# added clip settings\n");
    let meta_input = input_fingerprint(&root, &source, &bytes, Some(&edited_meta));
    let asset_input = input_fingerprint(&root, &source, b"changed asset", Some(&meta));
    for edited_input in [meta_input, asset_input] {
        assert_ne!(input, edited_input);
        let edited = BTreeMap::from([(("Rig.fbx".into(), edited_input), "output two".into())]);
        assert!(changed_outputs(26, 26, &original, &edited, &root).is_empty());
    }
    assert_ne!(
        input_fingerprint(&root, &source, &bytes, None),
        input_fingerprint(&root, &source, &bytes, Some(b""))
    );
}

#[test]
fn dependency_edits_and_checkout_moves_change_only_the_relevant_input_keys() {
    let cache = std::env::var_os("XDG_CACHE_HOME")
        .map(std::path::PathBuf::from)
        .unwrap_or_else(|| {
            std::path::PathBuf::from(std::env::var_os("HOME").unwrap()).join(".cache")
        });
    let root = cache
        .join("unbound/q199")
        .join(format!("input-control-{}", std::process::id()));
    let bytes = b"mtllib color.mtl\nv 0 0 0\n";
    let mut originals = Vec::new();
    for checkout in ["one", "two"] {
        let project = root.join(checkout);
        std::fs::create_dir_all(&project).unwrap();
        let source = project.join("Shape.obj");
        std::fs::write(&source, bytes).unwrap();
        let absent = input_fingerprint(&project, &source, bytes, None);
        std::fs::write(project.join("color.mtl"), b"newmtl Paint\nKd 1 0 0\n").unwrap();
        let original = input_fingerprint(&project, &source, bytes, None);
        assert_ne!(absent, original);
        std::fs::write(project.join("color.mtl"), b"newmtl Paint\nKd 0 1 0\n").unwrap();
        assert_ne!(original, input_fingerprint(&project, &source, bytes, None));
        originals.push(original);
    }
    assert_eq!(originals[0], originals[1]);
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn dependency_discovery_changes_do_not_masquerade_as_authored_edits() {
    let root = std::path::PathBuf::from(std::env::var_os("XDG_CACHE_HOME").unwrap_or_else(|| {
        std::path::PathBuf::from(std::env::var_os("HOME").unwrap())
            .join(".cache")
            .into_os_string()
    }))
    .join("unbound/q199")
    .join(format!("discovery-control-{}", std::process::id()));
    std::fs::create_dir_all(&root).unwrap();
    let source = root.join("Shape.obj");
    let bytes = b"mtllib color.mtl\nv 0 0 0\n";
    std::fs::write(&source, bytes).unwrap();
    std::fs::write(root.join("color.mtl"), b"newmtl Paint\nKd 1 0 0\n").unwrap();
    std::fs::write(root.join("other.mtl"), b"newmtl Other\nKd 0 0 1\n").unwrap();
    let input = input_fingerprint(&root, &source, bytes, None);
    let original = BTreeMap::from([(("Shape.obj".into(), input.clone()), "old output".into())]);
    for dependencies in [
        vec![],
        vec![(
            "other.mtl".into(),
            file_fingerprint(&root.join("other.mtl")),
        )],
    ] {
        let mut discovered = input.clone();
        discovered.dependencies = dependencies;
        assert_ne!(input, discovered);
        let changed = BTreeMap::from([(("Shape.obj".into(), discovered), "new output".into())]);
        assert_eq!(
            changed_outputs(30, 30, &original, &changed, &root),
            ["Shape.obj"]
        );
        assert!(changed_outputs(30, 31, &original, &changed, &root).is_empty());
    }
    std::fs::write(root.join("color.mtl"), b"newmtl Paint\nKd 0 1 0\n").unwrap();
    let edited = input_fingerprint(&root, &source, bytes, None);
    let changed = BTreeMap::from([(("Shape.obj".into(), edited), "new output".into())]);
    assert!(changed_outputs(30, 30, &original, &changed, &root).is_empty());
    std::fs::remove_file(root.join("color.mtl")).unwrap();
    let missing = input_fingerprint(&root, &source, bytes, None);
    let changed = BTreeMap::from([(("Shape.obj".into(), missing), "missing output".into())]);
    assert!(changed_outputs(30, 30, &original, &changed, &root).is_empty());
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn manifest_round_trips_paths_with_spaces() {
    let input = InputContent {
        source: "source hash".into(),
        meta: None,
        dependencies: vec![],
    };
    let expected = BTreeMap::from([(("model/Assets/A model.fbx".into(), input), "output".into())]);
    let (version, rows) = parse_manifest(&manifest_text(26, &expected));
    assert_eq!(version, 26);
    assert_eq!(rows, expected);
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
    let root = crate_dir.join("../../tests/projects");
    let actual = fingerprints(&root);
    let changed = changed_outputs(recorded_version, version, &recorded, &actual, &root);
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
        std::fs::write(&manifest_path, manifest_text(version, &actual)).unwrap();
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
        v.serialize(&mut Fingerprint(&mut hasher, "/checkout/tests/projects"))
            .unwrap();
        hasher.finalize()
    }
    assert_eq!(hash(&1.0f32), hash(&f32::from_bits(1.0f32.to_bits() + 1)));
    assert_ne!(hash(&1.0f32), hash(&1.01f32));
    assert_ne!(hash(&f32::INFINITY), hash(&f32::NEG_INFINITY));
    let guid = unbound_yaml::Guid;
    assert_ne!(hash(&guid(1 << 100)), hash(&guid(2 << 100)));
    assert_ne!(hash(&Some(0u8)), hash(&None::<u8>));
    // A file under the probes' root hashes the same from any checkout.
    let mut other = blake3::Hasher::new();
    "/elsewhere/tests/projects/a/b.png"
        .serialize(&mut Fingerprint(&mut other, "/elsewhere/tests/projects"))
        .unwrap();
    assert_eq!(hash(&"/checkout/tests/projects/a/b.png"), other.finalize());
    assert_ne!(hash(&"/checkout/tests/projects/a/b.png"), hash(&"/a/b.png"));
}
